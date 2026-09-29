// CliffLookPlayTests.cs - how the ground shader treats steep faces, on
// patches of one plain colour seen head on from past the close-up detail's
// reach: a face turned from the sun takes more of the sky's light and a
// rock pattern with the detail on, one in the sun keeps its colour, and
// flat ground and gentle slopes under 40 degrees are left exactly as they are.
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class CliffLookPlayTests
    {
        GameRoot root;
        readonly List<Object> made = new List<Object>();

        [TearDown]
        public void CleanUp()
        {
            GroundDetail.Apply(true);
            foreach (var o in made) if (o != null) Object.Destroy(o);
            made.Clear();
            if (root != null) Object.Destroy(root.gameObject);
        }

        // A 6 by 6 patch at centre, sloped by `slope` degrees and facing
        // `facing` (a flat direction) downhill.
        GameObject Patch(Vector3 centre, float slope, Vector3 facing, Material mat)
        {
            float t = slope * Mathf.Deg2Rad;
            var f = new Vector3(facing.x, 0f, facing.z).normalized;
            var n = new Vector3(f.x * Mathf.Sin(t), Mathf.Cos(t), f.z * Mathf.Sin(t));
            var along = new Vector3(-f.z, 0f, f.x);
            var up = Vector3.Cross(n, along).normalized;
            var v = new[] { centre - along * 3 - up * 3, centre + along * 3 - up * 3, centre + along * 3 + up * 3, centre - along * 3 + up * 3 };
            var mesh = new Mesh { vertices = v, uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }, normals = new[] { n, n, n, n } };
            bool front = Vector3.Dot(Vector3.Cross(v[1] - v[0], v[2] - v[0]), n) > 0;
            mesh.triangles = front ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            var go = new GameObject("cliff patch");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            made.Add(go);
            made.Add(mesh);
            return go;
        }

        static float Luma(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        // The mean brightness of the middle of a head-on shot of the patch,
        // and its mean spread within 8 pixel windows.
        static void Measure(Camera cam, RenderTexture rt, out float mean, out float grain)
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            var px = tex.GetPixels32();
            Object.Destroy(tex);
            int w = rt.width, lo = w * 35 / 100, hi = w * 65 / 100, win = 8;
            double sum = 0, spread = 0;
            int n = 0, windows = 0;
            for (int y0 = lo; y0 + win <= hi; y0 += win)
                for (int x0 = lo; x0 + win <= hi; x0 += win)
                {
                    double s = 0, sq = 0;
                    for (int y = y0; y < y0 + win; y++)
                        for (int x = x0; x < x0 + win; x++) { float l = Luma(px[y * w + x]); s += l; sq += l * l; }
                    int k = win * win;
                    sum += s;
                    n += k;
                    spread += System.Math.Sqrt(System.Math.Max(0, sq / k - (s / k) * (s / k)));
                    windows++;
                }
            mean = (float)(sum / n);
            grain = (float)(spread / windows);
        }

        [UnityTest]
        public IEnumerator SteepFacesTakeMoreLightAndRockAndFlatGroundKeeps()
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

            // One plain colour, so any pattern on a face is the shader's own.
            var plain = new Texture2D(32, 32, TextureFormat.RGBA32, true);
            var fill = new Color32[32 * 32];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(118, 100, 78, 255);
            plain.SetPixels32(fill);
            plain.Apply(true);
            made.Add(plain);
            var mat = Looks.Terrain(plain, -100f);
            made.Add(mat);

            var size = mock.Terrain.Size;
            var toSun = -root.World.Atmosphere.Sun.transform.forward;
            var sunward = new Vector3(toSun.x, 0f, toSun.z).normalized;
            var basePos = new Vector3(size.x * 0.25f, 40f, -size.y * 0.5f);
            var cases = new (string name, float slope, Vector3 facing)[]
            {
                ("flat", 0f, sunward), ("gentle", 30f, -sunward), ("shaded", 70f, -sunward), ("sunny", 70f, sunward),
            };
            var patches = new GameObject[cases.Length];
            for (int i = 0; i < cases.Length; i++)
                patches[i] = Patch(basePos + new Vector3(i * 12f, 0f, 0f), cases[i].slope, cases[i].facing, mat);

            var camGo = new GameObject("cliff test camera");
            made.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 12f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 400f;
            var rt = new RenderTexture(256, 256, 24);
            made.Add(rt);
            for (int i = 0; i < 20; i++) yield return null;

            var off = new (float mean, float grain)[cases.Length];
            var on = new (float mean, float grain)[cases.Length];
            for (int i = 0; i < cases.Length; i++)
            {
                // Head on from 70 units, past where the close-up detail fades.
                var mf = patches[i].GetComponent<MeshFilter>().sharedMesh;
                var centre = mf.bounds.center;
                var normal = mf.normals[0];
                cam.transform.position = centre + normal * 70f;
                cam.transform.LookAt(centre, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up);
                GroundDetail.Apply(false);
                Measure(cam, rt, out off[i].mean, out off[i].grain);
                GroundDetail.Apply(true);
                Measure(cam, rt, out on[i].mean, out on[i].grain);
                Debug.Log($"Cliff look {cases[i].name}: mean {off[i].mean:F1} to {on[i].mean:F1}, grain {off[i].grain:F2} to {on[i].grain:F2}");
            }

            Assert.AreEqual(off[0].mean, on[0].mean, off[0].mean * 0.015f + 0.5f, "flat ground keeps its look");
            Assert.AreEqual(off[1].mean, on[1].mean, off[1].mean * 0.02f + 0.5f, "a slope under 40 degrees keeps its look");
            Assert.Greater(on[2].mean, off[2].mean * 1.12f, "a face turned from the sun takes more of the sky's light");
            Assert.That(on[3].mean, Is.InRange(off[3].mean * 0.92f, off[3].mean * 1.3f), "a face in the sun keeps its colour");
            Assert.Greater(on[2].grain, 1.8f, "a shaded face shows rock");
            Assert.Greater(on[3].grain, 5.5f, "a sunlit face shows rock");
        }
    }
}
