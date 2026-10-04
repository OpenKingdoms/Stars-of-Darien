// FxBlastPlayTests.cs - the blasts' parts in a running game on the mock:
// Forward+ lets many blast lights light the ground at once, and an
// eight-seat battle heavy with blasts keeps the parts inside the frame
// budget at High, the main thread's time and the graphics card's.
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class FxBlastPlayTests
    {
        static IEnumerator Start(MockBackend mock, System.Action<GameRoot> ready, string map = "mock_highlands", int seats = 2)
        {
            var root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = map;
            root.Setup.LineOfSight = false;
            root.Setup.MapRevealed = true;
            if (seats > 2)
            {
                string[] sides = { "ARAMON", "TAROS", "VERUNA", "ZHON", "CREON", "TAROS", "VERUNA", "ARAMON" };
                root.Setup.Seats.Clear();
                for (int i = 0; i < seats; i++)
                    root.Setup.Seats.Add(new SeatSetup { Kind = i == 0 ? SeatKind.Human : SeatKind.Computer, Side = sides[i % sides.Length], Colour = i, Team = SeatTeam.Alone });
            }
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 120f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, "the game loaded: " + root.LastError);
            root.Options.GameSpeed = 0;
            root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            var hud = root.Screens.Screen("Hud");
            if (hud != null) hud.SetActive(false);
            ready(root);
        }

        // Flat dry ground near a point, as the mock's maps keep lakes in the middle.
        static Vector3 Dry(IGameBackend b, Vector3 near, float across)
        {
            float sea = b.Terrain.SeaLevel;
            for (float r = 0f; r < 80f; r += 2f)
                for (int a = 0; a < 16; a++)
                {
                    var p = near + new Vector3(Mathf.Cos(a * Mathf.PI / 8f) * r, 0f, Mathf.Sin(a * Mathf.PI / 8f) * r);
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int k = 0; k < 9; k++)
                    {
                        var q = p + new Vector3((k % 3 - 1) * across * 0.5f, 0f, (k / 3 - 1) * across * 0.5f);
                        float g = b.GroundHeight(q.x, q.z);
                        lo = Mathf.Min(lo, g);
                        hi = Mathf.Max(hi, g);
                    }
                    if (lo > sea + 0.5f && hi - lo < across * 0.25f) { p.y = b.GroundHeight(p.x, p.z); return p; }
                }
            Assert.Fail("no flat dry ground");
            return near;
        }

        static void Frame(GameRoot root, Vector3 at, float distance)
        {
            var cam = root.World.Camera;
            cam.focus = at;
            cam.pitch = GameCamera.ClassicPitch;
            cam.yaw = 0;
            cam.Zoom(distance);
        }

        static Color[] Shot(Camera cam, int w, int h)
        {
            var hdr = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var old = cam.targetTexture;
            cam.targetTexture = hdr;
            cam.Render();
            Graphics.Blit(hdr, rt);
            RenderTexture.ReleaseTemporary(hdr);
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = old;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels();
            Object.Destroy(tex);
            return px;
        }

        static float Luma(Color[] px, int w, int h, Vector3 viewport, int half)
        {
            int cx = Mathf.RoundToInt(viewport.x * w), cy = Mathf.RoundToInt(viewport.y * h), n = 0;
            float sum = 0f;
            for (int y = cy - half; y <= cy + half; y++)
                for (int x = cx - half; x <= cx + half; x++)
                {
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    var c = px[y * w + x];
                    sum += 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
                    n++;
                }
            return n > 0 ? sum / n : 0f;
        }

        // Under Forward+ every light in view lights each pixel, so twelve
        // blasts' lights over one patch of ground all show. The old Forward
        // path lit an object by four at most.
        [UnityTest]
        public IEnumerator MoreThanFourBlastLightsLightTheGroundAtOnce()
        {
            var before = FxQuality.Current.Level;
            FxQuality.Use(EffectsQuality.High);
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            GameRoot root = null;
            yield return Start(mock, r => root = r);
            var lights = new FxLights();
            try
            {
                var at = Dry(mock, mock.StageCentre, 16f);
                Frame(root, at, 34f);
                for (int i = 0; i < 5; i++) yield return null;
                var cam = Camera.main;
                cam.rect = new Rect(0, 0, 1, 1);
                const int w = 640, h = 360;
                // Points are found on the picture as it is drawn, at its own shape.
                cam.aspect = w / (float)h;
                var spots = new List<Vector3>();
                for (int i = 0; i < 12; i++)
                {
                    var p = at + new Vector3((i % 4 - 1.5f) * 3.6f, 0f, (i / 4 - 1f) * 3.6f);
                    p.y = mock.GroundHeight(p.x, p.z);
                    spots.Add(p);
                }
                lights.Begin();
                lights.Commit(at, 40f, 1f);
                var dark = Shot(cam, w, h);
                lights.Begin();
                for (int i = 0; i < spots.Count; i++) lights.Flash(spots[i] + Vector3.up * 1.4f, Color.white, 1.7f, 6f, 9000 + i);
                lights.Commit(at, 40f, 1f);
                Assert.AreEqual(spots.Count, lights.Lit, "twelve lights on");
                var lit = Shot(cam, w, h);
                var gains = new List<float>();
                foreach (var p in spots)
                {
                    var v = cam.WorldToViewportPoint(p);
                    gains.Add(Luma(lit, w, h, v, 3) - Luma(dark, w, h, v, 3));
                }
                cam.ResetAspect();
                Debug.Log("Forward+: each light's gain on the ground under it: " + string.Join(", ", gains.ConvertAll(g => g.ToString("0.000"))));
                for (int i = 0; i < gains.Count; i++) Assert.Greater(gains[i], 0.03f, $"light {i} lights the ground under it");
            }
            finally
            {
                lights.Dispose();
                Object.Destroy(root.gameObject);
                FxQuality.Use(before);
            }
        }

        static double Median(List<double> xs)
        {
            if (xs.Count == 0) return 0;
            xs.Sort();
            return xs[xs.Count / 2];
        }

        static double Percentile(List<double> xs, float p)
        {
            if (xs.Count == 0) return 0;
            xs.Sort();
            return xs[Mathf.Clamp(Mathf.RoundToInt((xs.Count - 1) * p), 0, xs.Count - 1)];
        }

        // An eight-seat battle on the mock's widest map with a crowd on each
        // side, blasts of every kind landing in view several times a second,
        // drawn at 1440p. The parts' own main thread time, and the time to
        // draw the frame and wait for the graphics card to finish it, with
        // the parts on against off.
        [UnityTest, Timeout(600000)]
        public IEnumerator TheBudgetHoldsInAnEightSeatBattle()
        {
            var before = FxQuality.Current.Level;
            FxQuality.Use(EffectsQuality.High);
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f, ExtraSoldiers = 120 };
            GameRoot root = null;
            yield return Start(mock, r => root = r, "mock_marches", 8);
            var rt = new RenderTexture(2560, 1440, 24, RenderTextureFormat.DefaultHDR);
            var cam = Camera.main;
            try
            {
                var fx = root.World.Effects;
                var at = Dry(mock, mock.StageCentre, 40f);
                Frame(root, at, 70f);
                cam.targetTexture = rt;
                // Drawn by hand each frame and waited for, so the time is the card's too.
                cam.enabled = false;
                string[] guns = { "ARACAN 1", "ARAPULT 1", "TARDRAG 2", "ARAKING 1", "VERMAGE 1", "ARAPRIES 2", "ARADRAG 1", "VERPULT 1", "TARNECRO 2", "ARACAN 1" };
                int frames = 0;
                var split = new double[3];
                // The graphics card's own time for the camera and its passes, where URP reports it.
                var names = new List<string>();
                UnityEngine.Profiling.Sampler.GetNames(names);
                var watched = new List<(string name, UnityEngine.Profiling.Recorder rec)>();
                foreach (var n in names)
                    if (n.Contains("RenderSingleCameraInternal: Main Camera") || n == "DrawOpaqueObjects" || n == "DrawTransparentObjects" || n.Contains("MainLightShadow") || n.Contains("Bloom"))
                    {
                        var r = UnityEngine.Profiling.Recorder.Get(n);
                        r.enabled = true;
                        watched.Add((n, r));
                    }
                var gpuSums = new double[2, 64];

                // One phase of volleys from the same seed, the parts on or off.
                IEnumerator Phase(bool parts, List<double> cpu, List<double> gpu, int[] most)
                {
                    fx.Blasts = parts;
                    fx.Clear();
                    var rng = new System.Random(11);
                    for (int f = 0; f < 300; f++)
                    {
                        if (f % 12 == 0)
                            for (int i = 0; i < guns.Length; i++)
                            {
                                var to = at + new Vector3((float)rng.NextDouble() * 50f - 25f, 0f, (float)rng.NextDouble() * 30f - 15f);
                                to.y = mock.GroundHeight(to.x, to.z);
                                mock.FireFx(guns[i], to + new Vector3(0f, 1f, -10f), to);
                            }
                        mock.Advance(1);
                        yield return null;
                        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                        cam.Render();
                        AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
                        double drawn = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                        frames++;
                        if (f < 60) continue;
                        cpu.Add(fx.BlastMs);
                        gpu.Add(drawn);
                        for (int w = 0; w < watched.Count && w < 64; w++) gpuSums[parts ? 1 : 0, w] += watched[w].rec.gpuElapsedNanoseconds / 1e6;
                        if (parts)
                        {
                            var pp = fx.Particles;
                            split[0] += pp.StepMs;
                            split[1] += pp.ChunkMs;
                            split[2] += pp.SendMs;
                        }
                        most[0] = Mathf.Max(most[0], fx.ParticleCount);
                        most[1] = Mathf.Max(most[1], fx.DebrisCount);
                        most[2] = Mathf.Max(most[2], fx.LightsLit);
                        most[3] = Mathf.Max(most[3], fx.RingCount);
                    }
                }

                var offCpu = new List<double>();
                var offGpu = new List<double>();
                var onCpu = new List<double>();
                var onGpu = new List<double>();
                var most = new int[4];
                var none = new int[4];
                yield return Phase(false, offCpu, offGpu, none);
                yield return Phase(true, onCpu, onGpu, most);
                var units = new UnitState[4096];
                int unitCount = mock.ReadUnits(units);
                double cpuMedian = Median(onCpu), cpuWorst = Percentile(onCpu, 0.95f);
                double gpuOn = Median(onGpu), gpuOff = Median(offGpu);
                Debug.Log($"Budget: 8 seats, {unitCount} units, {fx.BlastsPlayed} blasts given a look over {frames / 2} frames, at most {most[0]} particles, {most[1]} debris, {most[2]} lights, {most[3]} rings; " +
                          $"parts on the main thread median {cpuMedian:0.00} ms, 95th {cpuWorst:0.00} ms " +
                          $"(particles {split[0] / onCpu.Count:0.00}, debris {split[1] / onCpu.Count:0.00}, sorting and sending {split[2] / onCpu.Count:0.00}); " +
                          $"a 2560x1440 frame drawn and waited for: median {gpuOn:0.00} ms with the parts, {gpuOff:0.00} ms without, {gpuOn - gpuOff:0.00} ms for them");
                var card = new System.Text.StringBuilder();
                double passes = 0;
                bool reported = false;
                for (int w = 0; w < watched.Count && w < 64; w++)
                {
                    if (gpuSums[0, w] + gpuSums[1, w] <= 0) continue;
                    double without = gpuSums[0, w] / offCpu.Count, with = gpuSums[1, w] / onCpu.Count;
                    card.Append($" {watched[w].name}: {without:0.00} ms without, {with:0.00} ms with;");
                    // The passes the parts draw in or light, each counted once.
                    if (watched[w].name == "DrawOpaqueObjects" || watched[w].name == "DrawTransparentObjects") { passes += with - without; reported = true; }
                }
                Debug.Log("Budget, the graphics card's time:" + (card.Length > 0 ? card.ToString() : " not reported") + (reported ? $" the parts add {passes:0.00} ms to the opaque and transparent passes" : ""));
                Assert.Greater(fx.BlastsPlayed, 200, "a battle heavy with blasts");
                Assert.LessOrEqual(cpuMedian, 2.0, "the parts' main thread time at High");
                // The card's own time where URP reports it, else the frame drawn and waited for.
                Assert.LessOrEqual(reported ? passes : gpuOn - gpuOff, 2.5, "the parts' graphics time at High");
            }
            finally
            {
                cam.enabled = true;
                cam.targetTexture = null;
                rt.Release();
                Object.Destroy(rt);
                Object.Destroy(root.gameObject);
                FxQuality.Use(before);
            }
        }
    }
}
