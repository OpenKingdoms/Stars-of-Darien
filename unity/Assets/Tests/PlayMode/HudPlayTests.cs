// HudPlayTests.cs - the battle HUD on the mock: the original's layout at
// the player's interface size, the pool in the sidebar and no top bar,
// every build option at once, orders in their original slots with the live
// key letters, the help box and the target panel, the minimap's buttons in
// both control schemes, text that reads on the smallest screen, and the
// world camera kept to the play area.
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using UnityEngine;
using UnityEngine.EventSystems;
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

        IEnumerator Begin(int startMana = 1000)
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
            root.Setup.StartMana = startMana;
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

        static Rect Local(Rect r, Rect origin) => new Rect(r.x - origin.x, r.y - origin.y, r.width, r.height);

        static void Pointer(GameObject go, bool down)
        {
            var e = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            if (down) ExecuteEvents.Execute(go, e, ExecuteEvents.pointerDownHandler);
            else ExecuteEvents.Execute(go, e, ExecuteEvents.pointerUpHandler);
        }

        int[] Selection()
        {
            var into = new int[512];
            return into.Take(mock.ReadSelection(into)).ToArray();
        }

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
            StringAssert.IsMatch(@"^\d\d:\d\d$", Named<Text>("Clock").text, "the clock reads minutes and seconds");
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

            // A mixed selection shows the list of the one that builds.
            mock.Select(new[] { Own(MockBackend.Role.Knight), monarch.Handle }, false);
            yield return Settle();
            Assert.AreEqual(options.Length, GameObject.Find("Builds").transform.childCount, "the monarch's options beside its guard");
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
            Text KeyText(string id) => Button(id).GetComponentsInChildren<Text>().FirstOrDefault(t => t.name == "Key");
            string Key(string id) => KeyText(id) is Text t ? Plain(t.text) : null;
            Assert.AreEqual("M", Key("MOVE"));
            Assert.AreEqual("^A", Key("ATTACK"), "A pans the camera, so attack takes Ctrl, shown as a caret");
            Assert.AreEqual("^S", Key("STOP"));
            Assert.IsNull(Key("Offensive"), "stances have no key");
            foreach (var (id, slot) in slots)
            {
                var k = KeyText(id);
                if (k == null) continue;
                AtCp(Local(Hud.Layout.Badge(slot), HudLayout.Hit(HudLayout.Slot(slot))), k.rectTransform, id + "'s key in its corner");
            }
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
            Button("PrimaryWeapon").GetComponent<HoverHint>().Show(false);
            Button("ATTACK").GetComponent<HoverHint>().Show(true);
            Assert.AreEqual("Ctrl A", Hud.HelpLine2, "the help box spells out the key the badge abbreviates");
            Button("ATTACK").GetComponent<HoverHint>().Show(false);

            // An armed order names itself and how to let it go.
            Button("PATROL").GetComponent<Button>().onClick.Invoke();
            yield return Settle();
            Assert.AreEqual("Patrol", Hud.HelpLine1);
            StringAssert.Contains("Right click", Hud.HelpLine2);
            root.Orders.Disarm();
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

            // The target's bars tell the target's numbers, not the selection's.
            var hint = target.Find("Health").GetComponent<HoverHint>();
            hint.Show(true);
            var now = Units().First(u => u.Handle == enemy.Handle);
            var mine = Units().First(u => u.Handle == knight);
            Assert.AreNotEqual($"{mine.Health}/{mine.MaxHealth}", $"{now.Health}/{now.MaxHealth}", "the two can be told apart");
            Assert.AreEqual("Health", Hud.HelpLine1);
            Assert.AreEqual($"{now.Health}/{now.MaxHealth}", Hud.HelpLine2, "the enemy's health");
            hint.Show(false);
            var mana = target.Find("Mana");
            if (mana.gameObject.activeSelf)
            {
                mana.GetComponent<HoverHint>().Show(true);
                Assert.AreEqual($"{now.Mana}/{now.MaxMana}", Hud.HelpLine2, "the enemy's mana");
                mana.GetComponent<HoverHint>().Show(false);
            }

            // A group's health bar tells the whole group's.
            var two = Units().Where(u => u.Player == 1 && !mock.UnitDefs[u.Def].IsBuilding).Take(2).ToArray();
            mock.Select(two.Select(u => u.Handle).ToArray(), false);
            yield return Settle();
            var groupBar = Named<RectTransform>("Unit").Find("Health").GetComponent<HoverHint>();
            groupBar.Show(true);
            var both = Units().Where(u => two.Any(t => t.Handle == u.Handle)).ToArray();
            Assert.AreEqual($"{both.Sum(u => (long)u.Health)}/{both.Sum(u => (long)u.MaxHealth)}", Hud.HelpLine2);
            groupBar.Show(false);
        }

        [UnityTest]
        public IEnumerator TheMinimapsButtonsFollowTheControls()
        {
            yield return Begin();
            var input = Hud.Root.GetComponentInChildren<MinimapInput>();
            Assert.IsNotNull(input, "the minimap takes clicks");
            var corners = new Vector3[4];
            ((RectTransform)input.transform).GetWorldCorners(corners);
            var size = mock.Terrain.Size;
            var cam = root.World.Camera;
            int knight = Own(MockBackend.Role.Knight);

            Vector3 Ground(float u, float v) => new Vector3(u * size.x, 0, (v - 1f) * size.y);
            void Press(PointerEventData.InputButton button, float u, float v) => input.OnPointerDown(new PointerEventData(EventSystem.current)
            {
                button = button,
                position = new Vector2(Mathf.Lerp(corners[0].x, corners[2].x, u), Mathf.Lerp(corners[0].y, corners[2].y, v)),
            });
            bool LooksAt(float u, float v) => new Vector2(cam.focus.x - Ground(u, v).x, cam.focus.z - Ground(u, v).z).magnitude < 0.5f;
            bool Sent(float u, float v)
            {
                var o = mock.ReadOrder(knight);
                return o.Kind == OrderKind.Move && new Vector2(o.Target.x - Ground(u, v).x, o.Target.z - Ground(u, v).z).magnitude < 2f;
            }
            void Still()
            {
                mock.Command(GameCommand.To(CommandKind.Stop, knight, Vector3.zero));
                cam.focus = Ground(0.5f, 0.5f);
            }
            var left = PointerEventData.InputButton.Left;
            var right = PointerEventData.InputButton.Right;

            // Classic: the left button sends an own selection, the right looks.
            root.Orders.Classic = true;
            mock.Select(new[] { knight }, false);
            yield return Settle();
            Assert.IsTrue(root.World.Entities.Selected.Contains(knight));
            Still();
            Press(left, 0.2f, 0.8f);
            Assert.IsTrue(Sent(0.2f, 0.8f), "classic: a left click sends the selection there");
            Assert.IsFalse(LooksAt(0.2f, 0.8f), "and leaves the view");
            Still();
            Press(right, 0.7f, 0.3f);
            Assert.IsTrue(LooksAt(0.7f, 0.3f), "classic: a right click looks there");
            Assert.AreNotEqual(OrderKind.Move, mock.ReadOrder(knight).Kind, "and sends nobody");
            mock.Select(new int[0], false);
            yield return Settle();
            Assert.AreEqual(0, root.World.Entities.Selected.Count);
            Still();
            Press(left, 0.3f, 0.6f);
            Assert.IsTrue(LooksAt(0.3f, 0.6f), "classic: with nothing selected a left click looks there");

            // Modern: the left button looks, the right sends.
            root.Orders.Classic = false;
            root.World.Entities.Selected.Clear();
            root.World.Entities.Selected.Add(knight);
            Still();
            Press(left, 0.2f, 0.8f);
            Assert.IsTrue(LooksAt(0.2f, 0.8f), "modern: a left click looks there");
            Assert.AreNotEqual(OrderKind.Move, mock.ReadOrder(knight).Kind, "and sends nobody");
            Still();
            Press(right, 0.7f, 0.3f);
            Assert.IsTrue(Sent(0.7f, 0.3f), "modern: a right click sends the selection there");
        }

        [UnityTest]
        public IEnumerator SmallTextReadsAndFitsOnTheSmallestScreen()
        {
            yield return Begin();
            BattleHud.SizeOverride = new Vector2Int(1280, 720);
            foreach (int percent in new[] { 60, 80 })
            {
                root.Options.UiScale = percent;
                foreach (var role in new[] { MockBackend.Role.Knight, MockBackend.Role.Mage, MockBackend.Role.Monarch })
                {
                    mock.Select(new[] { Own(role) }, false);
                    yield return Settle();
                    float s = Hud.Layout.S;
                    string at = $"{role} at 1280x720 and {percent}%";
                    int keys = 0, costs = 0;
                    foreach (var t in Hud.Root.GetComponentsInChildren<Text>(false))
                    {
                        int size = t.resizeTextForBestFit ? t.resizeTextMinSize : t.fontSize;
                        Assert.GreaterOrEqual(size * s, HudLayout.BodyFloor - 0.01f, $"{t.name} '{Plain(t.text)}', {at}");
                        if (t.name == "Numbers" || t.name == "Clock" || t.name == "Income" || t.name == "Spend" || t.name == "Kills" || t.name == "Queued" || t.name == "Cost" || t.name == "Count")
                            Assert.GreaterOrEqual(size * s, HudLayout.NumberFloor - 0.01f, $"{t.name}, {at}");
                        if (t.name == "Key" || t.name == "Cost")
                        {
                            Assert.LessOrEqual(t.preferredWidth, t.rectTransform.rect.width + 0.01f, $"{t.name} '{t.text}' fits its tab, {at}");
                            if (t.name == "Key") keys++; else costs++;
                        }
                    }
                    if (role == MockBackend.Role.Knight) Assert.Greater(keys, 0, "the knight's keys were measured");
                    else Assert.Greater(costs, 0, "costs were measured, " + at);
                }
                // The widest numbers the strip shows fit their place.
                var numbers = Named<Text>("Numbers");
                numbers.text = "<color=#861E13>16000/16000</color>  <color=#213B78>1000/1000</color>";
                Assert.LessOrEqual(numbers.preferredWidth, numbers.rectTransform.rect.width, $"a monarch's numbers at {percent}%");
            }
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
        public IEnumerator AButtonGoesDownUnderThePointerAndComesBackUp()
        {
            yield return Begin();
            mock.Select(new[] { Own(MockBackend.Role.Knight) }, false);
            yield return Settle();
            var move = Button("MOVE");
            var plate = (RectTransform)move.Find("Plate");
            Assert.IsNotNull(plate, "the picture and bezel move together");
            var bezel = plate.Find("Bezel").GetComponent<Image>();
            var rest = plate.anchoredPosition;
            Pointer(move.gameObject, true);
            Assert.AreEqual(rest + new Vector2(1, -1), plate.anchoredPosition, "down and right by 1 cp");
            Assert.AreEqual(HudArt.GoldShadow, bezel.color, "the bezel in shadow");
            yield return Settle();
            Assert.AreEqual(rest + new Vector2(1, -1), plate.anchoredPosition, "a refresh keeps it down");
            Assert.AreEqual(HudArt.GoldShadow, bezel.color);
            Pointer(move.gameObject, false);
            Assert.AreEqual(rest, plate.anchoredPosition, "and up again");
            Assert.AreEqual(HudArt.Gold, bezel.color);

            // A build card's picture goes in by 1 cp, and the Menu lozenge goes down.
            mock.Select(new[] { Own(MockBackend.Role.Monarch) }, false);
            yield return Settle();
            var card = GameObject.Find("Builds").transform.GetChild(0);
            var cardPlate = (RectTransform)card.Find("Plate");
            Assert.IsNotNull(cardPlate);
            var cardRest = cardPlate.anchoredPosition;
            Pointer(card.gameObject, true);
            Assert.AreEqual(cardRest + new Vector2(1, -1), cardPlate.anchoredPosition, "the card's picture goes in");
            Assert.IsTrue(cardPlate.GetComponentsInChildren<Image>().Where(i => i.name == "Bezel").All(i => i.color == HudArt.GoldShadow), "its bezel in shadow");
            Pointer(card.gameObject, false);
            Assert.AreEqual(cardRest, cardPlate.anchoredPosition);
            var menu = Named<Image>("Menu");
            var lozenge = menu.transform.Find("Lozenge") as RectTransform;
            var lozRest = lozenge.anchoredPosition;
            Pointer(menu.gameObject, true);
            Assert.AreEqual(lozRest + new Vector2(1, -1), lozenge.anchoredPosition, "the Menu lozenge goes down");
            Pointer(menu.gameObject, false);
            Assert.AreEqual(lozRest, lozenge.anchoredPosition);
        }

        [UnityTest]
        public IEnumerator ACardThePoolCannotPayForSaysSo()
        {
            yield return Begin(100);
            var monarch = Units().First(u => u.Player == 1 && mock.RoleOf(u.Def) == MockBackend.Role.Monarch);
            mock.Select(new[] { monarch.Handle }, false);
            yield return Settle();
            var options = mock.UnitDefs[monarch.Def].BuildOptions.Select(i => mock.UnitDefs[i]).ToArray();
            var dear = options.OrderByDescending(d => d.ManaCost).First();
            var cheap = options.OrderBy(d => d.ManaCost).First();
            Assert.Greater(dear.ManaCost, 200, "the mock's dearest option is more than the pool");
            Assert.LessOrEqual(cheap.ManaCost, 100);
            var builds = GameObject.Find("Builds").transform;
            Transform Card(UnitDef d) => builds.Find("Build " + d.Name);
            Text Cost(UnitDef d) => Card(d).GetComponentsInChildren<Text>().First(t => t.name == "Cost");
            Image Wash(UnitDef d) => Card(d).GetComponentsInChildren<Image>(true).First(i => i.name == "Wash");
            Assert.AreEqual(HudArt.Minium, Cost(dear).color, "the cost the pool cannot pay in minium");
            Assert.IsTrue(Wash(dear).enabled, "and its picture washed");
            Assert.AreEqual(HudArt.AzuriteDeep, Cost(cheap).color, "what it can pay as before");
            Assert.IsFalse(Wash(cheap).enabled);
            Assert.IsTrue(Card(dear).GetComponent<Button>().interactable, "still clickable: the engine refuses and says why");
            Card(dear).GetComponent<HoverHint>().Show(true);
            Assert.AreEqual(Nice(dear) + ", " + dear.ManaCost + " mana", Hud.HelpLine1);
            Assert.AreEqual("Not enough mana", Hud.HelpLine2);
            Card(dear).GetComponent<HoverHint>().Show(false);
            Card(cheap).GetComponent<HoverHint>().Show(true);
            StringAssert.StartsWith("Click to place", Hud.HelpLine2, "a builder's card says how to place it");
            Card(cheap).GetComponent<HoverHint>().Show(false);

            // A factory's card gives its queue keys.
            mock.Select(new[] { Own(MockBackend.Role.Lodge) }, false);
            yield return Settle();
            var lodge = Units().First(u => u.Player == 1 && mock.RoleOf(u.Def) == MockBackend.Role.Lodge);
            var made = mock.UnitDefs[lodge.Def].BuildOptions.Select(i => mock.UnitDefs[i]).OrderBy(d => d.ManaCost).First();
            Card(made).GetComponent<HoverHint>().Show(true);
            Assert.AreEqual("Shift 5, Ctrl repeat, right click removes", Hud.HelpLine2);
            Card(made).GetComponent<HoverHint>().Show(false);
        }

        static string Nice(UnitDef d) =>
            !string.IsNullOrEmpty(d.Title) ? d.Title : string.IsNullOrEmpty(d.Description) ? d.Name : d.Description;

        [UnityTest]
        public IEnumerator TheRosterListsTheSelectionsKindsAndNarrowsIt()
        {
            yield return Begin();
            var mine = Units().Where(u => u.Player == 1).ToArray();
            int[] Of(MockBackend.Role role) => mine.Where(u => mock.RoleOf(u.Def) == role).Select(u => u.Handle).ToArray();
            var knights = Of(MockBackend.Role.Knight);
            var archers = Of(MockBackend.Role.Archer);
            var mages = Of(MockBackend.Role.Mage);
            Assert.AreEqual(3, new[] { knights.Length, archers.Length, mages.Length }.Distinct().Count(), "three kinds in different numbers");
            mock.Select(knights.Concat(archers).Concat(mages).ToArray(), false);
            yield return Settle();
            var roster = Named<RectTransform>("Roster");
            Assert.IsNotNull(roster, "the slot under the minimap holds the roster");
            Assert.IsNull(Named<RawImage>("Filler"), "not the empty filler");
            var rows = roster.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Kind ")).ToArray();
            Assert.AreEqual(3, rows.Length, "a row for each kind");
            string Count(Button b) => b.GetComponentsInChildren<Text>().First(t => t.name == "Count").text;
            var expect = new[] { knights, archers, mages }.OrderByDescending(h => h.Length).ToArray();
            for (int i = 0; i < 3; i++)
            {
                var def = mock.UnitDefs[mine.First(u => u.Handle == expect[i][0]).Def];
                Assert.AreEqual("Kind " + def.Name, rows[i].name, "the most numerous first");
                Assert.AreEqual(expect[i].Length.ToString(), Count(rows[i]));
                Assert.AreEqual(Nice(def), Plain(rows[i].GetComponentsInChildren<Text>().First(t => t.name == "Title").text));
            }
            int archerDef = mine.First(u => u.Handle == archers[0]).Def;
            rows.First(b => b.name == "Kind " + mock.UnitDefs[archerDef].Name).onClick.Invoke();
            CollectionAssert.AreEquivalent(archers, Selection(), "the engine keeps only the archers");
            CollectionAssert.AreEquivalent(archers, root.World.Entities.Selected, "and so does the HUD");
            yield return Settle();
            Assert.AreEqual(1, Named<RectTransform>("Roster").GetComponentsInChildren<Button>().Count(b => b.name.StartsWith("Kind ")));

            // One unit: its description, rank and kills.
            int knight = knights[0];
            mock.SetKills(knight, 12);
            mock.Select(new[] { knight }, false);
            yield return Settle();
            var kd = mock.UnitDefs[mine.First(u => u.Handle == knight).Def];
            Assert.AreEqual(BattleHud.Describe(kd), Named<Text>("Description").text);
            Assert.AreEqual("Champion, 12 kills", Named<Text>("Record").text);
        }

        [UnityTest]
        public IEnumerator WithNothingSelectedTheRosterFindsAnIdleBuilder()
        {
            yield return Begin();
            var monarch = Units().First(u => u.Player == 1 && mock.RoleOf(u.Def) == MockBackend.Role.Monarch);
            // Held where it stands, as the mock's idle units otherwise wander.
            Assert.IsTrue(mock.MoveFormation(new[] { monarch.Handle }, new[] { new Vector2(monarch.Position.x, monarch.Position.z) }, 0f, false, false));
            mock.Select(new int[0], false);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (mock.ReadOrder(monarch.Handle).Kind != OrderKind.None && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(OrderKind.None, mock.ReadOrder(monarch.Handle).Kind, "the monarch stands idle");
            yield return new WaitForSecondsRealtime(0.7f);
            Assert.AreEqual("Idle builders: 1", Named<Text>("Idle").text);
            var next = Named<Button>("Next idle");
            Assert.IsNotNull(next, "with a plate to find it");
            var cam = root.World.Camera;
            cam.focus = new Vector3(monarch.Position.x + 30f, cam.focus.y, monarch.Position.z - 30f);
            next.onClick.Invoke();
            CollectionAssert.AreEqual(new[] { monarch.Handle }, Selection(), "the plate selects the builder");
            CollectionAssert.AreEquivalent(new[] { monarch.Handle }, root.World.Entities.Selected);
            var now = Units().First(u => u.Handle == monarch.Handle).Position;
            Assert.Less(new Vector2(cam.focus.x - now.x, cam.focus.z - now.z).magnitude, 0.5f, "and looks at it");
        }

        [UnityTest]
        public IEnumerator TheStripDescribesOneUnitAndItsRank()
        {
            yield return Begin();
            int knight = Own(MockBackend.Role.Knight);
            mock.SetKills(knight, 4);
            mock.Select(new[] { knight }, false);
            yield return Settle();
            var def = mock.UnitDefs[Units().First(u => u.Handle == knight).Def];
            Assert.IsNotEmpty(def.Description);
            Assert.AreEqual(def.Description, Named<Text>("Group").text, "the description in the strip's spare width");
            Assert.AreEqual("Veteran.", Named<Text>("Rank").text, "after the rank word");
            Assert.AreEqual("4", Named<Text>("Kills").text);
            Assert.AreEqual("kills", Named<Text>("KillsLabel").text, "the count says what it counts");
            mock.SetKills(knight, 0);
            yield return Settle();
            Assert.IsNull(Named<Text>("KillsLabel"), "no word without a count");
            Assert.IsNull(Named<Text>("Rank"), "nor a rank");

            var two = new[] { knight, Own(MockBackend.Role.Archer) };
            mock.Select(two, false);
            yield return Settle();
            StringAssert.Contains(", ", Named<Text>("Group").text, "several units show their make-up there");
        }

        [UnityTest]
        public IEnumerator NumbersAreInSquareCapitals()
        {
            yield return Begin();
            mock.Select(new[] { Own(MockBackend.Role.Monarch) }, false);
            yield return Settle();
            foreach (var name in new[] { "Clock", "Income", "Spend", "Kills", "Numbers" })
                Assert.AreSame(UiKit.TitleFont, Named<Text>(name).font, name + " in Cinzel");
            var cost = GameObject.Find("Builds").GetComponentsInChildren<Text>().First(t => t.name == "Cost");
            Assert.AreSame(UiKit.TitleFont, cost.font, "a card's cost too");
            Assert.AreNotSame(UiKit.Frame, Named<Image>("View").sprite, "the minimap's view box is painted in gold");
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
