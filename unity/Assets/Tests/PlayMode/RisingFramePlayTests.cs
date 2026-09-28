// RisingFramePlayTests.cs - a frame under construction shows from 0%,
// risen as far as it is built: at 10%, 50% and 90% the solid part reaches
// that share of its height, the shown build follows a new read smoothly
// and a held build holds. With OKU_CAPTURE_DIR it also writes a strip of
// a lodge rising, for a person to judge.
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class RisingFramePlayTests
    {
        GameRoot root;
        MockBackend mock;
        Camera side;

        [TearDown]
        public void CleanUp()
        {
            EntityRenderer.GhostShows = 1f;
            if (side != null) Object.Destroy(side.gameObject);
            if (root != null) Object.Destroy(root.gameObject);
        }

        IEnumerator Begin()
        {
            mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f, BuildSeconds = 0f };
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
        }

        UnitState[] Units()
        {
            var u = new UnitState[512];
            int n = mock.ReadUnits(u);
            return u.Take(n).ToArray();
        }

        // A lodge's frame on open ground near the monarch.
        int Frame(float built, out Vector3 site, out int def)
        {
            var units = Units();
            var monarch = units.First(u => u.Player == mock.LocalPlayer && mock.RoleOf(u.Def) == MockBackend.Role.Monarch);
            def = units.First(u => u.Player == mock.LocalPlayer && mock.RoleOf(u.Def) == MockBackend.Role.Lodge).Def;
            site = default;
            bool found = false;
            for (int dx = 8; dx < 40 && !found; dx += 2) found = mock.CanBuildAt(def, monarch.Position + new Vector3(dx, 0, -4), 0, out site);
            Assert.IsTrue(found, "open ground for the frame");
            return mock.SpawnFrame(def, site, built);
        }

        IEnumerator Settle(int handle, float built)
        {
            mock.SetBuilt(handle, built);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (Mathf.Abs(root.World.Entities.BuildShown(handle) - built) > 0.003f && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
        }

        Texture2D Shot(Camera cam, RenderTexture rt)
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            return tex;
        }

        // The highest pixel row, at or under below, where two pictures differ.
        static int TopChange(Texture2D a, Texture2D b, int below)
        {
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            for (int y = Mathf.Min(a.height - 1, below); y >= 0; y--)
                for (int x = 0; x < a.width; x++)
                {
                    var p = pa[y * a.width + x];
                    var q = pb[y * a.width + x];
                    if (System.Math.Abs(p.r - q.r) + System.Math.Abs(p.g - q.g) + System.Math.Abs(p.b - q.b) > 30) return y;
                }
            return -1;
        }

        [UnityTest]
        public IEnumerator AFrameRisesAsItIsBuilt()
        {
            yield return Begin();
            EntityRenderer.GhostShows = 0f;
            int handle = Frame(0.1f, out var site, out _);
            yield return null;
            var model = root.World.Models.Get(Units().First(u => u.Handle == handle).Model);
            var rest = model.RestBounds;
            float bottom = site.y + Mathf.Min(0f, rest.min.y), top = site.y + rest.max.y;

            side = new GameObject("side camera").AddComponent<Camera>();
            side.enabled = false;
            side.orthographic = true;
            side.orthographicSize = (top - bottom) * 0.8f + 0.5f;
            side.transform.SetPositionAndRotation(new Vector3(site.x, (top + bottom) / 2, site.z - 30f), Quaternion.identity);
            float reach = Mathf.Max(rest.extents.x, rest.extents.z) + 1.5f;
            side.nearClipPlane = 30f - reach;
            side.farClipPlane = 30f + reach;
            side.clearFlags = CameraClearFlags.SolidColor;
            side.backgroundColor = Color.black;
            var rt = RenderTexture.GetTemporary(512, 512, 24);

            float Row(float y) => side.WorldToScreenPoint(new Vector3(site.x, y, site.z)).y * 512f / side.pixelHeight;
            // Only the frame is drawn, so nothing else moving changes a pixel.
            root.World.Entities.Hidden = u => u.Handle != handle;
            var shots = new System.Collections.Generic.Dictionary<float, Texture2D>();
            foreach (float p in new[] { 0.1f, 0.5f, 0.9f, 1f })
            {
                yield return Settle(handle, p);
                shots[p] = Shot(side, rt);
            }
            root.World.Entities.Hidden = u => true;
            yield return null;
            yield return null;
            var bare = Shot(side, rt);
            RenderTexture.ReleaseTemporary(rt);

            // Rows above the lodge's top hold its health bar while it is built.
            int limit = Mathf.RoundToInt(Row(top + 0.2f));
            float baseRow = Row(bottom);
            float full = TopChange(shots[1f], bare, limit) - baseRow;
            Assert.Greater(full, 40f, "the finished lodge stands tall in the picture");
            foreach (float p in new[] { 0.1f, 0.5f, 0.9f })
            {
                float risen = (TopChange(shots[p], bare, limit) - baseRow) / full;
                Debug.Log($"Rising frame: {p:P0} built shows {risen:P0} of its height");
                Assert.That(risen, Is.EqualTo(p).Within(0.08f), $"at {p:P0} built");
            }
        }

        [UnityTest]
        public IEnumerator TheShownBuildEasesAndHolds()
        {
            yield return Begin();
            int handle = Frame(0.5f, out _, out _);
            yield return Settle(handle, 0.5f);
            var ents = root.World.Entities;
            for (int i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(0.5f, ents.BuildShown(handle), 0.003f, "a held build holds");
            mock.SetBuilt(handle, 0.9f);
            yield return null;
            float next = ents.BuildShown(handle);
            Assert.Greater(next, 0.5f, "it moves toward the new read");
            Assert.Less(next, 0.85f, "without jumping to it");
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.AreEqual(0.9f, ents.BuildShown(handle), 0.01f, "and catches up");
        }

        [UnityTest]
        public IEnumerator CaptureALodgeRising()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_CAPTURE_DIR to capture a lodge rising");
            Directory.CreateDirectory(dir);
            yield return Begin();
            int handle = Frame(0.001f, out var site, out _);
            var gc = root.World.Camera;
            var cam = gc.GetComponent<Camera>();
            gc.focus = site;
            gc.pitch = 50f;
            gc.yaw = 20f;
            gc.Zoom(14f);
            for (int i = 0; i < 20; i++) yield return null;
            var rt = RenderTexture.GetTemporary(480, 400, 24);
            var steps = new[] { 0f, 0.15f, 0.3f, 0.5f, 0.7f, 0.85f, 1f };
            var strip = new Texture2D(480 * steps.Length, 400, TextureFormat.RGB24, false);
            for (int k = 0; k < steps.Length; k++)
            {
                yield return Settle(handle, Mathf.Max(0.001f, steps[k]));
                var shot = Shot(cam, rt);
                strip.SetPixels(480 * k, 0, 480, 400, shot.GetPixels());
                Object.Destroy(shot);
            }
            strip.Apply();
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(dir, "lodge-rising.png"), strip.EncodeToPNG());
        }
    }
}
