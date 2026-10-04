// ModalScreenTests.cs - the dialogs over the battle at small and wide game
// views: Pause, the question before leaving, Options, victory and defeat,
// every button topmost under the pointer, every word readable, keys answered.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Tests
{
    public class ModalScreenTests
    {
        GameRoot root;
        Camera uiCam;
        RenderTexture rt;
        string saves;

        [SetUp]
        public void OwnSaves() => saves = TempSaves.Use();

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
            if (uiCam != null) Object.Destroy(uiCam.gameObject);
            if (rt != null) rt.Release();
            TempSaves.Drop(saves);
        }

        // Draws the screens into a picture of the given size, as a Game view
        // of that size would show them.
        IEnumerator ViewAt(int w, int h)
        {
            if (rt != null) rt.Release();
            rt = new RenderTexture(w, h, 24);
            if (uiCam == null) uiCam = new GameObject("UI camera").AddComponent<Camera>();
            uiCam.targetTexture = rt;
            uiCam.enabled = true;
            var canvas = root.Screens.Canvas;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = uiCam;
            canvas.planeDistance = 1f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
        }

        // Clicks a button, after checking it shows whole on the screen and
        // nothing that takes clicks is drawn over it, inside its masks.
        void Click(string screen, string button)
        {
            var target = FindButton(screen, button);
            Assert.IsNotNull(target, $"{screen} has a {button} button");
            Uncovered(target);
            target.onClick.Invoke();
        }

        void Uncovered(Button target)
        {
            string button = target.name;
            var canvasRt = (RectTransform)root.Screens.Canvas.transform;
            var box = WorldRect(canvasRt);
            var own = WorldRect((RectTransform)target.transform);
            Assert.IsTrue(box.Contains(own.min) && box.Contains(own.max), $"{button} at {rt.width}x{rt.height} is wholly on screen: {own} in {box}");
            int depth = target.GetComponent<Graphic>().depth;
            foreach (var g in root.Screens.Canvas.GetComponentsInChildren<Graphic>(false))
            {
                if (!g.raycastTarget || g.depth <= depth || g.transform.IsChildOf(target.transform)) continue;
                var r = Clipped(g);
                if (r.width <= 0f || r.height <= 0f) continue;
                Assert.IsFalse(r.Overlaps(own), $"{g.name} (depth {g.depth}) lies over {button} (depth {depth})");
            }
        }

        // What a mask leaves of a graphic, as raycasts see it.
        static Rect Clipped(Graphic g)
        {
            var r = WorldRect(g.rectTransform);
            foreach (var m in g.GetComponentsInParent<RectMask2D>())
            {
                var c = WorldRect(m.rectTransform);
                r = Rect.MinMaxRect(Mathf.Max(r.xMin, c.xMin), Mathf.Max(r.yMin, c.yMin), Mathf.Min(r.xMax, c.xMax), Mathf.Min(r.yMax, c.yMax));
            }
            return r;
        }

        // Whether a rect lies inside another, edges touching included.
        static bool Within(Rect inner, Rect outer)
        {
            float e = 1e-4f * outer.height;
            return inner.xMin >= outer.xMin - e && inner.yMin >= outer.yMin - e && inner.xMax <= outer.xMax + e && inner.yMax <= outer.yMax + e;
        }

        static Rect WorldRect(RectTransform r)
        {
            var c = new Vector3[4];
            r.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        Button FindButton(string screen, string name)
        {
            var s = root.Screens.Screen(screen);
            foreach (var b in s.GetComponentsInChildren<Button>(false)) if (b.name == name) return b;
            return null;
        }

        [UnityTest]
        public IEnumerator OptionsClosesFromBackAndTheCrossAtAnySize()
        {
            root = GameRoot.Boot(new MockBackend { StageSeconds = 0f, DamageScale = 0f });
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State);

            foreach (var size in new[] { new Vector2Int(1915, 860), new Vector2Int(1280, 720), new Vector2Int(1024, 600) })
            {
                root.Flow.Fire(FlowEvent.Pause);
                yield return ViewAt(size.x, size.y);
                Click("Pause", "Options");
                Assert.AreEqual(FlowState.Options, root.Flow.State, $"Pause opens Options at {size}");
                yield return ViewAt(size.x, size.y);
                Click("Options", "Back");
                Assert.AreEqual(FlowState.Paused, root.Flow.State, $"Back returns to Pause at {size}");
                Click("Pause", "Options");
                yield return ViewAt(size.x, size.y);
                Click("Options", "Close");
                Assert.AreEqual(FlowState.Paused, root.Flow.State, $"the cross returns to Pause at {size}");
                Click("Pause", "Resume");
                Assert.AreEqual(FlowState.Playing, root.Flow.State);
            }
        }

        [UnityTest]
        public IEnumerator LoadAndSaveButtonsAreReachableSmall()
        {
            root = GameRoot.Boot(new MockBackend { StageSeconds = 0f, DamageScale = 0f });
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Flow.Fire(FlowEvent.OpenLoad);
            yield return ViewAt(1024, 600);
            Click("Load", "Back");
            Assert.AreEqual(FlowState.Skirmish, root.Flow.State);
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            root.Flow.Fire(FlowEvent.Pause);
            yield return ViewAt(1024, 600);
            Assert.IsNotNull(FindButton("Pause", "Save game"));
            Click("Pause", "Quit to menu");
            yield return null;
            Click("Leave", "Leave");
            Assert.AreEqual(FlowState.MainMenu, root.Flow.State);
        }

        // ---- The dialogs on vellum ----

        [TearDown]
        public void Restore()
        {
            UiKit.Motion.Off = false;
            BuildStamp.Reset();
            BuildStamp.SaveInAlpha = true;
            MenuScreens.SizeOverride = null;
        }

        IEnumerator Begin()
        {
            if (root != null) Object.Destroy(root.gameObject);
            yield return null;
            root = GameRoot.Boot(new MockBackend { StageSeconds = 0f, DamageScale = 0f });
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
        }

        // Pixels on the picture of a rect's height, whatever scales it.
        float Px(RectTransform r)
        {
            var own = WorldRect(r);
            var all = WorldRect((RectTransform)root.Screens.Canvas.transform);
            return own.height / all.height * rt.height;
        }

        // Every word on a screen is over the floor and fits its box, and
        // every plate is big enough to hit.
        void CheckReadable(string screen)
        {
            var s = root.Screens.Screen(screen);
            var canvas = (RectTransform)root.Screens.Canvas.transform;
            float unit = root.Screens.Canvas.scaleFactor;
            foreach (var t in s.GetComponentsInChildren<Text>(false))
            {
                if (string.IsNullOrEmpty(t.text)) continue;
                float px = t.fontSize * unit * t.rectTransform.lossyScale.y / canvas.lossyScale.y;
                Assert.GreaterOrEqual(px, 11.95f, $"{screen}: '{t.text}' is {px:0.0} px at {rt.width}x{rt.height}");
                Assert.LessOrEqual(t.preferredHeight, t.rectTransform.rect.height + 0.5f, $"{screen}: '{t.text}' fits its box at {rt.width}x{rt.height}");
            }
            foreach (var p in s.GetComponentsInChildren<Plate>(false))
                Assert.GreaterOrEqual(Px((RectTransform)p.transform), 31.9f, $"{screen}: {p.name} at {rt.width}x{rt.height}");
        }

        static Text Named(GameObject under, string name) => under.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.name == name);

        [UnityTest]
        public IEnumerator QuitToMenuAsksFirstAtAnySize()
        {
            UiKit.Motion.Off = true;
            yield return Begin();
            foreach (var size in new[] { new Vector2Int(1915, 860), new Vector2Int(1280, 720), new Vector2Int(1024, 600) })
            {
                root.Flow.Fire(FlowEvent.Pause);
                yield return ViewAt(size.x, size.y);
                Assert.AreEqual("Escape or F1 resumes", Named(root.Screens.Screen("Pause"), "Help").text);
                if (size.y >= 720) CheckReadable("Pause");
                Click("Pause", "Quit to menu");
                Assert.AreEqual(FlowState.Paused, root.Flow.State, $"the question comes first at {size}");
                yield return null;
                Assert.IsTrue(root.Screens.Screen("Leave").activeSelf, "Leave the battle? is up");
                if (size.y >= 720) CheckReadable("Leave");
                Click("Leave", "Stay");
                Assert.AreEqual(FlowState.Paused, root.Flow.State);
                Assert.IsFalse(root.Screens.Screen("Leave").activeSelf, "Stay puts the question away");
                Assert.IsTrue(root.Screens.Screen("Pause").activeSelf);
                Click("Pause", "Quit to menu");
                yield return null;
                Click("Leave", "Leave");
                Assert.AreEqual(FlowState.MainMenu, root.Flow.State, $"Leave goes to the menu at {size}");
                root.Flow.Fire(FlowEvent.OpenSkirmish);
                root.Screens.StartGame();
                float deadline = Time.realtimeSinceStartup + 30f;
                while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            }
        }

        [UnityTest]
        public IEnumerator EscapeF1AndEnterResumeAndEnterLeavesFromTheQuestion()
        {
            UiKit.Motion.Off = true;
            yield return Begin();
            foreach (var key in new[] { KeyCode.Escape, KeyCode.F1, KeyCode.Return })
            {
                root.Flow.Fire(FlowEvent.Pause);
                yield return null;
                Assert.IsTrue(root.Screens.Key(key), $"{key} is taken");
                Assert.AreEqual(FlowState.Playing, root.Flow.State, $"{key} resumes");
            }
            root.Flow.Fire(FlowEvent.Pause);
            yield return ViewAt(1280, 720);
            Click("Pause", "Quit to menu");
            yield return null;
            root.Screens.Key(KeyCode.Return);
            Assert.AreEqual(FlowState.MainMenu, root.Flow.State, "Enter answers the question");
        }

        [UnityTest]
        public IEnumerator OptionsExplainEveryRowAndKeepTheirKeys()
        {
            UiKit.Motion.Off = true;
            // The options live in PlayerPrefs, so the player's own come back after.
            var saved = GameOptions.Load();
            try
            {
                yield return Begin();
                root.Flow.Fire(FlowEvent.Pause);
                root.Flow.Fire(FlowEvent.OpenOptions);
                yield return ViewAt(1280, 720);
                var options = root.Screens.Screen("Options");
                var help = Named(options, "Help");
                Assert.AreEqual("Changes apply at once. Back keeps them.", help.text);
                foreach (var rubric in new[] { "Game", "Interface", "Sound", "Display" })
                    Assert.IsNotNull(Named(options, "Rubric " + rubric), $"the {rubric} section");
                var spots = options.GetComponentsInChildren<HelpSpot>(true).Where(h => h.name.StartsWith("Row ")).ToList();
                var rows = new[] { "Weather", "Game speed", "Controls", "Interface size", "Key letters", "Pointer size", "Sound", "Music", "Display", "Shadows", "Post effects", "Battle effects" };
                CollectionAssert.IsSubsetOf(rows, spots.Select(h => h.name.Substring(4)).ToList());
                var events = new PointerEventData(EventSystem.current);
                foreach (var spot in spots)
                {
                    Assert.IsFalse(string.IsNullOrEmpty(spot.Line), spot.name + " explains itself");
                    ExecuteEvents.Execute(spot.gameObject, events, ExecuteEvents.pointerEnterHandler);
                    Assert.AreEqual(spot.Line, help.text, "hovering " + spot.name + " writes its help");
                    ExecuteEvents.Execute(spot.gameObject, events, ExecuteEvents.pointerExitHandler);
                    Assert.AreEqual("Changes apply at once. Back keeps them.", help.text);
                }
                StringAssert.Contains("Look at the battle behind this panel", spots.First(h => h.name == "Row Interface size").Line);
                CheckReadable("Options");

                CyclePicker Picker(string label) => options.GetComponentsInChildren<CyclePicker>(true).First(c => c.name == "Picker " + label);
                var o = root.Options;
                Click("Options", "Defaults");
                Picker("Weather").Step(2);
                Picker("Game speed").Step(1);
                Assert.AreEqual(WeatherChoice.Rain, o.Weather);
                Assert.AreEqual(2, o.GameSpeed);
                Click("Options", "Defaults");
                Assert.AreEqual(WeatherChoice.ByMap, o.Weather, "Defaults puts the weather back");
                Assert.AreEqual(1, o.GameSpeed, "and the speed");
                Assert.AreEqual(0, Picker("Weather").Index);
                Assert.AreEqual("By map", Picker("Weather").GetComponentsInChildren<Text>().First(t => t.name == "Value").text);

                Assert.IsTrue(root.Screens.Key(KeyCode.DownArrow), "Down takes the first row");
                root.Screens.Key(KeyCode.RightArrow);
                Assert.AreEqual(WeatherChoice.Off, o.Weather, "Right steps the row with the focus");
                root.Screens.Key(KeyCode.LeftArrow);
                Assert.AreEqual(WeatherChoice.ByMap, o.Weather);
                root.Screens.Key(KeyCode.DownArrow);
                root.Screens.Key(KeyCode.RightArrow);
                Assert.AreEqual(2, o.GameSpeed, "Down moves to the next row");
                root.Screens.Key(KeyCode.LeftArrow);
                root.Screens.Key(KeyCode.Escape);
                Assert.AreEqual(FlowState.Paused, root.Flow.State, "Escape goes back to the pause menu");
            }
            finally { saved.Save(); }
        }

        static IEnumerator Until(System.Func<bool> done, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < deadline) yield return null;
        }

        int OrderButtons()
        {
            var grid = GameObject.Find("Orders");
            return grid == null ? 0 : grid.GetComponentsInChildren<Button>().Count(b => b.name.StartsWith("Action "));
        }

        [UnityTest]
        public IEnumerator ALostBattleWatchedFromTheFieldPlaysOnUntilOneSideIsLeft()
        {
            UiKit.Motion.Off = true;
            if (root != null) Object.Destroy(root.gameObject);
            yield return null;
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            var seats = root.Setup.Seats;
            for (int i = 0; i < seats.Count; i++)
            {
                seats[i].Kind = i == 0 ? SeatKind.Human : i < 3 ? SeatKind.Computer : SeatKind.Closed;
                seats[i].Team = SeatTeam.Alone;
            }
            root.Screens.StartGame();
            yield return Until(() => root.Flow.State == FlowState.Playing, 30f);
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
            yield return ViewAt(1280, 720);

            // Your army falls while two computers are still at war.
            mock.Rout(mock.LocalPlayer);
            root.Backend.Advance(root.Backend.TicksPerSecond);
            yield return null;
            Assert.AreEqual(FlowState.Defeat, root.Flow.State);
            uint ended = root.Backend.Tick;
            yield return null;
            yield return null;
            Assert.AreEqual(ended, root.Backend.Tick, "the battle holds under the plaque");

            var you = Texts("Result", "Player").First(t => t.text == "You");
            Assert.AreEqual(LobbyInk.Text, you.color, "the player's own row stays legible on its wash");
            Click("Result", "Look at the field");
            Assert.IsTrue(mock.PlaysOn, "the computers fight on");
            yield return Until(() => root.Backend.Tick > ended + 30, 10f);
            Assert.Greater(root.Backend.Tick, ended + 30, "the battle plays on while the field shows");
            Assert.AreEqual(FlowState.Defeat, root.Flow.State, "and stays lost");
            Assert.IsFalse(root.Screens.Screen("Result").activeSelf);

            root.BattleKey(KeyCode.F1);
            Assert.AreEqual(FlowState.Paused, root.Flow.State, "the pause menu still works");
            uint paused = root.Backend.Tick;
            yield return null;
            yield return null;
            Assert.AreEqual(paused, root.Backend.Tick, "and holds the battle");
            Click("Pause", "Resume");
            yield return null;
            Assert.AreEqual(FlowState.Defeat, root.Flow.State);
            Assert.IsFalse(root.Screens.Screen("Result").activeSelf, "back to the field, not the plaque");
            Assert.IsTrue(root.Screens.Screen("Results").activeSelf);

            Click("Results", "Results");
            var result = root.Screens.Screen("Result");
            Assert.IsTrue(result.activeSelf, "Results brings the plaque back");
            Assert.AreEqual("Defeat", Named(result, "Title").text, "the defeat stands");
            int secs = (int)(ended / (uint)root.Backend.TicksPerSecond);
            StringAssert.Contains($"after {secs / 60} min {secs % 60} s", Named(result, "Sentence").text, "at the time it fell");
            Click("Result", "Look at the field");

            // One computer left: the field stays, quietly.
            mock.Rout(mock.Players[2].Index);
            yield return Until(() => !mock.PlaysOn, 10f);
            Assert.IsFalse(mock.PlaysOn, "the war is over");
            uint over = root.Backend.Tick;
            yield return null;
            yield return null;
            Assert.AreEqual(over, root.Backend.Tick, "and the battle stands still");
            Assert.AreEqual(FlowState.Defeat, root.Flow.State);
            Assert.IsFalse(root.Screens.Screen("Result").activeSelf, "with no new plaque");

            Click("Results", "Results");
            Click("Result", "Return to menu");
            Assert.AreEqual(FlowState.MainMenu, root.Flow.State);
            Assert.AreEqual(GameStatus.Idle, root.Backend.Status, "the battle is gone");
        }

        [UnityTest]
        public IEnumerator WatchingALostBattleGivesNoOrders()
        {
            yield return Begin();
            var mock = (MockBackend)root.Backend;
            var units = new UnitState[512];
            int n = mock.ReadUnits(units);
            int own = units.Take(n).First(u => u.Player == mock.LocalPlayer && !mock.UnitDefs[u.Def].IsBuilding).Handle;
            mock.Select(new[] { own }, false);
            yield return Until(() => OrderButtons() > 0, 5f);
            Assert.Greater(OrderButtons(), 0, "your unit's orders show while you play");

            mock.Players[0].Alive = false;
            root.Flow.Fire(FlowEvent.Lost);
            UiKit.Motion.Off = true;
            yield return ViewAt(1280, 720);
            Click("Result", "Look at the field");
            yield return null;
            yield return null;
            Assert.AreEqual(0, OrderButtons(), "the HUD's orders stay off while you watch");
        }

        [UnityTest]
        public IEnumerator TheResultNamesTheKingdomsAndLetsThePlayerLookAtTheField()
        {
            yield return Begin();
            root.Flow.Fire(FlowEvent.Won);
            yield return ViewAt(1280, 720);
            var result = root.Screens.Screen("Result");
            Assert.IsTrue(result.activeSelf);
            var plaque = result.GetComponentInChildren<Opening>();
            Assert.AreEqual(0f, plaque.Group.alpha, "the field is seen before the plaque is read");
            Assert.IsFalse(plaque.Group.blocksRaycasts);
            UiKit.Motion.Off = true;
            yield return null;
            Assert.AreEqual(1f, plaque.Group.alpha, "captures and tests see it at once");
            var title = Named(result, "Title");
            Assert.AreEqual("Victory", title.text);
            Assert.AreEqual(HudArt.GoldHi, title.color);
            var words = result.GetComponentsInChildren<Text>().Select(t => t.text).ToList();
            CollectionAssert.Contains(words, "You", "the local player is in the table");
            foreach (var column in new[] { "Player", "Units", "Kills", "Losses", "Time", "Score" })
                CollectionAssert.Contains(words, column, "the original's columns");
            CheckReadable("Result");

            var mock = (MockBackend)root.Backend;
            Assert.IsFalse(mock.SeesAll);
            Click("Result", "Look at the field");
            yield return null;
            Assert.AreEqual(FlowState.Victory, root.Flow.State, "the battle stays won");
            Assert.IsTrue(mock.SeesAll, "the whole field is in view");
            Assert.IsFalse(result.activeSelf, "the plaque is put away");
            Assert.IsTrue(root.Screens.Screen("Hud").activeSelf, "the battlefield and the HUD stay");
            Click("Results", "Results");
            Assert.IsTrue(root.Screens.Screen("Result").activeSelf, "Results brings it back");
            Click("Result", "Return to menu");
            Assert.AreEqual(FlowState.MainMenu, root.Flow.State);

            yield return Begin();
            Assert.IsFalse(((MockBackend)root.Backend).SeesAll, "a new battle starts in the player's own sight");
            ((MockBackend)root.Backend).Players[0].Alive = false;
            root.Flow.Fire(FlowEvent.Lost);
            yield return ViewAt(1280, 720);
            result = root.Screens.Screen("Result");
            title = Named(result, "Title");
            Assert.AreEqual("Defeat", title.text);
            Assert.AreEqual(HudArt.Silver, title.color, "defeat in silver");
            StringAssert.StartsWith("Your kingdom has fallen", Named(result, "Sentence").text);
        }

        // ---- The remastered end screen ----

        List<Text> Texts(string screen, string name) =>
            root.Screens.Screen(screen).GetComponentsInChildren<Text>(false).Where(t => t.name == name).ToList();

        // A mock battle with these seats, its armies sent at each other for a
        // while, and won.
        IEnumerator Fought(int kingdoms, string map, int seconds)
        {
            UiKit.Motion.Off = false;
            if (root != null) Object.Destroy(root.gameObject);
            yield return null;
            var mock = new MockBackend { StageSeconds = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = map;
            var seats = root.Setup.Seats;
            for (int i = 0; i < seats.Count; i++)
            {
                seats[i].Kind = i == 0 ? SeatKind.Human : i < kingdoms ? SeatKind.Computer : SeatKind.Closed;
                seats[i].Team = SeatTeam.Alone;
            }
            root.Screens.StartGame();
            yield return Until(() => root.Flow.State == FlowState.Playing, 30f);
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
            Assert.AreEqual(kingdoms, root.Backend.Players.Count);
            var units = new UnitState[4096];
            int n = mock.ReadUnits(units);
            var centre = new Vector3(mock.Terrain.Size.x / 2f, 0, -mock.Terrain.Size.y / 2f);
            for (int i = 0; i < n; i++)
                if (!mock.UnitDefs[units[i].Def].IsBuilding) mock.Command(GameCommand.To(CommandKind.Move, units[i].Handle, centre));
            mock.Advance(seconds * MockBackend.Tps);
            root.Flow.Fire(FlowEvent.Won);
        }

        [UnityTest]
        public IEnumerator TheBannerStandsOverTheFieldBeforeThePage()
        {
            yield return Fought(2, "mock_frost", 5);
            yield return ViewAt(1280, 720);
            var result = root.Screens.Screen("Result");
            Assert.IsTrue(result.activeSelf);
            var banner = result.transform.Find("Banner").GetComponent<CanvasGroup>();
            Assert.AreEqual(1f, banner.alpha, "the word stands over the field");
            Assert.AreEqual("Victory", result.transform.Find("Banner/Banner word").GetComponent<Text>().text);
            var page = result.GetComponentInChildren<Opening>();
            Assert.AreEqual(0f, page.Group.alpha, "the page waits");
            Assert.AreEqual(3f, MenuScreens.ResultHold, "the original's 90 frames");
            UiKit.Motion.Off = true;
            yield return null;
            yield return null;
            Assert.AreEqual(1f, page.Group.alpha, "the page has come");
            Assert.AreEqual(0f, banner.alpha, "and the banner has gone");
        }

        [UnityTest]
        public IEnumerator ThePageCarriesTheOriginalsTalliesAndThreePagesMore()
        {
            yield return Fought(3, "mock_dunes", 45);
            UiKit.Motion.Off = true;
            yield return ViewAt(1280, 720);
            var record = root.Screens.Results.Record;
            var b = root.Backend;
            Assert.Greater(record.Kingdoms.Sum(k => k.Kills), 0, "the armies fought");

            // The original's table: a row a kingdom, its own numbers.
            var names = Texts("Result", "Player").Where(t => t.text != "").ToList();
            Assert.AreEqual(3, names.Count);
            var units = Texts("Result", "Units");
            var kills = Texts("Result", "Kills");
            var losses = Texts("Result", "Losses");
            var time = Texts("Result", "Time");
            var score = Texts("Result", "Score");
            for (int i = 0; i < b.Players.Count; i++)
            {
                var k = record.Of(b.Players[i].Index);
                Assert.AreEqual(MenuScreens.ResultName(b.Players[i]), names[i].text);
                Assert.AreEqual(k.UnitsBuilt.ToString("N0"), units[i].text);
                Assert.AreEqual(k.Kills.ToString("N0"), kills[i].text);
                Assert.AreEqual(k.Losses.ToString("N0"), losses[i].text);
                Assert.AreEqual(BattleRecord.Clock(k.LastAliveTick, record.TicksPerSecond), time[i].text);
                Assert.AreEqual(k.Score.ToString("N0"), score[i].text);
            }
            CheckReadable("Result");

            // Graphs: a line a kingdom over the battle, six to choose from.
            Click("Result", "Graphs");
            Assert.AreEqual(1, root.Screens.Results.Tab);
            var graph = root.Screens.Screen("Result").GetComponentInChildren<LineGraph>();
            Assert.IsNotNull(graph, "the graph shows");
            Assert.AreEqual(3, graph.Lines.Count);
            foreach (var line in graph.Lines) Assert.GreaterOrEqual(line.Points.Length, 9, "a point every 5 s and now");
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.Greater(graph.Drawn, 0, "the lines are drawn");
            // And they show on the picture: pixels in a kingdom's colour.
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            uiCam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = null;
            var want = (Color)graph.Lines.Last().Colour;
            int hits = tex.GetPixels().Count(c => Mathf.Abs(c.r - want.r) + Mathf.Abs(c.g - want.g) + Mathf.Abs(c.b - want.b) < 0.12f);
            Object.Destroy(tex);
            Assert.Greater(hits, 20, "the player's line shows");
            CheckReadable("Result");
            root.Screens.Results.ShowChart(4);
            var mine = record.Of(b.LocalPlayer);
            Assert.AreEqual(mine.Kills, (int)root.Screens.Results.ChartValues(mine, 4).Last(), "the kills chart ends at the tally");

            // A kingdom at a time, the player's own first, by keys too.
            Assert.IsTrue(root.Screens.Key(KeyCode.RightArrow));
            Assert.AreEqual(2, root.Screens.Results.Tab);
            Assert.AreEqual(mine.UnitsTrained.ToString("N0"), Texts("Result", "Units trained").Single().text);
            Assert.AreEqual(mine.DamageDealt.ToString("N0"), Texts("Result", "Damage dealt").Single().text);
            Assert.AreEqual(3, root.Screens.Screen("Result").GetComponentsInChildren<Clicker>(false).Count(c => c.name.StartsWith("Kingdom ")));
            CheckReadable("Result");

            // The annals: the start, the first blood, the verdict, and honours.
            Assert.IsTrue(root.Screens.Key(KeyCode.RightArrow));
            Assert.AreEqual(3, root.Screens.Results.Tab);
            var moments = Texts("Result", "Moment").Select(t => t.text).ToList();
            StringAssert.StartsWith("The battle begins on", moments[0]);
            Assert.IsTrue(moments.Any(m => m.StartsWith("First blood")), string.Join(" | ", moments));
            Assert.AreEqual("Victory.", moments.Last());
            Assert.Greater(Texts("Result", "Honour").Count, 0);
            CheckReadable("Result");

            // Round to the tallies again.
            Assert.IsTrue(root.Screens.Key(KeyCode.Tab));
            Assert.AreEqual(0, root.Screens.Results.Tab);
        }

        [UnityTest]
        public IEnumerator EightKingdomsFitEveryPage()
        {
            yield return Fought(8, "mock_marches", 20);
            UiKit.Motion.Off = true;
            foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(3840, 2160) })
            {
                yield return ViewAt(size.x, size.y);
                Assert.AreEqual(8, Texts("Result", "Player").Count(t => t.text != ""), $"eight rows at {size}");
                for (int tab = 0; tab < ResultScreen.Tabs.Length; tab++)
                {
                    Click("Result", ResultScreen.Tabs[tab]);
                    yield return null;
                    CheckReadable("Result");
                }
                Click("Result", "Kingdoms");
                yield return null;
                Assert.AreEqual(8, root.Screens.Screen("Result").GetComponentsInChildren<Clicker>(false).Count(c => c.name.StartsWith("Kingdom ")),
                    "every kingdom can be picked");
                Click("Result", "Graphs");
                yield return null;
                Assert.AreEqual(8, root.Screens.Screen("Result").GetComponentInChildren<LineGraph>().Lines.Count);
                Click("Result", "Tallies");
                yield return null;
                Uncovered(FindButton("Result", "Look at the field"));
                Uncovered(FindButton("Result", "Return to menu"));
            }
        }

        [UnityTest]
        public IEnumerator EnterLooksAtTheFieldAndEscapeReturnsToTheMenu()
        {
            yield return Fought(2, "mock_frost", 5);
            UiKit.Motion.Off = true;
            yield return ViewAt(1280, 720);
            Assert.IsTrue(root.Screens.Key(KeyCode.Return), "Enter presses Proceed");
            Assert.IsFalse(root.Screens.Screen("Result").activeSelf, "the page is put away");
            Assert.IsTrue(((MockBackend)root.Backend).SeesAll);
            Click("Results", "Results");
            Assert.IsTrue(root.Screens.Screen("Result").activeSelf);
            yield return null;
            Assert.IsTrue(root.Screens.Key(KeyCode.Escape), "Escape presses Main Menu");
            Assert.AreEqual(FlowState.MainMenu, root.Flow.State);
        }

        [UnityTest]
        public IEnumerator SaveGameStaysUnlessTheStampHidesIt()
        {
            UiKit.Motion.Off = true;
            BuildStamp.SkirmishOnly = true;
            yield return Begin();
            root.Flow.Fire(FlowEvent.Pause);
            yield return null;
            Assert.IsNotNull(FindButton("Pause", "Save game"), "the owner keeps Save game in the alpha");
            BuildStamp.SaveInAlpha = false;
            yield return Begin();
            root.Flow.Fire(FlowEvent.Pause);
            yield return null;
            Assert.IsNull(FindButton("Pause", "Save game"), "one switch hides it");
            Assert.IsNotNull(FindButton("Pause", "Resume"));
            Assert.IsNotNull(FindButton("Pause", "Options"));
            Assert.IsNotNull(FindButton("Pause", "Quit to menu"));
            BuildStamp.SkirmishOnly = false;
            yield return Begin();
            root.Flow.Fire(FlowEvent.Pause);
            yield return null;
            Assert.IsNotNull(FindButton("Pause", "Save game"), "a development build always saves");
        }

        [UnityTest]
        public IEnumerator SavingWritesTheNoteInVerdigris()
        {
            UiKit.Motion.Off = true;
            yield return Begin();
            root.Flow.Fire(FlowEvent.Pause);
            yield return null;
            FindButton("Pause", "Save game").onClick.Invoke();
            var note = Named(root.Screens.Screen("Pause"), "Save note");
            StringAssert.StartsWith("Saved as ", note.text);
            Assert.AreEqual(HudArt.Verdigris, note.color);
        }

        // ---- Fixes from the review ----

        // A capture lays the dialogs out for its own size, whatever the
        // batch run's window, so the dialog draws at the size it was built.
        [UnityTest]
        public IEnumerator UnderASizeOverrideADialogDrawsAtItsLayoutSize()
        {
            UiKit.Motion.Off = true;
            MenuScreens.SizeOverride = new Vector2Int(3840, 2160);
            yield return Begin();
            root.Flow.Fire(FlowEvent.Pause);
            // Drawn into a small picture, as a capture in a small window is.
            yield return ViewAt(640, 480);
            var dialog = root.Screens.Screen("Pause").GetComponentInChildren<Dialog>();
            Assert.AreEqual(1f, dialog.transform.localScale.x, 0.001f, "the pause dialog is not shrunk to the batch window");
        }

        // How much of the battle shows through a dim, as the eye sees it: in
        // linear colour a black at alpha a leaves (1 - a) of the light.
        static float SeenThrough(GameObject screen)
        {
            float a = screen.transform.Find("Dim").GetComponent<Image>().color.a;
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? Mathf.Pow(1f - a, 1f / 2.2f) : 1f - a;
        }

        [UnityTest]
        public IEnumerator TheDimsHalveTheBattleAsTheEyeSeesIt()
        {
            root = GameRoot.Boot(new MockBackend { StageSeconds = 0f, DamageScale = 0f });
            yield return null;
            Assert.AreEqual(0.5f, SeenThrough(root.Screens.Screen("Pause")), 0.03f, "Pause halves the battle behind it");
            Assert.AreEqual(0.5f, SeenThrough(root.Screens.Screen("Options")), 0.03f, "and so does Options");
            Assert.AreEqual(0.7f, SeenThrough(root.Screens.Screen("Leave")), 0.03f, "the question dims the pause menu a little more");
        }

        [UnityTest]
        public IEnumerator OptionsOpenOverThePageTheyCameFrom()
        {
            UiKit.Motion.Off = true;
            root = GameRoot.Boot(new MockBackend { StageSeconds = 0f, DamageScale = 0f });
            yield return null;
            foreach (var from in new[] { FlowState.MainMenu, FlowState.Skirmish })
            {
                if (from == FlowState.Skirmish) root.Flow.Fire(FlowEvent.OpenSkirmish);
                root.Flow.Fire(FlowEvent.OpenOptions);
                yield return null;
                string page = from == FlowState.MainMenu ? "Menu" : "Skirmish";
                var under = root.Screens.Screen(page);
                var options = root.Screens.Screen("Options");
                Assert.IsTrue(under.activeSelf && options.activeSelf, $"{page} stays under Options");
                Assert.Greater(options.transform.GetSiblingIndex(), under.transform.GetSiblingIndex(), "Options draws over it");
                root.Flow.Fire(FlowEvent.Back);
                Assert.AreEqual(from, root.Flow.State);
            }
        }

        [UnityTest]
        public IEnumerator TheSavedGamesAreOnVellum()
        {
            UiKit.Motion.Off = true;
            yield return Begin();
            root.Flow.Fire(FlowEvent.Pause);
            Assert.IsTrue(root.SaveNow(out var path), "a save to list");
            Assert.AreEqual(saves, System.IO.Path.GetDirectoryName(path), "saved in the test's own folder");
            // Fifteen older saves, more rows than the list shows at once.
            string json = System.IO.File.ReadAllText(path);
            for (int day = 1; day <= 15; day++)
            {
                long at = new System.DateTime(2026, 9, day, 12, 0, 0, System.DateTimeKind.Utc).Ticks;
                System.IO.File.WriteAllText(System.IO.Path.Combine(saves, $"2026-09-{day:00} 12-00-00 Twin Isles.oksav"),
                    System.Text.RegularExpressions.Regex.Replace(json, "\"savedAt\":\\d+", "\"savedAt\":" + at));
            }
            root.Flow.Fire(FlowEvent.ToMenu);
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Flow.Fire(FlowEvent.OpenLoad);
            yield return ViewAt(1280, 720);
            var load = root.Screens.Screen("Load");
            var dialog = load.GetComponentInChildren<Dialog>();
            Assert.IsNotNull(dialog, "the saved games are a dialog on vellum");
            Assert.AreEqual("Load a game", dialog.Title.text);
            Assert.IsFalse(load.GetComponentsInChildren<Image>(true).Any(i => i.sprite == UiKit.Stone), "no grey stone left");
            CheckReadable("Load");
            var rows = load.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Save ")).ToList();
            Assert.AreEqual(16, rows.Count, "a row for every save");
            var entry = FindButton("Load", "Save " + System.IO.Path.GetFileName(path));
            Assert.IsNotNull(entry, "the save is a row in the list");
            Assert.AreSame(rows[0], entry, "the newest first");
            Assert.AreEqual(HudArt.Ink, entry.GetComponentInChildren<Text>().color, "in ink on the vellum");
            var scroll = entry.GetComponentInParent<ScrollRect>();
            Assert.Greater(scroll.content.rect.height, scroll.viewport.rect.height, "the list is longer than its window");

            // Every row the window shows whole takes its own clicks, at the
            // top of the list and scrolled to its foot.
            foreach (float place in new[] { 1f, 0f })
            {
                scroll.verticalNormalizedPosition = place;
                yield return null;
                Canvas.ForceUpdateCanvases();
                yield return null;
                var view = WorldRect(scroll.viewport);
                var shown = rows.Where(r => Within(WorldRect((RectTransform)r.transform), view)).ToList();
                Assert.GreaterOrEqual(shown.Count, 6, "the window shows six rows or more");
                Assert.Contains(place > 0.5f ? rows[0] : rows[rows.Count - 1], shown, "the end of the list it is scrolled to");
                foreach (var row in shown) Uncovered(row);
            }
            scroll.verticalNormalizedPosition = 1f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
            Click("Load", entry.name);
            Assert.AreEqual(FlowState.Loading, root.Flow.State, "a row loads its game");
        }
    }
}
