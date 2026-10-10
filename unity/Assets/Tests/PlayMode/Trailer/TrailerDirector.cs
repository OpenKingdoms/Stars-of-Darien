// TrailerDirector.cs - films the Stars of Darien trailer on the real engine
// without a window: each scene sets up a skirmish through the engine, puts
// armies down, gives orders through IGameBackend and films shots with a
// still or slowly moving camera. Every frame is drawn into a render
// texture at a fixed 60 a second, piped to ffmpeg, and the sounds the game
// would play go to a log beside each shot for the offline mixer.
// TrailerScenes.cs holds the scenes, TrailerCaptures.cs the entry point.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OpenKingdomsUnity.Tests.Trailer
{
    public sealed partial class TrailerDirector
    {
        // The frame's size, 1080p unless a run asks for another before Boot.
        public static int W = 1920, H = 1080;
        public const int Fps = 60;
        // Stills saved from each shot for review and contact sheets, evenly spaced.
        public int StillsPerShot = 5;
        // Called with the frame's index after each frame of a shot is read back, while FrameTexture holds it.
        public Action<int> AfterFrame;
        public Texture2D FrameTexture => tex;

        public readonly string OutDir, Ffmpeg;
        public GameRoot Root { get; private set; }
        public IGameBackend B => Root.Backend;
        public GameCamera Gc => Root.World.Camera;
        public Camera Cam => Root.World.Camera.GetComponent<Camera>();
        public MapInfo Map { get; private set; }
        public int Frame { get; private set; }
        public SoundTap Tap { get; private set; }

        readonly List<string> report = new List<string>();
        RenderTexture rt;
        Texture2D tex;
        FrameSink sink;
        string shotName;
        bool hudShown;

        public TrailerDirector(string outDir, string ffmpeg)
        {
            OutDir = outDir;
            Ffmpeg = ffmpeg;
        }

        public void Note(string line)
        {
            report.Add(line);
            Debug.Log("Trailer: " + line);
            try { File.AppendAllText(Path.Combine(OutDir, "director.log"), DateTime.Now.ToString("HH:mm:ss ") + line + "\n"); }
            catch (IOException) { }
        }

        // ---- The run ----

        public IEnumerator Run(string which) => Run(which, Scenes);

        public IEnumerator Run(string which, (string Name, Func<TrailerDirector, IEnumerator> Run)[] scenes)
        {
            Directory.CreateDirectory(OutDir);
            var want = new HashSet<string>((which ?? "all").Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0));
            yield return Boot();
            foreach (var (name, scene) in scenes)
            {
                if (!want.Contains("all") && !want.Contains(name)) continue;
                // "all" films the shots: probes, the census and the measures run by name.
                if (want.Contains("all") && (name.StartsWith("probe") || name.StartsWith("perf") || name == "census")) continue;
                Note($"scene {name}");
                float began = Time.realtimeSinceStartup;
                yield return Safe(name, scene(this));
                EndShot();
                Note($"scene {name} done in {Time.realtimeSinceStartup - began:0}s");
            }
            Time.captureFramerate = 0;
            if (rt != null) { rt.Release(); Object.Destroy(rt); }
            if (tex != null) Object.Destroy(tex);
        }

        // Steps a scene and every coroutine it starts, so one that throws
        // ends only that scene and the rest are still filmed.
        IEnumerator Safe(string name, IEnumerator scene)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(scene);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                bool more;
                try { more = top.MoveNext(); }
                catch (Exception e)
                {
                    Note($"scene {name} failed: {e}");
                    yield break;
                }
                if (!more) { stack.Pop(); continue; }
                if (top.Current is IEnumerator inner) stack.Push(inner);
                else yield return top.Current;
            }
        }

        IEnumerator Boot()
        {
            // The engine's own view boots into the test scene, and the engine
            // runs one game at a time.
            var view = GameObject.Find("OpenKingdoms");
            if (view != null) Object.Destroy(view);
            yield return null;
            yield return null;
            MenuScreens.SizeOverride = new Vector2Int(W, H);
            BattleHud.SizeOverride = new Vector2Int(W, H);
            FogView.Disabled = true;
            // The menus as the Alpha 1 build shows them.
            BuildStamp.Version = "Alpha 1";
            BuildStamp.SkirmishOnly = true;
            BuildStamp.MapEditor = false;
            Root = GameRoot.Boot();
            yield return null;
            Root.Options.Shadows = true;
            Root.Options.PostEffects = true;
            Root.Options.GameSpeed = 1;
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { name = "Trailer frame" };
            rt.Create();
            tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            Time.captureFramerate = Fps;
            Note($"booted on {B.Name}: {B.Maps.Count} maps, {B.UnitDefs.Count} units, tick rate {B.TicksPerSecond}");
        }

        public void TearDown()
        {
            EndShot();
            Time.captureFramerate = 0;
            MenuScreens.SizeOverride = null;
            BattleHud.SizeOverride = null;
            FogView.Disabled = false;
            BuildStamp.Reset();
            if (Root != null) Object.Destroy(Root.gameObject);
            Root = null;
        }

        // ---- A battle ----

        public struct Seat
        {
            public string Side;
            public SeatKind Kind;
            public int Team, Start;
            public AiDifficulty Difficulty;

            public static Seat You(string side, int start = -1) => new Seat { Side = side, Kind = SeatKind.Human, Team = 0, Start = start };
            public static Seat Ai(string side, int team, int start = -1, AiDifficulty d = AiDifficulty.Easy) =>
                new Seat { Side = side, Kind = SeatKind.Computer, Team = team, Start = start, Difficulty = d };
        }

        public MapInfo FindMap(string want)
        {
            want = want.Trim().ToLowerInvariant();
            return B.Maps.FirstOrDefault(m => m.Id.ToLowerInvariant() == want || (m.Name ?? "").ToLowerInvariant() == want)
                ?? B.Maps.FirstOrDefault(m => (m.Name ?? m.Id).ToLowerInvariant().Contains(want));
        }

        public bool Playing => Root.Flow.State == FlowState.Playing && Root.World != null;

        // A skirmish on map with these seats, the whole map shown and line of
        // sight off, then the world made ready for filming.
        public IEnumerator Battle(string map, WeatherChoice weather, int mana, params Seat[] seats)
        {
            EndShot();
            var info = FindMap(map);
            if (info == null) throw new InvalidOperationException($"no map {map}");
            Map = info;
            if (Root.Flow.State == FlowState.Playing) Root.Flow.Fire(FlowEvent.Pause);
            if (Root.Flow.State != FlowState.MainMenu) Root.Flow.Fire(FlowEvent.ToMenu);
            yield return null;
            Root.Flow.Fire(FlowEvent.OpenSkirmish);
            var s = Root.Setup;
            s.MapId = info.Id;
            s.MapRevealed = true;
            s.LineOfSight = false;
            s.StartMana = mana;
            s.UnitLimit = 1000;
            s.MonarchExpendable = true;
            s.RandomStarts = false;
            s.Seats.Clear();
            for (int i = 0; i < seats.Length; i++)
                s.Seats.Add(new SeatSetup { Kind = seats[i].Kind, Side = seats[i].Side, Colour = i, Team = seats[i].Team, Start = seats[i].Start, Difficulty = seats[i].Difficulty });
            Root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 300f;
            while (Root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            if (!Playing) throw new InvalidOperationException($"{info.Name} did not start: {Root.LastError}");
            Root.Orders.Frozen = true;
            Root.Screens.Screen("Hud")?.SetActive(false);
            Root.World.Fog?.Update(true);
            Root.World.Atmosphere.SetWeather(weather);
            var gc = Gc;
            gc.keyboard = false;
            gc.edgePan = false;
            gc.minPitch = 2f;
            gc.maxPitch = 89f;
            gc.minDistance = 2f;
            gc.maxDistance = 900f;
            B.Select(Array.Empty<int>(), false);
            Root.World.Entities.Selected.Clear();
            for (int i = 0; i < 10; i++) yield return null;
            if (!defsWritten) WriteDefs(Path.Combine(OutDir, "survey"));
            Note($"battle on {info.Name} ({info.Id}, {info.Climate}, {info.Size.x}x{info.Size.y}), players " +
                 string.Join(", ", B.Players.Select(p => $"{p.Index}:{p.Side}{(p.IsLocal ? " (you)" : "")} team {p.Team}")) +
                 $", starts {string.Join(" ", info.Starts.Select(v => $"({v.x:0},{v.y:0})"))}");
        }

        // The engine's player id for a seat.
        public int PlayerOf(int seat) => B.Players[seat].Index;

        public int Def(string name)
        {
            foreach (var d in B.UnitDefs)
                if (string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(d.ObjectName, name, StringComparison.OrdinalIgnoreCase))
                    return d.Id;
            return -1;
        }

        // Whole units set down by a player's start, ring by ring.
        public List<int> Place(int player, string unit, int count)
        {
            var list = new List<int>();
            int def = Def(unit);
            if (def < 0) { Note($"no unit {unit}"); return list; }
            for (int i = 0; i < count; i++)
            {
                int h = OkEngine.okx_place_unit(def, player);
                if (h < 0) { Note($"{unit} for player {player}: only {i} of {count} found room"); break; }
                list.Add(h);
            }
            return list;
        }

        readonly UnitState[] unitBuf = new UnitState[8192];

        public List<UnitState> Units(Func<UnitState, bool> keep = null)
        {
            int n = B.ReadUnits(unitBuf);
            var list = new List<UnitState>(n);
            for (int i = 0; i < n; i++)
                if ((unitBuf[i].Flags & UnitFlags.Dying) == 0 && (keep == null || keep(unitBuf[i]))) list.Add(unitBuf[i]);
            return list;
        }

        public List<UnitState> Of(IEnumerable<int> handles)
        {
            var set = new HashSet<int>(handles);
            return Units(u => set.Contains(u.Handle));
        }

        public static Vector3 Centre(IEnumerable<UnitState> units)
        {
            var c = Vector3.zero;
            int n = 0;
            foreach (var u in units) { c += u.Position; n++; }
            return n > 0 ? c / n : Vector3.zero;
        }

        public Vector3 Ground(float x, float z) => new Vector3(x, B.GroundHeight(x, z), z);

        public Vector3 Start(int seat) => Ground(Map.Starts[seat].x, -Map.Starts[seat].y);

        // Runs the battle on without filming, a burst of ticks a frame.
        public IEnumerator FastForward(int ticks, int perFrame = 120)
        {
            for (int done = 0; done < ticks && Playing;)
            {
                done += B.Advance(Mathf.Min(perFrame, ticks - done));
                yield return null;
                WatchFrame();
            }
        }

        // A formation move for the player's own units: each to its own spot,
        // all at the slowest one's pace, facing heading on arrival.
        public bool March(IList<int> handles, Vector3 centre, float heading, int perRow, float spacing)
        {
            var targets = Rows(handles.Count, centre, heading, perRow, spacing);
            return B.MoveFormation(handles.ToArray(), targets, heading, true, false);
        }

        // Ranks across a heading (degrees, 0 north, clockwise), front row first.
        public static Vector2[] Rows(int count, Vector3 centre, float heading, int perRow, float spacing)
        {
            var f = Quaternion.Euler(0, heading, 0) * Vector3.forward;
            var r = Quaternion.Euler(0, heading, 0) * Vector3.right;
            var list = new Vector2[count];
            int rows = Mathf.CeilToInt(count / (float)perRow);
            for (int i = 0; i < count; i++)
            {
                int row = i / perRow, col = i % perRow;
                int inRow = Mathf.Min(perRow, count - row * perRow);
                var p = centre + r * ((col - (inRow - 1) * 0.5f) * spacing) - f * ((row - (rows - 1) * 0.5f) * spacing);
                list[i] = new Vector2(p.x, p.z);
            }
            return list;
        }

        public void Attack(IEnumerable<int> handles, Vector3 at)
        {
            foreach (int h in handles)
                B.Command(GameCommand.To(CommandKind.Patrol, h, at));
        }

        // ---- Filming ----

        // Raises every pose's focus above the ground, for subjects in the air.
        public float Lift;

        public void Pose(ShotPose p)
        {
            var gc = Gc;
            var focus = p.Focus;
            focus.y = gc.SmoothGround(focus) + Lift;
            var sea = B.Terrain;
            if (sea != null && sea.SeaLevel > focus.y) focus.y = sea.SeaLevel;
            gc.focus = focus;
            gc.pitch = p.Pitch;
            gc.yaw = p.Yaw;
            gc.Zoom(p.Distance);
        }

        // A pose that looks at `at` from `distance` away, `pitch` down, facing yaw.
        public ShotPose Look(Vector3 at, float distance, float pitch, float yaw) => new ShotPose(at, distance, pitch, yaw);

        // A shot of `seconds`, the camera from the path at each moment (0 to 1),
        // and `each` run before every frame with its index, for timed orders.
        public IEnumerator Shot(string name, float seconds, Func<float, ShotPose> path, Action<int> each = null, bool hud = false)
        {
            if (!Playing) throw new InvalidOperationException($"{name}: no battle");
            int frames = Mathf.Max(1, Mathf.RoundToInt(seconds * Fps));
            ShowHud(hud);
            // Bars over the damaged show only as a player sees the field, with the HUD.
            Root.World.Entities.HideBars = !hud;
            Pose(path(0));
            for (int i = 0; i < 6; i++) yield return null;
            WarmWeather();
            BeginShot(name);
            var stills = new HashSet<int>();
            for (int k = 0; k < StillsPerShot; k++) stills.Add(StillsPerShot > 1 ? (frames - 1) * k / (StillsPerShot - 1) : 0);
            for (int f = 0; f < frames; f++)
            {
                Frame = f;
                each?.Invoke(f);
                Pose(path(frames > 1 ? f / (float)(frames - 1) : 0f));
                yield return null;
                if (!Playing) throw new InvalidOperationException($"{name}: the battle ended at frame {f}");
                if (!hud) Cam.rect = new Rect(0, 0, 1, 1);
                if (hud) GrabWithHud(); else GrabWorld();
                sink.Write(tex.GetRawTextureData<byte>());
                Tap?.Frame(f);
                AfterFrame?.Invoke(f);
                WatchFrame();
                if (stills.Contains(f)) Still(f);
            }
            EndShot();
            ShowHud(false);
        }

        // Rain, snow and mist fill the air over the camera before the first
        // frame, rather than starting to fall as the shot begins.
        void WarmWeather()
        {
            var w = Root.World.Atmosphere.Weather;
            if (w != WeatherChoice.Rain && w != WeatherChoice.Snow && w != WeatherChoice.Fog) return;
            var ps = GameObject.Find("Weather")?.GetComponent<ParticleSystem>();
            if (ps == null) return;
            ps.Simulate(w == WeatherChoice.Rain ? 2f : 14f, true, false, true);
            ps.Play(true);
        }

        // A shot of the menus, which have no world: every canvas drawn by a
        // camera of its own over black.
        public IEnumerator MenuShot(string name, float seconds, Action<int> each = null)
        {
            EndShot();
            int frames = Mathf.Max(1, Mathf.RoundToInt(seconds * Fps));
            var go = new GameObject("Trailer UI camera");
            go.transform.position = new Vector3(0, -100000f, 0);
            var ui = go.AddComponent<Camera>();
            ui.enabled = false;
            ui.orthographic = true;
            ui.nearClipPlane = 0.1f;
            ui.farClipPlane = 50f;
            ui.clearFlags = CameraClearFlags.SolidColor;
            ui.backgroundColor = Color.black;
            ui.targetTexture = rt;
            var moved = TakeCanvases(ui);
            try
            {
                for (int i = 0; i < 4; i++) yield return null;
                BeginShot(name, false);
                var stills = new HashSet<int> { 0, frames / 2, frames - 1 };
                for (int f = 0; f < frames; f++)
                {
                    Frame = f;
                    each?.Invoke(f);
                    yield return null;
                    // A screen shown since the last frame comes with new canvases.
                    moved.AddRange(TakeCanvases(ui));
                    Canvas.ForceUpdateCanvases();
                    ui.Render();
                    Read(rt);
                    sink.Write(tex.GetRawTextureData<byte>());
                    if (stills.Contains(f)) Still(f);
                }
                EndShot();
            }
            finally
            {
                GiveBack(moved);
                Object.Destroy(go);
            }
        }

        List<(Canvas c, RenderMode mode, Camera cam, float plane)> TakeCanvases(Camera ui)
        {
            var list = new List<(Canvas, RenderMode, Camera, float)>();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas && c.isActiveAndEnabled && c.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    list.Add((c, c.renderMode, c.worldCamera, c.planeDistance));
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = ui;
                    c.planeDistance = 10f;
                }
            return list;
        }

        static void GiveBack(List<(Canvas c, RenderMode mode, Camera cam, float plane)> moved)
        {
            foreach (var e in moved)
            {
                if (e.c == null) continue;
                e.c.renderMode = e.mode;
                e.c.worldCamera = e.cam;
                e.c.planeDistance = e.plane;
            }
        }

        void ShowHud(bool on)
        {
            if (!Playing) return;
            Root.Screens.Screen("Hud")?.SetActive(on);
            hudShown = on;
            if (!on) Cam.rect = new Rect(0, 0, 1, 1);
        }

        void BeginShot(string name, bool sound = true)
        {
            EndShot();
            shotName = name;
            Directory.CreateDirectory(Path.Combine(OutDir, "shots"));
            sink = new FrameSink(Ffmpeg, Path.Combine(OutDir, "shots", name + ".mp4"), W, H, Fps);
            Tap = sound && Playing ? new SoundTap(B, Cam, Path.Combine(OutDir, "shots", name + ".sounds.tsv")) : null;
            if (!sound) File.WriteAllText(Path.Combine(OutDir, "shots", name + ".sounds.tsv"), "# no sound\n");
            Note($"shot {name} begins at tick {(Playing ? B.Tick : 0)}, unscaled step {Time.unscaledDeltaTime:0.####}");
        }

        void EndShot()
        {
            if (sink != null)
            {
                int frames = sink.Frames;
                sink.Dispose();
                string err = sink.Errors.Trim();
                Note($"shot {shotName}: {frames} frames, ffmpeg exit {sink.ExitCode}{(err.Length > 0 ? ", " + err : "")}, {Tap?.Lines ?? 0} sound lines");
                File.WriteAllText(Path.Combine(OutDir, "shots", shotName + ".frames"), frames.ToString());
                sink = null;
            }
            Tap?.Dispose();
            Tap = null;
        }

        void GrabWorld()
        {
            var cam = Cam;
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = old;
            Read(rt);
        }

        void Read(RenderTexture from)
        {
            RenderTexture.active = from;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
            tex.Apply(false);
            RenderTexture.active = null;
        }

        // The world through its camera and every canvas over it, the canvases
        // drawn over black and over white to find each pixel's coverage, as
        // HudShots does for the HUD pictures.
        void GrabWithHud()
        {
            GrabWorld();
            var under = tex.GetRawTextureData<byte>().ToArray();
            var go = new GameObject("Trailer HUD camera");
            go.transform.position = new Vector3(0, -100000f, 0);
            var ui = go.AddComponent<Camera>();
            ui.enabled = false;
            ui.orthographic = true;
            ui.nearClipPlane = 0.1f;
            ui.farClipPlane = 50f;
            ui.allowHDR = false;
            ui.allowMSAA = false;
            ui.clearFlags = CameraClearFlags.SolidColor;
            ui.targetTexture = rt;
            var moved = TakeCanvases(ui);
            Canvas.ForceUpdateCanvases();
            ui.backgroundColor = new Color(0, 0, 0, 0);
            ui.Render();
            Read(rt);
            var black = tex.GetRawTextureData<byte>().ToArray();
            ui.backgroundColor = Color.white;
            ui.Render();
            Read(rt);
            GiveBack(moved);
            Object.Destroy(go);
            var white = tex.GetRawTextureData<byte>();
            var outPx = new byte[under.Length];
            for (int i = 0; i < under.Length; i++)
            {
                int bk = black[i], cover = 255 - Mathf.Max(0, white[i] - bk);
                outPx[i] = (byte)Mathf.Min(255, bk + under[i] * (255 - cover) / 255);
            }
            tex.LoadRawTextureData(outPx);
            tex.Apply(false);
        }

        // A picture of the frame for looking over the shot and for the stills.
        void Still(int f)
        {
            string dir = Path.Combine(OutDir, "review");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"{shotName}-{f:0000}.png"), tex.EncodeToPNG());
        }

        // ---- What the engine has ----

        bool defsWritten;

        // Unit and feature definitions, which the engine has only in a battle.
        void WriteDefs(string dir)
        {
            Directory.CreateDirectory(dir);
            var units = new List<string> { "id	name	title	side	building	fly	hover	float	speed	cost	health	options	animations	category" };
            foreach (var d in B.UnitDefs)
                units.Add($"{d.Id}	{d.Name}	{d.Title}	{d.Side}	{(d.IsBuilding ? 1 : 0)}	{(d.CanFly ? 1 : 0)}	{(d.Hovers ? 1 : 0)}	{d.Float}	{d.MaxSpeed:0.##}	{d.ManaCost}	{d.MaxHealth}	{d.BuildOptions.Length}	{string.Join(" ", d.Animations)}	{d.Category}");
            File.WriteAllLines(Path.Combine(dir, "units.tsv"), units);
            File.WriteAllLines(Path.Combine(dir, "features.tsv"), B.FeatureDefs.Select(f => $"{f.Id}	{f.Name}	{f.ObjectName}	{f.SequenceName}	{f.Category}	{f.Height:0.#}"));
            defsWritten = true;
        }

        public void Survey(string dir)
        {
            Directory.CreateDirectory(dir);
            var maps = new List<string> { "id\tname\tclimate\tsize\tplayers\tstarts" };
            foreach (var m in B.Maps)
                maps.Add($"{m.Id}\t{m.Name}\t{m.Climate}\t{m.Size.x:0}x{m.Size.y:0}\t{m.MaxPlayers}\t{string.Join(" ", m.Starts.Select(v => $"{v.x:0},{v.y:0}"))}");
            File.WriteAllLines(Path.Combine(dir, "maps.tsv"), maps);
            var units = new List<string> { "id\tname\ttitle\tside\tbuilding\tfly\thover\tfloat\tspeed\tcost\thealth\toptions\tanimations\tcategory" };
            foreach (var d in B.UnitDefs)
                units.Add($"{d.Id}\t{d.Name}\t{d.Title}\t{d.Side}\t{(d.IsBuilding ? 1 : 0)}\t{(d.CanFly ? 1 : 0)}\t{(d.Hovers ? 1 : 0)}\t{d.Float}\t{d.MaxSpeed:0.##}\t{d.ManaCost}\t{d.MaxHealth}\t{d.BuildOptions.Length}\t{string.Join(" ", d.Animations)}\t{d.Category}");
            File.WriteAllLines(Path.Combine(dir, "units.tsv"), units);
            File.WriteAllLines(Path.Combine(dir, "sides.tsv"), B.Sides.Select(s => $"{s.Id}\t{s.Name}"));
            File.WriteAllLines(Path.Combine(dir, "features.tsv"), B.FeatureDefs.Select(f => $"{f.Id}\t{f.Name}\t{f.ObjectName}\t{f.SequenceName}\t{f.Category}\t{f.Height:0.#}"));
            // Map pictures from the player's own files, for choosing. They stay on this machine.
            string pics = Path.Combine(dir, "previews");
            Directory.CreateDirectory(pics);
            foreach (var m in B.Maps)
            {
                var img = B.MapPreview(m.Id, 384);
                if (img == null) continue;
                var flipped = new byte[img.Pixels.Length];
                int row = img.Width * 4;
                for (int y = 0; y < img.Height; y++) Buffer.BlockCopy(img.Pixels, y * row, flipped, (img.Height - 1 - y) * row, row);
                var png = ImageConversion.EncodeArrayToPNG(flipped, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, (uint)img.Width, (uint)img.Height);
                string file = new string(m.Id.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
                File.WriteAllBytes(Path.Combine(pics, file + ".png"), png);
            }
            Note($"survey: {B.Maps.Count} maps, {B.UnitDefs.Count} units, {B.FeatureDefs.Count} features in {dir}");
        }
    }
}
