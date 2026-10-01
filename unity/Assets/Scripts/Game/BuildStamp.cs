// BuildStamp.cs - what a built player says it is: its version, such as
// "Alpha 1 (b2b61d6)", whether it offers skirmish only, and whether it
// carries the map editor. The build writes Resources/BuildStamp.txt for
// the player, and the editor has none.
using System;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public static class BuildStamp
    {
        public const string ResourceName = "BuildStamp";

        static bool read;
        static string version;
        static bool skirmishOnly;
        static bool mapEditor = true;

        // Null in the editor and in a build without a stamp.
        public static string Version
        {
            get { Read(); return version; }
            set { read = true; version = value; }
        }

        // Multiplayer is closed: the alpha plays against the computer only.
        public static bool SkirmishOnly
        {
            get { Read(); return skirmishOnly; }
            set { read = true; skirmishOnly = value; }
        }

        // The map editor is in the game. An alpha leaves it out.
        public static bool MapEditor
        {
            get { Read(); return mapEditor; }
            set { read = true; mapEditor = value; }
        }

        // Forget what was read or set, for tests.
        public static void Reset()
        {
            read = false;
            version = null;
            skirmishOnly = false;
            mapEditor = true;
        }

        static void Read()
        {
            if (read) return;
            read = true;
            var t = Resources.Load<TextAsset>(ResourceName);
            if (t != null) Parse(t.text, out version, out skirmishOnly, out mapEditor);
        }

        // The stamp's lines are key=value. A stamp without mapEditor carries it.
        public static string Format(string version, bool skirmishOnly, bool mapEditor = true) =>
            "version=" + (version ?? "") + "\nskirmishOnly=" + (skirmishOnly ? "1" : "0") + "\nmapEditor=" + (mapEditor ? "1" : "0") + "\n";

        public static void Parse(string text, out string version, out bool skirmishOnly) =>
            Parse(text, out version, out skirmishOnly, out _);

        public static void Parse(string text, out string version, out bool skirmishOnly, out bool mapEditor)
        {
            version = null;
            skirmishOnly = false;
            mapEditor = true;
            foreach (var raw in (text ?? "").Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                if (key == "version" && value.Length > 0) version = value;
                else if (key == "skirmishOnly") skirmishOnly = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                else if (key == "mapEditor") mapEditor = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        // The first line of Player.log after Unity's own.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void LogStart()
        {
            Debug.Log($"{GameRoot.Title} {Version ?? "development build"}, Unity {Application.unityVersion}, {SystemInfo.operatingSystem}, " +
                $"{SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), {SystemInfo.systemMemorySize} MB");
        }
    }
}
