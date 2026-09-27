// MockControlsTests.cs - the mock's simple version of the game's own
// controls: a click selects a friend, orders the selection onto ground
// or an enemy, an armed command waits for the next click, and Cancel
// disarms before it deselects.
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class MockControlsTests
    {
        static MockBackend Loaded()
        {
            var b = new MockBackend { StageSeconds = 0 };
            var s = new SkirmishSetup { MapId = "mock_isles", Seed = 3 };
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ARAMON", Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Colour = 1, Team = 1 });
            b.StartSkirmish(s);
            for (int i = 0; i < 20 && !b.PumpLoading().Done; i++) { }
            return b;
        }

        [Test]
        public void AClickSelectsThenOrders()
        {
            var b = Loaded();
            var units = new UnitState[256];
            int n = b.ReadUnits(units);
            int mine = -1;
            for (int i = 0; i < n && mine < 0; i++)
                if (units[i].Player == b.LocalPlayer && !b.UnitDefs[units[i].Def].IsBuilding) mine = i;
            Assert.GreaterOrEqual(mine, 0);
            var u = units[mine];
            b.Click(u.Position, u.Handle, false);
            var sel = new int[16];
            Assert.AreEqual(1, b.ReadSelection(sel));
            Assert.AreEqual(u.Handle, sel[0]);

            b.Click(u.Position + new Vector3(3f, 0f, 0f), -1, false);
            Assert.AreEqual(OrderKind.Move, b.ReadOrder(u.Handle).Kind);

            b.Arm(CommandKind.Stop);
            b.Cancel();
            Assert.AreEqual(1, b.ReadSelection(sel), "Cancel disarms first");
            b.AssignGroup(2);
            b.Cancel();
            Assert.AreEqual(0, b.ReadSelection(sel));
            Assert.AreEqual(1, b.RecallGroup(2));
            Assert.IsTrue(b.OrderSelection(CommandKind.Stop));
            Assert.AreEqual(OrderKind.None, b.ReadOrder(u.Handle).Kind);
        }
    }
}
