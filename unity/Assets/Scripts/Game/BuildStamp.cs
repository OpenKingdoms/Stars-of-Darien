// BuildStamp.cs - what a built player says it is: its version, such as
// "Alpha 1 (b2b61d6)", and whether it offers skirmish only. The build
// writes Resources/BuildStamp.txt for the player, and the editor has none.
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

        // Forget what was read or set, for tests.
        public static void Reset()
        {
            read = false;
            version = null;
            skirmishOnly = false;
        }

        static void Read()
        {
            if (read) return;
            read = true;
            var t = Resources.Load<TextAsset>(ResourceName);
            if (t != null) Parse(t.text, out version, out skirmishOnly);
        }

        // The stamp's lines are key=value.
        public static string Format(string version, bool skirmishOnly) =>
            "version=" + (version ?? "") + "\nskirmishOnly=" + (skirmishOnly ? "1" : "0") + "\n";

        public static void Parse(string text, out string version, out bool skirmishOnly)
        {
            version = null;
            skirmishOnly = false;
            foreach (var raw in (text ?? "").Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                if (key == "version" && value.Length > 0) version = value;
                else if (key == "skirmishOnly") skirmishOnly = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
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
