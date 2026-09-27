// GroundDetailPlayTests.cs - the ground's close-up detail adds texture where
// the map's picture is soft, and keeps the ground's colour: up close and
// from afar the mean colour stays within 5% of the picture alone.
using System.Collections;
using System.IO;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class GroundDetailPlayTests
    {
        GameRoot root;

        [TearDown]
        public void CleanUp()
        {
            GroundDetail.Apply(true);
            if (root != null) Object.Destroy(root.gameObject);
        }

        static Texture2D Shot(Camera cam, RenderTexture rt)
        {
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = old;
            return tex;
        }

        static float Luma(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        static Vector3 Mean(Texture2D t)
        {
            var px = t.GetPixels32();
            double r = 0, g = 0, b = 0;
            foreach (var p in px) { r += p.r; g += p.g; b += p.b; }
            return new Vector3((float)(r / px.Length), (float)(g / px.Length), (float)(b / px.Length));
        }

        // The mean spread of brightness within small windows: fine texture.
        static float Grain(Texture2D t)
        {
            var px = t.GetPixels32();
            int w = t.width, h = t.height, win = 16, n = 0;
            double total = 0;
            for (int y0 = h / 4; y0 + win <= 3 * h / 4; y0 += win)
                for (int x0 = w / 4; x0 + win <= 3 * w / 4; x0 += win)
                {
                    double s = 0, sq = 0;
                    for (int y = y0; y < y0 + win; y++)
                        for (int x = x0; x < x0 + win; x++) { float l = Luma(px[y * w + x]); s += l; sq += l * l; }
                    int k = win * win;
                    total += System.Math.Sqrt(System.Math.Max(0, sq / k - (s / k) * (s / k)));
                    n++;
                }
            return (float)(total / n);
        }

        [UnityTest]
        public IEnumerator DetailShowsUpCloseAndKeepsTheColour()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_highlands";
            root.Setup.MapRevealed = true;
            root.Setup.LineOfSight = false;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
            root.Orders.Frozen = true;
            root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            root.Screens.Screen("Hud").SetActive(false);

            var gc = root.World.Camera;
            var cam = gc.GetComponent<Camera>();
            var size = mock.Terrain.Size;
            // Open land away from the bases and the lake.
            gc.focus = new Vector3(size.x * 0.3f, 0, -size.y * 0.35f);
            gc.pitch = 62f;
            gc.yaw = 0f;
            var rt = RenderTexture.GetTemporary(960, 540, 24);
            string dir = Application.temporaryCachePath;
            foreach (float distance in new[] { 12f, 90f })
            {
                gc.Zoom(distance);
                for (int i = 0; i < 30; i++) yield return null;
                GroundDetail.Apply(false);
                var plain = Shot(cam, rt);
                GroundDetail.Apply(true);
                var detailed = Shot(cam, rt);
                File.WriteAllBytes(Path.Combine(dir, $"detail-{distance}-off.png"), plain.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(dir, $"detail-{distance}-on.png"), detailed.EncodeToPNG());
                var a = Mean(plain);
                var b = Mean(detailed);
                float grainOff = Grain(plain), grainOn = Grain(detailed);
                Debug.Log($"Ground detail at {distance}: mean colour {a} without, {b} with, grain {grainOff:F2} without, {grainOn:F2} with, pictures in {dir}");
                for (int c = 0; c < 3; c++)
                    Assert.That(b[c], Is.EqualTo(a[c]).Within(a[c] * 0.05f + 1f), $"channel {c} keeps its mean at {distance}");
                if (distance < 20f) Assert.Greater(grainOn, grainOff * 1.15f, "up close the detail adds texture");
                Object.Destroy(plain);
                Object.Destroy(detailed);
            }
            RenderTexture.ReleaseTemporary(rt);
        }
    }
}
