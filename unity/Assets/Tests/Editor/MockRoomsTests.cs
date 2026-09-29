// MockRoomsTests.cs - the made-up relay keeps the room's rules: a seat
// claims a free start and gives it back, only the host moves seats, a new
// map frees every start, and the battle it starts deals the rest.
using NUnit.Framework;
using OpenKingdomsUnity.Game;

namespace OpenKingdomsUnity.Tests
{
    public class MockRoomsTests
    {
        static IGameRooms Hosting(MockBackend b, string map = "mock_marches")
        {
            var r = b.Rooms;
            Assert.IsTrue(r.Connect("mock://relay", "Zach"));
            r.Pump();
            Assert.AreEqual(RoomSession.Lobby, r.State);
            Assert.IsTrue(r.CreateRoom("", map, RoomRules.LineOfSight));
            r.Pump();
            return r;
        }

        [Test]
        public void ASeatClaimsAFreeStartAndGivesItBack()
        {
            var r = Hosting(new MockBackend());
            int guest = r.Room.Seats.FindIndex(s => s.Name == MockRooms.GuestName);
            Assert.Greater(guest, 0, "a guest joined");
            Assert.IsTrue(r.Edit(RoomEdit.Start, -1, 3));
            Assert.AreEqual(3, r.Room.Seats[0].Start);
            Assert.IsTrue(r.Edit(RoomEdit.Start, -1, 5), "taking another start moves you");
            Assert.AreEqual(5, r.Room.Seats[0].Start);
            Assert.IsFalse(r.Edit(RoomEdit.Start, -1, 8), "an eight-start map has no ninth");
            Assert.IsFalse(r.Edit(RoomEdit.Start, guest, 1), "not another seat's row");
            Assert.IsTrue(r.Edit(RoomEdit.Start, -1, -1));
            Assert.AreEqual(-1, r.Room.Seats[0].Start);
        }

        [Test]
        public void AStartAnotherSeatHoldsIsRefused()
        {
            var b = new MockBackend();
            var r = b.Rooms;
            r.Connect("mock://relay", "Zach");
            r.Pump();
            Assert.IsTrue(r.JoinRoom(5101, null));
            Assert.IsFalse(r.Room.YouHost);
            Assert.AreEqual(0, r.Room.Seats[0].Start, "the host holds the first start");
            Assert.IsFalse(r.Edit(RoomEdit.Start, -1, 0));
            Assert.AreEqual(-1, r.Room.Seats[r.Room.YourSeat].Start);
            Assert.IsTrue(r.Edit(RoomEdit.Start, -1, 1));
            Assert.AreEqual(1, r.Room.Seats[r.Room.YourSeat].Start);
        }

        [Test]
        public void OnlyTheHostMovesSeatsAndAMoveSwaps()
        {
            var b = new MockBackend();
            var r = Hosting(b);
            int guest = r.Room.Seats.FindIndex(s => s.Name == MockRooms.GuestName);
            r.Edit(RoomEdit.Start, -1, 2);
            Assert.IsTrue(r.Edit(RoomEdit.MoveStart, guest, 6));
            Assert.AreEqual(6, r.Room.Seats[guest].Start);
            Assert.IsTrue(r.Edit(RoomEdit.MoveStart, guest, 2), "onto the host's start");
            Assert.AreEqual(2, r.Room.Seats[guest].Start);
            Assert.AreEqual(6, r.Room.Seats[0].Start, "the host took the guest's old start");
            Assert.IsTrue(r.Edit(RoomEdit.MoveStart, guest, -1));
            Assert.AreEqual(-1, r.Room.Seats[guest].Start);
            Assert.IsFalse(r.Edit(RoomEdit.MoveStart, 5, 1), "an empty seat has nowhere to go");

            var g = new MockBackend().Rooms;
            g.Connect("mock://relay", "Zach");
            g.Pump();
            g.JoinRoom(0, "ashford");
            Assert.IsFalse(g.Edit(RoomEdit.MoveStart, 0, 3), "a guest cannot move the host");
            Assert.AreEqual(0, g.Room.Seats[0].Start);
        }

        [Test]
        public void ANewMapFreesEveryStart()
        {
            var r = Hosting(new MockBackend());
            r.Edit(RoomEdit.Start, -1, 4);
            Assert.IsTrue(r.Edit(RoomEdit.Map, -1, 0, "mock_frost"));
            Assert.AreEqual("mock_frost", r.Room.MapId);
            foreach (var s in r.Room.Seats) Assert.AreEqual(-1, s.Start);
            Assert.IsFalse(r.Edit(RoomEdit.Start, -1, 2), "the new map has two starts");
        }

        [Test]
        public void TheBattleDealsTheStartsLeftWithOurSeatFirst()
        {
            var b = new MockBackend();
            var r = Hosting(b);
            int guest = r.Room.Seats.FindIndex(s => s.Name == MockRooms.GuestName);
            Assert.IsTrue(r.Edit(RoomEdit.AddComputer, 5, 0));
            r.Edit(RoomEdit.Start, -1, 7);
            Assert.IsTrue(r.Edit(RoomEdit.MoveStart, guest, 0));
            Assert.IsTrue(r.StartMatch(), r.Why);
            Assert.AreEqual(RoomSession.Loading, r.Pump());
            var map = b.Maps[b.Maps.Count - 1];
            foreach (var m in b.Maps) if (m.Id == r.Room.MapId) map = m;
            var s = RoomSetup.ToSkirmish(r.Room, map, 9);
            Assert.AreEqual(SeatKind.Human, s.Seats[0].Kind);
            Assert.AreEqual(7, s.Seats[0].Start);
            int open = 0;
            foreach (var seat in s.Seats) if (seat.Kind != SeatKind.Closed) { open++; Assert.GreaterOrEqual(seat.Start, 0); }
            Assert.AreEqual(3, open);
            Assert.IsTrue(s.Seats.Exists(x => x.Start == 0 && x.Kind == SeatKind.Computer), "the guest plays as a computer here, at its start");
            Assert.IsTrue(s.Seats.Exists(x => x.Start == 1), "the computer took the first start left");
        }

        [Test]
        public void TheHostWaitsForEveryoneReady()
        {
            var r = Hosting(new MockBackend());
            int guest = r.Room.Seats.FindIndex(s => s.Name == MockRooms.GuestName);
            r.Room.Seats[guest].Ready = false;
            Assert.IsFalse(r.StartMatch());
            StringAssert.Contains(MockRooms.GuestName, r.Why);
            r.Room.Seats[guest].Ready = true;
            Assert.IsTrue(r.StartMatch());
        }
    }
}
