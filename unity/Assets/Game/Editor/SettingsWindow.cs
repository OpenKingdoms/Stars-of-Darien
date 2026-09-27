// SettingsWindow.cs - OpenKingdoms > Settings: the game folder (EditorPrefs,
// OK_GAME_DIR still wins), the sprite catalog and Blender, and a line on
// whether the real game or the stand-in world runs, and why.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    public sealed class SettingsWindow : EditorWindow
    {
        const string GameDirPref = "oku.gameDir", BlenderPref = "oku.sprites.blender";

        [MenuItem("OpenKingdoms/Settings", priority = 50)]
        public static void Open() => GetWindow<SettingsWindow>(true, "OpenKingdoms Settings").minSize = new Vector2(520, 330);

        static Type Settings => Type.GetType("OpenKingdomsUnity.Engine.EngineSettings, OpenKingdomsUnity.Engine");

        static T Read<T>(string name, T fallback)
        {
            var t = Settings;
            var p = t?.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (p != null) return (T)p.GetValue(null);
            var f = t?.GetField(name, BindingFlags.Public | BindingFlags.Static);
            return f != null ? (T)f.GetValue(null) : fallback;
        }

        public static string GameDir => Read("GameDir", "");
        public static string DefaultGameDir => Read("DefaultGameDir", "C:/GOG Games/Total Annihilation Kingdoms");

        // Where blender.exe is: as set here, or the newest under Program Files.
        public static string Blender
        {
            get
            {
                string set = EditorPrefs.GetString(BlenderPref, "");
                if (set.Length > 0) return set;
                try
                {
                    string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Blender Foundation");
                    if (!Directory.Exists(root)) return "";
                    return Directory.GetDirectories(root).OrderByDescending(d => d).Select(d => Path.Combine(d, "blender.exe")).FirstOrDefault(File.Exists) ?? "";
                }
                catch (Exception) { return ""; }
            }
            set => EditorPrefs.SetString(BlenderPref, value ?? "");
        }

        // Where the game folder setting comes from, in words.
        public static string Source()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OK_GAME_DIR"))) return "Set by the OK_GAME_DIR environment variable, which wins over this setting.";
            return EditorPrefs.GetString(GameDirPref, "").Length > 0 ? "Set here." : "The default place for the GOG edition.";
        }

        // A folder with the game's archives or its program in it.
        public static bool LooksLikeGame(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            try { return Directory.GetFiles(dir, "*.hpi").Length > 0 || File.Exists(Path.Combine(dir, "Kingdoms.exe")); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        // One line on what runs, with its reason. inSettings words the way
        // out for this window, otherwise for the studio's panels.
        public static string Summary(out bool real, bool inSettings = true)
        {
            real = false;
            string where = inSettings ? "Pick your game folder above." : "Set your game folder in OpenKingdoms, then Settings.";
            if (Settings == null) return "The game's engine is not in this project, so the stand-in world runs.";
            string blocked = Read<string>("Blocked", null);
            if (blocked != null) return blocked;
            var lib = EngineInstaller.Engine;
            bool installed = File.Exists(Path.Combine(EngineInstaller.PluginDir, "okengine.dll"));
            if (!installed) return EngineInstaller.Describe(lib);
            string dir = GameDir;
            if (!Directory.Exists(dir)) return $"Your game files are not at {dir}, so the stand-in world runs, with made-up maps and boxy soldiers. {where}";
            if (!LooksLikeGame(dir)) return $"{dir} is there but has no .hpi archives, so it may not be the game. The studio will try it.";
            real = true;
            return "The real game runs with your game files.";
        }

        // Applies the settings to every open studio window at once.
        public static void UseNow()
        {
            StudioBackend.Reload();
            StudioSession.Notify();
        }

        void OnGUI()
        {
            GUILayout.Label("Your game", EditorStyles.boldLabel);
            string pref = EditorPrefs.GetString(GameDirPref, "");
            using (new EditorGUILayout.HorizontalScope())
            {
                string shown = pref.Length > 0 ? pref : DefaultGameDir;
                string typed = EditorGUILayout.DelayedTextField("Game folder", shown);
                if (typed != shown) Set(typed == DefaultGameDir ? "" : typed);
                if (GUILayout.Button("Browse", GUILayout.Width(64)))
                {
                    string picked = EditorUtility.OpenFolderPanel("Your Total Annihilation: Kingdoms folder", Directory.Exists(shown) ? shown : "", "");
                    if (!string.IsNullOrEmpty(picked)) Set(picked);
                }
                if (GUILayout.Button("Default", GUILayout.Width(64))) Set("");
            }
            GUILayout.Label(Source(), EditorStyles.wordWrappedMiniLabel);
            GUILayout.Label("In use: " + GameDir, EditorStyles.wordWrappedMiniLabel);

            GUILayout.Space(8);
            GUILayout.Label("Engine", EditorStyles.boldLabel);
            string summary = Summary(out bool real);
            EditorGUILayout.HelpBox(summary, real ? MessageType.Info : MessageType.Warning);
            if (!string.IsNullOrEmpty(EngineInstaller.Message) && EngineInstaller.Message != summary)
                GUILayout.Label(EngineInstaller.Message, EditorStyles.wordWrappedMiniLabel);
            if (StudioBackend.PreferMock)
                GUILayout.Label("Use the stand-in world is ticked in the studio, so the studio stays on the stand-in world.", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Use these settings now")) UseNow();

            GUILayout.Space(8);
            GUILayout.Label("Tools (optional)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                StudioTargets.CatalogDir = EditorGUILayout.DelayedTextField("Sprite catalog", StudioTargets.CatalogDir);
                if (GUILayout.Button("Browse", GUILayout.Width(64)))
                {
                    string picked = EditorUtility.OpenFolderPanel("The folder extract.py wrote", StudioTargets.CatalogDir, "");
                    if (!string.IsNullOrEmpty(picked)) StudioTargets.CatalogDir = picked;
                }
            }
            string catalog = StudioTargets.CatalogDir;
            GUILayout.Label(catalog.Length > 0 && File.Exists(Path.Combine(catalog, "catalog.json"))
                ? "Found catalog.json, the list of the game's sprite features."
                : "Optional. tools/sprite-replace/extract.py makes a catalog from your game files, which needs Python. The studio works without it.", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                string blender = Blender;
                string typed = EditorGUILayout.DelayedTextField("Blender", blender);
                if (typed != blender) Blender = typed;
                if (GUILayout.Button("Browse", GUILayout.Width(64)))
                {
                    string picked = EditorUtility.OpenFilePanel("blender.exe", blender.Length > 0 ? Path.GetDirectoryName(blender) : "", "exe");
                    if (!string.IsNullOrEmpty(picked)) Blender = picked;
                }
            }
        }

        static void Set(string dir)
        {
            EditorPrefs.SetString(GameDirPref, dir ?? "");
            UseNow();
        }
    }
}
