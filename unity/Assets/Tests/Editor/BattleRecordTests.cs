// BattleRecordTests.cs - what the end screen works out from a battle's
// record: the honours, the graphs' scales, income from mana gathered, the
// original's clock, and the mock's own record of a battle it fights.
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class BattleRecordTests
    {
        static KingdomRecord K(int player, int kills = 0, int built = 0, float gathered = 0, int losses = 0, int fell = 0)
        {
            var k = new KingdomRecord { Player = player, Kills = kills, UnitsBuilt = built, ManaGathered = gathered, Losses = losses, FellTick = fell };
            for (int s = 0; s < k.Series.Length; s++) k.Series[s] = new int[0];
            return k;
        }

        [Test]
        public void EachHonourGoesToTheOneKingdomThatEarnedIt()
        {
            var r = new BattleRecord { TicksPerSecond = 60 };
            r.Kingdoms.Add(K(1, kills: 12, built: 30, gathered: 9000, losses: 4));
            r.Kingdoms.Add(K(2, kills: 7, built: 45, gathered: 12000, losses: 9));
            r.Kingdoms.Add(K(3, kills: 2, built: 10, gathered: 3000, losses: 20, fell: 5000));
            r.Kingdoms[1].ChampionDef = 5;
            r.Kingdoms[1].ChampionKills = 6;
            r.Moments.Add(new BattleMoment { Tick = 3600, Kind = MomentKind.FirstBlood, Player = 2, Other = 3 });
            var h = Honours.Award(r).ToDictionary(x => x.Title);
            Assert.AreEqual(1, h["Warlord"].Player);
            Assert.AreEqual("12 kills", h["Warlord"].Reason);
            Assert.AreEqual(2, h["Master builder"].Player);
            Assert.AreEqual(2, h["Treasurer"].Player);
            Assert.AreEqual("12,000 mana gathered", h["Treasurer"].Reason);
            Assert.AreEqual(1, h["Unbroken"].Player, "the fewest losses among the kingdoms still standing");
            Assert.AreEqual(2, h["First blood"].Player);
            Assert.AreEqual("drew it at 1:00", h["First blood"].Reason);
            Assert.AreEqual(2, h["Champion"].Player);
        }

        [Test]
        public void ATieGoesToNobodyAndNothingEarnedIsLeftOut()
        {
            var r = new BattleRecord();
            r.Kingdoms.Add(K(1, kills: 5, built: 3));
            r.Kingdoms.Add(K(2, kills: 5, built: 1));
            var titles = Honours.Award(r).Select(x => x.Title).ToList();
            CollectionAssert.DoesNotContain(titles, "Warlord", "two kingdoms tied on kills");
            CollectionAssert.Contains(titles, "Master builder");
            CollectionAssert.DoesNotContain(titles, "Treasurer", "nobody gathered anything");
            CollectionAssert.DoesNotContain(titles, "First blood");
            CollectionAssert.DoesNotContain(titles, "Champion");
        }

        [Test]
        public void TheGraphsCountInStepsAReaderTakesInAtAGlance()
        {
            Assert.AreEqual(1f, GraphScale.Step(3f));
            Assert.AreEqual(5f, GraphScale.Step(17f));
            Assert.AreEqual(500f, GraphScale.Step(1730f));
            Assert.AreEqual(2000f, GraphScale.Top(1730f));
            Assert.AreEqual(20f, GraphScale.Top(20f));
            Assert.AreEqual("1,250", GraphScale.Label(1250f));
            Assert.AreEqual("12.5k", GraphScale.Label(12500f));
            Assert.AreEqual("1.5M", GraphScale.Label(1500000f));
        }

        [Test]
        public void IncomeIsManaGatheredOverEachGapAndTheLastPointIsNow()
        {
            var r = new BattleRecord { Tick = 700, Every = 300, TicksPerSecond = 60 };
            var k = K(1);
            k.Series[(int)BattleSeries.Gathered] = new[] { 0, 100, 300, 340 };
            Assert.AreEqual(600, r.SampleTick(2, 4));
            Assert.AreEqual(700, r.SampleTick(3, 4), "the last value is the one now");
            var inc = r.Income(k);
            Assert.AreEqual(20f, inc[1], 0.001f, "100 in 5 s");
            Assert.AreEqual(40f, inc[2], 0.001f);
            Assert.AreEqual(24f, inc[3], 0.001f, "40 in the 100 ticks since the last sample");
            Assert.AreEqual(inc[1], inc[0], "the first point reads as the second");
        }

        [Test]
        public void TheTimeIsTheOriginalsClock()
        {
            Assert.AreEqual("00:00:01", BattleRecord.Clock(60, 60));
            Assert.AreEqual("01:02:03", BattleRecord.Clock((3600 + 120 + 3) * 60, 60));
            Assert.AreEqual("12:05", BattleRecord.Short((12 * 60 + 5) * 30, 30));
            Assert.AreEqual("1:00:00", BattleRecord.Short(3600 * 60, 60));
        }

        // teamlogos.gaf leads each side's twelve frames with two greyed
        // states, colorlogos2.gaf starts its ten at the first colour, so a
        // kingdom's badge is in the colour its units wear.
        [Test]
        public void ABadgeIsTheFrameOfTheKingdomsOwnColour()
        {
            Assert.AreEqual(2, LobbyScreens.LogoFrame(12, 0));
            Assert.AreEqual(11, LobbyScreens.LogoFrame(12, 9));
            Assert.AreEqual(0, LobbyScreens.LogoFrame(10, 0), "blue is the first frame of colorlogos2");
            Assert.AreEqual(9, LobbyScreens.LogoFrame(10, 9));
            Assert.AreEqual(1, LobbyScreens.LogoFrame(10, 11), "past the sheet it wraps");
        }

        [Test]
        public void TheEnginesVeteranLevelsAreTheHudsRanks()
        {
            Assert.AreEqual(0, EngineBackend.RankOf(0));
            Assert.AreEqual(1, EngineBackend.RankOf(1), "Veteran from the first level");
            Assert.AreEqual(2, EngineBackend.RankOf(2), "Champion from the second");
            Assert.AreEqual(2, EngineBackend.RankOf(10));
        }

        static MockBackend Fought(int seconds)
        {
            var b = new MockBackend { StageSeconds = 0 };
            // A small valley for two, where the armies meet on land.
            var s = new SkirmishSetup { MapId = "mock_frost", Seed = 3 };
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ARAMON", Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Colour = 1, Team = 1 });
            b.StartSkirmish(s);
            for (int i = 0; i < 20 && !b.PumpLoading().Done; i++) { }
            // Both armies meet in the middle.
            var units = new UnitState[256];
            int n = b.ReadUnits(units);
            var centre = new Vector3(b.Terrain.Size.x / 2f, 0, -b.Terrain.Size.y / 2f);
            for (int i = 0; i < n; i++)
                if (!b.UnitDefs[units[i].Def].IsBuilding) b.Command(GameCommand.To(CommandKind.Move, units[i].Handle, centre));
            b.Advance(seconds * MockBackend.Tps);
            return b;
        }

        [Test]
        public void TheMockKeepsItsRecordAsTheEngineDoes()
        {
            var b = Fought(60);
            var r = b.ReadBattle();
            Assert.IsNotNull(r);
            Assert.AreEqual(2, r.Kingdoms.Count);
            Assert.AreEqual(MockBackend.SampleTicks, r.Every);
            Assert.AreEqual((int)b.Tick, r.Tick);
            int kills = r.Kingdoms.Sum(k => k.Kills), losses = r.Kingdoms.Sum(k => k.Losses);
            Assert.Greater(kills, 0, "the armies fought");
            Assert.AreEqual(kills, losses, "every kill is another kingdom's loss");
            Assert.AreEqual(13, r.Kingdoms[0].UnitsBuilt, "every creation counts, as the original's does");
            Assert.Greater(r.Kingdoms[0].DamageDealt + r.Kingdoms[1].DamageDealt, 0);
            Assert.AreEqual(r.Kingdoms[0].DamageDealt, r.Kingdoms[1].DamageTaken);
            Assert.AreEqual(MomentKind.FirstBlood, r.Moments[0].Kind);
            foreach (var k in r.Kingdoms)
            {
                var army = k.Of(BattleSeries.Army);
                Assert.AreEqual(60 * MockBackend.Tps / MockBackend.SampleTicks + 2, army.Length, "a sample every 5 s from the first tick, and now");
                Assert.AreEqual(k.Kills, k.Of(BattleSeries.Kills).Last(), "the last point is now");
                Assert.AreEqual(Mathf.FloorToInt(k.ManaGathered), k.Of(BattleSeries.Gathered).Last(), "the mana gathered, as sampled now");
            }
            var champ = r.Kingdoms.First(k => k.ChampionDef >= 0);
            Assert.Greater(champ.ChampionKills, 0);
        }

        [Test]
        public void ARecordIsForABattle()
        {
            var b = Fought(5);
            Assert.IsNotNull(b.ReadBattle());
            b.EndGame();
            Assert.IsNull(b.ReadBattle(), "none outside a battle");
        }
    }
}
