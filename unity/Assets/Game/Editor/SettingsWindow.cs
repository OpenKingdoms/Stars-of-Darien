// SettingsWindow.cs - OpenKingdoms > Settings: the game folder (EditorPrefs,
// OK_GAME_DIR still wins), the sprite catalog and Blender, and a line on
// whether the real engine or the mock runs, and why.
using System;
using System.IO;
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

        // One line on what runs, with its reason.
        public static string Summary(out bool real)
        {
            real = false;
            if (Settings == null) return "The engine binding is not in this project, so the mock engine runs.";
            string blocked = Read<string>("Blocked", null);
            if (blocked != null) return blocked;
            var lib = EngineInstaller.Engine;
            bool installed = File.Exists(Path.Combine(EngineInstaller.PluginDir, "okengine.dll"));
            if (!installed) return EngineInstaller.Describe(lib);
            string dir = GameDir;
            if (!Directory.Exists(dir)) return $"Your game files are not at {dir}, so the mock engine runs, with made-up maps and boxy soldiers. Pick your game folder above.";
            if (!LooksLikeGame(dir)) return $"{dir} is there but has no .hpi archives, so it may not be the game. The engine will try it.";
            real = true;
            return "The real engine runs with your game files.";
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
            GUILayout.Label(EngineInstaller.Message ?? "", EditorStyles.wordWrappedLabel);
            string summary = Summary(out bool real);
            EditorGUILayout.HelpBox(summary, real ? MessageType.Info : MessageType.Warning);
            if (GUILayout.Button("Use these settings now"))
            {
                StudioBackend.Release();
                StudioBackend.PreferMock = false;
                StudioSession.Notify();
            }

            GUILayout.Space(8);
            GUILayout.Label("Tools", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                StudioTargets.CatalogDir = EditorGUILayout.DelayedTextField("Sprite catalog", StudioTargets.CatalogDir);
                if (GUILayout.Button("Browse", GUILayout.Width(64)))
                {
                    string picked = EditorUtility.OpenFolderPanel("The folder extract.py wrote", StudioTargets.CatalogDir, "");
                    if (!string.IsNullOrEmpty(picked)) StudioTargets.CatalogDir = picked;
                }
            }
            GUILayout.Label(File.Exists(Path.Combine(StudioTargets.CatalogDir, "catalog.json"))
                ? "Found catalog.json, the list of the game's sprite features."
                : "No catalog.json there. tools/sprite-replace/extract.py makes one from your game files. The studio works without it.", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                string blender = EditorPrefs.GetString(BlenderPref, "D:/Blender/blender-5.2.2-windows-x64/blender.exe");
                string typed = EditorGUILayout.DelayedTextField("Blender", blender);
                if (typed != blender) EditorPrefs.SetString(BlenderPref, typed);
                if (GUILayout.Button("Browse", GUILayout.Width(64)))
                {
                    string picked = EditorUtility.OpenFilePanel("blender.exe", Path.GetDirectoryName(blender), "exe");
                    if (!string.IsNullOrEmpty(picked)) EditorPrefs.SetString(BlenderPref, picked);
                }
            }
        }

        static void Set(string dir)
        {
            EditorPrefs.SetString(GameDirPref, dir ?? "");
            StudioBackend.Release();
            StudioSession.Notify();
        }
    }
}
