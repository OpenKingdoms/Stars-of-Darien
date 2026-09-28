// CaptureFiles.cs - where captures go and their names: shot-YYYYMMDD-HHMMSS.png,
// clip-YYYYMMDD-HHMMSS/frame-NNN.png with the clip's contact sheet beside the
// folder, and LATEST.txt naming the newest.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenKingdomsUnity.Game.Capture
{
    public static class CaptureFiles
    {
        // Set in OpenKingdoms > Settings. The editor's Play reads it too.
        public const string PrefKey = "oku.capture.dir";
        // Wins over the setting, for scripts and tests.
        public const string EnvKey = "OKU_SHOTS_DIR";
        public const string OwnerDefault = @"D:\OKBuild\owner-shots";
        public const string LatestFile = "LATEST.txt";

        // The folder captures go to now.
        public static string Dir => Resolve(Environment.GetEnvironmentVariable(EnvKey), PlayerPrefs.GetString(PrefKey, ""), OwnerDefault, RepoDir);

        // The first of: the environment, the setting, the owner's folder when
        // it or the folder it sits in is there, the repository's Captures
        // folder, and the player's own data folder.
        public static string Resolve(string env, string setting, string ownerDefault, string repoDir)
        {
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            if (!string.IsNullOrWhiteSpace(setting)) return setting.Trim();
            if (!string.IsNullOrEmpty(ownerDefault))
            {
                string parent = Path.GetDirectoryName(ownerDefault);
                if (Directory.Exists(ownerDefault) || (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))) return ownerDefault;
            }
            if (!string.IsNullOrEmpty(repoDir)) return Path.Combine(repoDir, "Captures");
            return Path.Combine(Application.persistentDataPath, "Captures");
        }

        // The clone's top folder, beside unity/, in the editor. Null in a
        // built player.
        public static string RepoDir
        {
            get
            {
                if (!Application.isEditor) return null;
                string unity = Path.GetDirectoryName(Application.dataPath);
                return string.IsNullOrEmpty(unity) ? null : Path.GetDirectoryName(unity);
            }
        }

        public static string ShotName(DateTime at) => "shot-" + Stamp(at) + ".png";
        public static string ClipName(DateTime at) => "clip-" + Stamp(at);
        public static string FrameName(int number) => $"frame-{number:000}.png";
        public static string Stamp(DateTime at) => at.ToString("yyyyMMdd-HHmmss");

        // Paths handed out whose files are still being written.
        static readonly HashSet<string> taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // A shot's path in dir, with -2, -3 added when that second is taken.
        public static string NewShot(string dir, DateTime at)
        {
            string stem = "shot-" + Stamp(at);
            lock (taken)
                for (int n = 1; ; n++)
                {
                    string path = Path.Combine(dir, n == 1 ? stem + ".png" : $"{stem}-{n}.png");
                    if (!File.Exists(path) && taken.Add(path)) return path;
                }
        }

        // A clip's frame folder and contact sheet, both new.
        public static (string folder, string sheet) NewClip(string dir, DateTime at)
        {
            string stem = ClipName(at);
            lock (taken)
                for (int n = 1; ; n++)
                {
                    string name = n == 1 ? stem : $"{stem}-{n}";
                    string folder = Path.Combine(dir, name), sheet = folder + ".png";
                    if (!Directory.Exists(folder) && !File.Exists(sheet) && taken.Add(sheet)) return (folder, sheet);
                }
        }

        // LATEST.txt in dir holds the newest capture's full path.
        public static void WriteLatest(string dir, string path)
        {
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, LatestFile), temp = file + ".tmp";
            File.WriteAllText(temp, Path.GetFullPath(path) + Environment.NewLine);
            if (File.Exists(file)) File.Delete(file);
            File.Move(temp, file);
        }
    }
}
