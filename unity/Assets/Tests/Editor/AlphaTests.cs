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
    }
}
