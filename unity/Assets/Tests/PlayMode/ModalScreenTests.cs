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

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
            if (uiCam != null) Object.Destroy(uiCam.gameObject);
            if (rt != null) rt.Release();
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
            target.onClick.Invoke();
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
                var rows = new[] { "Weather", "Game speed", "Controls", "Interface size", "Key letters", "Pointer size", "Sound", "Music", "Display", "Shadows", "Post effects" };
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
            CollectionAssert.Contains(words, "Standing");
            CollectionAssert.Contains(words, "Aramon", "with the kingdom's name");
            CheckReadable("Result");

            Click("Result", "Look at the field");
            yield return null;
            Assert.AreEqual(FlowState.Victory, root.Flow.State, "the battle stays won");
            Assert.IsFalse(result.activeSelf, "the plaque is put away");
            Assert.IsTrue(root.Screens.Screen("Hud").activeSelf, "the battlefield and the HUD stay");
            Click("Results", "Results");
            Assert.IsTrue(root.Screens.Screen("Result").activeSelf, "Results brings it back");
            Click("Result", "Return to menu");
            Assert.AreEqual(FlowState.MainMenu, root.Flow.State);

            yield return Begin();
            ((MockBackend)root.Backend).Players[0].Alive = false;
            root.Flow.Fire(FlowEvent.Lost);
            yield return ViewAt(1280, 720);
            result = root.Screens.Screen("Result");
            title = Named(result, "Title");
            Assert.AreEqual("Defeat", title.text);
            Assert.AreEqual(HudArt.Silver, title.color, "defeat in silver");
            CollectionAssert.Contains(result.GetComponentsInChildren<Text>().Select(t => t.text).ToList(), "Fallen");
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
    }
}
