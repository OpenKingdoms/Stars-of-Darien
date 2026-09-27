// StudioSession.cs - Studio Mode's state and actions: the stage, the loaded
// model and its hot reload, the target, checks, fixes, tweaks, Use in game
// and screenshots. The windows only draw this.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEditor;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    [InitializeOnLoad]
    public static class StudioSession
    {
        public static StudioStage Stage { get; private set; }
        public static StudioModel Model { get; private set; }
        public static StudioTarget Target { get; private set; } = new StudioTarget();
        public static StudioFix Fix { get; private set; } = StudioFix.None;
        public static MaterialTweaks Tweaks { get; private set; } = new MaterialTweaks();
        public static List<ModelCheck.Issue> Issues { get; private set; } = new List<ModelCheck.Issue>();
        public static StudioView View = StudioView.ClassicDefault;
        public static StudioView Free = StudioView.FreeDefault;
        public static bool Turning = true, KeepTweaks = true;
        public static string Status { get; private set; } = "";
        public static string LoadedMap { get; private set; }
        public static string[] OriginalPieces { get; private set; } = Array.Empty<string>();
        // Why the original is not on the stage, or null when it is.
        public static string OriginalMissing { get; private set; }
        public static event Action Changed;

        static Bounds? originalBounds;
        static Texture2D spriteTex;
        static double lastTick, lastPoll, lastDropScan;
        static HashSet<string> dropSeen;
        // Whether this script domain has read back the saved state yet. Until
        // it has, nothing it holds is written over what was saved.
        static bool restored;

        public static bool Active => Stage != null && Stage.Root != null;

        // ---- The look, kept between sessions ----

        public static readonly string[] Climates = { "grass", "snow", "desert", "swamp", "volcanic" };
        public static string MapChoice { get => EditorPrefs.GetString("oku.studio.map", "?"); set => EditorPrefs.SetString("oku.studio.map", value); }
        public static string ClimateChoice { get => EditorPrefs.GetString("oku.studio.climate", ""); set => EditorPrefs.SetString("oku.studio.climate", value); }
        public static WeatherChoice Weather { get => (WeatherChoice)EditorPrefs.GetInt("oku.studio.weather", (int)WeatherChoice.Off); set => EditorPrefs.SetInt("oku.studio.weather", (int)value); }
        public static TimeOfDay Time { get => (TimeOfDay)EditorPrefs.GetInt("oku.studio.time", 0); set => EditorPrefs.SetInt("oku.studio.time", (int)value); }
        public static bool Sea { get => EditorPrefs.GetBool("oku.studio.sea", true); set => EditorPrefs.SetBool("oku.studio.sea", value); }
        public static bool Shadows { get => EditorPrefs.GetBool("oku.studio.shadows", true); set => EditorPrefs.SetBool("oku.studio.shadows", value); }
        public static int TeamColour { get => EditorPrefs.GetInt("oku.studio.team", 0); set => EditorPrefs.SetInt("oku.studio.team", value); }
        public static Color Team => (Color)MockBackend.Palette[Mathf.Abs(TeamColour) % MockBackend.Palette.Length];

        // "" for the neutral ground, "?" for the backend's choice: the first
        // real map when game files are there, the neutral ground on the stand-in.
        static bool WantsRealGround(IGameBackend b) =>
            MapChoice == "?" ? b != null && b.Name != "Mock" : MapChoice.Length > 0;

        public static string Climate(IGameBackend b)
        {
            if (ClimateChoice.Length > 0) return ClimateChoice;
            var m = b?.Maps.FirstOrDefault(x => x.Id == LoadedMap);
            return WantsRealGround(b) && m != null && !string.IsNullOrEmpty(m.Climate) ? m.Climate : "grass";
        }

        static StudioSession()
        {
            EditorApplication.update += Tick;
            // Another backend, from Settings or the stand-in toggle, rebuilds
            // the stage on it, so the lists and the stage agree.
            StudioBackend.Changed += () =>
            {
                if (!Active) return;
                EditorApplication.delayCall += () =>
                {
                    if (StudioMode.IsOn && !EditorApplication.isPlayingOrWillChangePlaymode && StudioBackend.Get() != Stage?.Backend) Start(false);
                };
            };
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
        }

        // The stage and the model go, and their state stays in SessionState.
        static void BeforeReload()
        {
            Stop();
            Model?.Dispose();
            Model = null;
        }

        // ---- Starting and stopping ----

        // Builds the stage in the open scene. Returns false with Status set
        // when the studio cannot run now.
        public static bool Start(bool progress)
        {
            Stop();
            StudioStage.RemoveStray();
            var b = StudioBackend.Get();
            if (b == null) { Status = "The studio pauses while the game plays."; Notify(); return false; }
            if (!restored) Restore(b);
            LoadMap(b, progress);
            var stage = new StudioStage();
            try
            {
                stage.Build(b, WantsRealGround(b) && b.Terrain != null, Climate(b), Weather, Time, Sea, Shadows);
            }
            catch (Exception e)
            {
                stage.Dispose();
                Status = "The stage did not build: " + e.Message;
                Debug.LogException(e);
                Notify();
                return false;
            }
            Stage = stage;
            lastTick = 0;
            Stage.SetMonarch(MonarchModel(b), Team);
            RefreshOriginal();
            if (Model != null) Stage.SetModel(Model, Fix, Tweaks, Team);
            Recheck();
            Status = Model == null ? "Drop a model on Studio Drop or on the Studio View to begin." : "Showing " + Path.GetFileName(Model.SourcePath) + ".";
            Notify();
            return true;
        }

        public static void Stop()
        {
            if (Active) Remember();
            Stage?.Dispose();
            Stage = null;
            if (spriteTex != null) UnityEngine.Object.DestroyImmediate(spriteTex);
            spriteTex = null;
        }

        // Every map the backend offers, loaded into it so its units, features
        // and ground are known. Always one, even for the neutral ground.
        static void LoadMap(IGameBackend b, bool progress)
        {
            string want = WantsRealGround(b) && MapChoice != "?" ? MapChoice : null;
            if (want == null || b.Maps.All(m => m.Id != want)) want = b.Maps.Count > 0 ? b.Maps[0].Id : null;
            if (want == null) return;
            if (LoadedMap == want && b.Status == GameStatus.Running && b.Terrain != null) return;
            LoadedMap = null;
            var setup = GameRoot.DefaultSetup(b);
            setup.MapId = want;
            setup.Seed = 1;
            b.StartSkirmish(setup);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                while (clock.Elapsed.TotalSeconds < 300)
                {
                    var p = b.PumpLoading();
                    if (p.Failed) { Status = "The map did not load: " + p.Error; return; }
                    if (p.Done) { LoadedMap = want; return; }
                    if (progress && !Application.isBatchMode)
                        EditorUtility.DisplayProgressBar("Studio Mode", "Loading " + want + ": " + p.Stage, p.Fraction);
                }
            }
            finally { if (progress) EditorUtility.ClearProgressBar(); }
        }

        // A real monarch from the game, or null for the 4-cell stand-in, which
        // the stand-in world always gets since its units are not to scale.
        static PresentedModel MonarchModel(IGameBackend b)
        {
            if (b == null || b.Name == "Mock") return null;
            var def = StudioTargets.Monarch(b);
            if (def == null) return null;
            int id = b.LoadModel(def.ObjectName, TeamColour);
            return StudioBackend.Models?.Get(id);
        }

        // ---- The model ----

        public static bool LoadModel(string path)
        {
            string full = StudioModel.Absolute(path);
            var m = StudioModel.Load(full, out var error);
            if (m == null)
            {
                // A broken export is tried again only once its file changes again.
                if (Model != null && SamePath(Model.SourcePath, full)) Model.MarkRead();
                Status = "That model did not load: " + error;
                Notify();
                return false;
            }
            bool same = Model != null && SamePath(Model.SourcePath, m.SourcePath);
            Model?.Dispose();
            Model = m;
            if (!same) { Fix = StudioFix.None; GuessTarget(m.Name); }
            Stage?.SetModel(Model, Fix, Tweaks, Team);
            SessionState.SetString("oku.studio.model", m.SourcePath);
            EditorPrefs.SetString("oku.studio.lastModel", m.SourcePath);
            Recheck();
            Remember();
            Status = (same ? "Reloaded " : "Loaded ") + Path.GetFileName(m.SourcePath) + (m.Notes.Count > 0 ? ". " + string.Join(" ", m.Notes) : "");
            Notify();
            return true;
        }

        static bool SamePath(string a, string b) =>
            a != null && b != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        public static string SamplePath => Path.Combine(StudioModel.ProjectDir, "Assets/Game/Studio/Samples/SampleWell.glb");

        // What to fill the typed-in name with when a model's name matched
        // nothing the backend knows.
        public static string SuggestedName { get; private set; } = "";

        // A model named like something the game has aims at it at once.
        static void GuessTarget(string name)
        {
            if (Target.Kind != TargetKind.None) return;
            var b = StudioBackend.Get();
            var hit = StudioTargets.Features(b, StudioTargets.Catalog()).FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? StudioTargets.Units(b, true).FirstOrDefault(t => string.Equals(t.ObjectName, name, StringComparison.OrdinalIgnoreCase) && t.UnitDef >= 0)
                ?? StudioTargets.Units(b, false).FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(t.ObjectName, name, StringComparison.OrdinalIgnoreCase));
            SuggestedName = hit == null ? name : "";
            if (hit != null) SetTarget(hit);
        }

        public static void SetTarget(StudioTarget t)
        {
            Target = t?.Clone() ?? new StudioTarget();
            RefreshOriginal();
            Recheck();
            Remember();
            Notify();
        }

        public static void SetCardPiece(string piece, string texture = null)
        {
            if (Target.Kind != TargetKind.UnitCard) return;
            Target.ReplacesPiece = piece ?? "";
            if (texture != null) Target.ReplacesTexture = texture;
            RefreshOriginal();
            Recheck();
            Remember();
            Notify();
        }

        // The original beside the model: its 3D model, or its picture as the
        // game draws it, from the loaded map, from one placed out of sight
        // for a moment, or from the sprite catalog.
        static void RefreshOriginal()
        {
            originalBounds = null;
            OriginalPieces = Array.Empty<string>();
            OriginalMissing = null;
            if (spriteTex != null) UnityEngine.Object.DestroyImmediate(spriteTex);
            spriteTex = null;
            var b = StudioBackend.Get();
            PresentedModel pm = null;
            var rect = default(Rect);
            var t = Target;
            t.DrawnHeight = 0;
            t.DrawnFrom = null;
            if (b != null && t.Kind != TargetKind.None)
            {
                string obj = t.ObjectName;
                if (!string.IsNullOrEmpty(obj))
                {
                    int id = b.LoadModel(obj, TeamColour);
                    pm = StudioBackend.Models?.Get(id);
                    if (pm != null)
                    {
                        originalBounds = pm.RestBounds;
                        OriginalPieces = pm.Data.Pieces.Select(p => p.Name).Where(n => !string.IsNullOrEmpty(n)).ToArray();
                        if (!t.IsUnit) { t.DrawnHeight = pm.RestBounds.size.y; t.DrawnFrom = "model"; }
                    }
                }
                if (pm == null && t.Kind == TargetKind.Feature)
                {
                    if (FromMap(b, t, out rect) || Placed(b, t, out rect)) { t.DrawnHeight = rect.height; t.DrawnFrom = "map"; }
                    else if (t.SpriteFile != null && File.Exists(t.SpriteFile) && t.SpriteSize.y > 0)
                    {
                        spriteTex = new Texture2D(2, 2) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
                        spriteTex.LoadImage(File.ReadAllBytes(t.SpriteFile));
                        // One pixel is a sixteenth of a cell across and an eighth
                        // of a cell up, as the original's view draws height.
                        rect = new Rect(-t.Hotspot.x / 16f, -(t.SpriteSize.y - t.Hotspot.y) / 8f, t.SpriteSize.x / 16f, t.SpriteSize.y / 8f);
                        t.DrawnHeight = rect.height;
                        t.DrawnFrom = "picture";
                    }
                    else if (t.Typed && t.Height > 0) { t.DrawnHeight = t.Height; t.DrawnFrom = "typed"; }
                    else OriginalMissing = t.Typed ? "The original isn't in the game files the studio has, so only your numbers are there to go by."
                        : "The original isn't on this map. Pick a Ground map where it stands, under Look.";
                }
                else if (pm == null && t.IsUnit)
                    OriginalMissing = b.Name == "Mock" || t.UnitDef < 0 ? "The original model is only there with the game installed." : "The original model did not load.";
                if (t.Kind == TargetKind.UnitCard && pm != null) t.ReplacesPiece = StudioTargets.CardPiece(OriginalPieces, t.ReplacesPiece);
            }
            Stage?.SetOriginal(t, pm, spriteTex, rect);
        }

        static bool FromMap(IGameBackend b, StudioTarget t, out Rect rect)
        {
            rect = default;
            if (t.FeatureDef < 0 || b.Status != GameStatus.Running) return false;
            var feats = new FeatureState[16384];
            int n = Mathf.Min(b.ReadFeatures(feats), feats.Length);
            for (int i = 0; i < n; i++)
            {
                var f = feats[i];
                if (f.Def != t.FeatureDef || f.Sprite < 0) continue;
                return SpriteOf(b, f, out rect);
            }
            return false;
        }

        static bool SpriteOf(IGameBackend b, FeatureState f, out Rect rect)
        {
            rect = default;
            var img = b.Sprite(f.Sprite);
            if (img == null) return false;
            spriteTex = OpenKingdomsUnity.Game.UI.UiKit.ToTexture(img, true);
            rect = new Rect(-f.SpriteOffsetX, f.SpriteBottom, f.SpriteWidth, f.SpriteTop - f.SpriteBottom);
            return true;
        }

        // A feature the loaded map does not have, placed on an edge cell for
        // as long as it takes to read how the game draws it, then taken away.
        static bool Placed(IGameBackend b, StudioTarget t, out Rect rect)
        {
            rect = default;
            if (t.FeatureDef < 0 || b.Status != GameStatus.Running || b.Terrain == null) return false;
            var size = b.Terrain.Size;
            float cell = Mathf.Max(0.01f, b.Terrain.CellSize);
            int w = Mathf.Max(1, (int)(size.x / cell)), h = Mathf.Max(1, (int)(size.y / cell));
            var cells = new[] { new Vector2Int(3, 3), new Vector2Int(w - 4, h - 4), new Vector2Int(w - 4, 3), new Vector2Int(3, h - 4), new Vector2Int(w / 2, h / 2) };
            var feats = new FeatureState[16384];
            foreach (var c in cells)
            {
                int index = b.PlaceFeature(t.FeatureDef, c.x, c.y);
                if (index < 0) continue;
                try
                {
                    int n = Mathf.Min(b.ReadFeatures(feats), feats.Length);
                    for (int i = 0; i < n; i++)
                        if (feats[i].Index == index) return feats[i].Sprite >= 0 && SpriteOf(b, feats[i], out rect);
                }
                finally { b.RemoveFeature(index); }
            }
            return false;
        }

        // ---- Checks and fixes ----

        public static void Recheck()
        {
            Issues = Model != null ? ModelCheck.Run(Model.Facts, Fix, Target, originalBounds, OriginalPieces) : new List<ModelCheck.Issue>();
            if (Model != null && (StudioModel.FromPlayersFiles(Model.SourcePath) || Model.Facts.FromPlayersFiles))
                Issues.Insert(0, new ModelCheck.Issue
                {
                    Level = ModelCheck.Level.Problem,
                    Text = "The sprite tools made this model from your own game files, so it stays on your computer and can't go into the shared folders. Put it in Overrides/Generated to see it in your own game.",
                });
            if (Model != null && OriginalMissing != null && Target.Kind != TargetKind.None)
                Issues.Add(new ModelCheck.Issue { Level = ModelCheck.Level.Note, Text = OriginalMissing });
        }

        public static void ApplyFix(ModelCheck.FixKind k)
        {
            if (Model == null) return;
            SetFix(ModelCheck.Apply(k, Model.Facts, Fix, Target, originalBounds));
        }

        public static void SetFix(StudioFix f)
        {
            Fix = f;
            Stage?.SetFix(Fix);
            Recheck();
            Remember();
            Notify();
        }

        public static void SetTweaks(MaterialTweaks t)
        {
            Tweaks = t ?? new MaterialTweaks();
            Stage?.SetTweaks(Tweaks, Team);
            Remember();
            Notify();
        }

        public static void SetTeam(int colour)
        {
            TeamColour = colour;
            var b = StudioBackend.Get();
            Stage?.SetMonarch(MonarchModel(b), Team);
            Stage?.SetTweaks(Tweaks, Team);
            RefreshOriginal();
            Notify();
        }

        // ---- Into the game ----

        // Writes the model where the game finds it. With ask set, warnings
        // and an existing model are confirmed first. Returns what was written.
        public static List<string> UseInGame(bool ask)
        {
            var none = new List<string>();
            if (Model == null) { Status = "Drop a model first."; Notify(); return none; }
            if (ModelCheck.Blocks(Issues)) { Status = Issues.First(i => i.Level == ModelCheck.Level.Problem).Text; Notify(); return none; }
            string rel = OverrideWriter.PathFor(Target);
            string winner = OverrideWriter.Outranks(StudioModel.ProjectDir, Target);
            if (ask)
            {
                var warn = Issues.Where(i => i.Level == ModelCheck.Level.Warning).Select(i => "- " + i.Text).ToList();
                if (warn.Count > 0 && !EditorUtility.DisplayDialog("Use it anyway?", string.Join("\n", warn), "Use it", "Cancel")) return none;
                if (File.Exists(Path.Combine(StudioModel.ProjectDir, rel)) &&
                    !EditorUtility.DisplayDialog("Replace the model in the game?", $"{rel} is already in the game. Replace it with {Model.Name}?", "Replace", "Cancel")) return none;
                if (winner != null && !EditorUtility.DisplayDialog("Another model wins", $"{winner} is in the game for the same thing and wins over a .glb, so the game will keep showing it. Put yours in anyway?", "Put it in", "Cancel")) return none;
            }
            bool overSource = SamePath(Path.Combine(StudioModel.ProjectDir, rel), Model.SourcePath);
            var written = OverrideWriter.Write(StudioModel.ProjectDir, Target, Model.Glb, Fix, KeepTweaks ? Tweaks : null, out var error, Model.SourcePath);
            if (error != null) { Status = error; Notify(); return written; }
            foreach (var p in written) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            OverrideLoader.Reset();
            CardOverride.Forget();
            string sidecar = written.Count > 1 ? $" and {written[1]}, which says which piece it stands in for. Share both files" : "";
            Status = "In the game now: " + rel + sidecar + ". Press Play here to see it in a battle. After you export it again, press Use in game again." +
                (winner != null ? $" {winner} still wins over it." : "");
            if (overSource)
            {
                // The file now has the fixes in it, so the studio starts from it plain.
                Fix = StudioFix.None;
                Tweaks = new MaterialTweaks();
                LoadModel(Model.SourcePath);
                Status = "In the game now: " + rel + sidecar + ". Its fixes are in the file now.";
            }
            Notify();
            return written;
        }

        // ---- Screenshots ----

        public static string CapturesDir => Path.Combine(Path.GetDirectoryName(StudioModel.ProjectDir), "Captures");

        public static string Screenshot(bool classic, string dir = null)
        {
            if (!Active) return null;
            dir ??= CapturesDir;
            Directory.CreateDirectory(dir);
            string name = (Model?.Name ?? "studio") + (classic ? "-classic-" : "-free-") + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png";
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, Stage.Screenshot(classic ? View : Free, 1920, 1080));
            Status = "Saved " + path;
            Notify();
            return path;
        }

        // ---- Look ----

        public static void Relook(bool ground)
        {
            if (!Active) return;
            var b = StudioBackend.Get();
            if (ground) { Start(true); return; }
            Stage.SetClimate(Climate(b), Sea, Weather);
            Stage.SetShadows(Shadows);
            Stage.SetTime(Time);
            Notify();
        }

        // ---- Keeping state over script reloads and Play ----

        static void Remember()
        {
            if (!restored) return;
            SessionState.SetString("oku.studio.target", Target.Kind == TargetKind.None ? "" : JsonUtility.ToJson(Target));
            SessionState.SetString("oku.studio.fix", JsonUtility.ToJson(Fix));
            SessionState.SetString("oku.studio.tweaks", JsonUtility.ToJson(Tweaks));
        }

        // Reads back what the last script domain remembered, once.
        static void Restore(IGameBackend b)
        {
            restored = true;
            if (Model == null)
            {
                string path = SessionState.GetString("oku.studio.model", "");
                if (path.Length == 0) path = EditorPrefs.GetString("oku.studio.lastModel", "");
                if (path.Length > 0 && File.Exists(path))
                {
                    Model = StudioModel.Load(path, out _);
                    string fix = SessionState.GetString("oku.studio.fix", "");
                    if (Model != null && fix.Length > 0) { try { Fix = JsonUtility.FromJson<StudioFix>(fix); } catch (ArgumentException) { } }
                }
            }
            string tw = SessionState.GetString("oku.studio.tweaks", "");
            if (tw.Length > 0) { try { Tweaks = JsonUtility.FromJson<MaterialTweaks>(tw) ?? new MaterialTweaks(); } catch (ArgumentException) { } }
            if (Target.Kind == TargetKind.None)
            {
                string json = SessionState.GetString("oku.studio.target", "");
                StudioTarget saved = null;
                if (json.Length > 0) { try { saved = JsonUtility.FromJson<StudioTarget>(json); } catch (ArgumentException) { } }
                if (saved != null && saved.Kind != TargetKind.None) Target = Resolve(b, saved);
            }
        }

        // A saved target as this backend knows it, with its ids, or as saved
        // when the backend does not know it.
        static StudioTarget Resolve(IGameBackend b, StudioTarget saved)
        {
            if (saved.Typed) return saved;
            var list = saved.Kind == TargetKind.Feature ? StudioTargets.Features(b, StudioTargets.Catalog()) : StudioTargets.Units(b, saved.Kind == TargetKind.UnitCard);
            var t = list.FirstOrDefault(x => x.Kind == saved.Kind && string.Equals(x.Name, saved.Name, StringComparison.OrdinalIgnoreCase));
            if (t == null) { saved.FeatureDef = saved.UnitDef = -1; return saved; }
            t.ReplacesPiece = saved.ReplacesPiece;
            t.ReplacesTexture = saved.ReplacesTexture;
            return t;
        }

        // ---- Each editor frame ----

        public static event Action Repaint;

        static void Tick() => TickAt(EditorApplication.timeSinceStartup);

        // One editor frame at the given time: the turntable turns, a changed
        // model reloads, a new file in Drop loads, and the view repaints when
        // something moved.
        public static void TickAt(double now)
        {
            if (!Active) return;
            if (lastTick <= 0) { lastTick = lastPoll = lastDropScan = now; return; }
            float dt = (float)(now - lastTick);
            if (dt < 1f / 30f) return;
            lastTick = now;
            bool turning = Turning && !View.Classic;
            Stage.Tick(dt, turning);
            bool moved = turning || Stage.Animating;
            if (now - lastPoll > 0.5)
            {
                lastPoll = now;
                if (Model != null && Model.ChangedOnDisk()) { LoadModel(Model.SourcePath); moved = true; }
            }
            if (now - lastDropScan > 1.0)
            {
                lastDropScan = now;
                moved |= ScanDrop();
            }
            if (moved) Repaint?.Invoke();
        }

        // A model saved into the drop folder shows at once, once its file
        // has stopped changing. Returns true when one loaded.
        static bool ScanDrop()
        {
            var files = DropFiles();
            if (dropSeen == null) { dropSeen = new HashSet<string>(files.Select(f => f.FullName), StringComparer.OrdinalIgnoreCase); return false; }
            dropSeen.RemoveWhere(n => files.All(f => !string.Equals(f.FullName, n, StringComparison.OrdinalIgnoreCase)));
            var fresh = files.Where(f => !dropSeen.Contains(f.FullName) && (DateTime.UtcNow - f.LastWriteTimeUtc).TotalSeconds > 0.3).ToList();
            foreach (var f in fresh) dropSeen.Add(f.FullName);
            return fresh.Count > 0 && LoadModel(fresh[0].FullName);
        }

        public static List<FileInfo> DropFiles()
        {
            string dir = Path.Combine(StudioModel.ProjectDir, StudioModel.DropFolder);
            if (!Directory.Exists(dir)) return new List<FileInfo>();
            return new DirectoryInfo(dir).GetFiles().Where(f => StudioModel.CanLoad(f.Name)).OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        }

        public static void Notify() => Changed?.Invoke();

        // For tests: forget everything.
        public static void Reset()
        {
            Stop();
            Model?.Dispose();
            Model = null;
            Target = new StudioTarget();
            Fix = StudioFix.None;
            Tweaks = new MaterialTweaks();
            Issues = new List<ModelCheck.Issue>();
            SessionState.EraseString("oku.studio.model");
            SessionState.EraseString("oku.studio.target");
            SessionState.EraseString("oku.studio.fix");
            SessionState.EraseString("oku.studio.tweaks");
            dropSeen = null;
            lastTick = 0;
            SuggestedName = "";
            restored = true;
        }

        // For tests: what a script reload leaves, the saved state only.
        public static void ForgetAsAfterReload()
        {
            BeforeReload();
            Target = new StudioTarget();
            Fix = StudioFix.None;
            Tweaks = new MaterialTweaks();
            Issues = new List<ModelCheck.Issue>();
            dropSeen = null;
            lastTick = 0;
            restored = false;
        }
    }
}
