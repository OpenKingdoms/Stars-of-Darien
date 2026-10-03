// FormerNameTests.cs - the first start under the new name copies the saves
// and options kept under the old one, here on temp folders and a temp key.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class FormerNameTests
    {
        [Test]
        public void TheGamesOwnFilesAreCopiedOnceAndTheOldOnesStay()
        {
            string temp = Path.Combine(Path.GetTempPath(), "oku-former-" + Guid.NewGuid().ToString("N"));
            string old = Path.Combine(temp, "OpenKingdoms", FormerName.Title), now = Path.Combine(temp, "OpenKingdoms", "Stars of Darien");
            void Put(string path, string text = "old") { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text); }
            try
            {
                Put(Path.Combine(old, "Saves", "a.oksav"));
                Put(Path.Combine(old, "Saves", "b.oksav"));
                Put(Path.Combine(old, "User", "maps", "mine.tnt"));
                Put(Path.Combine(old, "Captures", "shot.png"));
                Put(Path.Combine(old, "Player.log"));
                Put(Path.Combine(old, "Unity", "cache.bin"));
                Put(Path.Combine(now, "Saves", "b.oksav"), "new");
                Assert.AreEqual(4, FormerName.CopyData(old, now));
                Assert.AreEqual("old", File.ReadAllText(Path.Combine(now, "Saves", "a.oksav")));
                Assert.AreEqual("old", File.ReadAllText(Path.Combine(now, "Saves", "b.oksav")), "what a batch run left under the new name gives way");
                Assert.IsTrue(File.Exists(Path.Combine(now, "User", "maps", "mine.tnt")));
                Assert.IsTrue(File.Exists(Path.Combine(now, "Captures", "shot.png")));
                Assert.IsFalse(File.Exists(Path.Combine(now, "Player.log")), "Unity's own files are not the game's");
                Assert.IsFalse(File.Exists(Path.Combine(now, "Unity", "cache.bin")));
                Assert.AreEqual(6, Directory.GetFiles(old, "*", SearchOption.AllDirectories).Length, "the old folder keeps everything and gains nothing");

                File.Delete(Path.Combine(now, "Saves", "a.oksav"));
                Assert.AreEqual(0, FormerName.CopyData(old, now), "once only");
                Assert.IsFalse(File.Exists(Path.Combine(now, "Saves", "a.oksav")), "a save deleted after the copy stays deleted");

                string fresh = Path.Combine(temp, "fresh");
                Assert.AreEqual(0, FormerName.CopyData(Path.Combine(temp, "nowhere"), fresh));
                Assert.IsFalse(File.Exists(Path.Combine(fresh, FormerName.CopiedMark)), "no old folder, nothing marked");
            }
            finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
        }

        [Test]
        public void TheOldOptionsAreReadFromTheRegistryAndCopiedOnce()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor) Assert.Ignore("PlayerPrefs live in the registry only on Windows");
            string key = Parent + @"\former-" + Guid.NewGuid().ToString("N");
            Assert.IsNull(FormerName.ReadWindowsPrefs(key), "no key, nothing read");
            try
            {
                // As Unity writes them: an int in four bytes, a float as a
                // double in eight, a string as UTF-8 ending in a zero byte.
                Write(key, "oku.speed_h1880094045", Dword, BitConverter.GetBytes(2));
                Write(key, "oku.volume_h1767059186", Dword, BitConverter.GetBytes((double)0.25f));
                Write(key, "oku.gameDir_h1234", Binary, Utf8z(@"D:\Games\TA Kingdoms"));
                Write(key, "oku.mp.name_h99", Binary, Utf8z("Zoë"));
                Write(key, "oku.music_h7", Dword, BitConverter.GetBytes(0));
                Write(key, "oku.autostart.map_h5", Binary, Utf8z("Small"));
                Write(key, "unity.cloud_userid_h2665564582", Binary, Utf8z("someone"));
                Write(key, "Screenmanager Resolution Width_h182942802", Dword, BitConverter.GetBytes(1920));

                var read = FormerName.ReadWindowsPrefs(key);
                Assert.IsNotNull(read);
                Assert.AreEqual(2, read["oku.speed"]);
                Assert.AreEqual(0.25f, read["oku.volume"]);
                Assert.AreEqual(@"D:\Games\TA Kingdoms", read["oku.gameDir"]);
                Assert.AreEqual("Zoë", read["oku.mp.name"]);

                var now = new Dictionary<string, object> { ["oku.music"] = 1 };
                Assert.AreEqual(5, FormerName.CopyPrefs(read, now.ContainsKey, (k, v) => now[k] = v));
                Assert.AreEqual(2, now["oku.speed"]);
                Assert.AreEqual(0.25f, now["oku.volume"]);
                Assert.AreEqual(@"D:\Games\TA Kingdoms", now["oku.gameDir"], "the game folder setting comes across");
                Assert.AreEqual("Zoë", now["oku.mp.name"]);
                Assert.AreEqual(0, now["oku.music"], "what a test or smoke run left under the new name gives way");
                Assert.IsFalse(now.ContainsKey("oku.autostart.map"), "a one-off start order is not a setting");
                Assert.IsFalse(now.ContainsKey("unity.cloud_userid"), "Unity's own values are not the game's");
                Assert.IsFalse(now.ContainsKey("Screenmanager Resolution Width"));
                Assert.AreEqual(FormerName.Title, now[FormerName.CopiedPref]);

                now.Remove("oku.speed");
                Assert.AreEqual(0, FormerName.CopyPrefs(FormerName.ReadWindowsPrefs(key), now.ContainsKey, (k, v) => now[k] = v), "once only");
                Assert.IsFalse(now.ContainsKey("oku.speed"), "a setting cleared after the copy stays cleared");
                Assert.AreEqual(8, FormerName.ReadWindowsPrefs(key).Count, "the old key keeps every value");

                var fresh = new Dictionary<string, object>();
                Assert.AreEqual(0, FormerName.CopyPrefs(null, fresh.ContainsKey, (k, v) => fresh[k] = v));
                Assert.AreEqual(0, fresh.Count, "no old key, nothing marked");
            }
            finally
            {
                RegDeleteTree(CurrentUser, key);
                RegDeleteKey(CurrentUser, Parent);
            }
        }

        const string Parent = @"Software\OpenKingdomsTests";
        const int Dword = 4, Binary = 3, KeyWrite = 0x20006;
        static readonly IntPtr CurrentUser = new IntPtr(unchecked((int)0x80000001));

        static void Write(string subKey, string name, int type, byte[] data)
        {
            Assert.AreEqual(0, RegCreateKeyEx(CurrentUser, subKey, 0, null, 0, KeyWrite, IntPtr.Zero, out var k, out _));
            try { Assert.AreEqual(0, RegSetValueEx(k, name, 0, type, data, data.Length)); }
            finally { RegCloseKey(k); }
        }

        static byte[] Utf8z(string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            Array.Resize(ref b, b.Length + 1);
            return b;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegCreateKeyExW")]
        static extern int RegCreateKeyEx(IntPtr key, string subKey, int reserved, string cls, int options, int access, IntPtr security, out IntPtr result, out int disposition);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegSetValueExW")]
        static extern int RegSetValueEx(IntPtr key, string name, int reserved, int type, byte[] data, int bytes);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegDeleteTreeW")]
        static extern int RegDeleteTree(IntPtr key, string subKey);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegDeleteKeyW")]
        static extern int RegDeleteKey(IntPtr key, string subKey);
        [DllImport("advapi32.dll")]
        static extern int RegCloseKey(IntPtr key);
    }
}
