// StudioMode.cs - OpenKingdoms > Studio Mode: opens the studio scene, builds
// the stage and docks the studio windows, Leave Studio Mode puts scenes and
// layout back, and Play here starts a battle with the model in it.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using OpenKingdomsUnity.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    [InitializeOnLoad]
    public static class StudioMode
    {
        public const string ScenePath = "Assets/Scenes/Studio.unity";
        const string ActiveKey = "oku.studio.active", ScenesKey = "oku.studio.previousScenes", PlayKey = "oku.studio.playHere";
        static string LayoutDir => Path.Combine(StudioModel.ProjectDir, "Library", "OkStudio");
        static string SavedLayout => Path.Combine(LayoutDir, "before-studio.wlt");

        public static bool IsOn => SessionState.GetBool(ActiveKey, false);

        static StudioMode()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
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

        [MenuItem("OpenKingdoms/Leave Studio Mode", true)]
        static bool CanLeave() => IsOn && !EditorApplication.isPlayingOrWillChangePlaymode;

        // Opens the studio. layout docks the windows, which a headless run skips.
        public static bool Open(bool layout)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (!Application.isBatchMode) EditorUtility.DisplayDialog("Studio Mode", "Stop the game first.", "OK");
                return false;
            }
            if (!IsOn)
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
                var open = Enumerable.Range(0, EditorSceneManager.sceneCount).Select(EditorSceneManager.GetSceneAt)
                    .Where(s => !string.IsNullOrEmpty(s.path) && s.path != ScenePath).Select(s => s.path).ToArray();
                SessionState.SetString(ScenesKey, string.Join("|", open));
                if (layout) SaveLayout();
            }
            OpenScene();
            SessionState.SetBool(ActiveKey, true);
            StartSceneIsGame();
            bool ok = StudioSession.Start(!Application.isBatchMode);
            if (layout) EditorApplication.delayCall += Dock;
            return ok;
        }

        public static void Close(bool layout)
        {
            StudioSession.Stop();
            SessionState.SetBool(ActiveKey, false);
            EditorSceneManager.playModeStartScene = null;
            var scenes = SessionState.GetString(ScenesKey, "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Where(File.Exists).ToArray();
            if (scenes.Length > 0)
            {
                EditorSceneManager.OpenScene(scenes[0], OpenSceneMode.Single);
                foreach (var s in scenes.Skip(1)) EditorSceneManager.OpenScene(s, OpenSceneMode.Additive);
            }
            else EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            if (layout) EditorApplication.delayCall += RestoreLayout;
            StudioSession.Notify();
        }

        // After a script reload or a battle, the stage comes back.
        static void Resume()
        {
            if (!IsOn || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorSceneManager.GetActiveScene().path != ScenePath) OpenScene();
            StudioSession.Start(false);
        }

        static void OpenScene()
        {
            if (!File.Exists(Path.Combine(StudioModel.ProjectDir, ScenePath)))
            {
                Directory.CreateDirectory(Path.Combine(StudioModel.ProjectDir, "Assets/Scenes"));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            else if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
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
        }

        // Starts a battle on a map the target is on (or the studio's map),
        // as its kingdom for a unit, with the feature placed by your start.
        public static bool PlayHere()
        {
            var b = StudioBackend.Get();
            if (b == null || b.Maps.Count == 0) return false;
            // Play always runs the real engine when it can, so the mock's maps
            // and features would not be there.
            SettingsWindow.Summary(out bool real);
            if (StudioBackend.PreferMock && real)
            {
                if (!Application.isBatchMode)
                    EditorUtility.DisplayDialog("Play here", "The studio is on the mock, but the game plays on the real engine. Untick Use mock in the Studio panel's toolbar first.", "OK");
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
            };
            SessionState.SetString(PlayKey, JsonUtility.ToJson(req));
            PlayerPrefs.SetString(GameRoot.AutoStartKey, map.Id);
            PlayerPrefs.Save();
            if (!File.Exists(Path.Combine(StudioModel.ProjectDir, RemasterMenu.ScenePath)))
            {
                RemasterMenu.CreateScene();
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

        // Puts the feature in a little row south-east of your start, where
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
                    int placed = 0;
                    foreach (var off in new[] { new Vector3(5, 0, -5), new Vector3(8, 0, -4), new Vector3(6, 0, -8) })
                    {
                        var at = start + off;
                        if (b.PlaceFeature(def.Id, Mathf.RoundToInt(at.x / cell), Mathf.RoundToInt(-at.z / cell)) >= 0) placed++;
                    }
                    focus = start + new Vector3(6, 0, -5);
                    Debug.Log(placed > 0 ? $"Play here: {placed} of {def.Name} stand south-east of your start." : $"Play here: the engine did not place {def.Name}.");
                }
                else Debug.Log($"Play here: {req.Feature} is not a feature of this engine.");
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

        static void RestoreLayout()
        {
            string path = File.Exists(SavedLayout) ? SavedLayout : Path.Combine(EditorApplication.applicationContentsPath, "Resources", "Layouts", "Default.wlt");
            if (File.Exists(path)) LoadLayout(path);
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
