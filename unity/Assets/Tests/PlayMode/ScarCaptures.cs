// ScarCaptures.cs - pictures of the scars for the owner, drawn offscreen on
// the mock. Runs only when OKU_SCARS_DIR names a folder, and OKU_SCARS_PARTS
// picks from "field,kinds,grounds,units,cost".
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class ScarCaptures
    {
        static string Env(string k) => System.Environment.GetEnvironmentVariable(k);
        static int W => int.TryParse(Env("OKU_CAPTURE_W"), out int w) ? w : 1600;
        static int H => int.TryParse(Env("OKU_CAPTURE_H"), out int h) ? h : 900;

        GameRoot root;
        MockBackend mock;
        readonly List<string> log = new List<string>();

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            FxQuality.Use(EffectsQuality.High);
        }

        [UnityTest, Timeout(3600000)]
        public IEnumerator CaptureScars()
        {
            string dir = Env("OKU_SCARS_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_SCARS_DIR to capture the scars");
            Directory.CreateDirectory(dir);
            string parts = Env("OKU_SCARS_PARTS") ?? "field,kinds,grounds,units,cost";
            if (parts.Contains("field")) yield return Field(dir);
            if (parts.Contains("kinds")) yield return Kinds(dir);
            if (parts.Contains("grounds")) yield return Grounds(dir);
            if (parts.Contains("units")) yield return Units(dir);
            if (parts.Contains("cost")) yield return Cost(dir);
            File.WriteAllLines(Path.Combine(dir, "log.txt"), log);
        }

        IEnumerator Boot(string map, EffectsQuality level = EffectsQuality.High, int extra = 0)
        {
            if (root != null) { Object.Destroy(root.gameObject); root = null; yield return null; yield return null; }
            mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f, ExtraSoldiers = extra };
            root = GameRoot.Boot(mock);
            yield return null;
            var cam = Camera.main;
            if (cam == null) { cam = new GameObject("Main Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; }
            root.Options.EffectsQuality = level;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = map;
            root.Setup.Seed = 7;
            root.Setup.LineOfSight = false;
            root.Setup.MapRevealed = true;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 120f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, map + ": " + root.LastError);
            root.Options.GameSpeed = 0;
            root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            var hud = root.Screens.Screen("Hud");
            if (hud != null) hud.SetActive(false);
        }

        // A low sun from the south west, so dips and rims throw shade.
        void LowSun(float elevation = 22f)
        {
            var sun = root.World.Atmosphere.Sun;
            if (sun != null) sun.transform.rotation = Quaternion.Euler(elevation, 40f, 0f);
        }

        IEnumerator Shoot(string dir, string name, Vector3 focus, float distance, float pitch, float yaw = 0f)
        {
            var gc = root.World.Camera;
            var cam = gc.GetComponent<Camera>();
            gc.focus = new Vector3(focus.x, mock.GroundHeight(focus.x, focus.z), focus.z);
            gc.pitch = pitch;
            gc.yaw = yaw;
            gc.maxDistance = Mathf.Max(gc.maxDistance, distance);
            gc.Zoom(distance);
            for (int i = 0; i < 4; i++) yield return null;
            var hdr = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            var rt = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var old = cam.targetTexture;
            cam.rect = new Rect(0, 0, 1, 1);
            cam.aspect = W / (float)H;
            cam.targetTexture = hdr;
            cam.Render();
            Graphics.Blit(hdr, rt);
            RenderTexture.ReleaseTemporary(hdr);
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = old;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            log.Add($"{name}: focus {focus.x:0.0},{focus.z:0.0} distance {distance} pitch {pitch} yaw {yaw}");
        }

        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        // Dry, fairly level ground near the map's middle.
        Vector3 Middle()
        {
            var c = mock.StageCentre;
            float sea = mock.Terrain.SeaLevel;
            for (float r = 0f; r < 60f; r += 3f)
                for (int a = 0; a < (r == 0f ? 1 : 16); a++)
                {
                    float x = c.x + Mathf.Cos(a * Mathf.PI / 8f) * r, z = c.z + Mathf.Sin(a * Mathf.PI / 8f) * r;
                    float h = mock.GroundHeight(x, z);
                    float slope = Mathf.Abs(mock.GroundHeight(x + 4f, z) - mock.GroundHeight(x - 4f, z)) + Mathf.Abs(mock.GroundHeight(x, z + 4f) - mock.GroundHeight(x, z - 4f));
                    if ((sea < 0f || h > sea + 0.8f) && slope < 1.6f) return new Vector3(x, h, z);
                }
            return new Vector3(c.x, mock.GroundHeight(c.x, c.z), c.z);
        }

        // Three minutes of a fight: the staged rows firing in turn at the
        // middle, and volleys of every kind along a front across the field,
        // stamped as their blasts would be.
        IEnumerator Field(string dir)
        {
            yield return Boot("mock_hollow", EffectsQuality.High, 4);
            LowSun();
            var mid = Middle();
            var size = mock.Terrain.Size;
            yield return Shoot(dir, "field-before", mid, 95f, 58f);
            yield return Shoot(dir, "field-before-low", mid + new Vector3(0f, 0f, -6f), 42f, 30f, 20f);
            var scars = root.World.Scars;
            var rng = new System.Random(11);
            string[] rows = { "ground", "ranged", "fire", "magic", "magic2", "bolts" };
            var kinds = new (ScarKind kind, float radius, float share)[]
            {
                (ScarKind.Gunpowder, 2.8f, 0.36f), (ScarKind.Siege, 3.1f, 0.14f), (ScarKind.Fire, 1.6f, 0.14f), (ScarKind.Lightning, 0f, 0.08f),
                (ScarKind.Frost, 2f, 0.07f), (ScarKind.Dark, 1.6f, 0.05f), (ScarKind.Water, 1.6f, 0.05f), (ScarKind.Holy, 2f, 0.03f),
                (ScarKind.Impact, 4.7f, 0.03f), (ScarKind.Breath, 1.6f, 0.05f),
            };
            int volleys = 0;
            for (int second = 0; second < 180; second++)
            {
                if (second % 30 == 0) mock.StageFx(rows[second / 30 % rows.Length], 2.5f);
                for (int v = 0; v < 3; v++)
                {
                    float pick = (float)rng.NextDouble(), sum = 0f;
                    var k = kinds[0];
                    foreach (var row in kinds) { sum += row.share; if (pick <= sum) { k = row; break; } }
                    float x = size.x * (0.18f + 0.64f * (float)rng.NextDouble());
                    float z = mid.z + Gauss(rng) * 7f + (k.kind == ScarKind.Siege || k.kind == ScarKind.Impact ? 4f : 0f);
                    var dir2 = new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f);
                    scars.Mark(k.kind, new Vector3(x, mock.GroundHeight(x, z), z), k.radius, dir2);
                    volleys++;
                }
                mock.Advance(MockBackend.Tps / 3);
                yield return null;
                mock.Advance(MockBackend.Tps / 3);
                yield return null;
                mock.Advance(MockBackend.Tps - 2 * (MockBackend.Tps / 3));
                yield return null;
            }
            yield return Frames(30);
            LowSun();
            log.Add($"field: 3 minutes on mock_hollow, {volleys} scripted volleys and {scars.BlastsSeen} blasts from the mock, " +
                    $"{scars.Stamped} stamps, {scars.Merged} merged, {scars.Dropped} gave way, {root.World.Terrain.RefinedRegions} regions dented, " +
                    $"{scars.TexW}x{scars.TexH} texels, {scars.Bytes / 1048576f:0.0} MB");
            yield return Shoot(dir, "field-after", mid, 95f, 58f);
            yield return Shoot(dir, "field-after-low", mid + new Vector3(0f, 0f, -6f), 42f, 30f, 20f);
            yield return Shoot(dir, "field-after-close", mid + new Vector3(-14f, 0f, -2f), 20f, 38f, 30f);
        }

        static float Gauss(System.Random r)
        {
            double u = 1.0 - r.NextDouble(), v = r.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u)) * System.Math.Cos(2.0 * System.Math.PI * v));
        }

        // Each kind's mark alone on clean ground, close up.
        IEnumerator Kinds(string dir)
        {
            yield return Boot("mock_hollow");
            LowSun(34f);
            var mid = Middle();
            var scars = root.World.Scars;
            var kinds = new (ScarKind kind, float radius, string note)[]
            {
                (ScarKind.Gunpowder, 2.8125f, "a cannon ball, ARACAN 1"),
                (ScarKind.Siege, 3.125f, "a catapult's stone, ARAPULT 1"),
                (ScarKind.Impact, 4.6875f, "a volcanic blast, TARNECRO 2"),
                (ScarKind.Fire, 3.125f, "a flame strike, TARHEL 1"),
                (ScarKind.Breath, 1.5625f, "a dragon's breath, ARADRAG 1"),
                (ScarKind.Lightning, 0f, "a lightning bolt, ARAKING 1"),
                (ScarKind.Frost, 2f, "a frost spell"),
                (ScarKind.Dark, 1.6f, "dark magic"),
                (ScarKind.Water, 1.5625f, "a water ball, VERMAGE 2"),
                (ScarKind.Holy, 2f, "holy light"),
                (ScarKind.Earth, 12.5f, "an earthquake"),
                (ScarKind.Dust, 0.75f, "a dust puff, CREGATL 1"),
            };
            for (int i = 0; i < kinds.Length; i++)
            {
                var at = mid + new Vector3((i % 4 - 1.5f) * 22f, 0f, (i / 4 - 1f) * 22f);
                at.y = mock.GroundHeight(at.x, at.z);
                scars.Mark(kinds[i].kind, at, kinds[i].radius, Vector3.right);
            }
            yield return Frames(12);
            for (int i = 0; i < kinds.Length; i++)
            {
                var at = mid + new Vector3((i % 4 - 1.5f) * 22f, 0f, (i / 4 - 1f) * 22f);
                var (kind, radius, note) = kinds[i];
                float dist = kind == ScarKind.Earth ? 26f : kind == ScarKind.Impact ? 17f : 13f;
                yield return Shoot(dir, $"kind-{i + 1:00}-{kind.ToString().ToLowerInvariant()}", at, dist, 48f, 25f);
                var s = ScarStamps.Make(kind, at, radius, Vector3.right);
                log.Add($"  {kind}: {note}, radius {radius:0.00} units, reach {s.Extent:0.0}, dip {s.Depth:0.0} px over {s.Dent:0.00} units, rim {s.Rim:0.0} px, " +
                        $"char {s.Char:0.00} soil {s.Soil:0.00} blight {s.Blight:0.00} stone {s.Stone:0.00} cracks {s.Crack:0.00}, " +
                        $"frost {s.Frost:0} s wet {s.Wet:0} s holy {s.Holy:0} s heat {s.Heat:0} s");
            }
            yield return Shoot(dir, "kinds-all", mid, 80f, 62f);
        }

        // A cannon's crater, a catapult's pit and fire's char on four grounds.
        IEnumerator Grounds(string dir)
        {
            foreach (var (map, name) in new[] { ("mock_hollow", "grass"), ("mock_dunes", "sand"), ("mock_frost", "snow"), ("mock_fens", "swamp") })
            {
                yield return Boot(map);
                LowSun(30f);
                var mid = Middle();
                var scars = root.World.Scars;
                var cannon = mid + new Vector3(-3f, 0f, 0f);
                scars.Mark(ScarKind.Gunpowder, cannon, 2.8125f, Vector3.right);
                scars.Mark(ScarKind.Siege, mid + new Vector3(4f, 0f, 2f), 3.125f, Vector3.right);
                scars.Mark(ScarKind.Fire, mid + new Vector3(3f, 0f, -5f), 2f, Vector3.right);
                scars.Mark(ScarKind.Gunpowder, mid + new Vector3(-1f, 0f, -6f), 2.8125f, Vector3.right);
                yield return Frames(12);
                bool wet = mock.Terrain.SeaLevel >= 0f && mock.GroundHeight(mid.x, mid.z) < mock.Terrain.SeaLevel;
                log.Add($"ground {name}: {map}, climate {scars.Climate}, middle {(wet ? "under water" : "dry")}, cannon dips {ScarMap.GroundOffset(cannon.x, cannon.z) * 16f:0.0} px");
                yield return Shoot(dir, $"ground-{name}", mid + new Vector3(0f, 0f, -1f), 16f, 40f, 25f);
            }
        }

        // The staged row's knights, struck by cannon, sitting in the craters
        // the balls dug, then the same with the dips off for comparison.
        IEnumerator Units(string dir)
        {
            yield return Boot("mock_hollow");
            LowSun(26f);
            mock.StageFx("ranged", 2.5f);
            BlastEvent hit = default;
            var buf = new BlastEvent[1024];
            for (int t = 0; t < 900; t += 2)
            {
                mock.Advance(2);
                yield return null;
                int n = mock.ReadBlasts(0, buf), struck = 0;
                for (int i = 0; i < n; i++)
                    if (buf[i].Weapon != null && buf[i].Weapon.Name == "ARACAN 1" && buf[i].Unit >= 0) { hit = buf[i]; struck++; }
                if (struck >= 3) break;
            }
            Assert.GreaterOrEqual(hit.Unit, 0, "the cannon struck a knight");
            // The row stops firing, so the picture is still.
            mock.StageFx("ranged", 100000f);
            yield return Frames(20);
            var at = hit.Position;
            log.Add($"units: knight {hit.Unit} sits {ScarMap.GroundOffset(at.x, at.z) * 16f:0.0} px down in its crater");
            yield return Shoot(dir, "units-in-crater", at + new Vector3(-0.5f, 0f, -0.5f), 9f, 24f, 30f);
            yield return Shoot(dir, "units-in-crater-above", at, 12f, 52f, 30f);
            FxQuality.Use(EffectsQuality.Medium);
            yield return Frames(6);
            yield return Shoot(dir, "units-in-crater-dips-off", at + new Vector3(-0.5f, 0f, -0.5f), 9f, 24f, 30f);
            FxQuality.Use(EffectsQuality.High);
            yield return Frames(6);
        }

        // What the scar map costs a frame: its own update with blasts coming,
        // and the camera's frame with scars on and off at 2560 by 1440.
        IEnumerator Cost(string dir)
        {
            yield return Boot("mock_marches", EffectsQuality.High, 6);
            var scars = root.World.Scars;
            var mid = Middle();
            var rng = new System.Random(3);
            var size = mock.Terrain.Size;
            double sum = 0, worst = 0;
            int frames = 0;
            string[] rows = { "ground", "ranged", "fire", "magic", "magic2", "bolts" };
            for (int f = 0; f < 900; f++)
            {
                if (f % 150 == 0) mock.StageFx(rows[f / 150 % rows.Length], 1f);
                for (int v = 0; v < 2; v++)
                {
                    float x = size.x * (float)rng.NextDouble(), z = -size.y * (float)rng.NextDouble();
                    scars.Mark(v == 0 ? ScarKind.Gunpowder : ScarKind.Fire, new Vector3(x, mock.GroundHeight(x, z), z), 2.8f);
                }
                mock.Advance(1);
                yield return null;
                sum += scars.LastMs;
                worst = System.Math.Max(worst, scars.LastMs);
                frames++;
            }
            log.Add($"cost: mock_marches, 8 seats, {frames} frames with staged rows and 2 marks a frame over the map: " +
                    $"scar update {sum / frames:0.000} ms a frame on average, {worst:0.00} ms at worst; {scars.Stamped} stamps, " +
                    $"{root.World.Terrain.RefinedRegions} regions dented, {scars.Bytes / 1048576f:0.0} MB");
            // The GPU: the same view drawn with the scar map's globals on and off.
            var gc = root.World.Camera;
            var cam = gc.GetComponent<Camera>();
            gc.focus = mid;
            gc.pitch = 45f;
            gc.Zoom(60f);
            yield return Frames(4);
            var target = RenderTexture.GetTemporary(2560, 1440, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            cam.targetTexture = target;
            double on = TimeFrames(cam, target, 120), off;
            var keep = Shader.GetGlobalVector("_OkuScarOn");
            Shader.SetGlobalVector("_OkuScarOn", Vector4.zero);
            off = TimeFrames(cam, target, 120);
            Shader.SetGlobalVector("_OkuScarOn", keep);
            on = System.Math.Min(on, TimeFrames(cam, target, 120));
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            log.Add($"cost: a 2560x1440 frame of the scarred field takes {on:0.00} ms with the scars and {off:0.00} ms without, " +
                    $"{on - off:0.00} ms for the scars (CPU and GPU together, waited on)");
        }

        // Milliseconds a frame for n frames drawn back to back and waited on.
        static double TimeFrames(Camera cam, RenderTexture target, int n)
        {
            var px = new Texture2D(1, 1, TextureFormat.RGBAHalf, false);
            void Wait()
            {
                RenderTexture.active = target;
                px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                px.Apply();
                RenderTexture.active = null;
            }
            cam.Render();
            Wait();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < n; i++) cam.Render();
            Wait();
            double ms = clock.Elapsed.TotalMilliseconds / n;
            Object.Destroy(px);
            return ms;
        }
    }
}
