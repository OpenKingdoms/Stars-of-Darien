// StartPositionTests.cs - a seat takes a free start and gives it back,
// computers take the starts left, the host's move swaps, and a new map lets
// go of starts it no longer has.
using System.Collections.Generic;
using NUnit.Framework;
using OpenKingdomsUnity.Game;

namespace OpenKingdomsUnity.Tests
{
    public class StartPositionTests
    {
        static List<SeatSetup> Seats(params SeatKind[] kinds)
        {
            var l = new List<SeatSetup>();
            foreach (var k in kinds) l.Add(new SeatSetup { Kind = k });
            return l;
        }

        [Test]
        public void ComputersTakeTheStartsLeftInSeatOrder()
        {
            var s = Seats(SeatKind.Human, SeatKind.Computer, SeatKind.Closed, SeatKind.Computer);
            Assert.IsTrue(StartPositions.Claim(s, 0, 2, 4));
            CollectionAssert.AreEqual(new[] { 2, 0, -1, 1 }, StartPositions.Assign(s, 4, false, 1));
            // Nobody claimed: seat order, as the original deals them.
            StartPositions.Release(s, 0);
            CollectionAssert.AreEqual(new[] { 0, 1, -1, 2 }, StartPositions.Assign(s, 4, false, 1));
        }

        [Test]
        public void ATakenStartIsRefusedUntilItsHolderGivesItBack()
        {
            var s = Seats(SeatKind.Human, SeatKind.Computer);
            Assert.IsTrue(StartPositions.Claim(s, 1, 0, 3));
            Assert.IsFalse(StartPositions.Claim(s, 0, 0, 3));
            Assert.AreEqual(-1, s[0].Start);
            Assert.AreEqual(1, StartPositions.HolderOf(s, 0));
            StartPositions.Release(s, 1);
            Assert.IsTrue(StartPositions.Claim(s, 0, 0, 3));
            Assert.AreEqual(0, StartPositions.HolderOf(s, 0));
            // Taking another start moves the seat and frees the first.
            Assert.IsTrue(StartPositions.Claim(s, 0, 2, 3));
            Assert.AreEqual(-1, StartPositions.HolderOf(s, 0));
        }

        [Test]
        public void StartsOffTheMapAndClosedSeatsAreRefused()
        {
            var s = Seats(SeatKind.Human, SeatKind.Closed);
            Assert.IsFalse(StartPositions.Claim(s, 0, 4, 4));
            Assert.IsFalse(StartPositions.Claim(s, 0, -1, 4));
            Assert.IsFalse(StartPositions.Claim(s, 1, 0, 4));
            Assert.IsFalse(StartPositions.Claim(s, 5, 0, 4));
        }

        [Test]
        public void AMoveSwapsWithTheSeatThatHoldsTheStart()
        {
            var s = Seats(SeatKind.Human, SeatKind.Human, SeatKind.Computer);
            StartPositions.Claim(s, 0, 0, 4);
            StartPositions.Claim(s, 1, 3, 4);
            Assert.IsTrue(StartPositions.Move(s, 0, 3, 4));
            Assert.AreEqual(3, s[0].Start);
            Assert.AreEqual(0, s[1].Start);
            // Onto a free start, and off the map altogether.
            Assert.IsTrue(StartPositions.Move(s, 2, 1, 4));
            Assert.AreEqual(1, s[2].Start);
            Assert.IsTrue(StartPositions.Move(s, 2, -1, 4));
            Assert.AreEqual(-1, s[2].Start);
            Assert.IsFalse(StartPositions.Move(s, 2, 9, 4));
        }

        [Test]
        public void ARandomDealIsSteadyForASeedAndKeepsWhatWasTaken()
        {
            var s = Seats(SeatKind.Human, SeatKind.Computer, SeatKind.Computer, SeatKind.Computer, SeatKind.Computer);
            StartPositions.Claim(s, 0, 5, 8);
            var a = StartPositions.Assign(s, 8, true, 42);
            CollectionAssert.AreEqual(a, StartPositions.Assign(s, 8, true, 42));
            Assert.AreEqual(5, a[0]);
            Assert.AreEqual(5, new HashSet<int>(a).Count, "every seat has its own start");
            foreach (int p in a) Assert.That(p, Is.InRange(0, 7));
            bool differs = false;
            for (uint seed = 0; seed < 20 && !differs; seed++)
                differs = !Equal(a, StartPositions.Assign(s, 8, true, seed));
            Assert.IsTrue(differs, "another seed deals differently");
        }

        [Test]
        public void SeatsBeyondTheMapsStartsGetNone()
        {
            var s = Seats(SeatKind.Human, SeatKind.Computer, SeatKind.Computer);
            CollectionAssert.AreEqual(new[] { 0, 1, -1 }, StartPositions.Assign(s, 2, false, 0));
            CollectionAssert.AreEqual(new[] { -1, -1, -1 }, StartPositions.Assign(s, 0, false, 0));
        }

        [Test]
        public void TidyLetsGoOfStartsTheMapNoLongerHas()
        {
            var s = Seats(SeatKind.Human, SeatKind.Computer, SeatKind.Computer);
            s[0].Start = 5;
            s[1].Start = 1;
            s[2].Start = 1;
            StartPositions.Tidy(s, 4);
            Assert.AreEqual(-1, s[0].Start);
            Assert.AreEqual(1, s[1].Start);
            Assert.AreEqual(-1, s[2].Start, "a start held twice goes to the first seat");
        }

        static bool Equal(int[] a, int[] b)
        {
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
