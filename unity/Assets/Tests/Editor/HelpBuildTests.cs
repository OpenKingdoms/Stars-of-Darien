// HelpBuildTests.cs - builders helping on a frame, on the mock the input
// is tested against: the hammer shows over the player's own frame only
// while a selected unit can help build it, the game's click there sends
// the helpers and moves the rest, and helpers add up, each paying for its
// own share, so two raise a frame in half the time for the same mana.
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class HelpBuildTests
    {
        static MockBackend Loaded()
        {
            // Frames hold unless someone works on them.
            var b = new MockBackend { StageSeconds = 0, DamageScale = 0f, BuildSeconds = 0f };
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

        static UnitState Unit(MockBackend b, int handle) => Units(b).First(u => u.Handle == handle);

        static UnitState Mine(MockBackend b, MockBackend.Role role) =>
            Units(b).First(u => u.Player == b.LocalPlayer && b.RoleOf(u.Def) == role);

        // Open ground for a lodge a walk from the monarch.
        static Vector3 Site(MockBackend b, float dx = 10f)
        {
            var monarch = Mine(b, MockBackend.Role.Monarch);
            int def = Mine(b, MockBackend.Role.Lodge).Def;
            Vector3 site = default;
            bool found = false;
            for (float x = dx; x < dx + 40f && !found; x += 2f) found = b.CanBuildAt(def, monarch.Position + new Vector3(x, 0, -4), 0, out site);
            Assert.IsTrue(found, "open ground for the frame");
            return site;
        }

        // A lodge's frame a tenth built.
        static int Frame(MockBackend b, float dx = 10f) => b.SpawnFrame(Mine(b, MockBackend.Role.Lodge).Def, Site(b, dx), 0.1f);

        static GameCommand Help(int unit, int frame) =>
            new GameCommand { Kind = CommandKind.Repair, Unit = unit, TargetUnit = frame, BuildDef = -1 };

        [Test]
        public void TheHammerShowsOverAFrameOnlyWithAHelperSelected()
        {
            var b = Loaded();
            int frame = Frame(b);
            var at = Unit(b, frame).Position;
            var monarch = Mine(b, MockBackend.Role.Monarch);
            var knight = Mine(b, MockBackend.Role.Knight);

            b.Select(new[] { knight.Handle }, false);
            Assert.AreNotEqual(GameCursor.Repair, b.CursorAt(at, frame, out _), "a knight cannot help build");
            Assert.IsFalse(b.CanHelpBuild(knight.Handle, frame));
            b.Select(new[] { monarch.Handle }, false);
            Assert.AreEqual(GameCursor.Repair, b.CursorAt(at, frame, out _), "the monarch can");
            Assert.IsTrue(b.CanHelpBuild(monarch.Handle, frame));

            var theirs = Units(b).First(u => u.Player != b.LocalPlayer && b.RoleOf(u.Def) == MockBackend.Role.Monarch);
            Assert.IsFalse(b.CanHelpBuild(theirs.Handle, frame), "only the frame's own player helps it");
            Assert.IsFalse(b.CanHelpBuild(monarch.Handle, Mine(b, MockBackend.Role.Lodge).Handle), "a finished building takes no help");
        }

        [Test]
        public void TheGamesClickOnAFrameSendsTheHelpersAndMovesTheRest()
        {
            var b = Loaded();
            int frame = Frame(b);
            var at = Unit(b, frame).Position;
            var monarch = Mine(b, MockBackend.Role.Monarch);
            var knight = Mine(b, MockBackend.Role.Knight);
            b.Select(new[] { monarch.Handle, knight.Handle }, false);
            b.Click(at, frame, false);
            var order = b.ReadOrder(monarch.Handle);
            Assert.AreEqual(OrderKind.Build, order.Kind, "the monarch goes to help");
            Assert.AreEqual(frame, order.Building);
            Assert.AreEqual(OrderKind.Move, b.ReadOrder(knight.Handle).Kind, "the knight walks there");
            Assert.AreEqual(2, b.ReadSelection(new int[8]), "the selection stays");

            // The monarch walks up, faces the frame and raises it.
            float before = Unit(b, frame).BuildProgress;
            b.Advance(MockBackend.Tps * 6);
            var m = Unit(b, monarch.Handle);
            var f = Unit(b, frame);
            Assert.Greater(f.BuildProgress, before + 0.2f, "the frame rose");
            var to = f.Position - m.Position;
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(m.Heading, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg)), 10f, "the monarch faces its work");
        }

        // Shift puts the help behind the order in hand.
        [Test]
        public void ShiftQueuesTheHelpBehindTheOrderInHand()
        {
            var b = Loaded();
            int frame = Frame(b);
            var monarch = Mine(b, MockBackend.Role.Monarch);
            Assert.IsTrue(b.Command(GameCommand.To(CommandKind.Move, monarch.Handle, monarch.Position + new Vector3(-4f, 0f, 0f))));
            var help = Help(monarch.Handle, frame);
            help.Queue = true;
            Assert.IsTrue(b.Command(help));
            var legs = new OrderLeg[8];
            Assert.AreEqual(2, b.ReadOrderQueue(monarch.Handle, legs));
            Assert.AreEqual(OrderKind.Move, legs[0].Kind);
            Assert.AreEqual(OrderKind.Repair, legs[1].Kind);
            b.Advance(MockBackend.Tps * 3);
            Assert.AreEqual(frame, b.ReadOrder(monarch.Handle).Building, "the help follows the move");
        }

        // The helpers stand beside a site, a lodge's frame a tenth built goes
        // up there and they raise it. Returns the ticks it took and the
        // mana spent.
        static (int Ticks, float Mana) Raise(MockBackend b, float dx, params int[] helpers)
        {
            var site = Site(b, dx);
            // A formation move, so each holds its spot instead of wandering.
            var spots = helpers.Select((h, i) => new Vector2(site.x + 3.5f, site.z + i - 0.5f)).ToArray();
            Assert.IsTrue(b.MoveFormation(helpers, spots, 270f, false, false));
            for (int t = 0; t < MockBackend.Tps * 30 && helpers.Any(h => b.ReadOrder(h).Kind != OrderKind.None); t++) b.Advance(1);
            int frame = b.SpawnFrame(Mine(b, MockBackend.Role.Lodge).Def, site, 0.1f);
            foreach (int h in helpers) Assert.IsTrue(b.Command(Help(h, frame)));
            float spent = b.ReadBattle().Kingdoms[0].ManaSpent;
            int ticks = 0;
            for (; ticks < MockBackend.Tps * 120 && Unit(b, frame).BuildProgress < 1f; ticks++) b.Advance(1);
            Assert.AreEqual(1f, Unit(b, frame).BuildProgress, "the frame was finished");
            b.Advance(2);
            foreach (int h in helpers) Assert.AreNotEqual(frame, b.ReadOrder(h).Building, "a helper lets go of a finished frame");
            return (ticks, b.ReadBattle().Kingdoms[0].ManaSpent - spent);
        }

        [Test]
        public void TwoHelpersRaiseAFrameInHalfTheTimeForTheSameMana()
        {
            var b = Loaded();
            var monarch = Mine(b, MockBackend.Role.Monarch);
            int second = b.SpawnFrame(monarch.Def, monarch.Position + new Vector3(0f, 0f, -2f), 1f);
            var one = Raise(b, 10f, monarch.Handle);
            var two = Raise(b, 24f, monarch.Handle, second);
            Assert.AreEqual(one.Ticks / 2f, two.Ticks, 2f, "two helpers take half the time");
            Assert.AreEqual(one.Mana, two.Mana, 2f, "and the same mana");
            Assert.Greater(one.Mana, 0f);
        }
    }
}
