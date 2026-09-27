// EdgeCaptures.cs - pictures of a map's edges, for judging the land past
// them: the south-west corner at the classic pitch and at a low tilt, and
// the middle of the west edge. Runs only with OKU_CAPTURE_DIR, on the
// engine with OKU_CAPTURE_BACKEND=engine, on OKU_CAPTURE_MAP.
using System.Collections;
using System.IO;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class EdgeCaptures
    {
        [UnityTest]
        public IEnumerator CaptureTheEdges()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_CAPTURE_DIR to capture edges");
            Directory.CreateDirectory(dir);
            string map = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_MAP");
            string tag = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_TAG") ?? "edge";
            GameRoot root;
            if (System.Environment.GetEnvironmentVariable("OKU_CAPTURE_BACKEND") == "engine")
            {
                if (GameRoot.BackendFactory == null) Assert.Ignore("no engine");
                var view = GameObject.Find("OpenKingdoms");
                if (view != null) Object.Destroy(view);
                yield return null;
                yield return null;
                root = GameRoot.Boot();
            }
            else root = GameRoot.Boot(new MockBackend { StageSeconds = 0f });
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            if (!string.IsNullOrEmpty(map)) root.Setup.MapId = map;
            root.Setup.MapRevealed = true;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 240f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
            root.Orders.Frozen = true;
            var cam = Camera.main;
            var gc = root.World.Camera;
            var size = root.Backend.Terrain.Size;
            var hud = root.Screens.Screen("Hud");
            hud.SetActive(false);
            yield return Shot(gc, cam, new Vector3(6, 0, -size.y + 6), 34f, 62f, 20f, Path.Combine(dir, tag + "-corner-classic.png"));
            yield return Shot(gc, cam, new Vector3(6, 0, -size.y + 6), 40f, 25f, 35f, Path.Combine(dir, tag + "-corner-low.png"));
            yield return Shot(gc, cam, new Vector3(4, 0, -size.y / 2), 45f, 40f, -70f, Path.Combine(dir, tag + "-west.png"));
            Object.Destroy(root.gameObject);
        }

        static IEnumerator Shot(OpenKingdomsUnity.Game.World.GameCamera gc, Camera cam, Vector3 focus, float distance, float pitch, float yaw, string path)
        {
            gc.focus = focus;
            gc.pitch = pitch;
            gc.yaw = yaw;
            gc.Zoom(distance);
            for (int i = 0; i < 20; i++) yield return null;
            var rt = RenderTexture.GetTemporary(1920, 1080, 24);
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = old;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
