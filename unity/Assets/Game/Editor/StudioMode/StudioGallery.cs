// StudioGallery.cs - every .glb in a folder on its own plinth at game scale,
// named, in a grid spaced by footprint, with a monarch for scale and the
// originals beside them on request, far west of the stage in its light and
// weather. Models load nearest the camera first within a time and memory
// budget each editor frame, and reload where they stand when they change.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace OpenKingdomsUnity.Studio
{
    public struct GalleryView
    {
        public bool Classic;
        public Vector3 Focus;
        public float Distance, Yaw, Pitch;

        public static GalleryView Lerp(GalleryView a, GalleryView b, float t) => new GalleryView
        {
            Classic = b.Classic, Focus = Vector3.Lerp(a.Focus, b.Focus, t), Distance = Mathf.Lerp(a.Distance, b.Distance, t),
            Yaw = Mathf.LerpAngle(a.Yaw, b.Yaw, t), Pitch = Mathf.Lerp(a.Pitch, b.Pitch, t),
        };
    }

    [InitializeOnLoad]
    public sealed class StudioGallery : IDisposable
    {
        public const string RootName = "Studio Gallery";
        // Far enough west that neither the studio's stage nor a map's land
        // is within the other's camera.
        public static readonly Vector3 Origin = new Vector3(-3000f, 0f, 0f);
        public const float PlinthHeight = 0.12f, Margin = 0.35f, OriginalGap = 0.6f;
        public const float LabelSize = 0.45f, LabelDistance = 90f, FlySeconds = 0.6f, TurnSpeed = 25f;
        // Seconds of loading on the main thread per editor frame.
        public const double TickBudget = 0.03;
        // Pictures and meshes kept loaded at once, in bytes.
        public static long MemoryBudget = 1536L << 20;
        public const string FolderKey = "oku.gallery.folder";
        public const string DefaultFolder = "Assets/Overrides/Generated/Units";

        public static readonly (string label, string path)[] QuickFolders =
        {
            ("Overrides/Generated/Units", "Assets/Overrides/Generated/Units"),
            ("Overrides/Generated", "Assets/Overrides/Generated"),
            ("Overrides/Features", "Assets/Overrides/Features"),
            ("Overrides/Units", "Assets/Overrides/Units"),
        };

        // The one the gallery window shows.
        public static StudioGallery Current { get; private set; }
        public static string LastFolder
        {
            get => EditorPrefs.GetString(FolderKey, DefaultFolder);
            set => EditorPrefs.SetString(FolderKey, value ?? DefaultFolder);
        }

        public sealed class Entry
        {
            public string Path, Name;
            public DateTime Time;
            public long Size;
            public Bounds Bounds = new Bounds(Vector3.up * 0.5f, Vector3.one);
            public bool Measured;
            public string Error;
            public GalleryLayout.Slot Slot;
            public bool Shown;
            public GameObject Node, Plinth, Spin, Model, Placeholder, Label, Original;
            // The original's footprint with its margin, its centre from its
            // anchor, and its height, in cells. Zero across when it has none.
            public Vector2 OriginalSize, OriginalCentre;
            public float OriginalHeight;
            public bool OriginalResolved;
            public string OriginalNote;
            internal Transform Card;
            internal Texture2D CardTexture;
            internal Material CardMaterial;
            internal Task<byte[]> Reading;
            internal long Bytes;
            internal bool Queued;

            public bool Loaded => Model != null;
            public bool Failed => Error != null;
            public Vector2 ModelSize => new Vector2(Mathf.Max(1f, Bounds.size.x + 2 * Margin), Mathf.Max(1f, Bounds.size.z + 2 * Margin));
            public Vector3 Centre => Origin + new Vector3(Slot.Centre.x, 0f, Slot.Centre.y);
        }

        public struct ProgressInfo
        {
            public string What;
            public int Done, Total;
        }

        public string Folder { get; private set; }
        public readonly List<Entry> Entries = new List<Entry>();
        public GalleryLayout.Result Layout { get; private set; } = new GalleryLayout.Result();
        public IReadOnlyList<Entry> Shown => shown;
        public string Filter { get; private set; } = "";
        public bool Compare { get; private set; }
        public bool Turning = true;
        public GalleryView View;
        public Entry Selected { get; private set; }
        public string Status { get; private set; } = "";
        public ProgressInfo? Progress { get; private set; }

        // A label's scale at d cells from the camera: its own size close up,
        // up to twice it further out, where it would be too small to read.
        public static float LabelScale(float d) => Mathf.Clamp(d / 30f, 1f, 2.2f);
        public GameObject Root { get; private set; }
        public Camera Camera { get; private set; }
        public GameObject Monarch => monarch;
        public bool MonarchIsStandIn { get; private set; }
        public long LoadedBytes => loadedBytes;
        public int LoadedCount => Entries.Count(e => e.Loaded);
        // The longest editor frame the gallery has taken, in seconds.
        public double LongestTick { get; private set; }
        // Nothing left to read, set out, find or load.
        public bool Idle => scan == null && buildQueue.Count == 0 && (!Compare || resolveQueue.Count == 0) && waiting == 0;
        public event Action Changed;

        readonly List<Entry> shown = new List<Entry>();
        readonly Queue<Entry> buildQueue = new Queue<Entry>();
        readonly Queue<Entry> resolveQueue = new Queue<Entry>();
        readonly List<Object> owned = new List<Object>();
        Task<List<Entry>> scan;
        int scanned, scanTotal, resolved, resolveTotal, waiting;
        GameObject monarch, monarchNode, monarchLabel, ground;
        float monarchWidth = 2f, monarchHeight = StudioTargets.MonarchHeight;
        Mesh cube, cardQuad;
        Material plinthMat, placeholderMat, failedMat, groundMat;
        Texture2D groundTex;
        string groundClimate;
        Font labelFont;
        IGameBackend backend;
        List<StudioTarget> features, cards, units;
        GalleryView flyFrom, flyTo;
        double flyStart = -1, lastTick, lastPoll;
        float spinYaw, aspect = 16f / 9f;
        Entry spun;
        long loadedBytes;
        bool disposed, blocked;

        static StudioGallery()
        {
            EditorApplication.update += () => Current?.Tick(EditorApplication.timeSinceStartup);
            // The engine plays one game at a time, and a reload loses the
            // gallery's hold on what it made, so both let it go first.
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.ExitingEditMode) Close(); };
            AssemblyReloadEvents.beforeAssemblyReload += Close;
        }

        // The gallery on folder, opened when it is not already.
        public static StudioGallery Show(string folder)
        {
            string full = StudioModel.Absolute(string.IsNullOrEmpty(folder) ? DefaultFolder : folder);
            if (Current != null && !Current.disposed && string.Equals(Current.Folder, full, StringComparison.OrdinalIgnoreCase)) return Current;
            Close();
            LastFolder = folder;
            Current = new StudioGallery();
            Current.Open(full);
            return Current;
        }

        public static void Close()
        {
            Current?.Dispose();
            Current = null;
        }

        // ---- Opening ----

        public void Open(string folder)
        {
            ClearWorld();
            disposed = false;
            Folder = Path.GetFullPath(folder);
            RemoveStray();
            Root = new GameObject(RootName) { hideFlags = HideFlags.DontSave };
            Root.transform.position = Vector3.zero;
            var camGo = new GameObject("Gallery Camera") { hideFlags = HideFlags.HideAndDontSave };
            camGo.transform.SetParent(Root.transform, false);
            Camera = camGo.AddComponent<Camera>();
            Camera.enabled = false;
            Camera.clearFlags = CameraClearFlags.Skybox;
            Camera.nearClipPlane = 0.3f;
            Camera.farClipPlane = 1500f;
            Camera.fieldOfView = StudioView.ClassicFov;
            Camera.depthTextureMode |= DepthTextureMode.Depth;
            StudioSession.Stage?.Atmosphere?.SetPostEffects(true, Camera);
            cube = Own(Cube());
            cardQuad = Own(CardQuad());
            plinthMat = Own(Looks.Model(null));
            plinthMat.color = new Color(0.52f, 0.5f, 0.47f);
            placeholderMat = Own(Looks.Overlay(new Color(0.85f, 0.9f, 1f, 0.22f)));
            failedMat = Own(Looks.Overlay(new Color(1f, 0.25f, 0.2f, 0.45f)));
            labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            View = new GalleryView { Classic = true, Focus = Origin, Distance = 40f, Pitch = GameCamera.ClassicPitch, Yaw = 0f };
            backend = StudioBackend.Get();
            BuildMonarch();
            RebuildGround(new Rect(-10, -10, 20, 20));
            if (!Directory.Exists(Folder)) { Status = "There is no folder at " + Folder + ". The gallery fills as soon as models appear there."; Notify(); return; }
            var files = new DirectoryInfo(Folder).GetFiles("*.glb").OrderBy(f => Path.GetFileNameWithoutExtension(f.Name), Comparer<string>.Create(GalleryLayout.NaturalCompare)).ToList();
            scanTotal = files.Count;
            scanned = 0;
            Status = files.Count == 0 ? "No .glb files in " + Folder + " yet." : $"Reading {files.Count} files...";
            scan = Task.Run(() =>
            {
                var list = new List<Entry>(files.Count);
                foreach (var f in files)
                {
                    list.Add(Measure(f));
                    Interlocked.Increment(ref scanned);
                }
                return list;
            });
            Progress = new ProgressInfo { What = "Reading the files", Done = 0, Total = scanTotal };
            Notify();
        }

        static Entry Measure(FileInfo f)
        {
            var e = new Entry { Path = f.FullName, Name = Path.GetFileNameWithoutExtension(f.Name), Time = f.LastWriteTimeUtc, Size = f.Length };
            if (GlbBounds.Read(f.FullName, out var b, out var error)) { e.Bounds = b; e.Measured = true; }
            else e.Error = error;
            return e;
        }

        // Galleries left by an earlier script domain.
        static void RemoveStray()
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go != null && go.name == RootName && go.transform.parent == null && !EditorUtility.IsPersistent(go))
                    Object.DestroyImmediate(go);
        }

        // ---- Each editor frame ----

        public void Tick(double now)
        {
            if (disposed || Root == null) return;
            if (!StudioMode.IsOn) { Close(); return; }
            if (!StudioSession.Active) return;
            var clock = Stopwatch.StartNew();
            float dt = lastTick > 0 ? Mathf.Clamp((float)(now - lastTick), 0f, 0.25f) : 0f;
            lastTick = now;
            bool moved = false;
            if (StudioBackend.Get() != backend) { NewBackend(); moved = true; }
            if (groundClimate != Climate) { RebuildGround(Layout.Bounds); moved = true; }
            if (scan != null)
            {
                if (!scan.IsCompleted)
                {
                    Progress = new ProgressInfo { What = "Reading the files", Done = Volatile.Read(ref scanned), Total = scanTotal };
                    Notify();
                    return;
                }
                FinishScan();
                moved = true;
            }
            double end = TickBudget;
            moved |= BuildSome(clock, end);
            if (flyStart >= 0)
            {
                float t = Mathf.Clamp01((float)((now - flyStart) / FlySeconds));
                View = GalleryView.Lerp(flyFrom, flyTo, Mathf.SmoothStep(0f, 1f, t));
                if (t >= 1f) flyStart = -1;
                moved = true;
            }
            if (Turning && !View.Classic && Selected != null && Selected.Loaded)
            {
                spinYaw = (spinYaw + dt * TurnSpeed) % 360f;
                moved = true;
            }
            if (Compare) moved |= ResolveSome(clock, end);
            moved |= LoadSome(clock, end);
            if (now - lastPoll > 1.0)
            {
                lastPoll = now;
                moved |= Poll();
            }
            UpdateProgress();
            LongestTick = Math.Max(LongestTick, clock.Elapsed.TotalSeconds);
            if (moved || StudioSession.Stage.Animating) Notify();
        }

        void Notify() => Changed?.Invoke();

        // Runs frames until nothing is left to do or the time is up, as the
        // editor would. For tests; returns whether it went idle.
        public bool Pump(double seconds)
        {
            var clock = Stopwatch.StartNew();
            double t = EditorApplication.timeSinceStartup;
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                t += 0.02;
                Tick(t);
                if (Idle && flyStart < 0) return true;
                if (scan != null && !scan.IsCompleted) Thread.Sleep(2);
            }
            return Idle;
        }

        void FinishScan()
        {
            var list = scan.Status == TaskStatus.RanToCompletion ? scan.Result : new List<Entry>();
            if (scan.Exception != null) Debug.LogWarning("Gallery: " + scan.Exception.GetBaseException().Message);
            scan = null;
            Entries.Clear();
            Entries.AddRange(list);
            Relayout();
            View = OverviewView(true);
            Status = Entries.Count == 0 ? "No .glb files in " + Folder + " yet." : $"{Entries.Count} models in {Shorten(Folder)}.";
        }

        void UpdateProgress()
        {
            if (buildQueue.Count > 0) { Progress = new ProgressInfo { What = "Setting out the plinths", Done = shown.Count - buildQueue.Count, Total = shown.Count }; return; }
            if (Compare && resolveQueue.Count > 0) { Progress = new ProgressInfo { What = "Finding the originals", Done = resolved, Total = resolveTotal }; return; }
            int done = 0, total = 0;
            foreach (var e in shown) { total++; if (e.Loaded || e.Failed) done++; }
            if (done < total) Progress = new ProgressInfo { What = blocked ? "Loaded as many as the memory budget allows, the rest load as the camera nears them" : "Loading the models, nearest first", Done = done, Total = total };
            else Progress = null;
        }

        // ---- Laying out ----

        string Climate => StudioSession.Stage?.ClimateName ?? "grass";

        Vector2 SizeOf(Entry e)
        {
            var m = e.ModelSize;
            if (!Compare || e.OriginalSize.x <= 0) return m;
            return new Vector2(m.x + OriginalGap + e.OriginalSize.x, Mathf.Max(m.y, e.OriginalSize.y));
        }

        void Relayout()
        {
            Layout = GalleryLayout.Arrange(Entries.Select(e => e.Name).ToList(), Entries.Select(SizeOf).ToList(), Filter);
            foreach (var e in Entries) e.Shown = false;
            shown.Clear();
            foreach (var s in Layout.Slots)
            {
                var e = Entries[s.Item];
                e.Slot = s;
                e.Shown = true;
                shown.Add(e);
            }
            foreach (var e in Entries)
            {
                if (e.Node != null)
                {
                    e.Node.SetActive(e.Shown);
                    if (e.Shown) Place(e);
                }
                else if (e.Shown && !e.Queued) { e.Queued = true; buildQueue.Enqueue(e); }
            }
            if (Selected != null && !Selected.Shown) Selected = null;
            PlaceMonarch();
            RebuildGround(Layout.Bounds);
            blocked = false;
            Notify();
        }

        public void SetFilter(string filter)
        {
            filter ??= "";
            if (filter == Filter) return;
            Filter = filter;
            Relayout();
            FlyTo(Selected != null ? ViewOf(Selected) : OverviewView(View.Classic));
        }

        public void SetCompare(bool on)
        {
            if (on == Compare) return;
            Compare = on;
            if (on)
            {
                resolveQueue.Clear();
                foreach (var e in Entries) if (!e.OriginalResolved) resolveQueue.Enqueue(e);
                resolved = 0;
                resolveTotal = resolveQueue.Count;
                if (resolveQueue.Count > 0) { Notify(); return; }
            }
            Relayout();
        }

        // Builds the nodes still waiting, within the frame's budget.
        bool BuildSome(Stopwatch clock, double end)
        {
            bool any = false;
            while (buildQueue.Count > 0 && clock.Elapsed.TotalSeconds < end)
            {
                var e = buildQueue.Dequeue();
                e.Queued = false;
                if (disposed || !Entries.Contains(e) || e.Node != null) continue;
                BuildNode(e);
                e.Node.SetActive(e.Shown);
                if (e.Shown) Place(e);
                any = true;
            }
            return any;
        }

        void BuildNode(Entry e)
        {
            e.Node = Child(Root.transform, e.Name);
            e.Plinth = Child(e.Node.transform, "Plinth");
            e.Plinth.AddComponent<MeshFilter>().sharedMesh = cube;
            var pr = e.Plinth.AddComponent<MeshRenderer>();
            pr.sharedMaterial = plinthMat;
            pr.shadowCastingMode = ShadowCastingMode.On;
            e.Spin = Child(e.Node.transform, "Model");
            e.Placeholder = Child(e.Spin.transform, "Placeholder");
            e.Placeholder.AddComponent<MeshFilter>().sharedMesh = cube;
            var hr = e.Placeholder.AddComponent<MeshRenderer>();
            hr.sharedMaterial = e.Failed ? failedMat : placeholderMat;
            hr.shadowCastingMode = ShadowCastingMode.Off;
            e.Label = Child(e.Node.transform, "Label");
            var tm = e.Label.AddComponent<TextMesh>();
            tm.font = labelFont;
            tm.fontSize = 64;
            tm.characterSize = LabelSize / 6.4f;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 0.92f, 0.7f);
            var lr = e.Label.GetComponent<MeshRenderer>();
            lr.sharedMaterial = labelFont.material;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (e.Original != null) e.Original.transform.SetParent(e.Node.transform, false);
        }

        // Puts an entry's plinth, model, original and label where its slot is.
        void Place(Entry e)
        {
            var size = e.Slot.Size;
            e.Node.transform.position = e.Centre;
            e.Plinth.transform.localPosition = new Vector3(0f, PlinthHeight * 0.5f, 0f);
            e.Plinth.transform.localScale = new Vector3(size.x, PlinthHeight, size.y);
            var m = e.ModelSize;
            float modelX = -size.x * 0.5f + m.x * 0.5f;
            var b = e.Bounds;
            e.Spin.transform.localPosition = new Vector3(modelX - b.center.x, PlinthHeight, -b.center.z);
            e.Placeholder.transform.localPosition = b.center;
            e.Placeholder.transform.localScale = Vector3.Max(b.size, Vector3.one * 0.05f);
            e.Placeholder.SetActive(!e.Loaded);
            e.Placeholder.GetComponent<MeshRenderer>().sharedMaterial = e.Failed ? failedMat : placeholderMat;
            float top = b.max.y;
            if (e.Original != null)
            {
                bool on = Compare && e.OriginalSize.x > 0;
                e.Original.SetActive(on);
                if (on)
                {
                    float x = size.x * 0.5f - e.OriginalSize.x * 0.5f;
                    e.Original.transform.localPosition = new Vector3(x - e.OriginalCentre.x, PlinthHeight, -e.OriginalCentre.y);
                    top = Mathf.Max(top, e.OriginalHeight);
                }
            }
            e.Label.transform.localPosition = new Vector3(0f, PlinthHeight + Mathf.Max(0.3f, top) + 0.35f, 0f);
            e.Label.GetComponent<TextMesh>().text = LabelText(e);
        }

        string LabelText(Entry e)
        {
            if (e.Failed) return e.Name + " (did not load)";
            if (Compare && e.OriginalResolved && e.OriginalSize.x <= 0) return e.Name + " (no original)";
            return e.Name;
        }

        // ---- The monarch ----

        void BuildMonarch()
        {
            if (monarchNode != null) Object.DestroyImmediate(monarchNode);
            monarchNode = Child(Root.transform, "Monarch for scale");
            var plinth = Child(monarchNode.transform, "Plinth");
            plinth.AddComponent<MeshFilter>().sharedMesh = cube;
            plinth.AddComponent<MeshRenderer>().sharedMaterial = plinthMat;
            var pm = StudioSession.MonarchModel(backend);
            MonarchIsStandIn = pm == null;
            if (pm != null)
            {
                monarch = StudioStage.Rest(pm, "Monarch", null, null);
                monarchWidth = Mathf.Max(pm.RestBounds.size.x, pm.RestBounds.size.z);
                monarchHeight = pm.RestBounds.max.y;
                monarch.transform.SetParent(monarchNode.transform, false);
                monarch.transform.localPosition = new Vector3(-pm.RestBounds.center.x, PlinthHeight, -pm.RestBounds.center.z);
            }
            else
            {
                monarch = StudioStage.Marker(StudioSession.Team, owned);
                monarchWidth = 2f;
                monarchHeight = StudioTargets.MonarchHeight;
                monarch.transform.SetParent(monarchNode.transform, false);
                monarch.transform.localPosition = new Vector3(0f, PlinthHeight, 0f);
            }
            float w = monarchWidth + 2 * Margin;
            plinth.transform.localPosition = new Vector3(0f, PlinthHeight * 0.5f, 0f);
            plinth.transform.localScale = new Vector3(w, PlinthHeight, w);
            monarchLabel = Child(monarchNode.transform, "Label");
            var tm = monarchLabel.AddComponent<TextMesh>();
            tm.font = labelFont;
            tm.fontSize = 64;
            tm.characterSize = LabelSize / 6.4f;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.8f, 0.9f, 1f);
            tm.text = MonarchIsStandIn ? "Monarch (stand-in, 4 cells)" : "Monarch";
            monarchLabel.GetComponent<MeshRenderer>().sharedMaterial = labelFont.material;
            monarchLabel.transform.localPosition = new Vector3(0f, PlinthHeight + monarchHeight + 0.35f, 0f);
            foreach (var t in monarchNode.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            PlaceMonarch();
        }

        // West of the first row, clear of it.
        void PlaceMonarch()
        {
            if (monarchNode == null) return;
            float w = monarchWidth + 2 * Margin;
            float z = shown.Count > 0 ? shown[0].Slot.Centre.y : 0f;
            monarchNode.transform.position = Origin + new Vector3(-(GalleryLayout.Gap * 1.5f + w * 0.5f), 0f, z);
        }

        public Rect MonarchRect
        {
            get
            {
                float w = monarchWidth + 2 * Margin;
                var p = monarchNode != null ? monarchNode.transform.position - Origin : Vector3.zero;
                return new Rect(p.x - w * 0.5f, p.z - w * 0.5f, w, w);
            }
        }

        // ---- The ground ----

        void RebuildGround(Rect grid)
        {
            groundClimate = Climate;
            if (ground != null) Object.DestroyImmediate(ground);
            if (groundMat != null) { owned.Remove(groundMat); Object.DestroyImmediate(groundMat); }
            if (groundTex != null) { owned.Remove(groundTex); Object.DestroyImmediate(groundTex); }
            groundTex = Own(StudioStage.GroundTexture(StudioStage.ClimateColour(groundClimate)));
            groundMat = Own(Looks.Terrain(groundTex, -100f));
            const float reach = 600f;
            float x0 = Origin.x + grid.xMin - reach, x1 = Origin.x + grid.xMax + reach, z0 = Origin.z + grid.yMin - reach, z1 = Origin.z + grid.yMax + reach;
            var mesh = Own(new Mesh { name = "gallery ground" });
            mesh.vertices = new[] { new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), new Vector3(x1, 0f, z0) };
            mesh.uv = new[] { new Vector2(x0, z0) / 4f, new Vector2(x0, z1) / 4f, new Vector2(x1, z1) / 4f, new Vector2(x1, z0) / 4f };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.RecalculateBounds();
            ground = Child(Root.transform, "Ground");
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = ground.AddComponent<MeshRenderer>();
            r.sharedMaterial = groundMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---- Loading ----

        bool LoadSome(Stopwatch clock, double end)
        {
            waiting = 0;
            List<Entry> todo = null;
            foreach (var e in shown)
                if (e.Node != null && !e.Loaded && !e.Failed) (todo ??= new List<Entry>()).Add(e);
            if (todo == null) return false;
            waiting = todo.Count;
            var focus = View.Focus;
            todo.Sort((a, b) => (a.Centre - focus).sqrMagnitude.CompareTo((b.Centre - focus).sqrMagnitude));
            // The eight nearest files read ahead, and what was read for ones
            // the camera has since left behind let go.
            for (int i = 0; i < todo.Count; i++)
            {
                var e = todo[i];
                if (i < 8 && e.Reading == null)
                {
                    string path = e.Path;
                    e.Reading = Task.Run(() => File.ReadAllBytes(path));
                }
                else if (i >= 16 && e.Reading != null && e.Reading.IsCompleted) e.Reading = null;
            }
            bool any = false;
            foreach (var e in todo)
            {
                // Strictly nearest first: a nearer file still being read holds
                // the rest back.
                if (clock.Elapsed.TotalSeconds >= end || e.Reading == null || !e.Reading.IsCompleted) break;
                var task = e.Reading;
                e.Reading = null;
                if (task.Status != TaskStatus.RanToCompletion) { Fail(e, task.Exception?.GetBaseException().Message ?? "it could not be read"); any = true; continue; }
                var bytes = task.Result;
                long guess = bytes.LongLength * 3;
                if (loadedBytes + guess > MemoryBudget && !MakeRoom(guess, (e.Centre - focus).sqrMagnitude))
                {
                    // Far ones wait until the camera comes nearer, this one
                    // with its file already read.
                    e.Reading = task;
                    blocked = true;
                    break;
                }
                LoadNow(e, bytes);
                any = true;
            }
            return any;
        }

        void LoadNow(Entry e, byte[] bytes)
        {
            GameObject go = null;
            string error = null;
            try { go = GlbLoader.Load(bytes, e.Name, out error); }
            catch (Exception x) { error = x.Message; }
            if (go == null) { Fail(e, error ?? "it did not read"); return; }
            go.transform.SetParent(e.Spin.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.SetActive(true);
            e.Model = go;
            e.Error = null;
            e.Bytes = Weigh(go);
            loadedBytes += e.Bytes;
            e.Placeholder.SetActive(false);
            e.Label.GetComponent<TextMesh>().text = LabelText(e);
        }

        void Fail(Entry e, string why)
        {
            e.Error = why;
            if (e.Node == null) return;
            e.Placeholder.SetActive(true);
            e.Placeholder.GetComponent<MeshRenderer>().sharedMaterial = failedMat;
            e.Label.GetComponent<TextMesh>().text = LabelText(e);
        }

        void Unload(Entry e)
        {
            if (e.Model != null) StudioModel.DisposeTemplate(e.Model);
            e.Model = null;
            loadedBytes -= e.Bytes;
            e.Bytes = 0;
            if (e.Placeholder != null) e.Placeholder.SetActive(true);
        }

        // Lets go of hidden models, then the farthest of those farther away
        // than the one wanted, until need fits.
        bool MakeRoom(long need, float nearerThan)
        {
            var focus = View.Focus;
            var loaded = Entries.Where(x => x.Loaded && (!x.Shown || (x.Centre - focus).sqrMagnitude > nearerThan))
                .OrderByDescending(x => x.Shown ? (x.Centre - focus).sqrMagnitude : float.MaxValue).ToList();
            foreach (var x in loaded)
            {
                if (loadedBytes + need <= MemoryBudget) break;
                Unload(x);
            }
            return loadedBytes + need <= MemoryBudget;
        }

        // Pictures and meshes a loaded model holds, roughly, in bytes.
        static long Weigh(GameObject go)
        {
            long n = 0;
            var seen = new HashSet<Object>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.mainTexture is Texture2D t && seen.Add(t)) n += (long)t.width * t.height * 16 / 3;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && seen.Add(mf.sharedMesh)) n += (long)mf.sharedMesh.vertexCount * 48;
            }
            return Math.Max(n, 1024);
        }

        // ---- Hot reload ----

        // Looks for new, changed and removed files now, as it does every
        // second. True when something changed.
        public bool CheckFolder() => Poll();

        // New, changed and removed files, once each has stopped changing.
        bool Poll()
        {
            if (scan != null) return false;
            FileInfo[] files;
            try { files = Directory.Exists(Folder) ? new DirectoryInfo(Folder).GetFiles("*.glb") : new FileInfo[0]; }
            catch (IOException) { return false; }
            var byPath = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in Entries) byPath[e.Path] = e;
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool relayout = false, changed = false;
            foreach (var f in files)
            {
                present.Add(f.FullName);
                bool settled = (DateTime.UtcNow - f.LastWriteTimeUtc).TotalSeconds > 0.3;
                if (!byPath.TryGetValue(f.FullName, out var e))
                {
                    if (!settled) continue;
                    var fresh = Measure(f);
                    int at = Entries.FindIndex(x => GalleryLayout.NaturalCompare(x.Name, fresh.Name) > 0);
                    Entries.Insert(at < 0 ? Entries.Count : at, fresh);
                    if (Compare) { resolveQueue.Enqueue(fresh); resolveTotal++; }
                    relayout = true;
                    continue;
                }
                if (f.LastWriteTimeUtc == e.Time && f.Length == e.Size) continue;
                if (!settled) continue;
                relayout |= Refresh(e, f);
                changed = true;
            }
            foreach (var e in Entries.Where(x => !present.Contains(x.Path)).ToList())
            {
                Remove(e);
                relayout = true;
            }
            if (relayout)
            {
                Relayout();
                Status = $"{Entries.Count} models in {Shorten(Folder)}.";
            }
            return relayout || changed;
        }

        // A changed file measured and reloaded in place. True when its size
        // changed, so the grid moves.
        bool Refresh(Entry e, FileInfo f)
        {
            var old = SizeOf(e);
            e.Time = f.LastWriteTimeUtc;
            e.Size = f.Length;
            var m = Measure(f);
            e.Bounds = m.Bounds;
            e.Measured = m.Measured;
            bool wasLoaded = e.Loaded;
            if (e.Reading != null) e.Reading = null;
            Unload(e);
            e.Error = m.Error;
            if (e.Node != null && e.Error == null && (wasLoaded || e.Shown))
            {
                try { LoadNow(e, File.ReadAllBytes(e.Path)); }
                catch (IOException x) { Fail(e, x.Message); }
            }
            else if (e.Error != null) Fail(e, e.Error);
            if (e.Node != null && e.Shown) Place(e);
            Status = "Reloaded " + e.Name + ".";
            return SizeOf(e) != old;
        }

        void Remove(Entry e)
        {
            Unload(e);
            DropOriginal(e);
            if (e.Node != null) Object.DestroyImmediate(e.Node);
            e.Node = null;
            Entries.Remove(e);
            if (Selected == e) Selected = null;
            if (spun == e) spun = null;
        }

        // ---- The originals ----

        bool ResolveSome(Stopwatch clock, double end)
        {
            if (resolveQueue.Count == 0) return false;
            var b = backend;
            if (features == null)
            {
                features = StudioTargets.Features(b, StudioTargets.Catalog());
                cards = StudioTargets.Units(b, true);
                units = StudioTargets.Units(b, false);
            }
            while (resolveQueue.Count > 0 && clock.Elapsed.TotalSeconds < end)
            {
                var e = resolveQueue.Dequeue();
                if (!Entries.Contains(e) || e.OriginalResolved) continue;
                try { Resolve(e, b); }
                catch (Exception x) { e.OriginalNote = x.Message; Debug.LogWarning($"Gallery: the original of {e.Name}: {x.Message}"); }
                e.OriginalResolved = true;
                resolved++;
            }
            if (resolveQueue.Count > 0) return false;
            Relayout();
            return true;
        }

        void Resolve(Entry e, IGameBackend b)
        {
            DropOriginal(e);
            var t = StudioTargets.Match(e.Name, features, cards, units);
            if (t == null || b == null) { e.OriginalNote = "nothing in the game has this name"; return; }
            var pm = StudioSession.OriginalModel(b, t, StudioSession.TeamColour);
            if (pm != null)
            {
                e.Original = StudioStage.Rest(pm, "Original", null, null);
                var rb = pm.RestBounds;
                e.OriginalCentre = new Vector2(rb.center.x, rb.center.z);
                e.OriginalSize = new Vector2(Mathf.Max(1f, rb.size.x + 2 * Margin), Mathf.Max(1f, rb.size.z + 2 * Margin));
                e.OriginalHeight = rb.max.y;
            }
            else if (t.Kind == TargetKind.Feature && StudioSession.OriginalPicture(b, t, out var tex, out var rect))
            {
                e.CardTexture = tex;
                e.CardMaterial = Looks.Model(tex);
                e.CardMaterial.SetFloat("_Cull", 0f);
                e.Original = new GameObject("Original");
                var face = new GameObject("Face");
                face.transform.SetParent(e.Original.transform, false);
                var quad = new GameObject("Picture");
                quad.transform.SetParent(face.transform, false);
                quad.transform.localPosition = new Vector3(rect.x, rect.y, 0f);
                quad.transform.localScale = new Vector3(rect.width, rect.height, 1f);
                quad.AddComponent<MeshFilter>().sharedMesh = cardQuad;
                var r = quad.AddComponent<MeshRenderer>();
                r.sharedMaterial = e.CardMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                e.Card = face.transform;
                e.OriginalCentre = new Vector2(rect.x + rect.width * 0.5f, 0f);
                e.OriginalSize = new Vector2(Mathf.Max(1f, rect.width + 2 * Margin), 1f + 2 * Margin);
                e.OriginalHeight = rect.yMax;
            }
            else { e.OriginalNote = "the game has no original to show for it"; return; }
            foreach (var tr in e.Original.GetComponentsInChildren<Transform>(true)) tr.gameObject.hideFlags = HideFlags.HideAndDontSave;
            // Out of sight until Place stands it on its plinth.
            e.Original.SetActive(false);
            if (e.Node != null) e.Original.transform.SetParent(e.Node.transform, false);
        }

        void DropOriginal(Entry e)
        {
            if (e.Original != null) Object.DestroyImmediate(e.Original);
            if (e.CardMaterial != null) Object.DestroyImmediate(e.CardMaterial);
            if (e.CardTexture != null) Object.DestroyImmediate(e.CardTexture);
            e.Original = null;
            e.Card = null;
            e.CardMaterial = null;
            e.CardTexture = null;
            e.OriginalSize = e.OriginalCentre = Vector2.zero;
            e.OriginalHeight = 0f;
        }

        // Another backend (settings, the stand-in toggle) brings its own
        // monarch and originals.
        void NewBackend()
        {
            backend = StudioBackend.Get();
            features = cards = units = null;
            if (Root == null) return;
            BuildMonarch();
            foreach (var e in Entries) { DropOriginal(e); e.OriginalResolved = false; }
            if (Compare)
            {
                resolveQueue.Clear();
                foreach (var e in Entries) resolveQueue.Enqueue(e);
                resolved = 0;
                resolveTotal = resolveQueue.Count;
            }
            Relayout();
        }

        // ---- The camera ----

        public void FlyTo(GalleryView to)
        {
            flyFrom = View;
            flyTo = to;
            flyStart = EditorApplication.timeSinceStartup;
            Notify();
        }

        public void SetClassic(bool classic)
        {
            if (View.Classic == classic) return;
            var v = View;
            v.Classic = classic;
            if (!classic) { v.Pitch = 30f; v.Yaw = 20f; }
            if (Selected != null)
            {
                var s = ViewOf(Selected, classic);
                v.Focus = s.Focus;
            }
            View = v;
            flyStart = -1;
            Notify();
        }

        // The whole grid and the monarch in frame.
        public GalleryView OverviewView(bool classic)
        {
            var r = Layout.Slots.Count > 0 ? Layout.Bounds : new Rect(-5, -5, 10, 10);
            var m = MonarchRect;
            r = Rect.MinMaxRect(Mathf.Min(r.xMin, m.xMin), Mathf.Min(r.yMin, m.yMin), Mathf.Max(r.xMax, m.xMax), Mathf.Max(r.yMax, m.yMax));
            float pitch = classic ? GameCamera.ClassicPitch : 45f;
            float half = StudioView.ClassicFov * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(half) * aspect);
            float byWidth = r.width * 0.5f / Mathf.Tan(hHalf);
            float byDepth = r.height * Mathf.Sin(pitch * Mathf.Deg2Rad) * 0.5f / Mathf.Tan(half);
            return new GalleryView
            {
                Classic = classic, Focus = Origin + new Vector3(r.center.x, 0f, r.center.y),
                Distance = Mathf.Clamp(Mathf.Max(byWidth, byDepth) * 1.1f + 6f, 12f, 1200f),
                Pitch = pitch, Yaw = classic ? 0f : 20f,
            };
        }

        public GalleryView ViewOf(Entry e) => ViewOf(e, View.Classic);

        GalleryView ViewOf(Entry e, bool classic)
        {
            float size = Mathf.Max(e.Slot.Size.x, e.Slot.Size.y, e.Bounds.size.y * 1.4f);
            var focus = e.Centre + Vector3.up * PlinthHeight;
            if (!classic) focus += Vector3.up * Mathf.Clamp(e.Bounds.center.y, 0.3f, 6f);
            return new GalleryView
            {
                Classic = classic, Focus = focus, Distance = Mathf.Clamp(size * 2.2f + 5f, 7f, 90f),
                Pitch = classic ? GameCamera.ClassicPitch : (View.Classic ? 30f : View.Pitch), Yaw = classic ? 0f : (View.Classic ? 20f : View.Yaw),
            };
        }

        public void Overview()
        {
            Selected = null;
            FlyTo(OverviewView(View.Classic));
        }

        public void Select(Entry e)
        {
            if (e == null || !e.Shown) return;
            Selected = e;
            spinYaw = 0f;
            FlyTo(ViewOf(e));
        }

        // The next (1) or previous (-1) model in the grid's order.
        public void Step(int step)
        {
            if (shown.Count == 0) return;
            int i = Selected != null ? shown.IndexOf(Selected) : (step > 0 ? -1 : 0);
            i = ((i + step) % shown.Count + shown.Count) % shown.Count;
            Select(shown[i]);
        }

        // The model under a ray from the camera, or null.
        public Entry Pick(Ray ray)
        {
            Entry best = null;
            float bestD = float.MaxValue;
            foreach (var e in shown)
            {
                if (e.Node == null) continue;
                float top = PlinthHeight + Mathf.Max(0.5f, e.Bounds.max.y, Compare ? e.OriginalHeight : 0f);
                var box = new Bounds(e.Centre + Vector3.up * top * 0.5f, new Vector3(e.Slot.Size.x, top, e.Slot.Size.y));
                if (box.IntersectRay(ray, out float d) && d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        public Ray RayAt(Vector2 pixel, Vector2Int size)
        {
            Aim(size.x / (float)Mathf.Max(1, size.y));
            return Camera.ViewportPointToRay(new Vector3(pixel.x / Mathf.Max(1, size.x), 1f - pixel.y / Mathf.Max(1, size.y), 0f));
        }

        // Drag in free view turns round the focus, in classic view it pans.
        public void Drag(Vector2 delta)
        {
            flyStart = -1;
            var v = View;
            if (v.Classic)
            {
                float k = v.Distance * 0.0022f;
                v.Focus += new Vector3(-delta.x * k, 0f, delta.y * k);
            }
            else
            {
                v.Yaw += delta.x * 0.5f;
                v.Pitch = Mathf.Clamp(v.Pitch + delta.y * 0.4f, -5f, 88f);
            }
            View = v;
            Notify();
        }

        public void Zoom(float wheel)
        {
            flyStart = -1;
            var v = View;
            v.Distance = Mathf.Clamp(v.Distance * (1f + wheel * 0.05f), 3f, 1200f);
            View = v;
            Notify();
        }

        // Points the camera, turns the chosen model, and faces labels and
        // cards to the camera, showing only the labels near enough to read.
        void Aim(float viewAspect)
        {
            if (Camera == null) return;
            aspect = viewAspect > 0 ? viewAspect : aspect;
            var v = View;
            float pitch = v.Classic ? GameCamera.ClassicPitch : v.Pitch, yaw = v.Classic ? 0f : v.Yaw;
            Camera.fieldOfView = StudioView.ClassicFov;
            Camera.aspect = aspect;
            Camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            var pos = v.Focus - Camera.transform.forward * v.Distance;
            if (!v.Classic) pos.y = Mathf.Max(pos.y, 0.5f);
            Camera.transform.position = pos;
            Camera.farClipPlane = Mathf.Max(1500f, v.Distance * 3f);
            if (spun != null && spun != Selected && spun.Spin != null) spun.Spin.transform.localRotation = Quaternion.identity;
            spun = Selected;
            if (Selected?.Spin != null) Selected.Spin.transform.localRotation = !v.Classic && Turning ? Quaternion.Euler(0f, spinYaw, 0f) : Quaternion.identity;
            var rot = Camera.transform.rotation;
            foreach (var e in shown)
            {
                if (e.Label != null)
                {
                    float d = (e.Label.transform.position - pos).magnitude;
                    bool near = d < LabelDistance || e == Selected;
                    if (e.Label.activeSelf != near) e.Label.SetActive(near);
                    if (near)
                    {
                        // Names keep a readable size a little way out.
                        e.Label.transform.rotation = rot;
                        e.Label.transform.localScale = Vector3.one * LabelScale(d);
                    }
                }
                if (e.Card != null && e.Original != null && e.Original.activeInHierarchy)
                {
                    EffectRenderer.Nudged(e.Card.parent.position, Camera.transform, EntityRenderer.CardNudge, out var pivot, out float k);
                    e.Card.SetPositionAndRotation(pivot, rot);
                    e.Card.localScale = Vector3.one * k;
                }
            }
            if (monarchLabel != null)
            {
                monarchLabel.transform.rotation = rot;
                monarchLabel.transform.localScale = Vector3.one * LabelScale((monarchLabel.transform.position - pos).magnitude);
            }
            StudioSession.Stage?.Atmosphere?.Follow(v.Focus, pos.y - v.Focus.y, v.Distance);
        }

        static readonly int DetailId = Shader.PropertyToID("_OkuDetailParams"), MapSizeId = Shader.PropertyToID("_OkuMapSize"), FogOnId = Shader.PropertyToID("_OkuFogOn");

        // Draws the gallery into rt. A map's ground detail, edge band and fog
        // of war are left over in the shader globals from a game, so they are
        // off while the gallery's plain ground draws.
        public bool Render(RenderTexture rt)
        {
            if (Camera == null || rt == null) return false;
            Aim(rt.width / (float)Mathf.Max(1, rt.height));
            var detail = Shader.GetGlobalVector(DetailId);
            var mapSize = Shader.GetGlobalVector(MapSizeId);
            float fogOn = Shader.GetGlobalFloat(FogOnId);
            var was = Camera.targetTexture;
            try
            {
                Shader.SetGlobalVector(DetailId, Vector4.zero);
                Shader.SetGlobalVector(MapSizeId, Vector4.zero);
                Shader.SetGlobalFloat(FogOnId, 0f);
                Camera.targetTexture = rt;
                Camera.Render();
            }
            finally
            {
                Camera.targetTexture = was;
                Shader.SetGlobalVector(DetailId, detail);
                Shader.SetGlobalVector(MapSizeId, mapSize);
                Shader.SetGlobalFloat(FogOnId, fogOn);
            }
            return true;
        }

        // ---- Pieces ----

        GameObject Child(Transform parent, string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(parent, false);
            return go;
        }

        T Own<T>(T o) where T : Object
        {
            o.hideFlags = HideFlags.HideAndDontSave;
            owned.Add(o);
            return o;
        }

        static string Shorten(string folder)
        {
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            string f = folder.Replace('\\', '/');
            return f.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase) ? f.Substring(assets.Length + 1) : folder;
        }

        public string FolderLabel => Folder == null ? "" : Shorten(Folder);

        // A unit cube on its centre.
        static Mesh Cube()
        {
            var m = new Mesh { name = "gallery cube" };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var tris = new List<int>();
            void Face(Vector3 normal, Vector3 u, Vector3 w)
            {
                int s = v.Count;
                var c = normal * 0.5f;
                v.Add(c - u * 0.5f - w * 0.5f); v.Add(c - u * 0.5f + w * 0.5f); v.Add(c + u * 0.5f + w * 0.5f); v.Add(c + u * 0.5f - w * 0.5f);
                for (int i = 0; i < 4; i++) n.Add(normal);
                tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
            Face(Vector3.up, Vector3.right, Vector3.forward);
            Face(Vector3.down, Vector3.forward, Vector3.right);
            Face(Vector3.right, Vector3.forward, Vector3.up);
            Face(Vector3.left, Vector3.up, Vector3.forward);
            Face(Vector3.forward, Vector3.up, Vector3.right);
            Face(Vector3.back, Vector3.right, Vector3.up);
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh CardQuad()
        {
            var m = new Mesh { name = "gallery card" };
            m.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            // Cards light like the ground under them, as in the game.
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = new[] { 0, 3, 2, 0, 2, 1 };
            m.RecalculateBounds();
            return m;
        }

        // ---- Housekeeping ----

        // Everything made, gone. The folder and settings stay.
        public void ClearWorld()
        {
            scan = null;
            buildQueue.Clear();
            resolveQueue.Clear();
            foreach (var e in Entries)
            {
                if (e.Model != null) StudioModel.DisposeTemplate(e.Model);
                e.Model = null;
                DropOriginal(e);
            }
            Entries.Clear();
            shown.Clear();
            loadedBytes = 0;
            Selected = null;
            spun = null;
            if (Root != null) Object.DestroyImmediate(Root);
            Root = null;
            Camera = null;
            monarch = monarchNode = monarchLabel = ground = null;
            foreach (var o in owned) if (o != null) Object.DestroyImmediate(o);
            owned.Clear();
            groundMat = null;
            groundTex = null;
            features = cards = units = null;
            Progress = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            ClearWorld();
            disposed = true;
            Notify();
        }
    }
}
