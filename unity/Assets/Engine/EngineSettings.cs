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
        // archives.
        public const string DefaultGameDir = "C:/GOG Games/Total Annihilation Kingdoms";
        public const string DefaultDataDir = "C:/Projects/TAK-RE/data/extracted";

        public static string GameDir => FromEnv("OK_GAME_DIR", DefaultGameDir);
        public static string DataDir
        {
            get
            {
                string d = FromEnv("OK_DATA_DIR", DefaultDataDir);
                return Directory.Exists(d) ? d : "";
            }
        }

        public static string PluginDir => Path.Combine(Application.dataPath, "Plugins", "x86_64");
        public static string OverrideDir => Path.Combine(Application.streamingAssetsPath, "Overrides");
        // The player's own folder: saved maps (under maps/) and anything
        // else the player adds, mounted over the game's files.
        public static string UserDir => Path.Combine(Application.persistentDataPath, "User");

        // The engine can run when its library and the game files are here.
        public static bool EngineAvailable =>
            File.Exists(Path.Combine(PluginDir, "okengine.dll")) && Directory.Exists(GameDir);

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
