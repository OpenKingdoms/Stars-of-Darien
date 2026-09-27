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
            string map = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_MAP") ?? "mock_isles";
            var mock = new MockBackend { StageSeconds = 0.3f };
            var root = GameRoot.Boot(mock);
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
            while (root.Flow.State != FlowState.Playing) yield return null;
            for (int i = 0; i < 90; i++) yield return null;
            // Select a few of the player's units, so rings and bars show.
            for (int i = 0; i < root.World.Entities.UnitCount && i < 6; i++)
                root.World.Entities.Selected.Add(root.World.Entities.Units[i].Handle);
            yield return Shoot(cam, canvas, Path.Combine(dir, "4-game.png"));
            root.World.Camera.pitch = 38f;
            root.World.Camera.yaw = 30f;
            for (int i = 0; i < 5; i++) yield return null;
            yield return Shoot(cam, canvas, Path.Combine(dir, "5-game-tilted.png"));
            root.Flow.Fire(FlowEvent.Pause);
            yield return null;
            yield return Shoot(cam, canvas, Path.Combine(dir, "6-pause.png"));
            Object.Destroy(root.gameObject);
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
