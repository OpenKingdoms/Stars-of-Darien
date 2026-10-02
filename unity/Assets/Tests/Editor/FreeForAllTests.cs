// FreeForAllTests.cs - a kingdom can fight alone: the Team clicker goes on
// from Team 4 to Alone, computer seats start alone, a kingdom alone is
// nobody's ally, and the engine and the rooms get numbers that keep it so.
using System.Collections.Generic;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;

namespace OpenKingdomsUnity.Tests
{
    public class FreeForAllTests
    {
        const int Alone = SeatTeam.Alone;

        [Test]
        public void TheTeamClickerGoesFromTeamFourToAloneAndBack()
        {
            int t = 0;
            var seen = new List<int>();
            for (int i = 0; i < 5; i++) { t = LobbyScreens.NextTeam(t, 1); seen.Add(t); }
            CollectionAssert.AreEqual(new[] { 1, 2, 3, Alone, 0 }, seen);
            Assert.AreEqual(Alone, LobbyScreens.NextTeam(0, -1), "a right click goes back");
            Assert.AreEqual(3, LobbyScreens.NextTeam(Alone, -1));
            Assert.AreEqual("Team 1", LobbyScreens.TeamLabel(0));
            Assert.AreEqual("Team 4", LobbyScreens.TeamLabel(3));
            Assert.AreEqual("Alone", LobbyScreens.TeamLabel(Alone));
        }

        [Test]
        public void EverySeatButYoursStartsAlone()
        {
            foreach (int yours in new[] { 0, 2, Alone })
            {
                Assert.AreEqual(yours, LobbyScreens.NewSeatTeam(0, yours), "your seat keeps its team");
                for (int i = 1; i < LobbyScreens.SeatRows; i++)
                    Assert.AreEqual(Alone, LobbyScreens.NewSeatTeam(i, yours), $"row {i + 1}");
            }
            var mock = new MockBackend();
            var s = GameRoot.DefaultSetup(mock);
            Assert.AreEqual(0, s.Seats[0].Team, "you start on Team 1");
            for (int i = 1; i < s.Seats.Count; i++) Assert.AreEqual(Alone, s.Seats[i].Team, $"seat {i + 1}");
            mock.Dispose();
        }

        [Test]
        public void AKingdomAloneIsNobodysAlly()
        {
            Assert.IsFalse(SeatTeam.Allied(Alone, Alone));
            Assert.IsFalse(SeatTeam.Allied(Alone, 0));
            Assert.IsFalse(SeatTeam.Allied(0, 1));
            Assert.IsTrue(SeatTeam.Allied(2, 2));
        }

        // The embed numbers a seat sent as team 0 after the seat, which a
        // team picked on another seat can equal, so a kingdom alone goes
        // to the engine as a number no team and no other seat uses.
        [Test]
        public void TheEngineGetsANumberOfItsOwnForEachKingdomAlone()
        {
            var used = new HashSet<int>();
            for (int seat = 0; seat < 8; seat++)
            {
                int e = EngineBackend.EngineTeam(Alone, seat);
                Assert.That(e, Is.GreaterThan(8), "past every team the engine reads as one");
                Assert.IsTrue(used.Add(e), $"seat {seat} shares its number");
                Assert.AreEqual(Alone, EngineBackend.PlayerTeam(e), "and reads back alone");
            }
            for (int team = 0; team < 8; team++)
            {
                Assert.AreEqual(team + 1, EngineBackend.EngineTeam(team, 5));
                Assert.AreEqual(team, EngineBackend.PlayerTeam(team + 1));
            }
            Assert.AreEqual(Alone, EngineBackend.PlayerTeam(0), "a room's side alone");
        }

        [Test]
        public void ARoomTakesTheEnginesOwnNumbers()
        {
            Assert.AreEqual(0, EngineRooms.RoomTeam(Alone), "0 is a side alone, as the browser game has it");
            Assert.AreEqual(1, EngineRooms.RoomTeam(0));
            Assert.AreEqual(4, EngineRooms.RoomTeam(3));
        }

        [Test]
        public void TheSeatsLinePutsEachKingdomAloneOnItsOwn()
        {
            var sides = new List<SideInfo> { new SideInfo { Id = "ARAMON", Name = "Aramon" }, new SideInfo { Id = "TAROS", Name = "Taros" } };
            var s = new SkirmishSetup();
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ARAMON", Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Team = Alone, Difficulty = AiDifficulty.Easy });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Team = Alone, Difficulty = AiDifficulty.Hard });
            var lines = MenuScreens.SeatsLine(s, sides).Split('\n');
            CollectionAssert.AreEqual(new[] { "You, Aramon", "against  Computer (Easy), Taros", "against  Computer (Hard), Taros" }, lines);
        }

        [Test]
        public void NotEveryoneCanBeOnOneTeamButEveryoneCanBeAlone()
        {
            var seats = new List<SeatSetup>
            {
                new SeatSetup { Kind = SeatKind.Human, Team = 0 },
                new SeatSetup { Kind = SeatKind.Computer, Team = 0 },
                new SeatSetup { Kind = SeatKind.Closed, Team = 1 },
            };
            Assert.AreEqual("Not everyone can be on one team.", SkirmishCheck.WhyNot(seats, null));
            seats[1].Team = Alone;
            Assert.IsNull(SkirmishCheck.WhyNot(seats, null));
            seats[0].Team = Alone;
            Assert.IsNull(SkirmishCheck.WhyNot(seats, null), "everyone alone is a free for all");
        }

        [Test]
        public void TheMockFightsOnWhileTwoKingdomsAloneStand()
        {
            var b = new MockBackend { StageSeconds = 0, DamageScale = 0 };
            var s = new SkirmishSetup { MapId = "mock_isles", Seed = 3 };
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ARAMON", Colour = 0, Team = Alone });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Colour = 1, Team = Alone });
            b.StartSkirmish(s);
            for (int i = 0; i < 20 && !b.PumpLoading().Done; i++) { }
            b.Advance(30);
            Assert.AreEqual(GameStatus.Running, b.Status, "no victory while an enemy stands");
            Assert.IsFalse(b.Allied(b.Players[0].Index, b.Players[1].Index));
            b.Dispose();
        }

        [Test]
        public void ARoomKeepsAKingdomAloneAndRefusesOneTeam()
        {
            var b = new MockBackend();
            var r = b.Rooms;
            Assert.IsTrue(r.Connect("mock://relay", "Zach"));
            r.Pump();
            Assert.IsTrue(r.CreateRoom("", "mock_marches", RoomRules.LineOfSight));
            r.Pump();
            int guest = r.Room.Seats.FindIndex(x => x.Name == MockRooms.GuestName);
            Assert.Greater(guest, 0, "a guest joined");
            Assert.IsTrue(r.Edit(RoomEdit.AddComputer, 5, 0));
            Assert.AreEqual(Alone, r.Room.Seats[5].Team, "a computer joins alone");
            Assert.IsTrue(r.Edit(RoomEdit.Team, -1, Alone));
            Assert.AreEqual(Alone, r.Room.Seats[0].Team);
            Assert.IsTrue(r.Edit(RoomEdit.RemoveComputer, 5, 0));
            Assert.IsTrue(r.Edit(RoomEdit.Team, -1, r.Room.Seats[guest].Team));
            Assert.IsFalse(r.StartMatch(), "you and the guest on one team");
            Assert.AreEqual("Not everyone can be on one team.", r.Why);
            Assert.IsTrue(r.Edit(RoomEdit.AddComputer, 5, 0));
            Assert.IsTrue(r.StartMatch(), "a kingdom alone is not on that team");
            b.Dispose();
        }
    }
}
