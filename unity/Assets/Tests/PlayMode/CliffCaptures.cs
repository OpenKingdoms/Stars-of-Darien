// CliffCaptures.cs - the most cliff-crowded spot of real maps from the
// classic camera and a low three-quarter view looking up the slope, for a
// person to judge the ground shader's steep faces. Runs only with
// OKU_CLIFF_DIR, on the engine, and writes <tag>-<map>-<view>.png there;
// OKU_CLIFF_TAG names the run and OKU_CLIFF_MAPS lists the maps.
using System.Collections;
using System.IO;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class CliffCaptures
    {
        GameRoot root;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator CaptureCliffs()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_CLIFF_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_CLIFF_DIR to capture cliffs");
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            Directory.CreateDirectory(dir);
            string tag = System.Environment.GetEnvironmentVariable("OKU_CLIFF_TAG") ?? "now";
            string maps = System.Environment.GetEnvironmentVariable("OKU_CLIFF_MAPS") ?? "abnar's terrace;black heart jungle";
            var view = GameObject.Find("OpenKingdoms");
            if (view != null) Object.Destroy(view);
            yield return null;
            yield return null;
            foreach (var map in maps.Split(';'))
            {
                root = GameRoot.Boot();
                yield return null;
                root.Flow.Fire(FlowEvent.OpenSkirmish);
                root.Setup.MapId = map;
                root.Setup.MapRevealed = true;
                root.Setup.LineOfSight = false;
                root.Screens.StartGame();
                float deadline = Time.realtimeSinceStartup + 240f;
                while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
                root.Orders.Frozen = true;
                root.World.Atmosphere.SetWeather(WeatherChoice.Off);
                root.Screens.Screen("Hud").SetActive(false);
                var b = root.Backend;
                var size = b.Terrain.Size;

                // The 24 unit square with the most ground steeper than 45
                // degrees, and which way its slopes fall on the whole.
                float Slope(float x, float z, out Vector2 down)
                {
                    float gx = b.GroundHeight(x + 1, z) - b.GroundHeight(x - 1, z);
                    float gz = b.GroundHeight(x, z + 1) - b.GroundHeight(x, z - 1);
                    down = -new Vector2(gx, gz);
                    return Mathf.Sqrt(gx * gx + gz * gz) / 2f;
                }
                Vector3 best = new Vector3(size.x / 2, 0, -size.y / 2);
                Vector2 bestDown = Vector2.up;
                int bestCount = -1;
                for (float cx = 24; cx < size.x - 24; cx += 8)
                    for (float cz = -24; cz > -size.y + 24; cz -= 8)
                    {
                        int count = 0;
                        var down = Vector2.zero;
                        for (float x = cx - 12; x < cx + 12; x += 2)
                            for (float z = cz - 12; z < cz + 12; z += 2)
                                if (Slope(x, z, out var d) > 1f) { count++; down += d; }
                        if (count > bestCount) { bestCount = count; best = new Vector3(cx, 0, cz); bestDown = down; }
                    }
                best.y = b.GroundHeight(best.x, best.z);
                if (bestDown.sqrMagnitude < 1e-6f) bestDown = Vector2.up;
                bestDown.Normalize();

                var gc = root.World.Camera;
                var cam = gc.GetComponent<Camera>();
                string slug = map.Replace("'", "").Replace(' ', '-');
                foreach (var (name, pitch, yaw, zoom) in new[]
                {
                    ("classic", GameCamera.ClassicPitch, 0f, 34f),
                    // Stand downhill and look up the slope.
                    ("low", 30f, Mathf.Atan2(-bestDown.x, -bestDown.y) * Mathf.Rad2Deg, 26f),
                })
                {
                    // The focus at the height the camera eases to, then held
                    // until the view is still, so every run frames alike.
                    gc.focus = new Vector3(best.x, gc.SmoothGround(best), best.z);
                    gc.pitch = pitch;
                    gc.yaw = yaw;
                    gc.Zoom(zoom);
                    var was = gc.transform.position;
                    for (int i = 0, still = 0; i < 3000 && still < 20; i++)
                    {
                        yield return null;
                        still = (gc.transform.position - was).sqrMagnitude < 1e-8f ? still + 1 : 0;
                        was = gc.transform.position;
                    }
                    var rt = RenderTexture.GetTemporary(1280, 720, 24);
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    cam.targetTexture = null;
                    RenderTexture.ReleaseTemporary(rt);
                    File.WriteAllBytes(Path.Combine(dir, $"{tag}-{slug}-{name}.png"), tex.EncodeToPNG());
                    Object.Destroy(tex);
                }
                Debug.Log($"Cliffs: {tag} on {map} at ({best.x:F0}, {best.z:F0}), {bestCount} steep samples");
                Object.Destroy(root.gameObject);
                root = null;
                yield return null;
                yield return null;
            }
        }
    }
}
