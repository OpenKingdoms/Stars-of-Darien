// HudPlayTests.cs - the battle HUD on the mock: the original's layout at
// the player's interface size, the pool in the sidebar and no top bar,
// every build option at once, orders in their original slots with the live
// key letters, the help box and the target panel, and the world camera
// kept to the play area.
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Tests
{
    public class HudPlayTests
    {
        GameRoot root;
        MockBackend mock;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
            BattleHud.SizeOverride = null;
        }

        IEnumerator Begin()
        {
            BattleHud.SizeOverride = new Vector2Int(1920, 1080);
            mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Options.UiScale = HudLayout.DefaultScale;
            root.Options.HotkeyLetters = true;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_highlands";
            root.Setup.MapRevealed = true;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
            yield return Settle();
        }

        // The panel refreshes ten times a second of real time.
        static IEnumerator Settle() { yield return null; yield return new WaitForSecondsRealtime(0.35f); }

        BattleHud Hud => root.Screens.Hud;

        UnitState[] Units()
        {
            var u = new UnitState[512];
            int n = mock.ReadUnits(u);
            return u.Take(n).ToArray();
        }

        int Own(MockBackend.Role role) => Units().First(u => u.Player == 1 && mock.RoleOf(u.Def) == role).Handle;

        T Named<T>(string name) where T : Component =>
            Hud.Root.GetComponentsInChildren<T>(false).FirstOrDefault(c => c.name == name);

        RectTransform Button(string id)
        {
            var orders = GameObject.Find("Orders");
            Assert.IsNotNull(orders, "the sidebar has the orders");
            var b = orders.GetComponentsInChildren<Button>().FirstOrDefault(x => x.name == "Action " + id);
            Assert.IsNotNull(b, "the HUD shows " + id);
            return (RectTransform)b.transform;
        }

        static string Plain(string rich) => Regex.Replace(rich, "<[^>]+>", "");

        static void AtCp(Rect expected, RectTransform rt, string what)
        {
            Assert.AreEqual(expected.x, rt.anchoredPosition.x, 0.01f, what + " x");
            Assert.AreEqual(-expected.y, rt.anchoredPosition.y, 0.01f, what + " y");
            Assert.AreEqual(expected.width, rt.sizeDelta.x, 0.01f, what + " width");
            Assert.AreEqual(expected.height, rt.sizeDelta.y, 0.01f, what + " height");
        }

        [UnityTest]
        public IEnumerator ThePoolSitsInTheSidebarAndTheTopBarIsGone()
        {
            yield return Begin();
            Assert.IsNull(GameObject.Find("Top"), "no bar across the top");
            Assert.AreEqual(1.8f, Hud.Layout.S, 0.001f, "80 percent at 1080p");
            Assert.AreEqual(Hud.Layout.S, Hud.Canvas.scaleFactor, 0.001f);
            Assert.Less(Hud.Canvas.sortingOrder, root.Screens.Canvas.sortingOrder, "pause and options draw over the HUD");
            var e = mock.ReadEconomy(mock.LocalPlayer);
            Assert.AreEqual("Mana", Hud.HelpLine1, "with nothing selected the help box shows the pool");
            StringAssert.EndsWith("/" + Mathf.FloorToInt(e.Storage), Hud.HelpLine2);
            StringAssert.StartsWith("+", Named<Text>("Income").text);
            StringAssert.StartsWith("-", Named<Text>("Spend").text);
            Assert.IsNotNull(Named<RawImage>("Ball").texture, "the crystal ball is painted");
            Assert.IsNotNull(Named<Text>("Clock").text);
            Assert.IsFalse(Named<RectTransform>("Unit") != null, "the strip is bare with nothing selected");
        }

        [UnityTest]
        public IEnumerator EveryBuildOptionShowsAtOnceOnTheStrip()
        {
            yield return Begin();
            var monarch = Units().First(u => u.Player == 1 && mock.RoleOf(u.Def) == MockBackend.Role.Monarch);
            mock.Select(new[] { monarch.Handle }, false);
            yield return Settle();
            var options = mock.UnitDefs[monarch.Def].BuildOptions;
            var builds = GameObject.Find("Builds").transform;
            Assert.AreEqual(options.Length, builds.childCount, "every option, no page control");
            Assert.IsNull(GameObject.Find("More"));
            var g = Hud.Layout.Builds(options.Length);
            Assert.AreEqual(1, g.Rows);
            for (int i = 0; i < options.Length; i++)
            {
                var card = (RectTransform)builds.GetChild(i);
                Assert.AreEqual("Build " + mock.UnitDefs[options[i]].Name, card.name, "in the backend's order");
                AtCp(Hud.Layout.BuildCell(g, i), card, card.name);
            }
            // Arming one rings it, and the help box shows how to turn it.
            builds.GetChild(2).GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual(CommandKind.Build, root.Orders.Armed);
            yield return Settle();
            Assert.IsNotNull(GameObject.Find("Builds").transform.GetChild(2).Find("Armed"), "the armed card has its ring");
            StringAssert.Contains("turn", Hud.HelpLine1 + Hud.HelpLine2);
            root.Orders.Disarm();
        }

        [UnityTest]
        public IEnumerator OrdersSitInTheOriginalsSlotsWithTheLiveKeys()
        {
            yield return Begin();
            mock.Select(new[] { Own(MockBackend.Role.Knight) }, false);
            yield return Settle();
            var slots = new (string id, string slot)[]
            {
                ("MOVE", "O1L"), ("PATROL", "O1R"), ("ATTACK", "O2L"), ("GUARD", "O2R"), ("STOP", "O4C"),
                ("Offensive", "S1"), ("Defensive", "S2"), ("Passive", "S3"),
            };
            foreach (var (id, slot) in slots)
                AtCp(HudLayout.Hit(HudLayout.Slot(slot)), Button(id), id);
            string Key(string id) => Button(id).GetComponentsInChildren<Text>().FirstOrDefault(t => t.name == "Key")?.text;
            Assert.AreEqual("M", Key("MOVE"));
            Assert.AreEqual("^A", Key("ATTACK"), "A pans the camera, so attack takes Ctrl");
            Assert.AreEqual("^S", Key("STOP"));
            Assert.IsNull(Key("Offensive"), "stances have no key");
            root.Options.HotkeyLetters = false;
            yield return Settle();
            Assert.IsNull(Key("MOVE"), "the letters can be hidden");

            mock.Select(new[] { Own(MockBackend.Role.Mage) }, false);
            yield return Settle();
            AtCp(HudLayout.Hit(HudLayout.Slot("W1")), Button("PrimaryWeapon"), "the first spell");
            AtCp(HudLayout.Hit(HudLayout.Slot("W2")), Button("SecondaryWeapon"), "the second spell");
            var cost = Button("PrimaryWeapon").GetComponentsInChildren<Text>().First(t => t.name == "Cost");
            Assert.AreEqual(MockBackend.FireballCost.ToString(), cost.text, "the cost sits in the gutter under the spell");
        }

        [UnityTest]
        public IEnumerator HoveringAButtonFillsTheHelpBox()
        {
            yield return Begin();
            mock.Select(new[] { Own(MockBackend.Role.Mage) }, false);
            yield return Settle();
            var hint = Button("PATROL").GetComponent<HoverHint>();
            hint.Show(true);
            Assert.AreEqual("Patrol", Hud.HelpLine1);
            Assert.AreEqual("P", Hud.HelpLine2);
            hint.Show(false);
            Assert.AreEqual("Mana", Hud.HelpLine1);
            Button("PrimaryWeapon").GetComponent<HoverHint>().Show(true);
            Assert.AreEqual("Fireball", Hud.HelpLine1);
            Assert.AreEqual(MockBackend.FireballCost + " mana", Hud.HelpLine2);
        }

        [UnityTest]
        public IEnumerator TheSelectionsTargetFillsTheStripsRightPanel()
        {
            yield return Begin();
            int knight = Own(MockBackend.Role.Knight);
            var enemy = Units().First(u => u.Player == 2);
            mock.Select(new[] { knight }, false);
            Assert.IsTrue(mock.Command(new GameCommand { Kind = CommandKind.Attack, Unit = knight, TargetUnit = enemy.Handle, BuildDef = -1 }));
            yield return Settle();
            var target = Named<RectTransform>("Target");
            Assert.IsNotNull(target, "the target panel shows");
            var name = target.GetComponentsInChildren<Text>().First(t => t.name == "Name");
            var d = mock.UnitDefs[enemy.Def];
            string nice = !string.IsNullOrEmpty(d.Title) ? d.Title : string.IsNullOrEmpty(d.Description) ? d.Name : d.Description;
            Assert.AreEqual(nice, Plain(name.text));
            Assert.AreEqual("Attacking", Named<Text>("Status").text);
        }

        [UnityTest]
        public IEnumerator AVeteranShowsItsKillsAndShield()
        {
            yield return Begin();
            int knight = Own(MockBackend.Role.Knight);
            mock.SetKills(knight, 4);
            mock.Select(new[] { knight }, false);
            yield return Settle();
            Assert.AreEqual("4", Named<Text>("Kills").text);
            Assert.IsTrue(Named<RawImage>("Shield").enabled);
            var numbers = Plain(Named<Text>("Numbers").text);
            var u = Units().First(x => x.Handle == knight);
            Assert.AreEqual($"{u.Health}/{u.MaxHealth}", numbers.Trim(), "a unit without mana shows its health alone");
        }

        [UnityTest]
        public IEnumerator TheWorldCameraDrawsOnlyThePlayArea()
        {
            yield return Begin();
            var cam = root.World.Camera.GetComponent<Camera>();
            var v = Hud.Layout.Viewport;
            Assert.AreEqual(v.x, cam.rect.x, 1e-4f);
            Assert.AreEqual(v.y, cam.rect.y, 1e-4f);
            Assert.AreEqual(v.width, cam.rect.width, 1e-4f);
            Assert.AreEqual(v.height, cam.rect.height, 1e-4f);
            root.Screens.Screen("Hud").SetActive(false);
            Assert.AreEqual(new Rect(0, 0, 1, 1), cam.rect, "without the HUD the world takes the whole screen");
            root.Screens.Screen("Hud").SetActive(true);
            yield return Settle();
            Assert.AreEqual(v.width, cam.rect.width, 1e-4f);
        }

        [UnityTest]
        public IEnumerator TheInterfaceSizeOptionRescalesTheHud()
        {
            yield return Begin();
            root.Options.UiScale = 100;
            yield return Settle();
            Assert.AreEqual(2.25f, Hud.Layout.S, 0.001f, "100 percent is the original at 480 lines");
            Assert.AreEqual(2.25f, Hud.Canvas.scaleFactor, 0.001f);
            root.Options.UiScale = 60;
            yield return Settle();
            Assert.AreEqual(1.35f, Hud.Layout.S, 0.001f);
            Assert.IsNotNull(GameObject.Find("Orders"), "the panels are made again at the new size");
        }
    }
}
