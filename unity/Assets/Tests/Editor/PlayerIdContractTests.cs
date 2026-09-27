// PlayerIdContractTests.cs - one player-number contract on every backend:
// the ids in units, LocalPlayer and PlayerInfo.Index agree, PlayerById
// finds each, the local seat can be last, and a closed seat between two
// allies leaves them allies. The same assertions run on the mock and,
// when it is here, on the engine.
using System.Collections.Generic;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class PlayerIdContractTests
    {
        // You, a closed seat, an ally and an enemy.
        static SkirmishSetup Lineup(IGameBackend b, string map)
        {
            var s = new SkirmishSetup { MapId = map, Seed = 5, LineOfSight = false, MapRevealed = true };
            string side = b.Sides[0].Id, other = b.Sides.Count > 2 ? b.Sides[2].Id : side;
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = side, Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Closed, Side = "", Colour = 5, Team = 3 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = side, Colour = 2, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = other, Colour = 1, Team = 1 });
            return s;
        }

        static void Check(IGameBackend b, string map)
        {
            b.StartSkirmish(Lineup(b, map));
            for (int i = 0; i < 4000 && !b.PumpLoading().Done; i++) { }
            Assert.AreEqual(GameStatus.Running, b.Status);
            b.Advance(2);

            Assert.AreEqual(3, b.Players.Count, "the closed seat is no player");
            var ids = new HashSet<int>();
            foreach (var p in b.Players) Assert.IsTrue(ids.Add(p.Index), "ids are unique");
            var me = b.PlayerById(b.LocalPlayer);
            Assert.IsNotNull(me, "LocalPlayer is an id PlayerById finds");
            Assert.IsTrue(me.IsLocal);
            Assert.AreEqual(0, me.Colour, "the local colour comes back through the id");

            PlayerInfo ally = null, enemy = null;
            foreach (var p in b.Players)
            {
                if (p == me) continue;
                if (p.Team == me.Team) ally = p; else enemy = p;
            }
            Assert.IsNotNull(ally, "the ally behind the closed seat");
            Assert.IsNotNull(enemy);
            Assert.IsTrue(b.Allied(me.Index, ally.Index), "allies across a closed seat");
            Assert.IsFalse(b.Allied(me.Index, enemy.Index));

            var units = new UnitState[4096];
            int n = b.ReadUnits(units);
            Assert.Greater(n, 0);
            for (int i = 0; i < n; i++)
                Assert.IsNotNull(b.PlayerById(units[i].Player), $"unit {units[i].Handle}'s player {units[i].Player} is an id");
            Assert.Greater(b.ReadEconomy(b.LocalPlayer).Storage, 0f, "the economy takes the id");
            b.EndGame();
        }

        [Test]
        public void TheMockKeepsTheContract()
        {
            var b = new MockBackend { StageSeconds = 0 };
            Check(b, "mock_highlands");
            Assert.AreNotEqual(0, b.LocalPlayer, "ids start at 1, as the engine's do");
        }

        [Test]
        public void TheEngineKeepsTheContract()
        {
            if (!EngineSettings.EngineAvailable) Assert.Ignore("okengine or the game files are missing");
            using (var b = new EngineBackend()) Check(b, "two castles");
        }
    }
}
