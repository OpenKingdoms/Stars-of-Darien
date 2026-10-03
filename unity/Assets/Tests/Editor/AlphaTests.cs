// AlphaTests.cs - the build stamp a player reads, and the smoke run's
// command line, map and seats.
using System.Collections.Generic;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class AlphaTests
    {
        [TearDown]
        public void After() => BuildStamp.Reset();

        [Test]
        public void TheStampReadsBackWhatTheBuildWrote()
        {
            BuildStamp.Parse(BuildStamp.Format("Alpha 1 (b2b61d6)", true), out var version, out bool only);
            Assert.AreEqual("Alpha 1 (b2b61d6)", version);
            Assert.IsTrue(only);
            BuildStamp.Parse(BuildStamp.Format("Alpha 2 (abc)", false), out version, out only);
            Assert.AreEqual("Alpha 2 (abc)", version);
            Assert.IsFalse(only);
            BuildStamp.Parse(BuildStamp.Format("Alpha 2 (abc)", true, false), out version, out only, out bool editor);
            Assert.IsTrue(only);
            Assert.IsFalse(editor, "an alpha leaves the map editor out");
            BuildStamp.Parse(BuildStamp.Format("Alpha 2 (abc)", true), out version, out only, out editor);
            Assert.IsTrue(editor);
            BuildStamp.Parse("version=Alpha 1 (b2b61d6)\nskirmishOnly=1\n", out version, out only, out editor);
            Assert.IsTrue(editor, "a stamp from before the key keeps the editor");
        }

        [Test]
        public void AnEmptyStampIsADevelopmentBuild()
        {
            BuildStamp.Parse("", out var version, out bool only);
            Assert.IsNull(version);
            Assert.IsFalse(only);
            BuildStamp.Parse("junk\r\nversion=\r\n", out version, out only);
            Assert.IsNull(version);
        }

        [Test]
        public void TheEditorHasNoStamp()
        {
            BuildStamp.Reset();
            Assert.IsNull(BuildStamp.Version);
            Assert.IsFalse(BuildStamp.SkirmishOnly);
            Assert.IsTrue(BuildStamp.MapEditor);
        }

        [Test]
        public void TheSmokeFlagTakesItsSeconds()
        {
            Assert.IsNull(SmokeRun.Seconds(new[] { "game.exe", "-batchmode" }));
            Assert.AreEqual(30f, SmokeRun.Seconds(new[] { "game.exe", "-okSmoke", "30" }));
            Assert.AreEqual(SmokeRun.DefaultSeconds, SmokeRun.Seconds(new[] { "game.exe", "-oksmoke" }));
            Assert.AreEqual(SmokeRun.DefaultSeconds, SmokeRun.Seconds(new[] { "game.exe", "-okSmoke", "-nographics" }));
            Assert.AreEqual("Darien Pass", SmokeRun.MapArg(new[] { "-okSmokeMap", "Darien Pass" }));
            Assert.IsNull(SmokeRun.MapArg(new[] { "-okSmokeMap" }));
        }

        [Test]
        public void SmokeViewsReadLabelAnchorNearAndRadius()
        {
            var v = SmokeRun.ViewsArg(new[] { "-okSmokeViews", "henge:*mana*:*henge*:8;bad;ruin:arawell01a,aratow04a:*" });
            Assert.AreEqual(2, v.Count, "a view without an anchor and a near is skipped");
            Assert.AreEqual("henge", v[0].Label);
            Assert.AreEqual(8f, v[0].Radius);
            Assert.AreEqual(8f, v[1].Radius, "the radius defaults to 8");
            Assert.IsTrue(SceneryViews.Matches("AraMana02", v[0].Anchor));
            Assert.IsFalse(SceneryViews.Matches("AraHenge07", v[0].Anchor));
            Assert.IsTrue(SceneryViews.Matches("AraTow04a", v[1].Anchor), "choices split by commas, any case");
            Assert.IsFalse(SceneryViews.Matches("AraTow04", v[1].Anchor));
            CollectionAssert.IsEmpty(SmokeRun.ViewsArg(new[] { "game.exe" }));
        }

        static MapInfo Map(string id, float size, int players) =>
            new MapInfo { Id = id, Name = id, MaxPlayers = players, Size = new Vector2(size, size), Starts = new Vector2[players] };

        [Test]
        public void TheSmokeRunTakesTheSmallestMapForTwo()
        {
            var maps = new List<MapInfo> { Map("Big", 256, 4), Map("Tiny solo", 32, 1), Map("Small b", 64, 2), Map("Small a", 64, 2) };
            Assert.AreEqual("Small a", SmokeRun.PickMap(maps, null).Id);
            Assert.AreEqual("Big", SmokeRun.PickMap(maps, "big").Id);
            Assert.IsNull(SmokeRun.PickMap(maps, "Nowhere"));
            Assert.IsNull(SmokeRun.PickMap(new List<MapInfo> { Map("Solo", 32, 1) }, null));
        }

        [Test]
        public void TheSmokeRunSeatsYouAndOneComputer()
        {
            var mock = new MockBackend();
            var s = GameRoot.DefaultSetup(mock);
            s.Seats[2].Kind = SeatKind.Computer;
            s.Seats[0].Start = 3;
            SmokeRun.Seat(s, Map("Small", 64, 2));
            Assert.AreEqual("Small", s.MapId);
            Assert.AreEqual(SeatKind.Human, s.Seats[0].Kind);
            Assert.AreEqual(SeatKind.Computer, s.Seats[1].Kind);
            for (int i = 2; i < s.Seats.Count; i++) Assert.AreEqual(SeatKind.Closed, s.Seats[i].Kind);
            Assert.AreEqual(-1, s.Seats[0].Start);
            mock.Dispose();
        }

        [Test]
        public void OnlyTheGamesOwnOldSavesMoveAndOnlyOnce()
        {
            string temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oku-move-" + System.Guid.NewGuid().ToString("N"));
            string old = System.IO.Path.Combine(temp, "DefaultCompany", "unity"), now = System.IO.Path.Combine(temp, "OpenKingdoms", "Stars of Darien");
            void Put(string path) { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)); System.IO.File.WriteAllText(path, "x"); }
            try
            {
                Put(System.IO.Path.Combine(old, "Saves", "a.oksav"));
                Put(System.IO.Path.Combine(old, "User", "maps", "mine.tnt"));
                Put(System.IO.Path.Combine(old, "TestResults.xml"));
                Put(System.IO.Path.Combine(old, "Another game", "save.dat"));
                Assert.AreEqual(2, GameRoot.MoveOldData(old, now));
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(now, "Saves", "a.oksav")));
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(now, "User", "maps", "mine.tnt")));
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(old, "TestResults.xml")), "what is not the game's stays");
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(old, "Another game", "save.dat")));
                Assert.IsFalse(System.IO.File.Exists(System.IO.Path.Combine(now, GameRoot.MovedMark)), "the mark stays behind");
                Put(System.IO.Path.Combine(old, "Saves", "b.oksav"));
                Assert.AreEqual(0, GameRoot.MoveOldData(old, now), "once only");
                Assert.AreEqual(0, GameRoot.MoveOldData(System.IO.Path.Combine(temp, "nowhere"), now));
            }
            finally { if (System.IO.Directory.Exists(temp)) System.IO.Directory.Delete(temp, true); }
        }

        [Test]
        public void AWindowFitsOnTheScreen()
        {
            Assert.AreEqual(new Vector2Int(1536, 864), GameOptions.WindowSize(1920, 1080));
            Assert.AreEqual(new Vector2Int(3072, 1728), GameOptions.WindowSize(3840, 2160));
            Assert.AreEqual(new Vector2Int(1024, 614), GameOptions.WindowSize(1280, 768), "even pixels");
            Assert.AreEqual(new Vector2Int(640, 480), GameOptions.WindowSize(700, 500), "never smaller than the original's screen");
            Assert.AreEqual(new Vector2Int(600, 400), GameOptions.WindowSize(600, 400), "nor larger than the screen");
        }

        [Test]
        public void AFastQaRunPutsBackThePlayersSpeed()
        {
            int saved = PlayerPrefs.GetInt("oku.speed", 1);
            try
            {
                GameOptions.SaveSpeed(1);
                var o = GameOptions.Load();
                int before = QaRun.Speed(o, true, 0);
                Assert.AreEqual(2, o.GameSpeed, "the run plays fast");
                Assert.AreEqual(before, QaRun.Speed(o, true, before), "a second fast start keeps the first speed");
                o.Save();
                Assert.AreEqual(0, QaRun.Speed(o, false, before));
                Assert.AreEqual(1, o.GameSpeed);
                Assert.AreEqual(1, GameOptions.Load().GameSpeed, "a screen saved the fast speed, and it is undone");
            }
            finally { GameOptions.SaveSpeed(saved); }
        }

        [Test]
        public void TheQaFlagsTakeTheirValues()
        {
            Assert.IsNull(QaRun.SoakMinutes(new[] { "game.exe", "-okSmoke", "20" }));
            Assert.AreEqual(5f, QaRun.SoakMinutes(new[] { "game.exe", "-okSoak", "5" }));
            Assert.AreEqual(QaRun.DefaultSoakMinutes, QaRun.SoakMinutes(new[] { "game.exe", "-okSoak", "-batchmode" }));
            Assert.AreEqual(12, QaRun.SweepFrom(new[] { "-okMapSweep", "D:/qa/maps.csv", "-okSweepFrom", "12" }));
            Assert.AreEqual(0, QaRun.SweepFrom(new[] { "-okSweepFrom", "x" }));
            Assert.IsNull(QaRun.SweepOnly(new[] { "-okMapSweep", "maps.csv" }));
            var only = QaRun.SweepOnly(new[] { "-okSweepOnly", "yew wood; Zhorl Valley;" });
            Assert.AreEqual(2, only.Count);
            Assert.IsTrue(only.Contains("zhorl valley"), "names in any case");
            Assert.AreEqual("D:/qa/maps.csv", QaRun.Arg(new[] { "-okmapsweep", "D:/qa/maps.csv" }, QaRun.SweepFlag));
            Assert.IsNull(QaRun.Arg(new[] { "-okMapSweep", "-batchmode" }, QaRun.SweepFlag), "a flag is never a value");
        }

        [Test]
        public void TheSoakTakesTheLargestMapForEightAndFillsEverySeat()
        {
            var maps = new List<MapInfo> { Map("Huge four", 512, 4), Map("Big b", 256, 8), Map("Big a", 256, 8), Map("Small eight", 64, 8) };
            Assert.AreEqual("Big a", QaRun.LargestFor(maps, 8).Id);
            Assert.IsNull(QaRun.LargestFor(maps, 10));
            var mock = new MockBackend();
            var s = GameRoot.DefaultSetup(mock);
            QaRun.SeatAll(s, maps[1], QaRun.SoakSeats);
            Assert.AreEqual(QaRun.SoakSeats, s.Seats.Count);
            Assert.AreEqual(SeatKind.Human, s.Seats[0].Kind, "the engine plays seat 0 as you");
            for (int i = 1; i < s.Seats.Count; i++)
            {
                Assert.AreEqual(SeatKind.Computer, s.Seats[i].Kind);
                Assert.AreEqual(i == 1 ? s.Seats[0].Team : i, s.Seats[i].Team, "the first guards you, the rest each its own team");
            }
            Assert.IsTrue(s.MapRevealed);
            Assert.IsFalse(s.LineOfSight);
            mock.Dispose();
        }

        [Test]
        public void QaRowsKeepCommasAndCountWhatIsLogged()
        {
            Assert.AreEqual("plain", QaRun.Field("plain"));
            Assert.AreEqual("\"a, \"\"b\"\"\"", QaRun.Field("a, \"b\""));
            Assert.AreEqual("one two", QaRun.Field("one\ntwo"));
            var t = new QaRun.Tally();
            t.Hear("fine", "", LogType.Log);
            t.Hear(QaRun.Prefix + "our own line", "", LogType.Warning);
            t.Hear("a warning", "", LogType.Warning);
            t.Hear("boom", "at x", LogType.Exception);
            t.Hear("boom", "at x", LogType.Exception);
            t.Hear("bad", "", LogType.Error);
            Assert.AreEqual(1, t.Warnings);
            Assert.AreEqual(2, t.Exceptions);
            Assert.AreEqual(1, t.Errors);
            Assert.AreEqual("Exception: boom", t.First, "the first error or exception beats a warning");
            Assert.AreEqual(2, t.Lines["Exception: boom"]);
            t.Reset();
            Assert.AreEqual(0, t.Exceptions);
            Assert.IsNull(t.First);
        }
    }
}
