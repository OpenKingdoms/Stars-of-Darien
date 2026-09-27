// GridCaptures.cs - a close top-down picture of the ground, and how much
// sharper its edges are on the block lines than between them, for judging
// whether the block grid shows. Runs only with OKU_CAPTURE_DIR, on the
// engine with OKU_CAPTURE_BACKEND=engine, on OKU_CAPTURE_MAP. Writes the
// picture and a high-contrast copy.
using System.Collections;
using System.IO;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class GridCaptures
    {
        const int W = 1920, H = 1080;

        [UnityTest]
        public IEnumerator CaptureTheBlockGrid()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_CAPTURE_DIR to capture the ground");
            Directory.CreateDirectory(dir);
            string map = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_MAP");
            string tag = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_TAG") ?? "grid";
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
            root.Setup.LineOfSight = false;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 240f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
            root.Orders.Frozen = true;
            root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            root.Screens.Screen("Hud").SetActive(false);

            var t = root.Backend.Terrain;
            var gc = root.World.Camera;
            var cam = gc.GetComponent<Camera>();
            // The flattest spot on a coarse grid, so heights do not bend the lines.
            Vector3 best = new Vector3(t.Size.x / 2, 0, -t.Size.y / 2);
            float bestRange = float.MaxValue;
            for (float x = 20; x < t.Size.x - 20; x += 8)
                for (float z = -20; z > -t.Size.y + 20; z -= 8)
                {
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (float dx = -10; dx <= 10; dx += 2)
                        for (float dz = -6; dz <= 6; dz += 2)
                        {
                            float h = root.Backend.GroundHeight(x + dx, z + dz);
                            lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                        }
                    if (lo < t.SeaLevel + 0.5f) continue;
                    if (hi - lo < bestRange) { bestRange = hi - lo; best = new Vector3(x, 0, z); }
                }
            best.y = root.Backend.GroundHeight(best.x, best.z);
            gc.focus = best;
            gc.pitch = gc.maxPitch;
            gc.yaw = 0f;
            gc.Zoom(30f);
            for (int i = 0; i < 40; i++) yield return null;

            var rt = RenderTexture.GetTemporary(W, H, 24);
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            // Block lines across the middle band, as screen columns.
            var lines = new System.Collections.Generic.List<float>();
            for (int k = 0; k * t.BlockSize < t.Size.x; k++)
            {
                var s = cam.WorldToScreenPoint(new Vector3(k * t.BlockSize, best.y, best.z));
                if (s.z > 0 && s.x > 8 && s.x < W - 8) lines.Add(s.x);
            }
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = old;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(dir, tag + "-ground.png"), tex.EncodeToPNG());

            // Column by column, the mean step in brightness to the next
            // column over the middle band of rows.
            var px = tex.GetPixels32();
            var step = new float[W];
            int y0 = H / 2 - 150, y1 = H / 2 + 150;
            for (int y = y0; y < y1; y++)
                for (int x = 0; x + 1 < W; x++)
                    step[x] += Mathf.Abs(Luma(px[y * W + x + 1]) - Luma(px[y * W + x]));
            for (int x = 0; x < W; x++) step[x] /= y1 - y0;

            // High contrast: the steps, stretched, as a picture.
            var filtered = new Texture2D(W, H, TextureFormat.RGB24, false);
            var f = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x + 1 < W; x++)
                {
                    byte v = (byte)Mathf.Clamp(Mathf.Abs(Luma(px[y * W + x + 1]) - Luma(px[y * W + x])) * 8f, 0, 255);
                    f[y * W + x] = new Color32(v, v, v, 255);
                }
            filtered.SetPixels32(f);
            filtered.Apply();
            File.WriteAllBytes(Path.Combine(dir, tag + "-contrast.png"), filtered.EncodeToPNG());

            float onLine = 0, between = 0;
            int n = 0;
            for (int i = 0; i + 1 < lines.Count; i++)
            {
                onLine += Peak(step, lines[i]);
                between += Peak(step, (lines[i] + lines[i + 1]) / 2);
                n++;
            }
            Assert.Greater(n, 4, "the view crosses several block lines");
            float ratio = onLine / Mathf.Max(1e-3f, between);
            string report = $"Grid: {tag} on {map} at ({best.x:F0}, {best.z:F0}), {n} block lines {(lines[1] - lines[0]):F0} px apart, edges on the lines {ratio:F2} times those between";
            Debug.Log(report);
            File.WriteAllText(Path.Combine(dir, tag + "-grid.txt"), report);
            Object.Destroy(root.gameObject);
        }

        // The steepest slope on the map, seen from below at a low tilt, with
        // the ground detail off and on, for judging streaks down cliffs.
        [UnityTest]
        public IEnumerator CaptureACliff()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_CAPTURE_DIR to capture the ground");
            Directory.CreateDirectory(dir);
            string map = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_MAP");
            string tag = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_TAG") ?? "cliff";
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
            root.Setup.LineOfSight = false;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 240f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
            root.Orders.Frozen = true;
            root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            root.Screens.Screen("Hud").SetActive(false);

            var t = root.Backend.Terrain;
            Vector3 best = default;
            Vector2 down = Vector2.up;
            float steepest = 0;
            for (float x = 12; x < t.Size.x - 12; x += 2)
                for (float z = -12; z > -t.Size.y + 12; z -= 2)
                {
                    float gx = root.Backend.GroundHeight(x + 1, z) - root.Backend.GroundHeight(x - 1, z);
                    float gz = root.Backend.GroundHeight(x, z + 1) - root.Backend.GroundHeight(x, z - 1);
                    float g = Mathf.Sqrt(gx * gx + gz * gz) / 2f;
                    if (g > steepest) { steepest = g; best = new Vector3(x, 0, z); down = -new Vector2(gx, gz).normalized; }
                }
            best.y = root.Backend.GroundHeight(best.x, best.z);
            var gc = root.World.Camera;
            var cam = gc.GetComponent<Camera>();
            gc.focus = best;
            gc.pitch = 35f;
            // Stand downhill and look up the slope.
            gc.yaw = Mathf.Atan2(-down.x, -down.y) * Mathf.Rad2Deg;
            gc.Zoom(22f);
            for (int i = 0; i < 40; i++) yield return null;
            foreach (bool on in new[] { false, true })
            {
                GroundDetail.Apply(on);
                var rt = RenderTexture.GetTemporary(W, H, 24);
                var old = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = old;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(Path.Combine(dir, $"{tag}-cliff-{(on ? "detail" : "plain")}.png"), tex.EncodeToPNG());
            }
            GroundDetail.Apply(true);
            Debug.Log($"Cliff: {tag} on {map} at ({best.x:F0}, {best.z:F0}), slope {steepest:F2}");
            Object.Destroy(root.gameObject);
        }

        static float Luma(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        // The biggest column step within two pixels of x.
        static float Peak(float[] step, float x)
        {
            int c = Mathf.RoundToInt(x);
            float m = 0;
            for (int i = c - 2; i <= c + 2; i++) if (i >= 0 && i < step.Length) m = Mathf.Max(m, step[i]);
            return m;
        }
    }
}
