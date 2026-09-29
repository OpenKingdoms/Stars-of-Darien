// HudActionPlayTests.cs - the battle HUD's sidebar on the mock: a mage's
// spell is chosen and cast and its mana drops, a stance toggles, a box of
// enemies is attacked, and a wagon loads a box of soldiers and unloads
// them.
using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Tests
{
    public class HudActionPlayTests
    {
        GameRoot root;
        MockBackend mock;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        IEnumerator Begin()
        {
            mock = new MockBackend { StageSeconds = 0f, DamageScale = 1f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_highlands";
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
        }

        UnitState[] Units()
        {
            var u = new UnitState[512];
            int n = mock.ReadUnits(u);
            return u.Take(n).ToArray();
        }

        int Own(MockBackend.Role role) =>
            Units().First(u => u.Player == 1 && mock.RoleOf(u.Def) == role).Handle;

        // The HUD's button for an action, once the panel has refreshed.
        IEnumerator Button(string id, System.Action<Button> found)
        {
            Button b = null;
            float deadline = Time.realtimeSinceStartup + 5f;
            while (b == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                var grid = GameObject.Find("Orders");
                if (grid != null)
                    foreach (var x in grid.GetComponentsInChildren<Button>()) if (x.name == "Action " + id) b = x;
            }
            Assert.IsNotNull(b, "the HUD shows " + id);
            found(b);
        }

        [UnityTest]
        public IEnumerator AMageCastsAndItsManaDrops()
        {
            yield return Begin();
            int mage = Own(MockBackend.Role.Mage);
            mock.Select(new[] { mage }, false);
            Button fire = null;
            yield return Button("PrimaryWeapon", b => fire = b);
            Assert.IsTrue(fire.interactable);
            fire.onClick.Invoke();
            Assert.IsNull(root.Orders.ArmedAction, "the button chooses the spell and arms nothing");

            var enemy = Units().First(u => u.Player == 2);
            int before = mock.ManaOf(mage);
            Assert.IsTrue(mock.DoAction("PrimaryWeapon", enemy.Position, enemy.Handle, default, false));
            Assert.AreEqual(before - MockBackend.FireballCost, mock.ManaOf(mage), "the cast costs its mana");
            var hurt = Units().First(u => u.Handle == enemy.Handle);
            Assert.Less(hurt.Health, enemy.Health, "the target was hit");

            // Cast until the mana runs out: the button then shows it cannot.
            while (mock.ManaOf(mage) >= MockBackend.FireballCost) mock.DoAction("PrimaryWeapon", enemy.Position, -1, default, false);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Button("PrimaryWeapon", b => fire = b);
            Assert.IsFalse(fire.interactable, "no mana, no fireball");
            var action = mock.SelectionActions().First(a => a.Id == "PrimaryWeapon");
            Assert.AreEqual("Not enough mana", action.Why);
        }

        // A spell's button chooses the weapon, as the original's do, and
        // arms nothing: after the mage fights with it, a plain click on the
        // ground is a move.
        [UnityTest]
        public IEnumerator APickedSpellLeavesTheNextClickAMove()
        {
            yield return Begin();
            root.Orders.Classic = true;
            var frame = new PointerFrame { Focused = true, Dpi = 96 };
            root.Orders.Formation.Source = () =>
            {
                var r = frame;
                frame.LeftDown = frame.LeftUp = frame.RightDown = frame.RightUp = false;
                return r;
            };
            int mage = Own(MockBackend.Role.Mage);
            mock.Select(new[] { mage }, false);
            Button frost = null;
            yield return Button("SecondaryWeapon", b => frost = b);
            frost.onClick.Invoke();
            Assert.IsTrue(mock.SelectionActions().First(a => a.Id == "SecondaryWeapon").Toggled, "the spell is chosen");
            var enemy = Units().First(u => u.Player == 2);
            Assert.IsTrue(mock.Command(new GameCommand { Kind = CommandKind.Attack, Unit = mage, TargetUnit = enemy.Handle, BuildDef = -1 }));
            // Open ground near the mage, at the middle of the view.
            var from = Units().First(u => u.Handle == mage).Position;
            var at = from;
            for (int k = 0; k < 16; k++)
            {
                at = from + Quaternion.Euler(0, k * 22.5f, 0) * new Vector3(0f, 0f, 5f);
                var p = at;
                if (!Units().Any(u => (new Vector2(u.Position.x - p.x, u.Position.z - p.z)).magnitude < 2.5f)) break;
            }
            at.y = mock.GroundHeight(at.x, at.z);
            var cam = root.World.Camera;
            cam.focus = at;
            cam.yaw = 0f;
            cam.pitch = GameCamera.ClassicPitch;
            cam.Zoom(30f);
            for (int i = 0; i < 3; i++) yield return null;
            frame.Screen = cam.GetComponent<Camera>().WorldToScreenPoint(at);
            frame.LeftDown = frame.LeftHeld = true;
            yield return null;
            frame.LeftHeld = false;
            frame.LeftUp = true;
            yield return null;
            yield return null;
            Assert.IsNull(root.Orders.ArmedAction, "nothing is left armed");
            Assert.AreEqual(OrderKind.Move, mock.ReadOrder(mage).Kind, "a plain click on the ground moves");
        }

        [UnityTest]
        public IEnumerator AStanceTogglesFromItsButton()
        {
            yield return Begin();
            int knight = Own(MockBackend.Role.Knight);
            mock.Select(new[] { knight }, false);
            Button passive = null;
            yield return Button("Passive", b => passive = b);
            passive.onClick.Invoke();
            Assert.AreEqual(MockBackend.Stance.Passive, mock.StanceOf(knight));
            Assert.IsTrue(mock.SelectionActions().First(a => a.Id == "Passive").Toggled);
            Assert.IsFalse(mock.SelectionActions().First(a => a.Id == "Offensive").Toggled, "one stance at a time");
        }

        [UnityTest]
        public IEnumerator AttackingABoxTakesOnTheEnemiesInIt()
        {
            yield return Begin();
            var knights = Units().Where(u => u.Player == 1 && mock.RoleOf(u.Def) == MockBackend.Role.Knight).Select(u => u.Handle).ToArray();
            mock.Select(knights, false);
            var enemies = Units().Where(u => u.Player == 2).ToArray();
            var c = enemies[0].Position;
            var box = Rect.MinMaxRect(c.x - 6, c.z - 6, c.x + 6, c.z + 6);
            Assert.IsTrue(mock.DoAction("ATTACK", c, -1, box, false), "a box with enemies in it takes the attack");
            mock.Advance(10);
            Assert.IsTrue(knights.All(h => mock.ReadOrder(h).Kind == OrderKind.Attack || mock.ReadOrder(h).TargetUnit >= 0), "every knight goes for someone in the box");
        }

        [UnityTest]
        public IEnumerator AWagonLoadsABoxAndUnloadsIt()
        {
            yield return Begin();
            int wagon = Own(MockBackend.Role.Wagon);
            var archers = Units().Where(u => u.Player == 1 && mock.RoleOf(u.Def) == MockBackend.Role.Archer).ToArray();
            mock.Select(new[] { wagon }, false);
            var c = archers[0].Position;
            var box = Rect.MinMaxRect(c.x - 5, c.z - 5, c.x + 5, c.z + 5);
            int before = Units().Length;
            Assert.IsTrue(mock.DoAction("LOAD", c, -1, box, false));
            Assert.Less(Units().Length, before, "the loaded soldiers leave the field");
            Assert.IsTrue(mock.SelectionActions().First(a => a.Id == "UNLOAD").Enabled);
            Assert.IsTrue(mock.DoAction("UNLOAD", c + new Vector3(4, 0, 0), -1, default, false));
            Assert.AreEqual(before, Units().Length, "and come back where they were put down");
            yield return null;
        }
    }
}
