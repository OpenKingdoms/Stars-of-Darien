// ExeVersionTests.cs - the alpha build writes the game's name, company and
// version over the properties of Unity's player exe.
using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace OpenKingdomsUnity.Studio.Tests
{
    public class ExeVersionTests
    {
        [Test]
        public void ThePlayerExeCarriesTheGamesNameCompanyAndVersion()
        {
            string player = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "windowsstandalonesupport", "Variations",
                "win64_player_nondevelopment_mono", "WindowsPlayer.exe");
            if (!File.Exists(player)) Assert.Ignore("no Windows player in this editor");
            string temp = Path.Combine(Path.GetTempPath(), "oku-exe-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(temp);
            try
            {
                string exe = Path.Combine(temp, "Stars of Darien.exe");
                File.Copy(player, exe);
                string copyright = FileVersionInfo.GetVersionInfo(exe).LegalCopyright;
                Assert.IsNull(ExeVersion.Stamp(exe, "Stars of Darien", "OpenKingdoms", "Alpha 1"));
                var read = FileVersionInfo.GetVersionInfo(exe);
                Assert.AreEqual("Stars of Darien", read.ProductName);
                Assert.AreEqual("Stars of Darien", read.FileDescription);
                Assert.AreEqual("OpenKingdoms", read.CompanyName);
                Assert.AreEqual("Alpha 1", read.ProductVersion);
                Assert.AreEqual("Alpha 1", read.FileVersion);
                Assert.AreEqual("Stars of Darien.exe", read.OriginalFilename);
                Assert.AreEqual(0, read.FileMajorPart);
                Assert.AreEqual(1, read.FileMinorPart, "Alpha 1 is 0.1");
                Assert.AreEqual(copyright, read.LegalCopyright, "Unity's notice stays");
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
