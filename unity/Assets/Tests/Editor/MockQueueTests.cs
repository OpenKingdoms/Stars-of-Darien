// MockQueueTests.cs - the mock's order queue and factories, which the
// input and the HUD are tested against, and the selection ring's shape.
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class MockQueueTests
    {
        static MockBackend Loaded()
        {
            var b = new MockBackend { StageSeconds = 0, DamageScale = 0f };
            var s = new SkirmishSetup { MapId = "mock_highlands", Seed = 3 };
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ARAMON", Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Colour = 1, Team = 1 });
            b.StartSkirmish(s);
            for (int i = 0; i < 20 && !b.PumpLoading().Done; i++) { }
            return b;
        }

        static UnitState[] Units(MockBackend b)
        {
            var u = new UnitState[512];
            return u.Take(b.ReadUnits(u)).ToArray();
        }

        static int Count(MockBackend b, int handle) => b.ReadOrderQueue(handle, new OrderLeg[16]);

        [Test]
        public void QueuedOrdersWaitTheirTurn()
        {
            var b = Loaded();
            var knight = Units(b).First(u => u.Player == b.LocalPlayer && b.RoleOf(u.Def) == MockBackend.Role.Knight);
            var p = knight.Position;
            Assert.IsTrue(b.Command(GameCommand.To(CommandKind.Move, knight.Handle, p + new Vector3(2, 0, 0))));
            var second = GameCommand.To(CommandKind.Move, knight.Handle, p + new Vector3(2, 0, -6));
            second.Queue = true;
            Assert.IsTrue(b.Command(second));
            var third = GameCommand.To(CommandKind.Patrol, knight.Handle, p + new Vector3(-2, 0, -3));
            third.Queue = true;
            Assert.IsTrue(b.Command(third));
            var legs = new OrderLeg[16];
            Assert.AreEqual(3, b.ReadOrderQueue(knight.Handle, legs));
            Assert.AreEqual(OrderKind.Patrol, legs[2].Kind);

            b.Advance(MockBackend.Tps * 2);
            Assert.AreEqual(2, Count(b, knight.Handle), "the first is done and the second begun");
            Assert.AreEqual(-6f, b.ReadOrder(knight.Handle).Target.z - p.z, 0.01f);

            Assert.IsTrue(b.Command(GameCommand.To(CommandKind.Move, knight.Handle, p)));
            Assert.AreEqual(1, Count(b, knight.Handle), "an order without Shift replaces the queue");
        }

        [Test]
        public void AFactoryCountsRepeatsAndRallies()
        {
            var b = Loaded();
            var lodge = Units(b).First(u => u.Player == b.LocalPlayer && b.UnitDefs[u.Def].IsBuilding);
            int def = b.UnitDefs[lodge.Def].BuildOptions[0];
            Assert.IsTrue(b.AddToQueue(lodge.Handle, def, 5));
            Assert.AreEqual(5, b.QueuedCount(lodge.Handle, def));
            Assert.IsTrue(b.AddToQueue(lodge.Handle, def, -2));
            Assert.AreEqual(3, b.QueuedCount(lodge.Handle, def));
            Assert.IsTrue(b.SetRepeat(lodge.Handle, def, true));
            Assert.AreEqual(def, b.RepeatOf(lodge.Handle));
            Assert.IsTrue(b.SetRepeat(lodge.Handle, def, false));
            Assert.AreEqual(-1, b.RepeatOf(lodge.Handle));
            Assert.AreEqual(0, b.QueuedCount(lodge.Handle, def), "ending a repeat drops that def from the queue");

            var rally = lodge.Position + new Vector3(-6f, 0f, -6f);
            Assert.IsTrue(b.Command(GameCommand.To(CommandKind.Move, lodge.Handle, rally)), "Move on a factory sets its rally");
            var legs = new OrderLeg[4];
            Assert.AreEqual(1, b.ReadOrderQueue(lodge.Handle, legs));
            Assert.AreEqual(rally.x, legs[0].Target.x, 0.01f);

            int before = Units(b).Length;
            b.AddToQueue(lodge.Handle, def, 1);
            b.Advance(MockBackend.Tps * 6);
            var made = Units(b).Where(u => u.Def == def && u.Player == b.LocalPlayer).OrderByDescending(u => u.Handle).First();
            Assert.AreEqual(before + 1, Units(b).Length);
            Assert.AreEqual(OrderKind.Move, b.ReadOrder(made.Handle).Kind, "the new unit heads for the rally");
            Assert.AreEqual(rally.z, b.ReadOrder(made.Handle).Target.z, 0.01f);
        }

        [Test]
        public void TheSelectionRingIsACircleOverTheGround()
        {
            var lodge = new UnitDef { IsBuilding = true, Footprint = new Vector2Int(4, 2) };
            Assert.AreEqual(2.3f, SelectionRing.Radius(lodge, 0.9f), 1e-4f, "from the footprint's longer side");
            Assert.AreEqual(0.9f, SelectionRing.Radius(new UnitDef(), 0.9f), 1e-4f, "from a unit's model");

            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            var centre = new Vector3(10f, 0f, -10f);
            float Ground(float x, float z) => 0.4f * (x - 10f) + 0.1f * Mathf.Sin(z);
            SelectionRing.Add(centre, 2.3f, Ground, float.MinValue, verts, tris);
            for (int i = 1; i < verts.Count; i += 2)
            {
                var v = verts[i];
                Assert.AreEqual(2.3f, new Vector2(v.x - centre.x, v.z - centre.z).magnitude, 1e-3f, "the same radius all round");
                Assert.AreEqual(Ground(v.x, v.z) + SelectionRing.Lift, v.y, 1e-4f, "and just over the ground under it");
            }
        }
    }
}
