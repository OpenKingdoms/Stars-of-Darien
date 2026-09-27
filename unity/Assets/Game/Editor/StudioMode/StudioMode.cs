// StudioMode.cs - OpenKingdoms > Studio Mode: opens the studio scene, builds
// the stage and docks the studio windows, Leave Studio Mode puts scenes and
// layout back, and Play here starts a battle with the model in it. Opening
// another scene leaves Studio Mode, so the studio never replaces your work.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using OpenKingdomsUnity.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenKingdomsUnity.Studio
{
    [InitializeOnLoad]
    public static class StudioMode
    {
        public const string ScenePath = "Assets/Scenes/Studio.unity";
        const string ActiveKey = "oku.studio.active", PlayKey = "oku.studio.playHere";
        static string LayoutDir => Path.Combine(StudioModel.ProjectDir, "Library", "OkStudio");
        static string SavedLayout => Path.Combine(LayoutDir, "before-studio.wlt");
        static string SavedScenes => Path.Combine(LayoutDir, "before-studio-scenes.txt");
        // Set while the studio itself opens scenes.
        static bool switching;

        public static bool IsOn => SessionState.GetBool(ActiveKey, false);

        static StudioMode()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorSceneManager.activeSceneChangedInEditMode += (from, to) => SceneChanged(to);
            EditorSceneManager.sceneOpened += (s, mode) => SceneChanged(SceneManager.GetActiveScene());
            EditorSceneManager.newSceneCreated += (s, setup, mode) => SceneChanged(SceneManager.GetActiveScene());
            if (EditorApplication.isPlayingOrWillChangePlaymode) HookPlayHere();
            else if (IsOn) EditorApplication.delayCall += Resume;
            if (IsOn) StartSceneIsGame();
        }

        [MenuItem("OpenKingdoms/Studio Mode", priority = 10)]
        public static void Enter() => Open(true);

        [MenuItem("OpenKingdoms/Studio Mode", true)]
        static bool CanEnter() => !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem("OpenKingdoms/Leave Studio Mode", priority = 11)]
        public static void Leave() => Close(true);

        // Also after a restart in Studio Mode, while its layout is still up.
        [MenuItem("OpenKingdoms/Leave Studio Mode", true)]
        static bool CanLeave() => (IsOn || File.Exists(SavedLayout)) && !EditorApplication.isPlayingOrWillChangePlaymode;

        // Opens the studio. layout docks the windows, which a headless run skips.
        public static bool Open(bool layout)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (!Application.isBatchMode) EditorUtility.DisplayDialog("Studio Mode", "Stop the game first.", "OK");
                return false;
            }
            if (!SaveFirst()) return false;
            if (!IsOn)
            {
                var open = Enumerable.Range(0, EditorSceneManager.sceneCount).Select(EditorSceneManager.GetSceneAt)
                    .Where(s => !string.IsNullOrEmpty(s.path) && s.path != ScenePath).Select(s => s.path).ToArray();
                // What was kept before a restart in Studio Mode stays.
                Directory.CreateDirectory(LayoutDir);
                if (!File.Exists(SavedScenes)) File.WriteAllLines(SavedScenes, open);
                if (layout && !File.Exists(SavedLayout)) SaveLayout();
            }
            if (!OpenScene()) return false;
            SessionState.SetBool(ActiveKey, true);
            StartSceneIsGame();
            bool ok = StudioSession.Start(!Application.isBatchMode);
            if (layout) EditorApplication.delayCall += Dock;
            return ok;
        }

        // Puts back the scenes from before Studio Mode, when it is still on,
        // and the window layout.
        public static void Close(bool layout)
        {
            bool wasOn = IsOn;
            if (wasOn && !SaveFirst()) return;
            StudioSession.Stop();
            SessionState.SetBool(ActiveKey, false);
            EditorSceneManager.playModeStartScene = null;
            if (wasOn)
            {
                var scenes = File.Exists(SavedScenes) ? File.ReadAllLines(SavedScenes).Where(p => p.Length > 0 && File.Exists(Path.Combine(StudioModel.ProjectDir, p))).ToArray() : new string[0];
                switching = true;
                try
                {
                    if (scenes.Length > 0)
                    {
                        EditorSceneManager.OpenScene(scenes[0], OpenSceneMode.Single);
                        foreach (var s in scenes.Skip(1)) EditorSceneManager.OpenScene(s, OpenSceneMode.Additive);
                    }
                    else if (SceneManager.GetActiveScene().path == ScenePath) EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                }
                finally { switching = false; }
            }
            if (File.Exists(SavedScenes)) File.Delete(SavedScenes);
            if (layout) EditorApplication.delayCall += RestoreLayout;
            StudioSession.Notify();
        }

        // Unsaved changes in the open scenes, other than the studio's own,
        // are offered for saving before the studio opens a scene over them.
        static bool SaveFirst()
        {
            if (Application.isBatchMode) return true;
            var dirty = Enumerable.Range(0, EditorSceneManager.sceneCount).Select(EditorSceneManager.GetSceneAt)
                .Where(s => s.isDirty && s.path != ScenePath).ToArray();
            return dirty.Length == 0 || EditorSceneManager.SaveModifiedScenesIfUserWantsTo(dirty);
        }

        // Another scene opened by hand ends Studio Mode quietly, keeping that
        // scene, and the studio's layout stays until Leave Studio Mode.
        static void SceneChanged(Scene now)
        {
            if (switching || !IsOn || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (now.path == ScenePath) return;
            Abandon();
        }

        static void Abandon()
        {
            StudioSession.Stop();
            SessionState.SetBool(ActiveKey, false);
            EditorSceneManager.playModeStartScene = null;
            if (File.Exists(SavedScenes)) File.Delete(SavedScenes);
            StudioSession.Notify();
        }

        // After a script reload or a battle, the stage comes back, but only
        // in the studio's own scene.
        static void Resume()
        {
            if (!IsOn || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var active = SceneManager.GetActiveScene();
            if (active.path != ScenePath)
            {
                bool empty = string.IsNullOrEmpty(active.path) && !active.isDirty && EditorSceneManager.sceneCount == 1;
                if (!empty) { Abandon(); return; }
                if (!OpenScene()) return;
            }
            StudioSession.Start(false);
        }

        static bool OpenScene()
        {
            switching = true;
            try
            {
                if (!File.Exists(Path.Combine(StudioModel.ProjectDir, ScenePath)))
                {
                    Directory.CreateDirectory(Path.Combine(StudioModel.ProjectDir, "Assets/Scenes"));
                    var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    EditorSceneManager.SaveScene(scene, ScenePath);
                }
                else if (SceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                return SceneManager.GetActiveScene().path == ScenePath;
            }
            finally { switching = false; }
        }

        // Play from the studio plays the game, not the studio scene.
        static void StartSceneIsGame()
        {
            var game = AssetDatabase.LoadAssetAtPath<SceneAsset>(RemasterMenu.ScenePath);
            if (game != null) EditorSceneManager.playModeStartScene = game;
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (!IsOn) return;
            if (s == PlayModeStateChange.ExitingEditMode) StudioSession.Stop();
            else if (s == PlayModeStateChange.EnteredPlayMode) HookPlayHere();
            else if (s == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.EraseString(PlayKey);
                EditorApplication.delayCall += Resume;
            }
        }

        // ---- Play here ----

        [Serializable]
        public sealed class PlayRequest
        {
            public string Map = "", Feature = "", Unit = "", Side = "";
            public int Width = 1, Depth = 1;
        }

        // Starts a battle on a map the target is on (or the studio's map),
        // as its kingdom for a unit, with the feature placed by your start.
        public static bool PlayHere()
        {
            var b = StudioBackend.Get();
            if (b == null || b.Maps.Count == 0) return false;
            // Play always runs the real game when it can, so the stand-in
            // world's maps and features would not be there.
            SettingsWindow.Summary(out bool real);
            if (StudioBackend.PreferMock && real)
            {
                if (!Application.isBatchMode)
                    EditorUtility.DisplayDialog("Play here", "The studio is on the stand-in world, but the game plays with your game files. Untick Use the stand-in world at the top of the Studio panel first.", "OK");
                return false;
            }
            var t = StudioSession.Target;
            MapInfo map = t.Kind == TargetKind.Feature ? StudioTargets.MapNamed(b, t.Maps) : null;
            map ??= b.Maps.FirstOrDefault(m => m.Id == StudioSession.LoadedMap) ?? b.Maps[0];
            var req = new PlayRequest
            {
                Map = map.Id,
                Feature = t.Kind == TargetKind.Feature ? t.Name : "",
                Unit = t.IsUnit ? t.Name : "",
                Side = t.IsUnit ? t.Side ?? "" : "",
                Width = Mathf.Max(1, t.Footprint.x),
                Depth = Mathf.Max(1, t.Footprint.y),
            };
            SessionState.SetString(PlayKey, JsonUtility.ToJson(req));
            PlayerPrefs.SetString(GameRoot.AutoStartKey, map.Id);
            PlayerPrefs.Save();
            if (!File.Exists(Path.Combine(StudioModel.ProjectDir, RemasterMenu.ScenePath)))
            {
                switching = true;
                try { RemasterMenu.CreateScene(); }
                finally { switching = false; }
                OpenScene();
            }
            StartSceneIsGame();
            EditorApplication.isPlaying = true;
            return true;
        }

        static void HookPlayHere()
        {
            string json = SessionState.GetString(PlayKey, "");
            if (json.Length == 0) return;
            var req = JsonUtility.FromJson<PlayRequest>(json);
            GameRoot.AutoStarting = root =>
            {
                if (req.Side.Length == 0 || root.Setup.Seats.Count == 0) return;
                var side = root.Backend.Sides.FirstOrDefault(s => string.Equals(s.Id, req.Side, StringComparison.OrdinalIgnoreCase));
                if (side != null) root.Setup.Seats[0].Side = side.Id;
            };
            GameRoot.WorldLoaded = root =>
            {
                GameRoot.WorldLoaded = null;
                GameRoot.AutoStarting = null;
                SessionState.EraseString(PlayKey);
                try { Show(root, req); }
                catch (Exception e) { Debug.LogWarning("Play here: " + e.Message); }
            };
        }

        // Cells south-east of the start for a feature's copies: a footprint
        // and two cells apart, the flattest first.
        public static List<Vector3> PlaceFor(Func<float, float, float> ground, Vector3 start, int width, int depth, int count)
        {
            float stepX = width + 2f, stepZ = depth + 2f;
            var spots = new List<(Vector3 at, float slope)>();
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 4; col++)
                {
                    var at = start + new Vector3(4f + width * 0.5f + col * stepX, 0, -(4f + depth * 0.5f + row * stepZ));
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (float dz = -depth * 0.5f; dz <= depth * 0.5f + 0.01f; dz += 0.5f)
                        for (float dx = -width * 0.5f; dx <= width * 0.5f + 0.01f; dx += 0.5f)
                        {
                            float h = ground(at.x + dx, at.z + dz);
                            lo = Mathf.Min(lo, h);
                            hi = Mathf.Max(hi, h);
                        }
                    spots.Add((at, hi - lo + (row + col) * 0.05f));
                }
            return spots.OrderBy(s => s.slope).Take(count).Select(s => s.at).ToList();
        }

        // Puts the feature in a little group south-east of your start, where
        // the camera sees it, and frames it.
        static void Show(GameRoot root, PlayRequest req)
        {
            var b = root.Backend;
            var units = new UnitState[1024];
            int n = b.ReadUnits(units);
            Vector3 start = new Vector3(b.Terrain.Size.x / 2, 0, -b.Terrain.Size.y / 2);
            for (int i = 0; i < n; i++)
                if (units[i].Player == b.LocalPlayer)
                {
                    start = units[i].Position;
                    if (req.Unit.Length > 0 && units[i].Def >= 0 && units[i].Def < b.UnitDefs.Count &&
                        string.Equals(b.UnitDefs[units[i].Def].Name, req.Unit, StringComparison.OrdinalIgnoreCase)) break;
                    if (req.Unit.Length == 0) break;
                }
            var focus = start;
            if (req.Feature.Length > 0)
            {
                var def = b.FeatureDefs.FirstOrDefault(d => string.Equals(d.Name, req.Feature, StringComparison.OrdinalIgnoreCase));
                if (def != null)
                {
                    float cell = Mathf.Max(0.01f, b.Terrain.CellSize);
                    int w = Mathf.Max(req.Width, def.Footprint.x), d = Mathf.Max(req.Depth, def.Footprint.y);
                    var spots = PlaceFor(b.GroundHeight, start, w, d, 3);
                    int placed = 0;
                    foreach (var at in spots)
                        if (b.PlaceFeature(def.Id, Mathf.RoundToInt(at.x / cell), Mathf.RoundToInt(-at.z / cell)) >= 0) placed++;
                    if (spots.Count > 0) focus = spots.Aggregate(Vector3.zero, (s, p) => s + p) / spots.Count;
                    Debug.Log(placed > 0 ? $"Play here: {placed} of {def.Name} stand south-east of your start." : $"Play here: the engine did not place {def.Name}.");
                }
                else Debug.Log($"Play here: {req.Feature} is not a feature of this game.");
            }
            var cam = root.World?.Camera;
            if (cam != null)
            {
                cam.focus = new Vector3(focus.x, cam.focus.y, focus.z);
                cam.Zoom(18f);
            }
        }

        // ---- Window layout ----

        static void Dock()
        {
            // A known base, so the studio's windows land in the same places.
            string def = Path.Combine(EditorApplication.applicationContentsPath, "Resources", "Layouts", "Default.wlt");
            if (File.Exists(def)) LoadLayout(def);
            var editor = typeof(EditorWindow).Assembly;
            var inspector = editor.GetType("UnityEditor.InspectorWindow");
            var project = editor.GetType("UnityEditor.ProjectBrowser");
            var view = EditorWindow.GetWindow<StudioViewWindow>("Studio View", false, typeof(SceneView));
            if (inspector != null) EditorWindow.GetWindow<StudioWindow>("Studio", false, inspector);
            else EditorWindow.GetWindow<StudioWindow>("Studio");
            if (project != null) EditorWindow.GetWindow<StudioDropWindow>("Studio Drop", false, project);
            else EditorWindow.GetWindow<StudioDropWindow>("Studio Drop");
            EditorWindow.GetWindow<StudioWindow>().Focus();
            view.Focus();
        }

        // Unity keeps saving a layout internal, so it is found by name.
        public static MethodInfo SaveLayoutMethod()
        {
            var t = typeof(EditorWindow).Assembly.GetType("UnityEditor.WindowLayout");
            return t?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(x => x.Name == "SaveWindowLayout" && x.GetParameters().Length >= 1 && x.GetParameters()[0].ParameterType == typeof(string));
        }

        public static MethodInfo LoadLayoutMethod()
        {
            var u = typeof(EditorUtility).GetMethod("LoadWindowLayout", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string) }, null);
            if (u != null) return u;
            var t = typeof(EditorWindow).Assembly.GetType("UnityEditor.WindowLayout");
            return t?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(x => (x.Name == "TryLoadWindowLayout" || x.Name == "LoadWindowLayout") && x.GetParameters().Length >= 1 && x.GetParameters()[0].ParameterType == typeof(string));
        }

        static void SaveLayout()
        {
            Directory.CreateDirectory(LayoutDir);
            var m = SaveLayoutMethod();
            if (m == null) return;
            var args = m.GetParameters().Select(p => p.ParameterType == typeof(string) ? (object)SavedLayout : p.HasDefaultValue ? p.DefaultValue : null).ToArray();
            try { m.Invoke(null, args); }
            catch (Exception e) { Debug.LogWarning("Studio Mode could not keep your layout: " + (e.InnerException ?? e).Message); }
        }

        // Your layout from before Studio Mode, then forgotten. Without one the
        // layout stays as it is rather than falling back to Unity's default.
        static void RestoreLayout()
        {
            if (!File.Exists(SavedLayout)) return;
            LoadLayout(SavedLayout);
            try { File.Delete(SavedLayout); } catch (IOException) { }
        }

        static void LoadLayout(string path)
        {
            try
            {
                var m = LoadLayoutMethod();
                if (m == null) return;
                var args = m.GetParameters().Select((p, i) => i == 0 ? path : p.ParameterType == typeof(bool) ? (object)false : p.HasDefaultValue ? p.DefaultValue : null).ToArray();
                m.Invoke(null, args);
            }
            catch (Exception e) { Debug.LogWarning("Studio Mode could not change the layout: " + (e.InnerException ?? e).Message); }
        }
    }
}
