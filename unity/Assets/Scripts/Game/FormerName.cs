// FormerName.cs - the game was called Darien Reforged until Alpha 1. Unity
// keeps a player's data folder and PlayerPrefs under the product's name, so
// the first start under the new name copies them across. The old data is
// never deleted.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public static class FormerName
    {
        public const string Title = "Darien Reforged";
        // Written in the new folder, and set in the new PlayerPrefs, once
        // the copy is made, so a save or a setting removed later stays gone.
        public const string CopiedMark = "COPIED.txt";
        public const string CopiedPref = "oku.copiedFrom";

        // The game's own folders. Player.log and Unity's caches stay behind.
        static readonly string[] Parts = { "Saves", "User", "Captures" };

        // Before anything reads a PlayerPref. Test and smoke runs in batch
        // mode leave the player's real data alone.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void CarryOver()
        {
            if (Application.isBatchMode) return;
            string now = Application.persistentDataPath;
            string parent = Path.GetDirectoryName(now);
            if (parent == null) return;
            CopyData(Path.Combine(parent, Title), now);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (PlayerPrefs.HasKey(CopiedPref)) return;
            string key = (Application.isEditor ? @"Software\Unity\UnityEditor\" : @"Software\") + Application.companyName + @"\" + Title;
            var old = ReadWindowsPrefs(key);
            if (old == null) return;
            CopyPrefs(old, PlayerPrefs.HasKey, SetPref);
            PlayerPrefs.Save();
#endif
        }

        static void SetPref(string key, object value)
        {
            if (value is int i) PlayerPrefs.SetInt(key, i);
            else if (value is float f) PlayerPrefs.SetFloat(key, f);
            else if (value is string s) PlayerPrefs.SetString(key, s);
        }

        // Copies the game's own files from old to now, once. Whatever is
        // under the new name before then came from a batch run, a test or a
        // smoke, which never copies, so the old files win. Returns the files
        // copied.
        public static int CopyData(string old, string now)
        {
            int copied = 0;
            try
            {
                if (!Directory.Exists(old) || File.Exists(Path.Combine(now, CopiedMark))) return 0;
                if (string.Equals(Path.GetFullPath(old), Path.GetFullPath(now), StringComparison.OrdinalIgnoreCase)) return 0;
                foreach (var part in Parts)
                {
                    string from = Path.Combine(old, part);
                    if (!Directory.Exists(from)) continue;
                    foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                    {
                        string to = Path.Combine(now, file.Substring(old.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(to));
                        File.Copy(file, to, true);
                        copied++;
                    }
                }
                Directory.CreateDirectory(now);
                File.WriteAllText(Path.Combine(now, CopiedMark), "Copied from " + old + Environment.NewLine);
            }
            catch (Exception e) { Debug.LogWarning("The saves kept under " + Title + " were not copied: " + e.Message); }
            return copied;
        }

        // The game's own settings from the old PlayerPrefs, once, and like
        // the files they win over what a batch run left. A pending map start
        // is an order, not a setting. Returns the settings copied.
        public static int CopyPrefs(IDictionary<string, object> old, Func<string, bool> has, Action<string, object> set)
        {
            if (old == null || has(CopiedPref)) return 0;
            int copied = 0;
            foreach (var kv in old)
            {
                if (!kv.Key.StartsWith("oku.", StringComparison.Ordinal) || kv.Key == GameRoot.AutoStartKey || kv.Key == CopiedPref) continue;
                set(kv.Key, kv.Value);
                copied++;
            }
            set(CopiedPref, Title);
            return copied;
        }

        // PlayerPrefs under a key of HKEY_CURRENT_USER, by name without
        // Unity's _h<hash> ending, or null when there is no such key. Unity
        // writes an int as a four byte DWORD, a float as a double in an eight
        // byte one, and a string as UTF-8 ending in a zero byte.
        public static Dictionary<string, object> ReadWindowsPrefs(string subKey)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                if (RegOpenKeyEx(CurrentUser, subKey, 0, KeyRead, out var key) != 0) return null;
                var prefs = new Dictionary<string, object>();
                try
                {
                    var name = new StringBuilder(16384);
                    var data = new byte[1024];
                    for (int i = 0; ; i++)
                    {
                        int chars = name.Capacity, bytes = data.Length;
                        int r = RegEnumValue(key, i, name, ref chars, IntPtr.Zero, out int type, data, ref bytes);
                        if (r == MoreData) { data = new byte[Math.Max(bytes, data.Length * 2)]; i--; continue; }
                        if (r != 0) break;
                        string pref = PrefName(name.ToString());
                        object value = Decode(type, data, bytes);
                        if (pref != null && value != null) prefs[pref] = value;
                    }
                }
                finally { RegCloseKey(key); }
                return prefs;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException) { return null; }
#else
            return null;
#endif
        }

        static string PrefName(string value)
        {
            int h = value.LastIndexOf("_h", StringComparison.Ordinal);
            if (h <= 0 || h + 2 >= value.Length) return null;
            for (int i = h + 2; i < value.Length; i++)
                if (value[i] < '0' || value[i] > '9') return null;
            return value.Substring(0, h);
        }

        static object Decode(int type, byte[] data, int bytes)
        {
            if ((type == Dword || type == Qword) && bytes == 8) return (float)BitConverter.ToDouble(data, 0);
            if (type == Dword && bytes == 4) return BitConverter.ToInt32(data, 0);
            if (type == Binary)
            {
                int n = bytes;
                while (n > 0 && data[n - 1] == 0) n--;
                return Encoding.UTF8.GetString(data, 0, n);
            }
            return null;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        const int Binary = 3, Dword = 4, Qword = 11, KeyRead = 0x20019, MoreData = 234;
        static readonly IntPtr CurrentUser = new IntPtr(unchecked((int)0x80000001));

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegOpenKeyExW")]
        static extern int RegOpenKeyEx(IntPtr key, string subKey, int options, int access, out IntPtr result);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegEnumValueW")]
        static extern int RegEnumValue(IntPtr key, int index, StringBuilder name, ref int nameChars, IntPtr reserved, out int type, byte[] data, ref int bytes);
        [DllImport("advapi32.dll")]
        static extern int RegCloseKey(IntPtr key);
#else
        const int Binary = 3, Dword = 4, Qword = 11;
#endif
    }
}
