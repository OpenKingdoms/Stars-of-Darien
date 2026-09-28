// MockFormationTests.cs - the mock's MoveFormation keeps the contract: the
// slowest unit's pace, the heading held on arrival, queued moves in turn,
// a new order replacing them, and only the player's own mobile units moved.
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class MockFormationTests
    {
        static MockBackend Loaded()
        {
            var b = new MockBackend { StageSeconds = 0, DamageScale = 0 };
            var s = new SkirmishSetup { MapId = "mock_frost", Seed = 3 };
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ARAMON", Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Colour = 1, Team = 1 });
            b.StartSkirmish(s);
            for (int i = 0; i < 20 && !b.PumpLoading().Done; i++) { }
            return b;
        }

        static UnitState[] Units(MockBackend b)
        {
            var u = new UnitState[256];
            return u.Take(b.ReadUnits(u)).ToArray();
        }

        static UnitState Unit(MockBackend b, int handle) => Units(b).First(u => u.Handle == handle);

        static int[] Own(MockBackend b, MockBackend.Role role) =>
            Units(b).Where(u => u.Player == b.LocalPlayer && b.RoleOf(u.Def) == role).Select(u => u.Handle).ToArray();

        static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        [Test]
        public void TheGroupKeepsToItsSlowestPace()
        {
            var b = Loaded();
            var knights = Own(b, MockBackend.Role.Knight);
            var archers = Own(b, MockBackend.Role.Archer);
            var all = knights.Concat(archers).ToArray();
            var to = all.Select(h => Flat(Unit(b, h).Position) + new Vector2(0, 14)).ToArray();
            Assert.IsTrue(b.MoveFormation(all, to, 0f, true, false));
            Assert.AreEqual(MockBackend.FootSpeed, b.PaceOf(knights[0]), "knights wait for the archers");
            var before = knights.Select(h => Unit(b, h).Position).ToArray();
            for (int t = 0; t < 60; t++)
            {
                b.Advance(1);
                for (int i = 0; i < knights.Length; i++)
                {
                    var now = Unit(b, knights[i]).Position;
                    Assert.LessOrEqual(Flat(now - before[i]).magnitude, MockBackend.FootSpeed / MockBackend.Tps + 1e-4f, "a knight outpaced the archers");
                    before[i] = now;
                }
            }
            Assert.IsTrue(b.MoveFormation(knights, knights.Select(h => Flat(Unit(b, h).Position) + new Vector2(0, 5)).ToArray(), 0f, false, false));
            Assert.IsNull(b.PaceOf(knights[0]), "without group speed each keeps its own");
        }

        [Test]
        public void ArrivedUnitsTurnToTheHeadingAndHoldIt()
        {
            var b = Loaded();
            var k = Own(b, MockBackend.Role.Knight)[0];
            var goal = Flat(Unit(b, k).Position) + new Vector2(3, 3);
            Assert.IsTrue(b.MoveFormation(new[] { k }, new[] { goal }, 90f, false, false));
            b.Advance(300);
            var u = Unit(b, k);
            Assert.Less((Flat(u.Position) - goal).magnitude, 0.5f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(u.Heading, 90f), 1f, "turned to the formation's heading");
            b.Advance(900);
            u = Unit(b, k);
            Assert.Less((Flat(u.Position) - goal).magnitude, 0.5f, "it stays in its place");
            Assert.AreEqual(0f, Mathf.DeltaAngle(u.Heading, 90f), 1f, "and keeps facing");
        }

        [Test]
        public void AQueuedMoveWaitsAndAReplaceClearsTheQueue()
        {
            var b = Loaded();
            var k = Own(b, MockBackend.Role.Knight)[0];
            var start = Flat(Unit(b, k).Position);
            var p1 = start + new Vector2(0, 6);
            var p2 = p1 + new Vector2(6, 0);
            Assert.IsTrue(b.MoveFormation(new[] { k }, new[] { p1 }, 0f, false, false));
            Assert.IsTrue(b.MoveFormation(new[] { k }, new[] { p2 }, 180f, false, true));
            Assert.AreEqual(1, b.QueuedLegs(k));
            Assert.AreEqual(p1, Flat(b.ReadOrder(k).Target), "the first point first");
            b.Advance(600);
            var u = Unit(b, k);
            Assert.Less((Flat(u.Position) - p2).magnitude, 0.5f, "then the queued one");
            Assert.AreEqual(0f, Mathf.DeltaAngle(u.Heading, 180f), 1f);
            Assert.AreEqual(0, b.QueuedLegs(k));

            Assert.IsTrue(b.MoveFormation(new[] { k }, new[] { start }, null, false, false));
            Assert.IsTrue(b.MoveFormation(new[] { k }, new[] { p1 }, null, false, true));
            Assert.AreEqual(1, b.QueuedLegs(k));
            Assert.IsTrue(b.MoveFormation(new[] { k }, new[] { p2 }, null, false, false));
            Assert.AreEqual(0, b.QueuedLegs(k), "a new formation replaces the queue");
            Assert.IsTrue(b.MoveFormation(new[] { k }, new[] { p1 }, 90f, true, true));
            Assert.IsTrue(b.Command(GameCommand.To(CommandKind.Stop, k, Vector3.zero)));
            Assert.AreEqual(0, b.QueuedLegs(k), "any other order lets it go");
            Assert.IsNull(b.PaceOf(k));
        }

        [Test]
        public void OnlyThePlayersOwnMobileUnitsMove()
        {
            var b = Loaded();
            var units = Units(b);
            var mine = units.First(u => u.Player == b.LocalPlayer && b.RoleOf(u.Def) == MockBackend.Role.Knight);
            var theirs = units.First(u => u.Player != b.LocalPlayer && b.RoleOf(u.Def) == MockBackend.Role.Knight);
            var lodge = units.First(u => u.Player == b.LocalPlayer && b.RoleOf(u.Def) == MockBackend.Role.Lodge);
            var to = new[] { Flat(theirs.Position) + Vector2.up * 4, Flat(lodge.Position) + Vector2.up * 4 };
            Assert.IsFalse(b.MoveFormation(new[] { theirs.Handle, lodge.Handle }, to, 0f, true, false), "nobody took it");
            Assert.AreEqual(OrderKind.None, b.ReadOrder(lodge.Handle).Kind);
            var goal = Flat(mine.Position) + Vector2.up * 4;
            Assert.IsTrue(b.MoveFormation(new[] { theirs.Handle, lodge.Handle, mine.Handle }, new[] { to[0], to[1], goal }, 0f, true, false));
            Assert.AreEqual(OrderKind.Move, b.ReadOrder(mine.Handle).Kind);
            Assert.AreNotEqual(to[0], Flat(b.ReadOrder(theirs.Handle).Target), "the enemy knight was not moved");
            Assert.AreEqual(1, b.LastFormation.Accepted);
            Assert.AreEqual(3, b.LastFormation.Units.Length);
            Assert.IsFalse(b.MoveFormation(new[] { mine.Handle }, new Vector2[0], 0f, true, false), "the arrays must match");
        }

        [Test]
        public void AFlyerKeepsAFlyersPaceAndEndGameForgetsTheOrders()
        {
            var b = Loaded();
            var flyer = Own(b, MockBackend.Role.Flyer)[0];
            var knight = Own(b, MockBackend.Role.Knight)[0];
            Assert.IsTrue(b.MoveFormation(new[] { flyer }, new[] { Flat(Unit(b, flyer).Position) + new Vector2(0, 10) }, 0f, true, false));
            Assert.AreEqual(MockBackend.FlyerSpeed, b.PaceOf(flyer));
            Assert.IsTrue(b.MoveFormation(new[] { knight }, new[] { Flat(Unit(b, knight).Position) + new Vector2(0, 10) }, 0f, true, true));
            Assert.AreEqual(2, b.FormationCalls.Count);
            b.EndGame();
            Assert.AreEqual(0, b.FormationCalls.Count);
            Assert.IsNull(b.LastFormation);
            Assert.IsNull(b.PaceOf(flyer));
            Assert.AreEqual(0, b.QueuedLegs(knight));
        }

        [Test]
        public void CategoriesReadLikeTheGames()
        {
            var b = new MockBackend();
            var knight = b.UnitDefs.First(d => d.Name == "aramon_knight");
            Assert.AreEqual("ARA MELEE ATTACK", knight.Category);
            Assert.AreEqual(OpenKingdomsUnity.Game.World.FormationRole.Ranged,
                OpenKingdomsUnity.Game.World.FormationRoles.Classify("zhon_archer", b.UnitDefs.First(d => d.Name == "zhon_archer").Category, 1).Role);
            Assert.AreEqual(OpenKingdomsUnity.Game.World.FormationRole.Command,
                OpenKingdomsUnity.Game.World.FormationRoles.Classify("veruna_monarch", b.UnitDefs.First(d => d.Name == "veruna_monarch").Category, 1).Role);
        }
    }
}
