// KeepQueuePlayTests.cs - Ctrl on an order, as the original's manual has
// it: a Ctrl-click replaces the order in hand and keeps the ones queued
// behind it, in both schemes, while a Ctrl drag stays a formation.
using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class KeepQueuePlayTests
    {
        GameRoot root;
        MockBackend mock;
        PointerFrame frame;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        IEnumerator Begin(bool classic)
        {
            mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_highlands";
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
            root.Orders.Classic = classic;
            frame = new PointerFrame { Focused = true, Dpi = 96 };
            root.Orders.Formation.Source = () =>
            {
                var r = frame;
                frame.LeftDown = frame.LeftUp = frame.RightDown = frame.RightUp = false;
                return r;
            };
        }

        UnitState[] Units()
        {
            var u = new UnitState[512];
            int n = mock.ReadUnits(u);
            return u.Take(n).ToArray();
        }

        Camera Cam => root.World.Camera.GetComponent<Camera>();

        // Open ground dist from a point, away from every unit.
        Vector3 OpenGround(Vector3 from, float dist, float turn = 0f)
        {
            var at = from;
            for (int k = 0; k < 16; k++)
            {
                at = from + Quaternion.Euler(0, turn + k * 22.5f, 0) * new Vector3(0f, 0f, dist);
                var p = at;
                if (!Units().Any(u => new Vector2(u.Position.x - p.x, u.Position.z - p.z).magnitude < 2.5f)) break;
            }
            at.y = mock.GroundHeight(at.x, at.z);
            return at;
        }

        IEnumerator Look(Vector3 at)
        {
            var cam = root.World.Camera;
            cam.focus = at;
            cam.yaw = 0f;
            cam.pitch = GameCamera.ClassicPitch;
            cam.Zoom(30f);
            for (int i = 0; i < 3; i++) yield return null;
        }

        IEnumerator Click(Vector3 at, bool shift = false, bool ctrl = false, bool right = false)
        {
            frame.Shift = shift;
            frame.Ctrl = ctrl;
            frame.Screen = Cam.WorldToScreenPoint(at);
            if (right) frame.RightDown = frame.RightHeld = true;
            else frame.LeftDown = frame.LeftHeld = true;
            yield return null;
            if (right) { frame.RightHeld = false; frame.RightUp = true; }
            else { frame.LeftHeld = false; frame.LeftUp = true; }
            yield return null;
            yield return null;
            frame.Shift = frame.Ctrl = false;
        }

        int Legs(int handle, out OrderLeg[] legs)
        {
            legs = new OrderLeg[16];
            return mock.ReadOrderQueue(handle, legs);
        }

        // A knight walking to a with b and c queued behind.
        IEnumerator Queue3(bool classic, bool right)
        {
            yield return Begin(classic);
            var knight = Units().First(u => u.Player == mock.LocalPlayer && mock.RoleOf(u.Def) == MockBackend.Role.Knight);
            knightHandle = knight.Handle;
            mock.Select(new[] { knight.Handle }, false);
            if (!classic) { root.Orders.Selected.Clear(); root.Orders.Selected.Add(knight.Handle); }
            yield return Look(knight.Position);
            yield return Click(OpenGround(knight.Position, 6f), right: right);
            yield return Click(OpenGround(knight.Position, 10f), shift: true, right: right);
            yield return Click(OpenGround(knight.Position, 14f), shift: true, right: right);
            Assert.AreEqual(3, Legs(knight.Handle, out _), "Shift queues two orders behind the first");
        }

        int knightHandle = -1;

        int Knight() => knightHandle;

        [UnityTest]
        public IEnumerator ACtrlClickReplacesTheOrderInHandAndKeepsTheQueue()
        {
            yield return Queue3(true, false);
            int knight = Knight();
            var legsBefore = new OrderLeg[16];
            Legs(knight, out legsBefore);
            var d = OpenGround(Units().First(u => u.Handle == knight).Position, 8f, 180f);
            yield return Click(d, ctrl: true);
            Assert.AreEqual(3, Legs(knight, out var legs), "the queue stays behind the new order");
            Assert.AreEqual(d.x, legs[0].Target.x, 0.6f, "the new order is the one in hand");
            Assert.AreEqual(d.z, legs[0].Target.z, 0.6f);
            Assert.AreEqual(legsBefore[1].Target.x, legs[1].Target.x, 0.6f, "and the queued ones follow as they were");
            Assert.AreEqual(legsBefore[2].Target.z, legs[2].Target.z, 0.6f);

            yield return Click(d);
            Assert.AreEqual(1, Legs(knight, out _), "a plain click still replaces them all");
        }

        [UnityTest]
        public IEnumerator ACtrlDragIsStillAFormation()
        {
            yield return Queue3(true, false);
            int knight = Knight();
            var from = Cam.WorldToScreenPoint(OpenGround(Units().First(u => u.Handle == knight).Position, 8f, 180f));
            frame.Ctrl = true;
            frame.Screen = from;
            frame.LeftDown = frame.LeftHeld = true;
            yield return null;
            for (int i = 1; i <= 6; i++)
            {
                frame.Screen = (Vector2)from + new Vector2(i * 12f, 0f);
                yield return null;
            }
            Assert.IsTrue(root.Orders.Formation.Live, "Ctrl with a drag lays out a formation");
            frame.LeftHeld = false;
            frame.LeftUp = true;
            yield return null;
            yield return null;
            frame.Ctrl = false;
            Assert.AreEqual(1, Legs(knight, out _), "a formation replaces the queue, as a plain drag does");
        }

        [UnityTest]
        public IEnumerator ACtrlRightClickKeepsTheQueueInTheModernScheme()
        {
            yield return Queue3(false, true);
            int knight = Knight();
            var d = OpenGround(Units().First(u => u.Handle == knight).Position, 8f, 180f);
            yield return Click(d, ctrl: true, right: true);
            Assert.AreEqual(3, Legs(knight, out var legs), "the queue stays behind the new order");
            Assert.AreEqual(d.x, legs[0].Target.x, 1.5f, "the new order is the one in hand");
            Assert.AreEqual(d.z, legs[0].Target.z, 1.5f);
        }
    }
}
