// WaterPlayTests.cs - the sea as it is drawn: ships sit in its surface, a
// lake inside a high border shows as water, fogged water is dimmed water,
// shallows are lighter than the deep, foam lines the shore, a moving ship
// leaves a wake, the open sea shows no stripes or tiling, and what it costs.
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace OpenKingdomsUnity.Tests
{
    public class WaterPlayTests
    {
        GameRoot root;
        MockBackend mock;

        [TearDown]
        public void CleanUp()
        {
            FogView.Disabled = false;
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }

        IEnumerator Begin(string map, bool revealed, bool lineOfSight)
        {
            mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = map;
            root.Setup.MapRevealed = revealed;
            root.Setup.LineOfSight = lineOfSight;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
            root.Orders.Frozen = true;
            root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            root.Screens.Screen("Hud").SetActive(false);
        }

        IEnumerator Look(Vector3 focus, float distance, float pitch, float yaw = 0f)
        {
            var gc = root.World.Camera;
            gc.focus = focus;
            gc.pitch = pitch;
            gc.yaw = yaw;
            gc.Zoom(distance);
            for (int i = 0; i < 20; i++) yield return null;
        }

        Camera Cam => root.World.Camera.GetComponent<Camera>();

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

        static void Save(Texture2D t, string name)
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_WATER_TEST_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Application.temporaryCachePath;
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name), t.EncodeToPNG());
        }

        // Where world points fall in a picture of rt's size.
        List<Vector2> OnScreen(RenderTexture rt, IEnumerable<Vector3> points)
        {
            var cam = Cam;
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            var list = new List<Vector2>();
            foreach (var p in points)
            {
                var s = cam.WorldToScreenPoint(p);
                if (s.z > 0 && s.x >= 4 && s.y >= 4 && s.x < rt.width - 4 && s.y < rt.height - 4) list.Add(new Vector2(s.x, s.y));
            }
            cam.targetTexture = old;
            return list;
        }

        static Color32 Px(Texture2D t, Vector2 p) => t.GetPixel((int)p.x, (int)p.y);
        static float Luma(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        static float Saturation(Color32 c)
        {
            int hi = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), lo = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            return hi > 0 ? (hi - lo) / (float)hi : 0;
        }

        static Color Mean(Texture2D t, List<Vector2> at)
        {
            var sum = Vector4.zero;
            foreach (var p in at) { Color32 c = Px(t, p); sum += new Vector4(c.r, c.g, c.b, 0); }
            sum /= Mathf.Max(1, at.Count);
            return new Color(sum.x, sum.y, sum.z);
        }

        UnitState UnitOf(int handle)
        {
            var e = root.World.Entities;
            for (int i = 0; i < e.UnitCount; i++) if (e.Units[i].Handle == handle) return e.Units[i];
            Assert.Fail("unit " + handle + " is drawn");
            return default;
        }

        // Points on the sea surface in a box, where the ground under them
        // is between two depths.
        List<Vector3> SeaPoints(float x0, float x1, float z0, float z1, float minDepth, float maxDepth, float step = 0.5f)
        {
            var t = mock.Terrain;
            var list = new List<Vector3>();
            for (float x = x0; x <= x1; x += step)
                for (float z = z0; z <= z1; z += step)
                {
                    float d = t.SeaLevel - mock.GroundHeight(x, z);
                    if (d >= minDepth && d <= maxDepth) list.Add(new Vector3(x, t.SeaLevel, z));
                }
            return list;
        }

        // ---- Ships ----

        // The engine reports a ship at the sea floor. Every ship drawn must
        // have its hull through the surface, keel under the water and most
        // of it above, deep water or shallow, at rest or under way.
        [UnityTest]
        public IEnumerator ShipsFloatInTheSurface()
        {
            yield return Begin("mock_bay", true, false);
            var t = mock.Terrain;
            int deep = mock.SpawnBoat(mock.LocalPlayer, new Vector3(68, 0, -40));
            int shallow = mock.SpawnBoat(mock.LocalPlayer, new Vector3(50, 0, -70));
            int sailing = mock.SpawnBoat(mock.LocalPlayer, new Vector3(66, 0, -90));
            Assert.GreaterOrEqual(deep, 0, "a boat on the deep water");
            Assert.GreaterOrEqual(shallow, 0, "a boat on the shallows");
            Assert.IsTrue(mock.Command(GameCommand.To(CommandKind.Move, sailing, new Vector3(66, 0, -110))));
            for (int i = 0; i < 30; i++) yield return null;
            var e = root.World.Entities;
            int afloat = 0, onLand = 0;
            for (int i = 0; i < e.UnitCount; i++)
            {
                var u = e.Units[i];
                float ground = mock.GroundHeight(u.Position.x, u.Position.z);
                if (Afloat.KindOf(mock.UnitDefs[u.Def]) != FloatKind.Ship)
                {
                    Assert.AreEqual(ground, u.Position.y, 0.01f, "a unit on land stands on the ground");
                    onLand++;
                    continue;
                }
                Assert.Less(ground, t.SeaLevel - 0.6f, "the boat is over water");
                var b = e.UnitBounds(u.Handle);
                Debug.Log($"Boat {u.Handle}: ground {ground:F2}, sea {t.SeaLevel:F2}, drawn from {b.min.y:F2} to {b.max.y:F2}");
                Assert.Less(b.min.y, t.SeaLevel - 0.1f, "its keel is under the surface");
                Assert.Greater(b.max.y, t.SeaLevel + 1f, "and it stands out of the water");
                float under = (t.SeaLevel - b.min.y) / b.size.y;
                Assert.Less(under, 0.3f, "with most of the ship above the surface");
                afloat++;
            }
            Assert.AreEqual(3, afloat, "every boat was checked");
            Assert.Greater(onLand, 0);
        }

        // ---- Castle: a lake inside a high border ----

        [UnityTest]
        public IEnumerator ALakeInsideAHighBorderShowsAsWater()
        {
            yield return Begin("mock_moat", true, false);
            FogView.Disabled = true;
            root.World.Fog.Update(true);
            var t = mock.Terrain;
            var centre = new Vector3(48, t.SeaLevel, -48);
            yield return Look(centre, 40f, 70f);
            var rt = RenderTexture.GetTemporary(960, 540, 24);
            var at = OnScreen(rt, SeaPoints(40, 56, -56, -40, 1.0f, 9f, 1f));
            var shot = Shot(Cam, rt);
            RenderTexture.ReleaseTemporary(rt);
            Save(shot, "water-test-moat.png");
            Assert.Greater(at.Count, 50, "the lake is in view");
            var c = Mean(shot, at);
            var c32 = (Color32)new Color(c.r / 255f, c.g / 255f, c.b / 255f);
            Debug.Log($"Moat lake: {c32}, saturation {Saturation(c32):F2}");
            Assert.Greater(Saturation(c32), 0.25f, "the lake is coloured water, not grey haze");
            Assert.Greater(c.b, c.r + 15f, "and bluer than it is red");
        }

        // ---- The fog of war over the sea ----

        [UnityTest]
        public IEnumerator FoggedSeaIsDimmedWater()
        {
            yield return Begin("mock_moat", false, true);
            var t = mock.Terrain;
            var centre = new Vector3(48, t.SeaLevel, -48);
            mock.Explore(centre, 16f);
            var fog = root.World.Fog;
            fog.Update(true);
            Assert.AreEqual(1, fog.State(centre), "the lake is seen before and not in sight");
            yield return Look(centre, 36f, 72f);
            var rt = RenderTexture.GetTemporary(960, 540, 24);
            var at = OnScreen(rt, SeaPoints(43, 53, -53, -43, 1.2f, 9f, 1f));
            var fogged = Shot(Cam, rt);
            FogView.Disabled = true;
            fog.Update(true);
            yield return null;
            var lit = Shot(Cam, rt);
            RenderTexture.ReleaseTemporary(rt);
            Save(fogged, "water-test-fogged.png");
            Save(lit, "water-test-lit.png");
            Assert.Greater(at.Count, 30);
            float ratio = at.Average(p => Luma(Px(fogged, p))) / Mathf.Max(1f, at.Average(p => Luma(Px(lit, p))));
            var c = Mean(fogged, at);
            var c32 = (Color32)new Color(c.r / 255f, c.g / 255f, c.b / 255f);
            Debug.Log($"Fogged sea: {ratio:P1} of lit, colour {c32}, saturation {Saturation(c32):F2}");
            Assert.That(ratio, Is.InRange(0.45f, 0.62f), "sea seen before shows at a little over half its lit brightness");
            Assert.Greater(Saturation(c32), 0.2f, "and is still the water's colour, dimmed, not grey");
        }

        // The fog's edge over deep water stays where it lies on the water
        // whatever the camera's tilt. Taken from the sea floor it would
        // slide by the depth over the tilt's tangent.
        [UnityTest]
        public IEnumerator TheFogsEdgeOverDeepWaterHoldsStill()
        {
            yield return Begin("mock_moat", false, true);
            var t = mock.Terrain;
            // Seen before north of z = -48 across the lake, never seen south.
            mock.Explore(new Vector3(48, 0, -30), 18f);
            var fog = root.World.Fog;
            fog.Update(true);
            var edges = new List<float>();
            foreach (float pitch in new[] { 70f, 35f })
            {
                yield return Look(new Vector3(48, t.SeaLevel, -48), 34f, pitch);
                var rt = RenderTexture.GetTemporary(960, 540, 24);
                var shot = Shot(Cam, rt);
                var zs = new List<float>();
                var lumas = new List<float>();
                for (float z = -56; z <= -40; z += 0.1f)
                {
                    var at = OnScreen(rt, Enumerable.Range(0, 9).Select(k => new Vector3(44 + k, t.SeaLevel, z)));
                    if (at.Count < 5) continue;
                    zs.Add(z);
                    lumas.Add(at.Average(p => Luma(Px(shot, p))));
                }
                RenderTexture.ReleaseTemporary(rt);
                Save(shot, $"water-test-fogedge-{pitch:F0}.png");
                Assert.Greater(zs.Count, 100, "the lake crosses the view");
                float lit = lumas.Skip(lumas.Count - 20).Average(), dark = lumas.Take(20).Average();
                Assert.Greater(lit, dark + 20f, "seen before to the north, never seen to the south");
                float half = (lit + dark) / 2;
                int i = lumas.FindIndex(l => l > half);
                edges.Add(zs[i]);
                Debug.Log($"Fog edge at pitch {pitch}: z {zs[i]:F2}, dark {dark:F1}, seen before {lit:F1}");
            }
            Assert.AreEqual(edges[0], edges[1], 0.5f, "the fog's edge on the water does not move with the camera's tilt");
        }

        // ---- The look ----

        [UnityTest]
        public IEnumerator ShallowWaterIsLighterThanDeep()
        {
            yield return Begin("mock_bay", true, false);
            FogView.Disabled = true;
            root.World.Fog.Update(true);
            yield return Look(new Vector3(56, 2.5f, -64), 34f, 80f);
            var rt = RenderTexture.GetTemporary(960, 540, 24);
            var shallow = OnScreen(rt, SeaPoints(44, 56, -74, -54, 0.6f, 1.0f));
            var deep = OnScreen(rt, SeaPoints(58, 72, -74, -54, 2.0f, 9f));
            var shot = Shot(Cam, rt);
            RenderTexture.ReleaseTemporary(rt);
            Save(shot, "water-test-depth.png");
            Assert.Greater(shallow.Count, 30);
            Assert.Greater(deep.Count, 30);
            float ls = shallow.Average(p => Luma(Px(shot, p))), ld = deep.Average(p => Luma(Px(shot, p)));
            Debug.Log($"Shallows {ls:F1}, deep {ld:F1}, shallow colour {Mean(shot, shallow)}, deep colour {Mean(shot, deep)}");
            Assert.Greater(ls, ld * 1.25f, "the shallows, over the sand, are lighter than the deep");
        }

        [UnityTest]
        public IEnumerator FoamLinesTheShore()
        {
            yield return Begin("mock_bay", true, false);
            FogView.Disabled = true;
            root.World.Fog.Update(true);
            yield return Look(new Vector3(48, 2.5f, -64), 24f, 80f);
            var rt = RenderTexture.GetTemporary(960, 540, 24);
            // Just inside the water's edge, and well out from it.
            var edge = OnScreen(rt, SeaPoints(40, 56, -74, -54, 0.01f, 0.08f, 0.25f));
            var open = OnScreen(rt, SeaPoints(40, 60, -74, -54, 1.6f, 2.0f, 0.25f));
            var shot = Shot(Cam, rt);
            RenderTexture.ReleaseTemporary(rt);
            Save(shot, "water-test-foam.png");
            Assert.Greater(edge.Count, 30, "the shore is in view");
            Assert.Greater(open.Count, 30);
            bool White(Color32 c) => Mathf.Min(c.r, Mathf.Min(c.g, c.b)) > 150 && Saturation(c) < 0.22f;
            float atEdge = edge.Count(p => White(Px(shot, p))) / (float)edge.Count;
            float offshore = open.Count(p => White(Px(shot, p))) / (float)open.Count;
            Debug.Log($"Foam: {atEdge:P0} of the water's edge is white, {offshore:P0} of open water");
            Assert.Greater(atEdge, 0.3f, "foam lines the water's edge");
            Assert.Less(offshore, 0.1f, "and not the open water");
        }

        [UnityTest]
        public IEnumerator AMovingShipLeavesAWake()
        {
            yield return Begin("mock_bay", true, false);
            FogView.Disabled = true;
            root.World.Fog.Update(true);
            int boat = mock.SpawnBoat(mock.LocalPlayer, new Vector3(68, 0, -24));
            Assert.GreaterOrEqual(boat, 0);
            Assert.IsTrue(mock.Command(GameCommand.To(CommandKind.Move, boat, new Vector3(68, 0, -110))));
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until) yield return null;
            var u = UnitOf(boat);
            Assert.Less(u.Position.z, -26f, "the boat is under way, south");
            var t = mock.Terrain;
            yield return Look(new Vector3(u.Position.x, t.SeaLevel, u.Position.z + 3f), 24f, 85f);
            u = UnitOf(boat);
            var rt = RenderTexture.GetTemporary(960, 540, 24);
            var behind = new List<Vector3>();
            var aside = new List<Vector3>();
            for (float d = 2.8f; d <= 5.5f; d += 0.25f)
                for (float s = -1.6f; s <= 1.6f; s += 0.2f)
                {
                    behind.Add(new Vector3(u.Position.x + s, t.SeaLevel, u.Position.z + d));
                    aside.Add(new Vector3(u.Position.x + 6f + s * 0.5f, t.SeaLevel, u.Position.z + d));
                    aside.Add(new Vector3(u.Position.x - 6f + s * 0.5f, t.SeaLevel, u.Position.z + d));
                }
            var b = OnScreen(rt, behind);
            var a = OnScreen(rt, aside);
            var shot = Shot(Cam, rt);
            RenderTexture.ReleaseTemporary(rt);
            Save(shot, "water-test-wake.png");
            Assert.Greater(b.Count, 50);
            Assert.Greater(a.Count, 50);
            float lb = b.Average(p => Luma(Px(shot, p))), la = a.Average(p => Luma(Px(shot, p)));
            Debug.Log($"Wake: {lb:F1} behind the boat, {la:F1} beside its path");
            Assert.Greater(lb, la + 10f, "foam trails behind a ship under way");
        }

        // Open water has no strong single frequency (stripes) and does not
        // repeat itself (tiling): seen from above, and looking toward the
        // sun, where the glint on the waves shows any pattern best.
        [UnityTest]
        public IEnumerator TheOpenSeaHasNoStripesOrTiling()
        {
            yield return Begin("mock_bay", true, false);
            FogView.Disabled = true;
            root.World.Fog.Update(true);
            var t = mock.Terrain;
            var centre = new Vector3(67, t.SeaLevel, -64);
            var views = new[] { (name: "above", pitch: 88f, yaw: 0f), (name: "sunward", pitch: 55f, yaw: -30f) };
            foreach (var v in views)
            {
                yield return Look(centre, 30f, v.pitch, v.yaw);
                var rt = RenderTexture.GetTemporary(960, 540, 24);
                var mid = OnScreen(rt, new[] { centre });
                var shot = Shot(Cam, rt);
                RenderTexture.ReleaseTemporary(rt);
                Save(shot, "water-test-open-" + v.name + ".png");
                Assert.AreEqual(1, mid.Count);
                Spectrum(shot, mid[0], 256, out double stripe, out double repeat, out double spread);
                Debug.Log($"Open sea from {v.name}: the strongest wave holds {stripe:P1} of the variation, it repeats at most {repeat:F2}, luma spread {spread:F1}");
                Assert.Less(stripe, 0.08, "no single wave dominates the open sea (stripes), seen from " + v.name);
                Assert.Less(repeat, 0.5, "the open sea does not repeat itself (tiling), seen from " + v.name);
            }
        }

        // For an n by n patch of luma around a point: the share of its
        // variation in the strongest wave (windowed), and the highest
        // correlation it has with itself shifted past its central lobe (and
        // 24 pixels at least). A smooth random sea falls away from itself and
        // stays away, while stripes and tiles come back.
        static void Spectrum(Texture2D shot, Vector2 at, int n, out double stripe, out double repeat, out double spread)
        {
            int x0 = (int)at.x - n / 2, y0 = (int)at.y - n / 2;
            var luma = new double[n * n];
            double mean = 0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++) mean += luma[y * n + x] = Luma(shot.GetPixel(x0 + x, y0 + y));
            mean /= n * n;
            double var = 0;
            for (int i = 0; i < n * n; i++) { luma[i] -= mean; var += luma[i] * luma[i]; }
            spread = System.Math.Sqrt(var / (n * n));
            var re = new double[n * n];
            var im = new double[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    double w = (0.5 - 0.5 * System.Math.Cos(2 * System.Math.PI * x / (n - 1))) * (0.5 - 0.5 * System.Math.Cos(2 * System.Math.PI * y / (n - 1)));
                    re[y * n + x] = luma[y * n + x] * w;
                }
            Fft2(re, im, n, false);
            // Waves with fewer than three crests across the patch are the
            // view's own shading, lighter far off and round the sun's glint,
            // not stripes, so they are left out.
            double total = 0, peak = 0;
            for (int i = 1; i < n * n; i++)
            {
                int ky = i / n, kx = i % n;
                if (ky > n / 2) ky -= n;
                if (kx > n / 2) kx -= n;
                if (kx * kx + ky * ky < 9) continue;
                double p = re[i] * re[i] + im[i] * im[i];
                total += p;
                peak = System.Math.Max(peak, p);
            }
            // Each wave shows in a mirrored pair of bins.
            stripe = 2 * peak / System.Math.Max(total, 1e-9);
            // Autocorrelation, circular and unwindowed, as the inverse
            // transform of the power.
            re = (double[])luma.Clone();
            im = new double[n * n];
            Fft2(re, im, n, false);
            for (int i = 0; i < n * n; i++) { re[i] = re[i] * re[i] + im[i] * im[i]; im[i] = 0; }
            Fft2(re, im, n, true);
            double zero = System.Math.Max(re[0], 1e-9);
            // The central lobe ends where the mean over a ring of shifts
            // first falls under 0.3.
            var ringSum = new double[n];
            var ringCount = new int[n];
            for (int dy = -n / 2; dy < n / 2; dy++)
                for (int dx = -n / 2; dx < n / 2; dx++)
                {
                    int r = (int)System.Math.Round(System.Math.Sqrt(dx * dx + dy * dy));
                    if (r >= n) continue;
                    ringSum[r] += re[((dy + n) % n) * n + (dx + n) % n] / zero;
                    ringCount[r]++;
                }
            int lobe = n / 2;
            for (int r = 1; r < n / 2; r++)
                if (ringCount[r] > 0 && ringSum[r] / ringCount[r] < 0.3) { lobe = r; break; }
            int past = System.Math.Max(24, 2 * lobe);
            repeat = 0;
            for (int dy = -n / 2; dy < n / 2; dy++)
                for (int dx = -n / 2; dx < n / 2; dx++)
                {
                    if (dx * dx + dy * dy < past * past) continue;
                    repeat = System.Math.Max(repeat, re[((dy + n) % n) * n + (dx + n) % n] / zero);
                }
        }

        // Rows then columns, in place, n a power of two.
        static void Fft2(double[] re, double[] im, int n, bool inverse)
        {
            var r = new double[n];
            var i = new double[n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++) { r[x] = re[y * n + x]; i[x] = im[y * n + x]; }
                Fft(r, i, inverse);
                for (int x = 0; x < n; x++) { re[y * n + x] = r[x]; im[y * n + x] = i[x]; }
            }
            for (int x = 0; x < n; x++)
            {
                for (int y = 0; y < n; y++) { r[y] = re[y * n + x]; i[y] = im[y * n + x]; }
                Fft(r, i, inverse);
                for (int y = 0; y < n; y++) { re[y * n + x] = r[y]; im[y * n + x] = i[y]; }
            }
        }

        static void Fft(double[] re, double[] im, bool inverse)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double a = 2 * System.Math.PI / len * (inverse ? 1 : -1);
                double wr = System.Math.Cos(a), wi = System.Math.Sin(a);
                for (int s = 0; s < n; s += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int p = s + k, q = s + k + len / 2;
                        double tr = re[q] * cr - im[q] * ci, ti = re[q] * ci + im[q] * cr;
                        re[q] = re[p] - tr; im[q] = im[p] - ti;
                        re[p] += tr; im[p] += ti;
                        double nr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = nr;
                    }
                }
            }
        }

        // What the sea costs a frame at 1920 by 1080 with water over most of
        // the screen: frames drawn with it and without it, alternately.
        [UnityTest]
        public IEnumerator TheSeaCostsLittle()
        {
            yield return Begin("mock_bay", true, false);
            FogView.Disabled = true;
            root.World.Fog.Update(true);
            var t = mock.Terrain;
            yield return Look(new Vector3(66, t.SeaLevel, -64), 22f, 60f);
            var cam = Cam;
            var sea = root.World.Terrain.Water;
            Assert.IsNotNull(sea);
            var data = Looks.Urp != null ? cam.GetUniversalAdditionalCameraData() : null;
            var colour = data != null ? data.requiresColorOption : CameraOverrideOption.UsePipelineSettings;
            var rt = RenderTexture.GetTemporary(1920, 1080, 24);
            var probe = new Texture2D(1, 1, TextureFormat.RGB24, false);
            double Batch(bool with, int frames)
            {
                sea.SetActive(with);
                if (data != null) data.requiresColorOption = with ? colour : CameraOverrideOption.Off;
                var old = cam.targetTexture;
                cam.targetTexture = rt;
                var clock = Stopwatch.StartNew();
                for (int f = 0; f < frames; f++)
                {
                    cam.Render();
                    RenderTexture.active = rt;
                    probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                    RenderTexture.active = null;
                }
                clock.Stop();
                cam.targetTexture = old;
                return clock.Elapsed.TotalMilliseconds / frames;
            }
            Batch(true, 10);
            Batch(false, 10);
            var with = new List<double>();
            var without = new List<double>();
            for (int round = 0; round < 6; round++)
            {
                with.Add(Batch(true, 15));
                without.Add(Batch(false, 15));
                yield return null;
            }
            sea.SetActive(true);
            if (data != null) data.requiresColorOption = colour;
            RenderTexture.ReleaseTemporary(rt);
            Object.Destroy(probe);
            with.Sort();
            without.Sort();
            double w = with[with.Count / 2], wo = without[without.Count / 2];
            string line = $"Sea cost at 1920x1080: {w:F2} ms a frame with the sea, {wo:F2} ms without, {w - wo:F2} ms for the sea ({SystemInfo.graphicsDeviceName})";
            Debug.Log(line);
            string dir = System.Environment.GetEnvironmentVariable("OKU_WATER_TEST_DIR");
            if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "water-cost.txt"), line); }
            Assert.Less(w - wo, 6.0, "the sea stays within a few milliseconds");
        }
    }
}
