// FormationPlayTests.cs - the formation drag in a running game on the mock,
// driven by synthetic pointer frames: a drag previews a slot per unit and
// the release marches each role block into it at its own pace, Shift
// queues, a short press is still a click in both schemes, a press on an
// enemy still attacks, a plain left drag is left to the box select, and a
// flyer forms in the air.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class FormationPlayTests
    {
        GameRoot root;
        MockBackend mock;
        PointerFrame frame;
        // When set, the pointer sits on this world point as the frame is
        // read, through the same camera the game picks with.
        System.Func<Vector3> follow;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        IEnumerator Begin(bool classic, int extra = 0)
        {
            mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f, ExtraSoldiers = extra };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_highlands";
            root.Setup.MapRevealed = true;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
            root.Orders.Classic = classic;
            var f = root.Orders.Formation;
            f.Shape = FormationShape.Line;
            f.Pace = true;
            f.Facing = FormationFacing.ByDrag;
            f.ClassicRightDrag = true;
            frame = new PointerFrame { Focused = true, Dpi = 96 };
            follow = null;
            // Edges (presses, releases, keys) last one frame.
            f.Source = () =>
            {
                var r = frame;
                r.Seconds = Time.unscaledTime;
                if (follow != null) r.Screen = Cam.WorldToScreenPoint(follow());
                frame.LeftDown = frame.RightDown = frame.LeftUp = frame.RightUp = false;
                frame.TabDown = frame.FDown = frame.GDown = frame.EscapeDown = false;
                return r;
            };
            for (int i = 0; i < 10; i++) yield return null;
        }

        UnitState[] Units()
        {
            var u = new UnitState[1024];
            return u.Take(mock.ReadUnits(u)).ToArray();
        }

        UnitState UnitOf(int h) => Units().First(u => u.Handle == h);

        int[] Own(MockBackend.Role role, int n) =>
            Units().Where(u => u.Player == mock.LocalPlayer && mock.RoleOf(u.Def) == role).Take(n).Select(u => u.Handle).ToArray();

        void Select(int[] handles)
        {
            mock.Select(handles, false);
            root.Orders.Selected.Clear();
            root.Orders.Selected.UnionWith(handles);
        }

        Camera Cam => root.World.Camera.GetComponent<Camera>();

        Vector2 ScreenOf(Vector2 ground)
        {
            var p = new Vector3(ground.x, mock.GroundHeight(ground.x, ground.y), ground.y);
            return Cam.WorldToScreenPoint(p);
        }

        // Frames: press, drag over a few frames, then held for more.
        IEnumerator Press(int button, Vector2 at, bool ctrl = false)
        {
            frame.Screen = ScreenOf(at);
            frame.Ctrl = ctrl;
            if (button == 0) frame.LeftDown = frame.LeftHeld = true; else frame.RightDown = frame.RightHeld = true;
            yield return null;
        }

        IEnumerator DragTo(Vector2 from, Vector2 to, int steps = 6)
        {
            for (int i = 1; i <= steps; i++)
            {
                frame.Screen = ScreenOf(Vector2.Lerp(from, to, (float)i / steps));
                yield return null;
            }
        }

        IEnumerator Release(int button, bool shift = false)
        {
            frame.Shift = shift;
            if (button == 0) { frame.LeftHeld = false; frame.LeftUp = true; } else { frame.RightHeld = false; frame.RightUp = true; }
            yield return null;
            frame.Shift = false;
            frame.Ctrl = false;
        }

        void Look(Vector2 at, float distance = 34f)
        {
            root.World.Camera.focus = new Vector3(at.x, mock.GroundHeight(at.x, at.y), at.y);
            root.World.Camera.yaw = 0;
            root.World.Camera.pitch = GameCamera.ClassicPitch;
            root.World.Camera.Zoom(distance);
        }

        // The march, tick by tick, with the fastest step each role took.
        (float knight, float archer) March(int[] knights, int[] archers, int ticks)
        {
            float k = 0, a = 0;
            var last = new Dictionary<int, Vector3>();
            foreach (var u in Units()) last[u.Handle] = u.Position;
            for (int t = 0; t < ticks; t++)
            {
                mock.Advance(1);
                foreach (var u in Units())
                {
                    if (!last.TryGetValue(u.Handle, out var was)) continue;
                    float step = new Vector2(u.Position.x - was.x, u.Position.z - was.z).magnitude;
                    if (knights.Contains(u.Handle)) k = Mathf.Max(k, step);
                    if (archers.Contains(u.Handle)) a = Mathf.Max(a, step);
                    last[u.Handle] = u.Position;
                }
            }
            return (k, a);
        }

        // The calls sent since a count of them was taken.
        MockBackend.FormationCall[] CallsSince(int count) => mock.FormationCalls.Skip(count).ToArray();

        [UnityTest]
        public IEnumerator ADragPreviewsASlotPerUnitAndTheyMarchIntoIt()
        {
            yield return Begin(false, extra: 8);
            var knights = Own(MockBackend.Role.Knight, 8);
            var archers = Own(MockBackend.Role.Archer, 4);
            Assert.AreEqual(8, knights.Length);
            Assert.AreEqual(4, archers.Length);
            var all = knights.Concat(archers).ToArray();
            Select(all);
            var centre = all.Select(h => UnitOf(h).Position).Aggregate(Vector3.zero, (s, p) => s + p) / all.Length;
            var a = new Vector2(centre.x - 9, centre.z + 12);
            var b = new Vector2(centre.x + 9, centre.z + 12);
            Look((a + new Vector2(centre.x, centre.z)) * 0.5f);
            for (int i = 0; i < 5; i++) yield return null;

            yield return Press(1, a);
            Assert.AreEqual(GestureState.Pending, root.Orders.Formation.Gesture.State, "the right press waits");
            yield return DragTo(a, b);
            var f = root.Orders.Formation;
            Assert.IsTrue(f.Live, "past the threshold the drag is live");
            Assert.AreEqual(12, f.SlotCount, "a slot per unit");
            Assert.AreEqual(12, f.Preview.Markers, "and a marker for each");
            Assert.AreEqual(GameCursor.Move, root.Orders.PointerCursor());
            StringAssert.StartsWith("Line", f.ReadoutText);
            Assert.IsNull(mock.LastFormation, "nothing is sent while the drag is live");
            for (int i = 0; i < 5; i++) yield return null;
            var shown = f.Layers[0].Slots.Take(12).Select(s => (f.Layers[0].Members[s.Member].Handle, s.World)).ToArray();

            yield return Release(1);
            Assert.IsFalse(f.Busy);
            var calls = CallsSince(0);
            Assert.AreEqual(2, calls.Length, "the release sends one order per role block");
            CollectionAssert.AreEquivalent(knights, calls[0].Units, "the knights, who are the melee");
            CollectionAssert.AreEquivalent(archers, calls[1].Units, "then the archers");
            Assert.AreEqual(12, calls.Sum(c => c.Accepted));
            foreach (var sent in calls)
            {
                Assert.AreEqual(0f, Mathf.DeltaAngle(sent.Heading.Value, 0f), 0.5f, "dragged left to right, the line faces away from the camera");
                Assert.IsTrue(sent.GroupSpeed);
                Assert.IsFalse(sent.Queue);
                for (int i = 0; i < sent.Units.Length; i++) Assert.AreEqual(shown.First(s => s.Handle == sent.Units[i]).World, sent.Targets[i], "what the preview showed is what is sent");
            }
            Assert.AreEqual(MockBackend.KnightSpeed, mock.PaceOf(knights[0]), "the knights keep their own block's pace");
            Assert.AreEqual(MockBackend.FootSpeed, mock.PaceOf(archers[0]), "and the archers theirs");

            var (knightStep, archerStep) = March(knights, archers, 30 * 20);
            Assert.LessOrEqual(knightStep, MockBackend.KnightSpeed / MockBackend.Tps + 1e-4f);
            Assert.LessOrEqual(archerStep, MockBackend.FootSpeed / MockBackend.Tps + 1e-4f);
            var face = FormationPlanner.DirOf(calls[0].Heading.Value);
            float rearKnight = float.MaxValue, frontArcher = float.MinValue;
            foreach (var sent in calls)
                for (int i = 0; i < sent.Units.Length; i++)
                {
                    var u = UnitOf(sent.Units[i]);
                    var at = new Vector2(u.Position.x, u.Position.z);
                    Assert.Less((at - sent.Targets[i]).magnitude, 0.5f, $"unit {u.Handle} stands in its slot");
                    Assert.Less(Mathf.Abs(Mathf.DeltaAngle(u.Heading, sent.Heading.Value)), 10f, $"unit {u.Handle} faces the formation's way");
                    float depth = Vector2.Dot(at, face);
                    if (knights.Contains(u.Handle)) rearKnight = Mathf.Min(rearKnight, depth);
                    else frontArcher = Mathf.Max(frontArcher, depth);
                }
            Assert.Less(frontArcher, rearKnight, "every archer stands behind every knight");
        }

        [UnityTest]
        public IEnumerator AShiftReleaseQueuesTheFormation()
        {
            yield return Begin(false);
            var knights = Own(MockBackend.Role.Knight, 4);
            Select(knights);
            var c = UnitOf(knights[0]).Position;
            var a = new Vector2(c.x - 5, c.z + 10);
            var b = new Vector2(c.x + 5, c.z + 10);
            Look(new Vector2(c.x, c.z + 8));
            for (int i = 0; i < 5; i++) yield return null;
            yield return Press(1, a);
            yield return DragTo(a, b);
            yield return Release(1);
            Assert.IsFalse(mock.LastFormation.Queue);
            var first = mock.LastFormation;

            var a2 = a + new Vector2(0, 8);
            var b2 = b + new Vector2(0, 8);
            yield return Press(1, a2);
            yield return DragTo(a2, b2);
            yield return Release(1, shift: true);
            Assert.IsTrue(mock.LastFormation.Queue, "Shift at the release queues");
            Assert.IsTrue(knights.All(h => mock.QueuedLegs(h) == 1), "the second waits behind the first");
            Assert.AreEqual(4, root.Orders.Formation.Preview.QueuedCount, "its markers stay on the ground");
            March(knights, new int[0], 30 * 15);
            var second = mock.LastFormation;
            for (int i = 0; i < second.Units.Length; i++)
            {
                var u = UnitOf(second.Units[i]);
                Assert.Less((new Vector2(u.Position.x, u.Position.z) - second.Targets[i]).magnitude, 0.5f, "they end in the queued formation");
            }
            Assert.AreNotEqual(first.Targets[0], second.Targets[0]);
            for (int i = 0; i < 20; i++) yield return null;
            Assert.AreEqual(0, root.Orders.Formation.Preview.QueuedCount, "the queued markers go once the units arrive");

            // A formation sent without Shift replaces what was queued, markers too.
            Look(new Vector2(c.x, c.z + 14));
            yield return Press(1, a);
            yield return DragTo(a, b);
            yield return Release(1);
            var a4 = a2 + new Vector2(0, 6);
            var b4 = b2 + new Vector2(0, 6);
            yield return Press(1, a4);
            yield return DragTo(a4, b4);
            yield return Release(1, shift: true);
            Assert.AreEqual(4, root.Orders.Formation.Preview.QueuedCount);
            var a3 = a - new Vector2(0, 6);
            var b3 = b - new Vector2(0, 6);
            yield return Press(1, a3);
            yield return DragTo(a3, b3);
            yield return Release(1);
            Assert.IsTrue(knights.All(h => mock.QueuedLegs(h) == 0), "the new formation replaces the queue");
            Assert.AreEqual(0, root.Orders.Formation.Preview.QueuedCount, "and its markers go with it");
        }

        [UnityTest]
        public IEnumerator AShortPressIsStillAClick()
        {
            // Modern: a short right press moves the selection to the press point.
            yield return Begin(false);
            var knights = Own(MockBackend.Role.Knight, 3);
            Select(knights);
            var c = UnitOf(knights[0]).Position;
            var at = new Vector2(c.x + 3, c.z + 11);
            Look(new Vector2(c.x, c.z + 7));
            for (int i = 0; i < 5; i++) yield return null;
            yield return Press(1, at);
            frame.Screen += new Vector2(4, 3);
            yield return null;
            yield return Release(1);
            Assert.IsNull(mock.LastFormation, "a click sends no formation");
            foreach (var h in knights)
            {
                var o = mock.ReadOrder(h);
                Assert.AreEqual(OrderKind.Move, o.Kind);
                Assert.Less(new Vector2(o.Target.x - at.x, o.Target.z - at.y).magnitude, 3f, "moved in a block to the press point");
            }
            Object.Destroy(root.gameObject);
            root = null;
            yield return null;

            // Classic: a short Ctrl left press is the ordinary left click.
            yield return Begin(true);
            knights = Own(MockBackend.Role.Knight, 3);
            mock.Select(knights, false);
            c = UnitOf(knights[0]).Position;
            at = new Vector2(c.x + 3, c.z + 11);
            Look(new Vector2(c.x, c.z + 7));
            for (int i = 0; i < 5; i++) yield return null;
            yield return Press(0, at, ctrl: true);
            Assert.AreEqual(GestureState.Pending, root.Orders.Formation.Gesture.State);
            yield return Release(0);
            Assert.IsNull(mock.LastFormation);
            foreach (var h in knights)
            {
                var o = mock.ReadOrder(h);
                Assert.AreEqual(OrderKind.Move, o.Kind);
                Assert.Less(new Vector2(o.Target.x - at.x, o.Target.z - at.y).magnitude, 1f, "the engine's click moves them there");
            }
        }

        [UnityTest]
        public IEnumerator AShortPressSpacesTheBlockByFootprint()
        {
            // Modern: units wider than a block slot are sent a footprint apart,
            // so a flock of flyers is not sent onto one another.
            yield return Begin(false);
            var knights = Own(MockBackend.Role.Knight, 4);
            mock.UnitDefs[UnitOf(knights[0]).Def].Footprint = new Vector2Int(3, 3);
            Select(knights);
            var c = UnitOf(knights[0]).Position;
            var at = new Vector2(c.x + 3, c.z + 11);
            Look(new Vector2(c.x, c.z + 7));
            for (int i = 0; i < 5; i++) yield return null;
            yield return Press(1, at);
            yield return null;
            yield return Release(1);
            var to = knights.Select(h => mock.ReadOrder(h).Target).ToArray();
            for (int i = 0; i < to.Length; i++)
                for (int j = i + 1; j < to.Length; j++)
                    Assert.GreaterOrEqual(new Vector2(to[i].x - to[j].x, to[i].z - to[j].z).magnitude, 2.99f, "slots a footprint apart");
        }

        [UnityTest]
        public IEnumerator TheClassicShortRightClickDisarmsThenDeselects()
        {
            yield return Begin(true);
            var knights = Own(MockBackend.Role.Knight, 3);
            mock.Select(knights, false);
            var c = UnitOf(knights[0]).Position;
            var at = new Vector2(c.x + 2, c.z + 6);
            Look(at);
            for (int i = 0; i < 5; i++) yield return null;

            // Armed, the right press cancels at once and no drag can start.
            root.Orders.Arm(CommandKind.Move);
            yield return Press(1, at);
            Assert.AreEqual(GestureState.Idle, root.Orders.Formation.Gesture.State);
            Assert.IsNull(root.Orders.Armed, "the press disarms");
            Assert.AreEqual(GameCursor.Normal, mock.CursorAt(Vector3.zero, -1, out _));
            Assert.AreEqual(3, mock.ReadSelection(new int[8]), "and keeps the selection");
            yield return Release(1);

            // Unarmed, a short flick waits for the release and then deselects.
            yield return Press(1, at);
            frame.Screen += new Vector2(10, 0);
            yield return null;
            Assert.AreEqual(GestureState.Pending, root.Orders.Formation.Gesture.State);
            Assert.AreEqual(3, mock.ReadSelection(new int[8]), "nothing happens until the release");
            yield return Release(1);
            Assert.AreEqual(0, mock.ReadSelection(new int[8]), "the release deselects");
            Assert.IsNull(mock.LastFormation);

            // With nothing selected the right press is left to the old path.
            yield return Press(1, at);
            Assert.AreEqual(GestureState.Idle, root.Orders.Formation.Gesture.State);
            yield return Release(1);
        }

        // The screen box around some units, with a margin.
        Rect BoxAround(int[] handles, float margin)
        {
            var pts = handles.Select(h => (Vector2)Cam.WorldToScreenPoint(UnitOf(h).Position + Vector3.up * 0.6f)).ToArray();
            return Rect.MinMaxRect(pts.Min(q => q.x) - margin, pts.Min(q => q.y) - margin, pts.Max(q => q.x) + margin, pts.Max(q => q.y) + margin);
        }

        IEnumerator LeftDragOnScreen(Vector2 from, Vector2 to, bool ctrl)
        {
            frame.Screen = from;
            frame.Ctrl = ctrl;
            frame.LeftDown = frame.LeftHeld = true;
            yield return null;
            for (int i = 1; i <= 6; i++)
            {
                frame.Screen = Vector2.Lerp(from, to, i / 6f);
                yield return null;
            }
        }

        int[] Selection()
        {
            if (!root.Orders.Classic) return root.Orders.Selected.ToArray();
            var into = new int[256];
            return into.Take(mock.ReadSelection(into)).ToArray();
        }

        [UnityTest]
        public IEnumerator APlainLeftDragIsStillTheBoxInBothSchemes()
        {
            foreach (bool classic in new[] { true, false })
            {
                yield return Begin(classic);
                var knights = Own(MockBackend.Role.Knight, 3);
                var archer = Own(MockBackend.Role.Archer, 1);
                Select(archer);
                var k0 = UnitOf(knights[0]).Position;
                Look(new Vector2(k0.x, k0.z));
                for (int i = 0; i < 5; i++) yield return null;
                var box = BoxAround(knights, 30f);
                yield return LeftDragOnScreen(box.min, box.max, false);
                Assert.AreEqual(GestureState.Idle, root.Orders.Formation.Gesture.State, "the left drag is not the formation's");
                yield return Release(0);
                Assert.IsNull(mock.LastFormation);
                CollectionAssert.IsSubsetOf(knights, Selection(), $"classic {classic}: the box selects the knights in it");

                if (classic)
                {
                    // Ctrl with nothing selected is the box too.
                    Select(new int[0]);
                    yield return null;
                    yield return LeftDragOnScreen(box.min, box.max, true);
                    Assert.AreEqual(GestureState.Idle, root.Orders.Formation.Gesture.State);
                    yield return Release(0);
                    CollectionAssert.IsSubsetOf(knights, Selection(), "Ctrl with nothing selected still boxes");
                    Assert.IsNull(mock.LastFormation);

                    // Ctrl with a selection draws a formation and leaves the selection be.
                    Select(archer);
                    yield return null;
                    var a = new Vector2(k0.x - 4, k0.z + 6);
                    var b = new Vector2(k0.x + 4, k0.z + 6);
                    yield return Press(0, a, ctrl: true);
                    yield return DragTo(a, b);
                    Assert.IsTrue(root.Orders.Formation.Live, "Ctrl with the left button draws a formation");
                    yield return Release(0);
                    Assert.IsNotNull(mock.LastFormation);
                    CollectionAssert.AreEqual(archer, mock.LastFormation.Units);
                    CollectionAssert.AreEquivalent(archer, Selection(), "and no box was drawn");
                }
                Object.Destroy(root.gameObject);
                root = null;
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator EscapeAbortsADragAndSendsNothing()
        {
            yield return Begin(true);
            var knights = Own(MockBackend.Role.Knight, 4);
            mock.Select(knights, false);
            var c = UnitOf(knights[0]).Position;
            var a = new Vector2(c.x - 5, c.z + 10);
            var b = new Vector2(c.x + 5, c.z + 10);
            Look(new Vector2(c.x, c.z + 8));
            for (int i = 0; i < 5; i++) yield return null;
            yield return Press(1, a);
            yield return DragTo(a, b);
            // The classic right button draws only once held past a flick.
            yield return new WaitForSecondsRealtime(FormationTuning.ClassicRightHoldSeconds + 0.05f);
            yield return null;
            Assert.IsTrue(root.Orders.Formation.Live, "held, the classic right drag makes a formation");
            root.BattleKey(KeyCode.Escape);
            Assert.IsFalse(root.Orders.Formation.Busy, "Escape aborts the drag");
            Assert.AreEqual(4, mock.ReadSelection(new int[8]), "and only the drag");
            yield return Release(1);
            Assert.IsNull(mock.LastFormation);
            Assert.AreEqual(4, mock.ReadSelection(new int[8]), "the release after it does nothing");
        }

        [UnityTest]
        public IEnumerator APressOnAnEnemyStillAttacks()
        {
            yield return Begin(false);
            var knights = Own(MockBackend.Role.Knight, 4);
            Select(knights);
            var enemy = Units().First(u => !mock.Allied(u.Player, mock.LocalPlayer) && mock.RoleOf(u.Def) == MockBackend.Role.Lodge);
            var at = new Vector2(enemy.Position.x, enemy.Position.z);
            Look(at);
            for (int i = 0; i < 5; i++) yield return null;
            Vector3 On() => enemy.Position + Vector3.up * 0.8f;
            follow = On;
            frame.RightDown = frame.RightHeld = true;
            yield return null;
            follow = null;
            Assert.AreEqual(enemy.Handle, root.Orders.PointerUnit, "the press lands on the enemy");
            Assert.AreEqual(GestureState.Idle, root.Orders.Formation.Gesture.State, "no formation starts on an enemy");
            frame.Screen = (Vector2)Cam.WorldToScreenPoint(On()) + new Vector2(40, 0);
            yield return null;
            yield return Release(1);
            Assert.AreEqual(0, mock.FormationCalls.Count, "dragged 40 pixels it sends no formation");
            foreach (var h in knights)
            {
                var o = mock.ReadOrder(h);
                Assert.AreEqual(OrderKind.Attack, o.Kind, $"knight {h} attacks");
                Assert.AreEqual(enemy.Handle, o.TargetUnit);
            }
        }

        [UnityTest]
        public IEnumerator ARightPressDuringABoxDragKeepsTheBox()
        {
            yield return Begin(false);
            var knights = Own(MockBackend.Role.Knight, 3);
            var archer = Own(MockBackend.Role.Archer, 1);
            Select(archer);
            var k0 = UnitOf(knights[0]).Position;
            Look(new Vector2(k0.x, k0.z));
            for (int i = 0; i < 5; i++) yield return null;
            var box = BoxAround(knights, 30f);
            yield return LeftDragOnScreen(box.min, box.max, false);
            frame.RightDown = frame.RightHeld = true;
            yield return null;
            Assert.AreEqual(GestureState.Idle, root.Orders.Formation.Gesture.State, "the right press does not take the mouse from the box");
            // The left comes up first, while the right is still held.
            yield return Release(0);
            CollectionAssert.IsSubsetOf(knights, Selection(), "the box still selects");
            frame.RightHeld = false;
            frame.RightUp = true;
            yield return null;
            Assert.AreEqual(0, mock.FormationCalls.Count);
        }

        [UnityTest]
        public IEnumerator AFlyerFormsInTheAirAtItsOwnPace()
        {
            yield return Begin(false);
            var knights = Own(MockBackend.Role.Knight, 4);
            var flyer = Own(MockBackend.Role.Flyer, 1);
            Assert.AreEqual(1, flyer.Length, "the mock gives every side a flyer");
            Select(knights.Concat(flyer).ToArray());
            var c = UnitOf(knights[0]).Position;
            var a = new Vector2(c.x - 6, c.z + 10);
            var b = new Vector2(c.x + 6, c.z + 10);
            Look(new Vector2(c.x, c.z + 8));
            for (int i = 0; i < 5; i++) yield return null;
            yield return Press(1, a);
            yield return DragTo(a, b);
            var f = root.Orders.Formation;
            Assert.IsTrue(f.Live);
            Assert.AreEqual(4, f.Layers[(int)FormationLayer.Ground].Count);
            Assert.AreEqual(1, f.Layers[(int)FormationLayer.Air].Count, "the flyer is in the air layer");
            Assert.AreEqual(FormationMover.Flyer, f.KindOf(UnitOf(flyer[0]).Def).Mover);
            yield return Release(1);
            var toAir = mock.FormationCalls.Single(k => k.Units.Contains(flyer[0]));
            CollectionAssert.AreEqual(flyer, toAir.Units, "the flyer gets its own order");
            Assert.AreEqual(MockBackend.FlyerSpeed, mock.PaceOf(flyer[0]), "at a flyer's speed, not a walker's");
            March(knights, flyer, 30 * 12);
            var u = UnitOf(flyer[0]);
            Assert.Less((new Vector2(u.Position.x, u.Position.z) - toAir.Targets[0]).magnitude, 0.5f, "it flies to its slot");
        }

        [UnityTest]
        public IEnumerator TheReadoutStaysOffTheFormation()
        {
            yield return Begin(false, extra: 8);
            // Eight on a twelve cell line stand two ranks deep.
            var knights = Own(MockBackend.Role.Knight, 8);
            Select(knights);
            var c = UnitOf(knights[0]).Position;
            Look(new Vector2(c.x, c.z + 8));
            for (int i = 0; i < 5; i++) yield return null;
            var f = root.Orders.Formation;
            var size = new Vector2(60, 20);
            foreach (bool rightToLeft in new[] { false, true })
            {
                var a = new Vector2(c.x + (rightToLeft ? 6 : -6), c.z + 10);
                var b = new Vector2(c.x + (rightToLeft ? -6 : 6), c.z + 10);
                yield return Press(1, a);
                yield return DragTo(a, b);
                Assert.IsTrue(f.Live);
                var box = f.ReadoutBox(size);
                float px = ScreenOf(b).x;
                if (rightToLeft) Assert.LessOrEqual(box.xMax, px, "the formation lies right of the pointer, so the readout goes left");
                else Assert.GreaterOrEqual(box.xMin, px, "and right when the formation lies left");
                // Facing away from the camera the ranks come toward it, below the pointer.
                if (!rightToLeft) Assert.LessOrEqual(box.yMax, Screen.height - ScreenOf(b).y, "above the pointer when the ranks lie below");
                yield return Release(1);
            }
        }
    }
}
