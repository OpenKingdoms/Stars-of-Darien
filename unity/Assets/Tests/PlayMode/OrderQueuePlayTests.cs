// OrderQueuePlayTests.cs - the owner's list of 2026-09-29 on the mock:
// Shift queues orders and shows their lines, a click beside a factory
// sets its rally, the move pointer shows where the selection would walk,
// build cards count by one and five and repeat, a frame takes only a
// queue, the menu and HUD buttons play the original's sounds, and a
// summons is placed over a unit in both schemes.
using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Tests
{
    public class OrderQueuePlayTests
    {
        GameRoot root;
        MockBackend mock;
        PointerFrame frame;

        [TearDown]
        public void CleanUp()
        {
            BattleHud.ShiftKey = () => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            BattleHud.CtrlKey = () => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (root != null) Object.Destroy(root.gameObject);
        }

        IEnumerator Begin()
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
            root.Orders.Classic = true;
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

        UnitState Own(MockBackend.Role role) => Units().First(u => u.Player == mock.LocalPlayer && mock.RoleOf(u.Def) == role);
        UnitState OwnLodge() => Units().First(u => u.Player == mock.LocalPlayer && mock.UnitDefs[u.Def].IsBuilding);
        Camera Cam => root.World.Camera.GetComponent<Camera>();

        // Open ground dist from a point, away from every unit.
        Vector3 OpenGround(Vector3 from, float dist)
        {
            var at = from;
            for (int k = 0; k < 16; k++)
            {
                at = from + Quaternion.Euler(0, k * 22.5f, 0) * new Vector3(0f, 0f, dist);
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

        IEnumerator Click(Vector3 at, bool shift = false)
        {
            frame.Shift = shift;
            frame.Screen = Cam.WorldToScreenPoint(at);
            frame.LeftDown = frame.LeftHeld = true;
            yield return null;
            frame.LeftHeld = false;
            frame.LeftUp = true;
            yield return null;
            yield return null;
        }

        int Legs(int handle, out OrderLeg[] legs)
        {
            legs = new OrderLeg[16];
            return mock.ReadOrderQueue(handle, legs);
        }

        // Shift appends a move after the one in hand, a plain click
        // replaces them all, and Shift shows a line through each.
        [UnityTest]
        public IEnumerator ShiftQueuesOrdersAndShowsTheirLines()
        {
            yield return Begin();
            var knight = Own(MockBackend.Role.Knight);
            mock.Select(new[] { knight.Handle }, false);
            var a = OpenGround(knight.Position, 6f);
            var b = OpenGround(knight.Position, 10f);
            var c = OpenGround(knight.Position, 14f);
            yield return Look(knight.Position);
            yield return Click(a);
            Assert.AreEqual(1, Legs(knight.Handle, out _), "a plain click is one order");
            yield return Click(b, shift: true);
            yield return Click(c, shift: true);
            Assert.AreEqual(3, Legs(knight.Handle, out var legs), "Shift adds each click after the one in hand");
            Assert.AreEqual(b.x, legs[1].Target.x, 0.6f);
            Assert.AreEqual(c.z, legs[2].Target.z, 0.6f);
            Assert.AreEqual(1, mock.ReadSelection(new int[8]), "the queue keeps the selection");

            frame.Shift = true;
            yield return null;
            yield return null;
            Assert.AreEqual(3, root.World.Entities.OrderLegsDrawn, "while Shift is held each leg has its line");
            frame.Shift = false;
            yield return null;
            yield return null;
            Assert.AreEqual(0, root.World.Entities.OrderLegsDrawn, "and none without it");

            yield return Click(a);
            Assert.AreEqual(1, Legs(knight.Handle, out _), "a plain click replaces the queue");
        }

        // A build armed for a builder no longer selected lets go, and a
        // click on the ground beside a factory sets its rally point.
        [UnityTest]
        public IEnumerator AClickBesideAFactorySetsItsRally()
        {
            yield return Begin();
            var monarch = Own(MockBackend.Role.Monarch);
            var lodge = OwnLodge();
            mock.Select(new[] { monarch.Handle }, false);
            yield return null;
            root.Orders.Arm(CommandKind.Build, mock.UnitDefs[monarch.Def].BuildOptions.Last());
            yield return null;
            mock.Select(new[] { lodge.Handle }, false);
            yield return null;
            yield return null;
            Assert.IsNull(root.Orders.Armed, "the monarch's build goes with the monarch");

            // Just past the lodge's longer side, where its drawn bounds reach.
            var fp = mock.UnitDefs[lodge.Def].Footprint;
            var at = lodge.Position - new Vector3(fp.x * 0.5f + 0.6f, 0f, 0f);
            at.y = mock.GroundHeight(at.x, at.z);
            yield return Look(lodge.Position);
            yield return Click(at);
            Assert.AreEqual(1, Legs(lodge.Handle, out var legs), "the lodge has a rally point");
            Assert.AreEqual(at.x, legs[0].Target.x, 0.6f);
            Assert.AreEqual(at.z, legs[0].Target.z, 0.6f);
            var sel = new int[8];
            Assert.AreEqual(1, mock.ReadSelection(sel));
            Assert.AreEqual(lodge.Handle, sel[0], "and stays selected");
        }

        // Where a click sends the selection, the pointer is the move
        // pointer; with nothing selected it is the arrow.
        [UnityTest]
        public IEnumerator ThePointerIsTheMovePointerWhereTheSelectionWouldGo()
        {
            yield return Begin();
            var knight = Own(MockBackend.Role.Knight);
            var ground = OpenGround(knight.Position, 8f);
            yield return Look(ground);
            frame.Screen = Cam.WorldToScreenPoint(ground);
            yield return null;
            yield return null;
            Assert.AreEqual(GameCursor.Normal, root.Orders.PointerCursor(), "nothing selected, the arrow");
            mock.Select(new[] { knight.Handle }, false);
            yield return null;
            yield return null;
            Assert.AreEqual(GameCursor.Move, root.Orders.PointerCursor(), "a knight selected, the move pointer");
            mock.Select(new[] { OwnLodge().Handle }, false);
            yield return null;
            yield return null;
            Assert.AreEqual(GameCursor.Move, root.Orders.PointerCursor(), "a factory's rally too");
        }

        IEnumerator Card(string name, System.Action<Button> found)
        {
            Button card = null;
            float deadline = Time.realtimeSinceStartup + 5f;
            while (card == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                var builds = GameObject.Find("Builds");
                if (builds != null)
                    foreach (var x in builds.GetComponentsInChildren<Button>()) if (x.name == "Build " + name) card = x;
            }
            Assert.IsNotNull(card, "the HUD shows " + name);
            found(card);
        }

        // The original's build card: a click adds one, Shift five, Ctrl
        // repeats it with +++ on the card, a right click takes one off or
        // ends a repeat.
        [UnityTest]
        public IEnumerator BuildCardsAddOneFiveAndRepeat()
        {
            yield return Begin();
            var lodge = OwnLodge();
            int def = mock.UnitDefs[lodge.Def].BuildOptions[0];
            mock.Select(new[] { lodge.Handle }, false);
            Button card = null;
            yield return Card(mock.UnitDefs[def].Name, b => card = b);
            bool shift = false, ctrl = false;
            BattleHud.ShiftKey = () => shift;
            BattleHud.CtrlKey = () => ctrl;
            var right = card.GetComponent<RightClick>();

            card.onClick.Invoke();
            Assert.AreEqual(1, mock.QueuedCount(lodge.Handle, def));
            shift = true;
            card.onClick.Invoke();
            Assert.AreEqual(6, mock.QueuedCount(lodge.Handle, def), "Shift adds five");
            right.Clicked();
            Assert.AreEqual(1, mock.QueuedCount(lodge.Handle, def), "Shift and a right click take five off");
            shift = false;
            right.Clicked();
            Assert.AreEqual(0, mock.QueuedCount(lodge.Handle, def));

            ctrl = true;
            card.onClick.Invoke();
            Assert.AreEqual(def, mock.RepeatOf(lodge.Handle), "Ctrl repeats it");
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual("+++", card.transform.Find("Queued").GetComponent<Text>().text);
            ctrl = false;
            right.Clicked();
            Assert.AreEqual(-1, mock.RepeatOf(lodge.Handle), "a right click ends the repeat");
            Assert.AreEqual(0, mock.QueuedCount(lodge.Handle, def));
        }

        int OwnOf(int def) => Units().Count(u => u.Player == mock.LocalPlayer && u.Def == def && u.BuildProgress >= 1f);

        // Ctrl on a walking builder's card for a unit arms a summons. The
        // click places it once, Shift or not, and the unit comes on that
        // spot over and over, each one stepping off. The card reads +++ and
        // a right click on it ends the summons.
        [UnityTest]
        public IEnumerator ABuildersCardWithCtrlSummonsWithoutEnd()
        {
            yield return Begin();
            var monarch = Own(MockBackend.Role.Monarch);
            int def = mock.UnitDefs[monarch.Def].BuildOptions[0];
            Assert.IsFalse(mock.UnitDefs[def].IsBuilding, "the monarch's first card is a unit");
            mock.Select(new[] { monarch.Handle }, false);
            string name = mock.UnitDefs[def].Name;
            Button card = null;
            yield return Card(name, b => card = b);
            bool ctrl = true;
            BattleHud.CtrlKey = () => ctrl;
            card.onClick.Invoke();
            ctrl = false;
            Assert.AreEqual(CommandKind.Build, root.Orders.Armed);
            Assert.IsTrue(root.Orders.ArmedRepeat, "Ctrl arms a summons");

            var site = OpenGround(monarch.Position, 8f);
            yield return Look(site);
            int before = OwnOf(def);
            yield return Click(site, shift: true);
            Assert.IsNull(root.Orders.Armed, "placed once, Shift or not");
            Assert.AreEqual(def, mock.RepeatOf(monarch.Handle), "the monarch summons it without end");
            mock.Advance(MockBackend.Tps * 21);
            Assert.GreaterOrEqual(OwnOf(def) - before, 4, "one after another");
            var spot = new Vector2(site.x, site.z);
            Assert.LessOrEqual(Units().Count(u => u.Def == def && (new Vector2(u.Position.x, u.Position.z) - spot).magnitude < 1f), 1, "each steps off the spot");

            // The panel is rebuilt as the monarch's orders change, so the
            // card is looked up after the wait.
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Card(name, b => card = b);
            Assert.AreEqual("+++", card.transform.Find("Queued").GetComponent<Text>().text);
            card.GetComponent<RightClick>().Clicked();
            Assert.AreEqual(-1, mock.RepeatOf(monarch.Handle), "a right click ends it");
            int made = OwnOf(def);
            mock.Advance(MockBackend.Tps * 12);
            Assert.AreEqual(made, OwnOf(def), "and no more come");
        }

        // The modern scheme sends the summons as a build without end, and
        // Ctrl held at the click replaces the queue rather than keeping it.
        [UnityTest]
        public IEnumerator AModernSummonsReplacesTheQueueEvenWithCtrl()
        {
            yield return Begin();
            root.Orders.Classic = false;
            var monarch = Own(MockBackend.Role.Monarch);
            int def = mock.UnitDefs[monarch.Def].BuildOptions[0];
            Assert.IsTrue(mock.Command(GameCommand.To(CommandKind.Move, monarch.Handle, OpenGround(monarch.Position, 10f))));
            var queued = GameCommand.To(CommandKind.Move, monarch.Handle, OpenGround(monarch.Position, 14f));
            queued.Queue = true;
            Assert.IsTrue(mock.Command(queued));
            Assert.AreEqual(2, Legs(monarch.Handle, out _));
            root.Orders.Selected.Clear();
            root.Orders.Selected.Add(monarch.Handle);
            root.Orders.Arm(CommandKind.Build, def, repeat: true);
            var site = OpenGround(monarch.Position, 6f);
            yield return Look(site);
            frame.Ctrl = true;
            yield return Click(site, shift: true);
            frame.Ctrl = false;
            Assert.IsNull(root.Orders.Armed, "placed once");
            Assert.AreEqual(def, mock.RepeatOf(monarch.Handle));
            Assert.AreEqual(1, Legs(monarch.Handle, out var legs), "the moves are gone");
            Assert.AreEqual(OrderKind.Build, legs[0].Kind);
        }

        // A summons given on the minimap is placed once with Shift held and
        // comes without end, in both schemes, as a click on the field does.
        [UnityTest]
        public IEnumerator TheMinimapPlacesASummonsOnce()
        {
            yield return Begin();
            var monarch = Own(MockBackend.Role.Monarch);
            int def = mock.UnitDefs[monarch.Def].BuildOptions[0];
            foreach (bool classic in new[] { true, false })
            {
                string scheme = classic ? "classic" : "modern";
                root.Orders.Classic = classic;
                root.Orders.Selected.Clear();
                if (classic) mock.Select(new[] { monarch.Handle }, false);
                else root.Orders.Selected.Add(monarch.Handle);
                Assert.IsTrue(mock.Command(GameCommand.To(CommandKind.Stop, monarch.Handle, Vector3.zero)));
                Assert.AreEqual(-1, mock.RepeatOf(monarch.Handle), scheme + ": nothing summoned yet");
                root.Orders.Arm(CommandKind.Build, def, repeat: true);
                frame.Shift = true;
                yield return null;
                yield return null;
                root.Orders.OrderAt(OpenGround(monarch.Position, 8f));
                frame.Shift = false;
                Assert.IsNull(root.Orders.Armed, scheme + ": placed once, Shift or not");
                Assert.AreEqual(0, root.Orders.Queued.Count, scheme + ": with no ghost left behind");
                Assert.AreEqual(def, mock.RepeatOf(monarch.Handle), scheme + ": summoned without end");
                yield return null;
            }
        }

        // A frame can be chosen only to queue units in it: no orders, no
        // rally, nothing on the panel but its build, and its queue waits.
        [UnityTest]
        public IEnumerator AFrameTakesOnlyAQueue()
        {
            yield return Begin();
            mock.BuildSeconds = 0f;
            var lodge = OwnLodge();
            var site = OpenGround(lodge.Position, 12f);
            int frame = mock.SpawnFrame(lodge.Def, site, 0.3f);
            int soldier = mock.SpawnFrame(mock.UnitDefs[lodge.Def].BuildOptions[0], OpenGround(site, 5f), 0.3f);
            mock.Select(new[] { soldier }, false);
            Assert.AreEqual(0, mock.ReadSelection(new int[8]), "a soldier being made cannot be chosen");
            mock.Select(new[] { frame }, false);
            Assert.AreEqual(1, mock.ReadSelection(new int[8]), "a factory being built can");
            Assert.IsFalse(mock.Command(GameCommand.To(CommandKind.Move, frame, OpenGround(site, 8f))), "but sets no rally");

            int def = mock.UnitDefs[lodge.Def].BuildOptions[0];
            Button card = null;
            yield return Card(mock.UnitDefs[def].Name, b => card = b);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(0, GameObject.Find("Orders").transform.childCount, "no order buttons");
            var panel = GameObject.Find("Unit");
            Assert.IsTrue(panel.GetComponentsInChildren<Text>().Any(t => t.text.StartsWith("Being built")), "its build shows");
            Assert.IsFalse(panel.GetComponentsInChildren<Text>().Any(t => t.text.Contains("/" + mock.UnitDefs[lodge.Def].MaxHealth)), "its health does not");

            card.onClick.Invoke();
            Assert.AreEqual(1, mock.QueuedCount(frame, def), "the queue is taken");
            int before = Units().Length;
            mock.Advance(MockBackend.Tps * 8);
            Assert.AreEqual(before, Units().Length, "and waits for the frame");
            mock.SetBuilt(frame, 1f);
            mock.Advance(MockBackend.Tps * 6);
            Assert.AreEqual(before + 1, Units().Length, "then the unit is made");
        }

        static void Press(string name)
        {
            var b = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == name && x.isActiveAndEnabled);
            Assert.IsNotNull(b, "a button " + name);
            b.onClick.Invoke();
        }

        // Menu buttons play the sounds the original's .gui files give them,
        // and the HUD's order buttons their own.
        [UnityTest]
        public IEnumerator ButtonsPlayTheOriginalsSounds()
        {
            mock = new MockBackend { StageSeconds = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            Press("Options");
            yield return null;
            Assert.Contains("menubutton.wav", mock.SoundsPlayed);
            var picker = Object.FindObjectsByType<CyclePicker>(FindObjectsSortMode.None).First(c => c.isActiveAndEnabled);
            picker.Step(1);
            picker.Step(-1);
            Assert.Contains("toggle.wav", mock.SoundsPlayed, "a setting's choice clicks as the original's toggles do");
            Press("Back");
            yield return null;
            Assert.Contains("cancel.wav", mock.SoundsPlayed);
            mock.SoundsPlayed.Clear();
            Press("Skirmish");
            Assert.Contains("skirmish.wav", mock.SoundsPlayed);
            Object.Destroy(root.gameObject);
            yield return null;

            yield return Begin();
            mock.SoundsPlayed.Clear();
            mock.Select(new[] { Own(MockBackend.Role.Knight).Handle }, false);
            Button move = null;
            float deadline = Time.realtimeSinceStartup + 5f;
            while (move == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                var grid = GameObject.Find("Orders");
                if (grid != null) move = grid.GetComponentsInChildren<Button>().FirstOrDefault(x => x.name == "Action MOVE");
            }
            Assert.IsNotNull(move);
            move.onClick.Invoke();
            Assert.Contains("move.wav", mock.SoundsPlayed);
        }

        // The ghost of a unit to summon is green over a knight, and the
        // placing click sends the order there, in either scheme.
        [UnityTest]
        public IEnumerator ASummonsIsPlacedOverAUnitInBothSchemes()
        {
            yield return Begin();
            var monarch = Own(MockBackend.Role.Monarch);
            var knight = Own(MockBackend.Role.Knight);
            foreach (bool classic in new[] { true, false })
            {
                root.Orders.Classic = classic;
                root.Orders.Selected.Clear();
                if (!classic) root.Orders.Selected.Add(monarch.Handle);
                mock.Select(new[] { monarch.Handle }, false);
                yield return Look(knight.Position);
                root.Orders.Arm(CommandKind.Build, knight.Def);
                frame.Screen = Cam.WorldToScreenPoint(knight.Position);
                yield return null;
                yield return null;
                Assert.IsTrue(root.Orders.GhostOk, "the ghost is green over the knight, classic " + classic);
                yield return Click(knight.Position);
                Assert.AreEqual(OrderKind.Build, mock.ReadOrder(monarch.Handle).Kind, "the monarch took the summons, classic " + classic);
                Assert.IsTrue(mock.Command(GameCommand.To(CommandKind.Stop, monarch.Handle, Vector3.zero)));
                mock.Advance(1);
            }
            root.Orders.Selected.Clear();
            root.Orders.Classic = true;
        }
    }
}
