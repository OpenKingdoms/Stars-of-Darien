// FlyerPickPlayTests.cs - every flyer up at its height on the real
// engine, pointed at where it is drawn. While they are yours the pointer
// shows the select hand over each and a click there selects it. Once they
// are the computer's, the pointer over each shows the attack cursor with
// your monarch selected, and a click there sends the monarch at it, the
// left click in the classic scheme and the right in the modern one. Needs
// okengine and the game files, and is ignored without them.
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
    public class FlyerPickPlayTests
    {
        GameRoot root;
        EngineBackend engine;
        PointerFrame frame;
        readonly UnitState[] units = new UnitState[1024];
        const int OrderAttack = 2;   // OKX_ORDER_ATTACK
        const int CmdGiveUnits = 24; // TAK_CMD_GIVE_UNITS, arg the seat receiving

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        IEnumerator Begin()
        {
            if (!EngineSettings.EngineAvailable) Assert.Ignore("okengine or the game files are missing");
            engine = new EngineBackend();
            root = GameRoot.Boot(engine);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "two castles";
            root.Setup.MapRevealed = true;
            root.Setup.LineOfSight = false;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 240f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, "the battle loaded: " + root.LastError);
            // The battle stands still and the test moves it on.
            root.Options.GameSpeed = 0;
            frame = new PointerFrame { Focused = true, Dpi = 96 };
            root.Orders.Formation.Source = () =>
            {
                var r = frame;
                frame.LeftDown = frame.LeftUp = frame.RightDown = frame.RightUp = false;
                return r;
            };
        }

        bool TryRead(int handle, out UnitState unit)
        {
            int n = engine.ReadUnits(units);
            for (int i = 0; i < n; i++) if (units[i].Handle == handle) { unit = units[i]; return true; }
            unit = default;
            return false;
        }

        UnitState Read(int handle)
        {
            int n = engine.ReadUnits(units);
            for (int i = 0; i < n; i++) if (units[i].Handle == handle) return units[i];
            Assert.Fail("unit " + handle + " is gone");
            return default;
        }

        Camera Cam => root.World.Camera.GetComponent<Camera>();

        // The middle of a flyer's body on screen, where it is drawn.
        IEnumerator PointAt(int handle)
        {
            var u = Read(handle);
            var gc = root.World.Camera;
            gc.focus = new Vector3(u.Position.x, engine.GroundHeight(u.Position.x, u.Position.z), u.Position.z);
            gc.yaw = 30f;
            gc.pitch = 45f;
            gc.Zoom(40f);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsTrue(root.World.Entities.DrawnSize.TryGetValue(handle, out var size), "the flyer is drawn");
            u = Read(handle);
            frame.Screen = Cam.WorldToScreenPoint(u.Position + Vector3.up * (size.x * 0.5f));
            yield return null;
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

        [UnityTest, Timeout(900000)]
        public IEnumerator EveryFlyerInTheAirIsSelectedAndAttackedWhereItIsDrawn()
        {
            yield return Begin();
            int me = engine.LocalPlayer;
            var defs = engine.UnitDefs;
            int n = engine.ReadUnits(units);
            var monarch = units.Take(n).First(u => u.Player == me && !defs[u.Def].IsBuilding && !defs[u.Def].CanFly);
            int them = units.Take(n).First(u => u.Player != me).Player;

            // One of every flyer at a time, yours, flown off its own way.
            root.Orders.Classic = true;
            var flyerDefs = Enumerable.Range(0, defs.Count).Where(d => defs[d].CanFly).ToList();
            Assert.Greater(flyerDefs.Count, 0);
            int up = 0;
            for (int k = 0; k < flyerDefs.Count; k++)
            {
                int h = OkEngine.okx_place_unit(flyerDefs[k], me);
                if (h < 0) continue;
                string name = defs[flyerDefs[k]].Name;
                var to = Read(h).Position + Quaternion.Euler(0f, 360f * k / flyerDefs.Count, 0f) * new Vector3(0f, 0f, 90f);
                engine.Command(GameCommand.To(CommandKind.Move, h, to));
                engine.Advance(150);
                // One may have fallen to the computer's army on the way.
                if (!TryRead(h, out var flown) || (flown.Flags & UnitFlags.Dying) != 0 || flown.Altitude <= 2.5f) continue;
                up++;

                // Yours: the select hand where it is drawn, and a click selects it.
                engine.Select(new int[0], false);
                yield return PointAt(h);
                Assert.AreEqual(h, root.Orders.PointerUnit, "the pointer is on " + name);
                Assert.AreEqual(GameCursor.Select, root.Orders.PointerCursor(), "the select hand over " + name);
                yield return Click(false);
                var sel = new int[16];
                Assert.AreEqual(1, engine.ReadSelection(sel));
                Assert.AreEqual(h, sel[0], "the click selected " + name);

                // The computer's, with the monarch selected: the attack cursor,
                // and the classic left click sends the monarch at it.
                Assert.AreEqual(0, OkEngine.okx_command(CmdGiveUnits, h, 0, 0, -1, -1, them));
                engine.Advance(1);
                Assert.AreEqual(them, Read(h).Player);
                engine.Select(new[] { monarch.Handle }, false);
                yield return PointAt(h);
                Assert.AreEqual(h, root.Orders.PointerUnit, "the pointer is on " + name);
                Assert.AreEqual(GameCursor.Attack, root.Orders.PointerCursor(), "the attack cursor over " + name);
                yield return Click(false);
                engine.Advance(1);
                Assert.AreEqual(0, OkEngine.okx_unit_order(monarch.Handle, out var order));
                Assert.AreEqual(OrderAttack, order.kind, "the classic click sent the monarch at " + name);
                Assert.AreEqual(h, order.target);
                engine.Command(GameCommand.To(CommandKind.Stop, monarch.Handle, Vector3.zero));
                engine.Advance(1);

                // The modern scheme's right click does the same.
                root.Orders.Classic = false;
                root.Orders.Selected.Clear();
                root.Orders.Selected.Add(monarch.Handle);
                engine.Select(new[] { monarch.Handle }, false);
                yield return PointAt(h);
                Assert.AreEqual(GameCursor.Attack, root.Orders.PointerCursor(), "the attack cursor over " + name + " in the modern scheme");
                yield return Click(true);
                engine.Advance(1);
                Assert.AreEqual(0, OkEngine.okx_unit_order(monarch.Handle, out order));
                Assert.AreEqual(OrderAttack, order.kind, "the right click sent the monarch at " + name);
                Assert.AreEqual(h, order.target);
                engine.Command(GameCommand.To(CommandKind.Stop, monarch.Handle, Vector3.zero));
                engine.Advance(1);
                root.Orders.Selected.Clear();
                root.Orders.Classic = true;
            }
            Debug.Log($"{flyerDefs.Count} flyers, {up} up");
            Assert.Greater(up, 0, "flyers went up to their height");
        }

        // The cells a footprint of fp cells covers from its corner, as the
        // engine stamps a unit at world x and z.
        static Vector2Int Corner(Vector3 p, int fp) =>
            new Vector2Int(Mathf.FloorToInt(p.x - fp * 0.5f), Mathf.FloorToInt(-p.z - fp * 0.5f));

        [UnityTest, Timeout(900000)]
        public IEnumerator HarpiesSentToOnePlaceEndApart()
        {
            // Ten Harpies ordered onto one point step out of each other's
            // cells in the air and land on clear ground, so none ends on another.
            yield return Begin();
            int me = engine.LocalPlayer;
            var defs = engine.UnitDefs;
            int harpy = Enumerable.Range(0, defs.Count).Where(d => string.Equals(defs[d].Name, "ZONHARP", System.StringComparison.OrdinalIgnoreCase)).DefaultIfEmpty(-1).First();
            if (harpy < 0 || !defs[harpy].CanFly) Assert.Ignore("no Harpy in these game files");
            int fp = Mathf.Max(1, defs[harpy].Footprint.x);
            var flock = new int[10];
            for (int i = 0; i < flock.Length; i++)
            {
                flock[i] = OkEngine.okx_place_unit(harpy, me);
                Assert.GreaterOrEqual(flock[i], 0, "a Harpy was placed");
            }
            var at = Read(flock[0]).Position + new Vector3(0f, 0f, 16f);
            foreach (var h in flock) engine.Command(GameCommand.To(CommandKind.Move, h, at));
            engine.Advance(3600);
            int shared = 0;
            float far = 0f;
            for (int i = 0; i < flock.Length; i++)
            {
                var a = Read(flock[i]);
                far = Mathf.Max(far, new Vector2(a.Position.x - at.x, a.Position.z - at.z).magnitude);
                Assert.Less(a.Altitude, 0.5f, "Harpy " + i + " landed");
                for (int j = i + 1; j < flock.Length; j++)
                {
                    var b = Read(flock[j]);
                    var ca = Corner(a.Position, fp);
                    var cb = Corner(b.Position, fp);
                    if (Mathf.Abs(ca.x - cb.x) < fp && Mathf.Abs(ca.y - cb.y) < fp) shared++;
                }
            }
            Debug.Log($"{shared} of 45 pairs share cells, farthest {far:0.0} from the point");
            Assert.AreEqual(0, shared, "no Harpy ends on another");
            Assert.Less(far, 32f, "they still gather at the point");
        }
    }
}
