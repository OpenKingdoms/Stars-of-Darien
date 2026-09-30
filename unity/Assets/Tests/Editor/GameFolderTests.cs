// GameFolderTests.cs - how a built player finds Total Annihilation: Kingdoms
// (saved choice, OK_GAME_DIR, the installer's record, the usual places, in
// that order) and what it says about a folder that is not the game.
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;

namespace OpenKingdomsUnity.Tests
{
    public class GameFolderTests
    {
        string temp;

        [SetUp]
        public void Before()
        {
            temp = Path.Combine(Path.GetTempPath(), "oku-gamefolder-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(temp);
        }

        [TearDown]
        public void After()
        {
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }

        string Game(string rel, params string[] files)
        {
            string dir = Path.Combine(temp, rel);
            Directory.CreateDirectory(dir);
            foreach (var f in files) File.WriteAllBytes(Path.Combine(dir, f), new byte[] { 1 });
            return GameFolder.Clean(dir);
        }

        static Func<string, bool> Only(params string[] good)
        {
            var set = new HashSet<string>(good, StringComparer.OrdinalIgnoreCase);
            return d => set.Contains(d);
        }

        [Test]
        public void TheSavedChoiceComesFirst()
        {
            string dir = GameFolder.Find("C:/Saved", "C:/Env", new[] { "C:/Reg" }, new[] { "C:/Usual" }, out var by, Only("C:/Saved", "C:/Env", "C:/Reg", "C:/Usual"));
            Assert.AreEqual("C:/Saved", dir);
            Assert.AreEqual(GameFolder.Source.Saved, by);
        }

        [Test]
        public void OkGameDirComesAfterTheSavedChoice()
        {
            string dir = GameFolder.Find("C:/Gone", "C:/Env", new[] { "C:/Reg" }, new[] { "C:/Usual" }, out var by, Only("C:/Env", "C:/Reg", "C:/Usual"));
            Assert.AreEqual("C:/Env", dir);
            Assert.AreEqual(GameFolder.Source.Environment, by);
        }

        [Test]
        public void TheInstallersRecordComesBeforeTheUsualPlaces()
        {
            string dir = GameFolder.Find("", null, new[] { "C:/Nope", "C:/Reg" }, new[] { "C:/Usual" }, out var by, Only("C:/Reg", "C:/Usual"));
            Assert.AreEqual("C:/Reg", dir);
            Assert.AreEqual(GameFolder.Source.Registry, by);
        }

        [Test]
        public void TheUsualPlacesAreTriedInOrder()
        {
            string dir = GameFolder.Find(null, "", null, new[] { "C:/One", "C:/Two", "C:/Three" }, out var by, Only("C:/Two", "C:/Three"));
            Assert.AreEqual("C:/Two", dir);
            Assert.AreEqual(GameFolder.Source.UsualPlace, by);
        }

        [Test]
        public void NothingFoundIsNull()
        {
            Assert.IsNull(GameFolder.Find("C:/A", "C:/B", new[] { "C:/C" }, new[] { "C:/D" }, out var by, Only()));
            Assert.AreEqual(GameFolder.Source.None, by);
        }

        [Test]
        public void FindChecksRealFoldersByDefault()
        {
            string good = Game("gog", "data.hpi", "terrain.hpi");
            string bad = Game("empty");
            Assert.AreEqual(good, GameFolder.Find(bad, null, null, new[] { bad, good }, out var by));
            Assert.AreEqual(GameFolder.Source.UsualPlace, by);
        }

        [Test]
        public void TheUsualPlacesStartWithGogsDefault()
        {
            var places = GameFolder.UsualPlaces();
            Assert.AreEqual("C:/GOG Games/Total Annihilation Kingdoms", places[0]);
            Assert.IsTrue(places.Exists(p => p.EndsWith("Steam/steamapps/common/Total Annihilation Kingdoms")));
            Assert.IsTrue(places.Contains("C:/Cavedog/Kingdoms"));
            Assert.IsTrue(places.TrueForAll(p => p.IndexOf('\\') < 0));
        }

        [Test]
        public void TheRegistryLookupNeverThrows()
        {
            var found = GameFolder.FromRegistry();
            Assert.IsNotNull(found);
            Assert.IsTrue(found.TrueForAll(p => p.IndexOf('\\') < 0));
        }

        [Test]
        public void AFolderWithBothArchivesHoldsTheGame()
        {
            string dir = Game("full", "DATA.HPI", "terrain.hpi", "english.hpi");
            Assert.IsNull(GameFolder.Problem(dir));
            Assert.IsTrue(GameFolder.Holds(dir));
        }

        [Test]
        public void AMissingArchiveIsNamed()
        {
            string dir = Game("half", "data.hpi", "english.hpi");
            string why = GameFolder.Problem(dir);
            StringAssert.Contains("terrain.hpi", why);
            StringAssert.DoesNotContain("data.hpi or", why);
        }

        [Test]
        public void AFolderWithoutArchivesSaysSo()
        {
            string dir = Game("docs-only", "readme.txt");
            StringAssert.Contains("no game archives", GameFolder.Problem(dir));
        }

        [Test]
        public void TheGamesOwnSubfolderPointsUp()
        {
            string dir = Game("TAK/Maps", "map.ufo");
            StringAssert.Contains("Pick the folder above it", GameFolder.Problem(dir));
        }

        [Test]
        public void NoFolderAndNothingTypedSaySo()
        {
            StringAssert.Contains("There is no folder at", GameFolder.Problem(Path.Combine(temp, "missing")));
            Assert.AreEqual(GameFolder.Hint, GameFolder.Problem("  "));
            string file = Path.Combine(temp, "a.txt");
            File.WriteAllText(file, "x");
            StringAssert.Contains("is a file", GameFolder.Problem(file));
        }

        [Test]
        public void ResolveFindsTheGameOneOrTwoFoldersDown()
        {
            string game = Game("GOG Games/Total Annihilation Kingdoms", "data.hpi", "terrain.hpi");
            Game("GOG Games/Another Game", "x.dat");
            Assert.AreEqual(game, GameFolder.Resolve(Path.Combine(temp, "GOG Games")));
            Assert.AreEqual(game, GameFolder.Resolve(temp));
            Assert.AreEqual(game, GameFolder.Resolve(game));
            Assert.IsNull(GameFolder.Resolve(Path.Combine(temp, "GOG Games", "Another Game")));
        }

        [Test]
        public void CleanTidiesAPath()
        {
            Assert.AreEqual("C:/GOG Games/TAK", GameFolder.Clean("  \"C:\\GOG Games\\TAK\\\"  "));
            Assert.AreEqual("D:/", GameFolder.Clean("D:"));
            Assert.AreEqual("D:/", GameFolder.Clean("D:\\"));
        }

        [Test]
        public void AnUninstallCommandGivesItsFolder()
        {
            Assert.AreEqual("C:/GOG Games/Total Annihilation Kingdoms", GameFolder.FolderOfCommand("\"C:\\GOG Games\\Total Annihilation Kingdoms\\unins000.exe\" /SILENT"));
            Assert.AreEqual("C:/Cavedog/Kingdoms", GameFolder.FolderOfCommand("C:\\Cavedog\\Kingdoms\\uninst.exe -y"));
            Assert.IsNull(GameFolder.FolderOfCommand(""));
        }
    }
}
