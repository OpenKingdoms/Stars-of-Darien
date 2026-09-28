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
        public static event Action Changed;

        static Bounds? originalBounds;
        static Texture2D spriteTex;
        static double lastTick, lastPoll, lastDropScan;
        static HashSet<string> dropSeen;

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
        // real map when game files are there, the neutral ground on the mock.
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
            StudioBackend.Changed += () => { if (Active) EditorApplication.delayCall += () => Start(false); };
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
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
            Stage.SetMonarch(MonarchModel(b), Team);
            Restore();
            RefreshOriginal();
            if (Model != null) Stage.SetModel(Model, Fix, Tweaks, Team);
            Recheck();
            Status = b.Name == "Mock" ? "Running on the mock engine. " + (StudioBackend.Problem ?? "") : "Running on the real engine with your game files.";
            Notify();
            return true;
        }

        public static void Stop()
        {
            Remember();
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

        static PresentedModel MonarchModel(IGameBackend b)
        {
            var def = StudioTargets.Monarch(b);
            if (def == null) return null;
            int id = b.LoadModel(def.ObjectName, TeamColour);
            return StudioBackend.Models?.Get(id);
        }

        // ---- The model ----

        public static bool LoadModel(string path)
        {
            var m = StudioModel.Load(path, out var error);
            if (m == null)
            {
                Status = "That model did not load: " + error;
                Notify();
                return false;
            }
            bool same = Model != null && string.Equals(Model.SourcePath, m.SourcePath, StringComparison.OrdinalIgnoreCase);
            Model?.Dispose();
            Model = m;
            if (!same) { Fix = StudioFix.None; GuessTarget(m.Name); }
            Stage?.SetModel(Model, Fix, Tweaks, Team);
            SessionState.SetString("oku.studio.model", m.SourcePath);
            EditorPrefs.SetString("oku.studio.lastModel", m.SourcePath);
            Recheck();
            Status = (same ? "Reloaded " : "Loaded ") + Path.GetFileName(m.SourcePath) + (m.Notes.Count > 0 ? ". " + string.Join(" ", m.Notes) : "");
            Notify();
            return true;
        }

        public static string SamplePath => Path.Combine(StudioModel.ProjectDir, "Assets/Game/Studio/Samples/SampleWell.glb");

        // A model named like something the game has aims at it at once.
        static void GuessTarget(string name)
        {
            if (Target.Kind != TargetKind.None) return;
            var b = StudioBackend.Get();
            var hit = StudioTargets.Features(b, StudioTargets.Catalog()).FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? StudioTargets.Units(b, false).FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(t.ObjectName, name, StringComparison.OrdinalIgnoreCase));
            if (hit != null) SetTarget(hit);
        }

        public static void SetTarget(StudioTarget t)
        {
            Target = t ?? new StudioTarget();
            RefreshOriginal();
            Recheck();
            Notify();
        }

        public static void SetCardPiece(string piece, string texture)
        {
            if (Target.Kind != TargetKind.UnitCard) return;
            Target.ReplacesPiece = piece ?? "";
            Target.ReplacesTexture = texture ?? "";
            RefreshOriginal();
            Recheck();
            Notify();
        }

        // The original beside the model: its 3D model, or its picture from the
        // loaded map when it stands there, or from the sprite catalog.
        static void RefreshOriginal()
        {
            originalBounds = null;
            OriginalPieces = Array.Empty<string>();
            if (spriteTex != null) UnityEngine.Object.DestroyImmediate(spriteTex);
            spriteTex = null;
            var b = StudioBackend.Get();
            PresentedModel pm = null;
            var rect = default(Rect);
            var t = Target;
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
                    }
                }
                if (pm == null && t.Kind == TargetKind.Feature)
                {
                    if (!FromMap(b, t, out rect) && t.SpriteFile != null && File.Exists(t.SpriteFile))
                    {
                        spriteTex = new Texture2D(2, 2) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
                        spriteTex.LoadImage(File.ReadAllBytes(t.SpriteFile));
                        // One pixel is a sixteenth of a cell across and an eighth
                        // of a cell up, as the original's view draws height.
                        rect = new Rect(-t.Hotspot.x / 16f, -(t.SpriteSize.y - t.Hotspot.y) / 8f, t.SpriteSize.x / 16f, t.SpriteSize.y / 8f);
                    }
                }
            }
            Stage?.SetOriginal(t, pm, spriteTex, rect);
        }

        static bool FromMap(IGameBackend b, StudioTarget t, out Rect rect)
        {
            rect = default;
            if (t.FeatureDef < 0 || b.Status != GameStatus.Running) return false;
            var feats = new FeatureState[8192];
            int n = Mathf.Min(b.ReadFeatures(feats), feats.Length);
            for (int i = 0; i < n; i++)
            {
                var f = feats[i];
                if (f.Def != t.FeatureDef || f.Sprite < 0) continue;
                var img = b.Sprite(f.Sprite);
                if (img == null) return false;
                spriteTex = OpenKingdomsUnity.Game.UI.UiKit.ToTexture(img, true);
                rect = new Rect(-f.SpriteOffsetX, f.SpriteBottom, f.SpriteWidth, f.SpriteTop - f.SpriteBottom);
                return true;
            }
            return false;
        }

        // ---- Checks and fixes ----

        public static void Recheck()
        {
            Issues = Model != null ? ModelCheck.Run(Model.Facts, Fix, Target, originalBounds, OriginalPieces) : new List<ModelCheck.Issue>();
            if (Model != null && StudioModel.FromPlayersFiles(Model.SourcePath))
                Issues.Insert(0, new ModelCheck.Issue
                {
                    Level = ModelCheck.Level.Problem,
                    Text = "The sprite tools made this model from your own game files, so it stays on your computer and can't go into the shared folders. It already shows in your game from Overrides/Generated.",
                });
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
            if (ask)
            {
                var warn = Issues.Where(i => i.Level == ModelCheck.Level.Warning).Select(i => "- " + i.Text).ToList();
                if (warn.Count > 0 && !EditorUtility.DisplayDialog("Use it anyway?", string.Join("\n", warn), "Use it", "Cancel")) return none;
                if (File.Exists(Path.Combine(StudioModel.ProjectDir, rel)) &&
                    !EditorUtility.DisplayDialog("Replace the model in the game?", $"{rel} is already in the game. Replace it with {Model.Name}?", "Replace", "Cancel")) return none;
            }
            var written = OverrideWriter.Write(StudioModel.ProjectDir, Target, Model.Glb, Fix, KeepTweaks ? Tweaks : null, out var error);
            if (error != null) { Status = error; Notify(); return written; }
            foreach (var p in written) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            OverrideLoader.Reset();
            CardOverride.Forget();
            Status = "In the game now: " + rel + ". Press Play here to see it in a battle.";
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

        // ---- Keeping state over script reloads ----

        static void Remember()
        {
            SessionState.SetString("oku.studio.target", Target.Kind == TargetKind.None ? "" :
                $"{(int)Target.Kind}|{Target.Name}|{Target.ReplacesPiece}|{Target.ReplacesTexture}");
            SessionState.SetString("oku.studio.fix", JsonUtility.ToJson(Fix));
            SessionState.SetString("oku.studio.tweaks", JsonUtility.ToJson(Tweaks));
        }

        static void Restore()
        {
            if (Model == null)
            {
                string path = SessionState.GetString("oku.studio.model", "");
                if (path.Length == 0) path = EditorPrefs.GetString("oku.studio.lastModel", "");
                if (path.Length > 0 && File.Exists(path))
                {
                    Model = StudioModel.Load(path, out _);
                    string fix = SessionState.GetString("oku.studio.fix", "");
                    if (Model != null && fix.Length > 0) Fix = JsonUtility.FromJson<StudioFix>(fix);
                }
            }
            string tw = SessionState.GetString("oku.studio.tweaks", "");
            if (tw.Length > 0) { try { Tweaks = JsonUtility.FromJson<MaterialTweaks>(tw) ?? new MaterialTweaks(); } catch (ArgumentException) { } }
            if (Target.Kind == TargetKind.None)
            {
                var parts = SessionState.GetString("oku.studio.target", "").Split('|');
                if (parts.Length == 4 && int.TryParse(parts[0], out int kind))
                {
                    var b = StudioBackend.Get();
                    var list = (TargetKind)kind == TargetKind.Feature ? StudioTargets.Features(b, StudioTargets.Catalog()) : StudioTargets.Units(b, (TargetKind)kind == TargetKind.UnitCard);
                    var t = list.FirstOrDefault(x => x.Name == parts[1]);
                    if (t != null) { t.ReplacesPiece = parts[2]; t.ReplacesTexture = parts[3]; Target = t; }
                }
            }
        }

        // ---- Each editor frame ----

        public static event Action Repaint;

        static void Tick()
        {
            if (!Active) return;
            double now = EditorApplication.timeSinceStartup;
            float dt = lastTick > 0 ? (float)(now - lastTick) : 0f;
            if (dt < 1f / 30f) return;
            lastTick = now;
            Stage.Tick(dt, Turning && !View.Classic);
            if (now - lastPoll > 0.5)
            {
                lastPoll = now;
                if (Model != null && Model.ChangedOnDisk()) LoadModel(Model.SourcePath);
            }
            if (now - lastDropScan > 1.0)
            {
                lastDropScan = now;
                ScanDrop();
            }
            Repaint?.Invoke();
        }

        // A model saved into the drop folder shows at once, once its file
        // has stopped changing.
        static void ScanDrop()
        {
            var files = DropFiles();
            if (dropSeen == null) { dropSeen = new HashSet<string>(files.Select(f => f.FullName), StringComparer.OrdinalIgnoreCase); return; }
            dropSeen.RemoveWhere(n => files.All(f => !string.Equals(f.FullName, n, StringComparison.OrdinalIgnoreCase)));
            var fresh = files.Where(f => !dropSeen.Contains(f.FullName) && (DateTime.UtcNow - f.LastWriteTimeUtc).TotalSeconds > 0.3).ToList();
            foreach (var f in fresh) dropSeen.Add(f.FullName);
            if (fresh.Count > 0) LoadModel(fresh[0].FullName);
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
        }
    }
}
