// EngineSettings.cs - where the player's game files are, and the space
// conversion between the engine and Unity.
using System;
using System.IO;
using UnityEngine;

namespace OpenKingdomsUnity.Engine
{
    public static class EngineSettings
    {
        // The install to read. OK_GAME_DIR and OK_DATA_DIR override these,
        // and the data folder is optional loose files that win over the
        // archives, read only in the editor unless OK_DATA_DIR names one.
        // A Mac keeps the game where OpenKingdoms' own Mac build looks.
        public const string WindowsGameDir = "C:/GOG Games/Total Annihilation Kingdoms";
        public static string MacGameDir => (Environment.GetEnvironmentVariable("HOME") ?? "") + "/Games/Total Annihilation Kingdoms";
        public static string DefaultGameDir => IsMac ? MacGameDir : WindowsGameDir;
        public const string DefaultDataDir = "C:/Projects/TAK-RE/data/extracted";

        // OpenKingdoms > Settings in the editor can name another folder, and
        // OK_GAME_DIR still wins over it. A built player looks for the game
        // itself (FindForPlayer) and is empty when it found none.
        public const string GameDirPref = "oku.gameDir";
        public static string GameDir => Application.isEditor ? FromEnv("OK_GAME_DIR", EditorGameDir() ?? DefaultGameDir) : PlayerGameDir();

        static string found;
        static bool searched;
        public static GameFolder.Source FoundBy { get; private set; }

        static string PlayerGameDir()
        {
            if (!searched) FindForPlayer();
            return found ?? "";
        }

        // The player's saved choice, OK_GAME_DIR, then where Windows and the
        // usual installs put the game. Remembered for the session.
        public static string FindForPlayer()
        {
            searched = true;
            found = GameFolder.Find(PlayerPrefs.GetString(GameDirPref, ""), Environment.GetEnvironmentVariable("OK_GAME_DIR"),
                GameFolder.FromRegistry(), GameFolder.UsualPlaces(), out var by);
            FoundBy = by;
            return found;
        }

        // Keeps the player's pick for this and every later start.
        public static void ChooseGameDir(string dir)
        {
            dir = GameFolder.Clean(dir);
#if UNITY_EDITOR
            if (Application.isEditor) { UnityEditor.EditorPrefs.SetString(GameDirPref, dir ?? ""); return; }
#endif
            PlayerPrefs.SetString(GameDirPref, dir ?? "");
            PlayerPrefs.Save();
            found = dir;
            searched = true;
            FoundBy = GameFolder.Source.Saved;
        }

        static string EditorGameDir()
        {
#if UNITY_EDITOR
            string d = UnityEditor.EditorPrefs.GetString(GameDirPref, "");
            return string.IsNullOrEmpty(d) ? null : d;
#else
            return null;
#endif
        }

        // Why the engine must not be called this session, or null. The
        // editor's engine installer sets it when Unity has another API
        // version of okengine loaded, which only a restart replaces.
        public static string Blocked;
        public static string DataDir
        {
            get
            {
                string d = FromEnv("OK_DATA_DIR", Application.isEditor ? DefaultDataDir : "");
                return Directory.Exists(d) ? d : "";
            }
        }

        // A Mac player keeps its plugins in Contents/PlugIns.
        public static string PluginDir => Application.platform == RuntimePlatform.OSXPlayer
            ? Path.Combine(Application.dataPath, "PlugIns")
            : Path.Combine(Application.dataPath, "Plugins", IsMac ? "macOS" : "x86_64");
        // The engine's file in PluginDir. DllImport("okengine") finds each.
        public static string LibraryFile => IsLinux ? "libokengine.so" : IsMac ? "libokengine.dylib" : "okengine.dll";
        static bool IsLinux => Application.platform == RuntimePlatform.LinuxEditor || Application.platform == RuntimePlatform.LinuxPlayer;
        static bool IsMac => Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer;
        public static string OverrideDir => Path.Combine(Application.streamingAssetsPath, "Overrides");
        // The player's own folder: saved maps (under maps/) and anything
        // else the player adds, mounted over the game's files.
        public static string UserDir => Path.Combine(Application.persistentDataPath, "User");

        // The engine can run when its library and the game files are here.
        public static bool EngineAvailable =>
            Blocked == null && File.Exists(Path.Combine(PluginDir, LibraryFile)) && Directory.Exists(GameDir);

        // Why the engine cannot run and what to do about it, or null.
        public static string Problem
        {
            get
            {
                if (Blocked != null) return Blocked;
                if (!File.Exists(Path.Combine(PluginDir, LibraryFile)))
                    return "The game's engine is not installed. Get the latest project, which carries it in the engine folder, and open it in Unity again.";
                if (!Application.isEditor && GameDir.Length == 0)
                    return "The game did not find your Total Annihilation: Kingdoms. " + GameFolder.Hint;
                if (!Directory.Exists(GameDir))
                    return $"Your Total Annihilation: Kingdoms files are not at {GameDir}. Pick your game folder in OpenKingdoms, then Settings, and press Play again.";
                return null;
            }
        }

        // Destroy in play, DestroyImmediate in the editor.
        public static void Release(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        static string FromEnv(string name, string fallback)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? fallback : v;
        }

        // One Unity unit is one map cell of 16 engine pixels. The engine's z
        // runs south and Unity's north, so z flips.
        public const float PxToUnits = 1f / 16f;

        public static Vector3 ToUnity(float x, float y, float z) =>
            new Vector3(x * PxToUnits, y * PxToUnits, -z * PxToUnits);

        public static Vector2 ToEngine(Vector3 p) => new Vector2(p.x / PxToUnits, -p.z / PxToUnits);

        // A row major 3x4 engine matrix at m[o], node space to engine
        // pixels, as a Unity matrix from node space to Unity world.
        public static Matrix4x4 PoseToUnity(float[] m, int o)
        {
            const float s = PxToUnits;
            var r = new Matrix4x4();
            r.m00 = m[o + 0] * s;  r.m01 = m[o + 1] * s;  r.m02 = m[o + 2] * s;  r.m03 = m[o + 3] * s;
            r.m10 = m[o + 4] * s;  r.m11 = m[o + 5] * s;  r.m12 = m[o + 6] * s;  r.m13 = m[o + 7] * s;
            r.m20 = -m[o + 8] * s; r.m21 = -m[o + 9] * s; r.m22 = -m[o + 10] * s; r.m23 = -m[o + 11] * s;
            r.m33 = 1;
            return r;
        }
    }
}
