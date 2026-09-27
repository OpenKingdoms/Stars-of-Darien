// ScreenCaptures.cs - pictures of each screen on the mock engine, for
// looking at the look without a window. Runs only when OKU_CAPTURE_DIR
// names a folder, and writes 1920 by 1080 PNGs there.
using System.Collections;
using System.IO;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class ScreenCaptures
    {
        [UnityTest]
        public IEnumerator CaptureEveryScreen()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_CAPTURE_DIR to capture screens");
            Directory.CreateDirectory(dir);
            bool engine = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_BACKEND") == "engine";
            string map = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_MAP");
            GameRoot root;
            if (engine)
            {
                if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
                // The engine's own view boots into the test scene. Take it away
                // first, as the engine runs one game at a time.
                var view = GameObject.Find("OpenKingdoms");
                if (view != null) Object.Destroy(view);
                yield return null;
                yield return null;
                root = GameRoot.Boot();
            }
            else root = GameRoot.Boot(new MockBackend { StageSeconds = 0.3f });
            if (string.IsNullOrEmpty(map)) map = root.Backend.Maps[0].Id;
            yield return null;
            var cam = Camera.main;
            if (cam == null) { cam = new GameObject("Main Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; }
            var canvas = root.GetComponentInChildren<Canvas>();

            yield return Shoot(cam, canvas, Path.Combine(dir, "1-menu.png"));
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = map;
            yield return null;
            root.Screens.Show(FlowState.Skirmish);
            yield return Shoot(cam, canvas, Path.Combine(dir, "2-skirmish.png"));
            root.Screens.StartGame();
            yield return null;
            yield return null;
            yield return Shoot(cam, canvas, Path.Combine(dir, "3-loading.png"));
            float deadline = Time.realtimeSinceStartup + 240f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, "the game loaded: " + root.LastError);
            for (int i = 0; i < 90; i++) yield return null;
            // Select a few of the player's units, so rings and bars show.
            var e = root.World.Entities;
            for (int i = 0, n = 0; i < e.UnitCount && n < 6; i++)
                if (e.Units[i].Player == root.Backend.LocalPlayer) { e.Selected.Add(e.Units[i].Handle); n++; }
            var cam3 = root.World.Camera;
            // The classic view, then close and low, then far and wide.
            yield return Shoot(cam, canvas, Path.Combine(dir, "4-classic.png"));
            yield return View(cam3, 14f, 38f, 25f);
            yield return Shoot(cam, canvas, Path.Combine(dir, "5-close.png"));
            yield return View(cam3, 95f, 50f, -20f);
            yield return Shoot(cam, canvas, Path.Combine(dir, "6-wide.png"));
            yield return View(cam3, 34f, OpenKingdomsUnity.Game.World.GameCamera.ClassicPitch, 0f);
            root.Flow.Fire(FlowEvent.Pause);
            yield return null;
            yield return Shoot(cam, canvas, Path.Combine(dir, "7-pause.png"));
            Object.Destroy(root.gameObject);
        }

        static IEnumerator View(OpenKingdomsUnity.Game.World.GameCamera c, float distance, float pitch, float yaw)
        {
            c.pitch = pitch;
            c.yaw = yaw;
            c.Zoom(distance);
            for (int i = 0; i < 20; i++) yield return null;
        }

        static IEnumerator Shoot(Camera cam, Canvas canvas, string path)
        {
            yield return null;
            var rt = RenderTexture.GetTemporary(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var mode = canvas.renderMode;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = cam.nearClipPlane + 0.1f;
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = old;
            canvas.renderMode = mode;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
