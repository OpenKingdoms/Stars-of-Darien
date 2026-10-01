// ModalScreenTests.cs - at small and wide game views, the in-game
// Options panel's Back and close buttons are the topmost thing under the
// pointer, above the battle HUD, and take the player back to Pause.
// Pause's own buttons and the Load screen's Back are checked the same way.
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
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
        // nothing that takes clicks is drawn over it.
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
                var r = WorldRect(g.rectTransform);
                Assert.IsFalse(r.Overlaps(own), $"{g.name} (depth {g.depth}) lies over {button} (depth {depth})");
            }
            target.onClick.Invoke();
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
            Assert.AreEqual(FlowState.MainMenu, root.Flow.State);
        }
    }
}
