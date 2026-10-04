// HelpBuildPlayTests.cs - builders helping on a frame, through the
// pointer. On the mock the hammer shows over the player's own frame with a
// builder selected, and the classic left click and the modern right click
// both send it to help, the modern one moving the rest, and with Shift
// both queue it. On the real engine two Mage Builders raise a wall in half
// the time one takes, each facing it with its order line on it and
// the build sparkles on it, and the hammer shows over the frame for the
// second. A modern right click on a frame only the monarch can raise
// sends him and moves the Mage Builder. The engine's tests need okengine
// and the game files, and are ignored without them.
using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class HelpBuildPlayTests
    {
        GameRoot root;
        IGameBackend backend;
        PointerFrame frame;
        readonly UnitState[] units = new UnitState[1024];

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        IEnumerator Begin(IGameBackend b, string map, float seconds)
        {
            backend = b;
            root = GameRoot.Boot(b);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = map;
            root.Setup.MapRevealed = true;
            root.Setup.LineOfSight = false;
            root.Setup.Seats[0].Side = "ARAMON";
            root.Setup.Seats[1].Side = "TAROS";
            root.Setup.Seats[1].Difficulty = AiDifficulty.Easy;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + seconds;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, "the battle loaded: " + root.LastError);
            // The battle stands still and the test moves it on.
            root.Options.GameSpeed = 0;
            root.Orders.Classic = true;
            frame = new PointerFrame { Focused = true, Dpi = 96 };
            root.Orders.Formation.Source = () =>
            {
                var r = frame;
                frame.LeftDown = frame.LeftUp = frame.RightDown = frame.RightUp = false;
                return r;
            };
        }

        UnitState Read(int handle)
        {
            int n = backend.ReadUnits(units);
            for (int i = 0; i < n; i++) if (units[i].Handle == handle) return units[i];
            Assert.Fail("unit " + handle + " is gone");
            return default;
        }

        int DefNamed(string name)
        {
            for (int i = 0; i < backend.UnitDefs.Count; i++)
                if (string.Equals(backend.UnitDefs[i].Name, name, System.StringComparison.OrdinalIgnoreCase)) return i;
            Assert.Fail("no " + name);
            return -1;
        }

        Camera Cam => root.World.Camera.GetComponent<Camera>();

        // The camera over a frame, and the pointer on it where it is drawn,
        // on a part of it no builder beside it stands in front of.
        IEnumerator PointAt(int handle)
        {
            var u = Read(handle);
            var gc = root.World.Camera;
            gc.focus = new Vector3(u.Position.x, backend.GroundHeight(u.Position.x, u.Position.z), u.Position.z);
            gc.yaw = 30f;
            gc.pitch = 50f;
            gc.Zoom(40f);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsTrue(root.World.Entities.DrawnSize.TryGetValue(handle, out var size), "the frame is drawn");
            var fp = backend.UnitDefs[u.Def].Footprint;
            var parts = new[] { Vector3.zero, new Vector3(-0.3f * fp.x, 0, 0), new Vector3(0.3f * fp.x, 0, 0), new Vector3(0, 0, -0.3f * fp.y), new Vector3(0, 0, 0.3f * fp.y) };
            foreach (var part in parts)
            {
                frame.Screen = Cam.WorldToScreenPoint(u.Position + part + Vector3.up * (size.x * 0.3f));
                yield return null;
                if (root.Orders.PointerUnit == handle) break;
            }
            Assert.AreEqual(handle, root.Orders.PointerUnit, "the pointer is on the frame");
        }

        IEnumerator Click(bool right)
        {
            if (right) frame.RightDown = frame.RightHeld = true;
            else frame.LeftDown = frame.LeftHeld = true;
            yield return null;
            if (right) { frame.RightHeld = false; frame.RightUp = true; }
            else { frame.LeftHeld = false; frame.LeftUp = true; }
            yield return null;
            yield return null;
        }

        // ---- On the mock ----

        [UnityTest]
        public IEnumerator TheHammerOverAFrameSendsTheBuilderInBothSchemes()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f, BuildSeconds = 0f };
            yield return Begin(mock, "mock_highlands", 30f);
            int n = mock.ReadUnits(units);
            var monarch = units.Take(n).First(u => u.Player == mock.LocalPlayer && mock.RoleOf(u.Def) == MockBackend.Role.Monarch);
            var knight = units.Take(n).First(u => u.Player == mock.LocalPlayer && mock.RoleOf(u.Def) == MockBackend.Role.Knight);
            int lodge = units.Take(n).First(u => u.Player == mock.LocalPlayer && mock.RoleOf(u.Def) == MockBackend.Role.Lodge).Def;
            Vector3 site = default;
            bool found = false;
            for (int dx = 8; dx < 40 && !found; dx += 2) found = mock.CanBuildAt(lodge, monarch.Position + new Vector3(dx, 0, -4), 0, out site);
            Assert.IsTrue(found);
            int built = mock.SpawnFrame(lodge, site, 0.3f);

            // Classic: the hammer, and the left click sends the monarch.
            mock.Select(new[] { monarch.Handle }, false);
            yield return PointAt(built);
            Assert.AreEqual(GameCursor.Repair, root.Orders.PointerCursor(), "the hammer over the frame");
            yield return Click(false);
            Assert.AreEqual(OrderKind.Build, mock.ReadOrder(monarch.Handle).Kind, "the classic click sends the monarch");
            Assert.AreEqual(built, mock.ReadOrder(monarch.Handle).Building);
            mock.Command(GameCommand.To(CommandKind.Stop, monarch.Handle, Vector3.zero));

            // Modern, with only a knight: no hammer, and the right click moves.
            root.Orders.Classic = false;
            root.Orders.Selected.Clear();
            root.Orders.Selected.Add(knight.Handle);
            mock.Select(new[] { knight.Handle }, false);
            yield return PointAt(built);
            Assert.AreNotEqual(GameCursor.Repair, root.Orders.PointerCursor(), "a knight gets no hammer");
            yield return Click(true);
            Assert.AreEqual(OrderKind.Move, mock.ReadOrder(knight.Handle).Kind);

            // Modern, with both: the hammer, the monarch helps and the knight moves.
            root.Orders.Selected.Add(monarch.Handle);
            mock.Select(new[] { knight.Handle, monarch.Handle }, false);
            yield return PointAt(built);
            Assert.AreEqual(GameCursor.Repair, root.Orders.PointerCursor(), "the hammer with the monarch among them");
            yield return Click(true);
            Assert.AreEqual(OrderKind.Build, mock.ReadOrder(monarch.Handle).Kind, "the right click sends the monarch to help");
            Assert.AreEqual(built, mock.ReadOrder(monarch.Handle).Building);
            Assert.AreEqual(OrderKind.Move, mock.ReadOrder(knight.Handle).Kind, "and the knight walks there");
            Assert.IsTrue(root.Orders.Selected.SetEquals(new[] { knight.Handle, monarch.Handle }), "the selection stays");
        }

        // With Shift either scheme's click on the frame puts the help behind
        // the order in hand.
        [UnityTest]
        public IEnumerator ShiftQueuesTheHelpInBothSchemes()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f, BuildSeconds = 0f };
            yield return Begin(mock, "mock_highlands", 30f);
            int n = mock.ReadUnits(units);
            var monarch = units.Take(n).First(u => u.Player == mock.LocalPlayer && mock.RoleOf(u.Def) == MockBackend.Role.Monarch);
            int lodge = units.Take(n).First(u => u.Player == mock.LocalPlayer && mock.RoleOf(u.Def) == MockBackend.Role.Lodge).Def;
            Vector3 site = default;
            bool found = false;
            for (int dx = 8; dx < 40 && !found; dx += 2) found = mock.CanBuildAt(lodge, monarch.Position + new Vector3(dx, 0, -4), 0, out site);
            Assert.IsTrue(found);
            int built = mock.SpawnFrame(lodge, site, 0.3f);
            var legs = new OrderLeg[8];
            foreach (bool classic in new[] { true, false })
            {
                string scheme = classic ? "classic" : "modern";
                root.Orders.Classic = classic;
                root.Orders.Selected.Clear();
                root.Orders.Selected.Add(monarch.Handle);
                mock.Select(new[] { monarch.Handle }, false);
                Assert.IsTrue(mock.Command(GameCommand.To(CommandKind.Move, monarch.Handle, monarch.Position + new Vector3(-6f, 0f, 0f))));
                yield return PointAt(built);
                frame.Shift = true;
                yield return Click(!classic);
                frame.Shift = false;
                Assert.AreEqual(2, mock.ReadOrderQueue(monarch.Handle, legs), scheme + ": the help waits behind the walk");
                Assert.AreEqual(OrderKind.Move, legs[0].Kind, scheme);
                Assert.AreEqual(OrderKind.Repair, legs[1].Kind, scheme);
                Assert.AreEqual(built, legs[1].TargetUnit, scheme + ": on the frame");
                mock.Command(GameCommand.To(CommandKind.Stop, monarch.Handle, Vector3.zero));
            }
        }

        // ---- On the real engine ----

        IEnumerator BeginEngine()
        {
            if (!EngineSettings.EngineAvailable) Assert.Ignore("okengine or the game files are missing");
            yield return Begin(new EngineBackend(), "two castles", 240f);
        }

        // Open ground for a def near a point, a few cells from it.
        Vector3 SiteNear(int def, Vector3 from)
        {
            Vector3 site = default;
            for (int r = 6; r <= 60; r += 2)
                for (int k = 0; k < 16; k++)
                    if (backend.CanBuildAt(def, from + Quaternion.Euler(0, k * 22.5f, 0) * new Vector3(r, 0, 0), 0, out site)) return site;
            Assert.Fail("no ground for " + backend.UnitDefs[def].Name);
            return site;
        }

        // The builders walk to stand beside the site, out of its way, and wait there.
        void Stand(int[] builders, Vector3 site, int def)
        {
            var fp = backend.UnitDefs[def].Footprint;
            float off = Mathf.Max(fp.x, fp.y) * 0.5f + 1.5f;
            var spots = builders.Select((h, i) => new Vector2(site.x + off, site.z + 1.5f * i - 0.75f)).ToArray();
            Assert.IsTrue(backend.MoveFormation(builders, spots, null, false, false));
            for (int t = 0; t < 60 * 60 && builders.Any(h => backend.ReadOrder(h).Kind != OrderKind.None); t += 5) backend.Advance(5);
            Assert.IsTrue(builders.All(h => backend.ReadOrder(h).Kind == OrderKind.None), "the builders stand beside the site");
        }

        // The first builder starts a frame at the site. Returns it.
        int Start(int builder, int def, Vector3 site)
        {
            Assert.IsTrue(backend.Command(new GameCommand { Kind = CommandKind.Build, Unit = builder, Target = site, TargetUnit = -1, BuildDef = def }));
            for (int t = 0; t < 60 * 30; t++)
            {
                backend.Advance(1);
                int f = backend.ReadOrder(builder).Building;
                if (f >= 0) return f;
            }
            Assert.Fail("the frame went up");
            return -1;
        }

        static bool AtWork(int h) => OkEngine.okx_unit_anim(h, null, 0) == OkEngine.AnimBuilding;

        // Ticks until the frame is finished, with the treasury never empty,
        // so nothing but the builders sets the pace.
        int Finish(int built)
        {
            int t = 0;
            float least = float.MaxValue;
            for (; t < 60 * 120 && (Read(built).Flags & UnitFlags.Building) != 0; t++)
            {
                backend.Advance(1);
                least = Mathf.Min(least, backend.ReadEconomy(backend.LocalPlayer).Mana);
            }
            Assert.AreEqual(0, (int)(Read(built).Flags & UnitFlags.Building), "the frame was finished");
            Assert.Greater(least, 1f, "the treasury paid for every tick of work");
            return t;
        }

        // Two Mage Builders raise a wall twice as fast as one: each adds its
        // own work to the frame, as in the original (legacy:39451-39505). A
        // wall stands on any open ground and costs little, so the treasury
        // never sets the pace. The second joins through the hammer and the
        // game's click, faces the frame and works there with its order line
        // on it, as the first does. The pace is timed from when all are at
        // work, so the walk up and the turn do not count.
        [UnityTest, Timeout(900000)]
        public IEnumerator TwoBuildersRaiseAFrameInHalfTheTimeOneTakes()
        {
            yield return BeginEngine();
            var engine = (EngineBackend)backend;
            int me = backend.LocalPlayer, mageDef = DefNamed("arabuild"), wall = DefNamed("arawall");
            CollectionAssert.Contains(backend.UnitDefs[mageDef].BuildOptions, wall);
            int a = OkEngine.okx_place_unit(mageDef, me), b = OkEngine.okx_place_unit(mageDef, me);
            Assert.IsTrue(a >= 0 && b >= 0, "two Mage Builders set down");
            backend.Advance(2);

            // One alone.
            var site = SiteNear(wall, Read(a).Position);
            Stand(new[] { a, b }, site, wall);
            int alone = Start(a, wall, site);
            for (int t = 0; t < 120 && !AtWork(a); t++) backend.Advance(1);
            Assert.IsTrue(AtWork(a), "the builder is at work");
            float left1 = 1f - Read(alone).BuildProgress;
            int one = Finish(alone);

            // Two: the second joins through the pointer as soon as the frame is up.
            site = SiteNear(wall, Read(a).Position);
            Stand(new[] { a, b }, site, wall);
            int pair = Start(a, wall, site);
            backend.Select(new[] { b }, false);
            yield return PointAt(pair);
            Assert.AreEqual(GameCursor.Repair, root.Orders.PointerCursor(), "the hammer over the frame");
            yield return Click(false);
            backend.Advance(1);
            Assert.AreEqual(OrderKind.Build, backend.ReadOrder(b).Kind, "the click sent the second builder");
            Assert.AreEqual(pair, backend.ReadOrder(b).Building);
            for (int t = 0; t < 120 && !(AtWork(a) && AtWork(b)); t++) backend.Advance(1);

            // Both at work on it, facing it, each order line ending on it.
            var f = Read(pair);
            var legs = new OrderLeg[8];
            foreach (int h in new[] { a, b })
            {
                Assert.IsTrue(AtWork(h), "builder " + h + " is at work");
                var u = Read(h);
                var to = f.Position - u.Position;
                float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                Assert.Less(Mathf.Abs(Mathf.DeltaAngle(u.Heading, bearing)), 20f, "builder " + h + " faces the frame");
                Assert.Greater(engine.ReadOrderQueue(h, legs), 0);
                Assert.Less(new Vector2(legs[0].Target.x - f.Position.x, legs[0].Target.z - f.Position.z).magnitude, 1f, "builder " + h + "'s order line ends on the frame");
            }
            // The build sparkles play on the frame they raise.
            var fx = new EffectState[1024];
            int nfx = backend.ReadEffects(fx);
            var fp = backend.UnitDefs[f.Def].Footprint;
            float ring = Mathf.Max(fp.x, fp.y) + 2f;
            Assert.IsTrue(fx.Take(nfx).Any(e => !e.IsProjectile && new Vector2(e.Position.x - f.Position.x, e.Position.z - f.Position.z).magnitude <= ring),
                "the build sparkles play on the frame");
            float left2 = 1f - Read(pair).BuildProgress;
            int two = Finish(pair);
            float pace1 = left1 / one, pace2 = left2 / two;
            Debug.Log($"a wall: {left1:F3} of it in {one} ticks alone, {left2:F3} in {two} with two");
            Assert.AreEqual(2f, pace2 / pace1, 0.1f, "two builders raise it twice as fast, so in half the time");
            // A helper that did not lay the last of the work lets go on its next tick.
            backend.Advance(2);
            foreach (int h in new[] { a, b }) Assert.AreNotEqual(pair, backend.ReadOrder(h).Building, "each lets go of the finished building");
        }

        // A frame only the monarch can raise, under a modern right click
        // with him and a Mage Builder selected: the hammer shows, he goes to
        // work on it and the Mage Builder, held to its own list, walks there.
        [UnityTest, Timeout(900000)]
        public IEnumerator AModernRightClickSendsOnlyTheBuildersThatCouldBuildIt()
        {
            yield return BeginEngine();
            int me = backend.LocalPlayer, mageDef = DefNamed("arabuild"), kingDef = DefNamed("araking");
            int n = backend.ReadUnits(units);
            int king = units.Take(n).First(u => u.Player == me && u.Def == kingDef).Handle;
            int mage = OkEngine.okx_place_unit(mageDef, me);
            Assert.GreaterOrEqual(mage, 0);
            backend.Advance(2);
            var mageList = backend.UnitDefs[mageDef].BuildOptions;
            int only = backend.UnitDefs[kingDef].BuildOptions.First(o => backend.UnitDefs[o].IsBuilding && System.Array.IndexOf(mageList, o) < 0);
            int built = Start(king, only, SiteNear(only, Read(king).Position));
            backend.Advance(60);
            backend.Command(GameCommand.To(CommandKind.Stop, king, Vector3.zero));
            backend.Advance(2);

            root.Orders.Classic = false;
            root.Orders.Selected.Clear();
            root.Orders.Selected.Add(mage);
            backend.Select(new[] { mage }, false);
            yield return PointAt(built);
            Assert.AreNotEqual(GameCursor.Repair, root.Orders.PointerCursor(), "no hammer for the Mage Builder alone");
            Assert.IsFalse(backend.CanHelpBuild(mage, built));

            root.Orders.Selected.Add(king);
            backend.Select(new[] { mage, king }, false);
            yield return PointAt(built);
            Assert.AreEqual(GameCursor.Repair, root.Orders.PointerCursor(), "the hammer with the monarch among them");
            yield return Click(true);
            backend.Advance(1);
            Assert.AreEqual(OrderKind.Build, backend.ReadOrder(king).Kind, "the monarch goes to work on it");
            Assert.AreEqual(built, backend.ReadOrder(king).Building);
            Assert.AreEqual(OrderKind.Move, backend.ReadOrder(mage).Kind, "the Mage Builder walks there");
            float before = Read(built).BuildProgress;
            backend.Advance(60 * 10);
            Assert.Greater(Read(built).BuildProgress, before, "the frame rises again");
        }
    }
}
