// FireCaptures.cs - pictures of fire and magic on scenery, rendered
// offscreen on the mock with the game's own tree, rock, wall and hut
// models: a forest fire spreading with the wind over a minute, rain on a
// fire, and each kind of magic on scenery. The camera holds still. Runs only
// when OKU_FIRE_DIR names a folder; OKU_FIRE_SCENES picks some.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class FireCaptures
    {
        static string Env(string k) => System.Environment.GetEnvironmentVariable(k);
        const int W = 1280, H = 720;

        GameRoot root;
        MockBackend mock;
        readonly List<string> log = new List<string>();

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }

        [UnityTest, Timeout(3600000)]
        public IEnumerator CaptureFireAndMagic()
        {
            string dir = Env("OKU_FIRE_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_FIRE_DIR to capture fire and magic on scenery");
            Directory.CreateDirectory(dir);
            string only = Env("OKU_FIRE_SCENES");
            foreach (var scene in new[] { "forest", "rain", "frost", "dark", "lightning", "holy", "earth", "water", "wind" })
            {
                if (!string.IsNullOrEmpty(only) && !only.Split(',').Contains(scene)) continue;
                yield return Scene(dir, scene);
                Object.Destroy(root.gameObject);
                root = null;
                for (int i = 0; i < 5; i++) yield return null;
            }
            File.WriteAllLines(Path.Combine(dir, "log.txt"), log);
        }

        IEnumerator Begin(string map)
        {
            mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = map;
            root.Setup.Seed = 3;
            root.Setup.MapRevealed = true;
            root.Setup.LineOfSight = false;
            root.Options.EffectsQuality = EffectsQuality.High;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 120f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
            root.Options.GameSpeed = 0;
            root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            var hud = root.Screens.Screen("Hud");
            if (hud != null) hud.SetActive(false);
            mock.SceneryBreaks = true;
            mock.SeeAll(true);
            var fs = new FeatureState[EntityRenderer.MaxFeatures];
            for (int n = mock.ReadFeatures(fs); n > 0; n--) mock.RemoveFeature(n - 1);
            // The game's own models, by the names the drop-ins go by.
            for (int t = 1; t <= 5; t++)
            {
                mock.AddSceneryKind($"AraTree0{t}a", "trees", 3f, 300, true, burnSeconds: 7f);
                mock.AddSceneryKind($"AraTree0{t}", "trees", 4.5f, t == 5 ? 1500 : 800, true, $"AraTree0{t}a", $"AraTree0{t}a", 1.5f, 10f);
            }
            mock.AddSceneryKind("AraRock01", "rocks", 1.2f, 0, size: 2);
            mock.AddSceneryKind("AraWall01b", "walls", 0.4f, 0, size: 2);
            mock.AddSceneryKind("AraWall01", "walls", 2.5f, 12000, size: 2);
            mock.AddSceneryKind("AraHut01a", "huts", 1f, 0, size: 2);
            mock.AddSceneryKind("AraHut01", "huts", 2.5f, 900, true, "AraHut01a", "AraHut01a", 1.5f, 14f, 2);
        }

        int Place(string kind, Vector3 at)
        {
            float cell = mock.Terrain.CellSize;
            int def = mock.FeatureDefs.First(d => d.Name == kind).Id;
            return mock.PlaceFeature(def, Mathf.RoundToInt(at.x / cell), Mathf.RoundToInt(-at.z / cell));
        }

        Vector3 At(int index)
        {
            var fs = new FeatureState[EntityRenderer.MaxFeatures];
            mock.ReadFeatures(fs);
            return fs[index].Position;
        }

        // Flat dry ground near a point, as the mock's maps keep lakes in the middle.
        static Vector3 Dry(IGameBackend b, Vector3 near, float across)
        {
            float sea = b.Terrain.SeaLevel;
            for (float r = 0f; r < 120f; r += 2f)
                for (int a = 0; a < 16; a++)
                {
                    var p = near + new Vector3(Mathf.Cos(a * Mathf.PI / 8f) * r, 0f, Mathf.Sin(a * Mathf.PI / 8f) * r);
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int k = 0; k < 49; k++)
                    {
                        var q = p + new Vector3((k % 7 - 3) * across / 6f, 0f, (k / 7 - 3) * across / 6f);
                        float g = b.GroundHeight(q.x, q.z);
                        lo = Mathf.Min(lo, g);
                        hi = Mathf.Max(hi, g);
                    }
                    if (lo > sea + 0.5f && hi - lo < across * 0.25f) { p.y = b.GroundHeight(p.x, p.z); return p; }
                }
            Assert.Fail("no flat dry ground");
            return near;
        }

        IEnumerator Frame(Vector3 focus, float distance, float yaw, float pitch)
        {
            var gc = root.World.Camera;
            gc.focus = focus;
            gc.yaw = yaw;
            gc.pitch = pitch;
            gc.Zoom(distance);
            for (int i = 0; i < 40; i++) yield return null;
        }

        // The camera looks the way the sun shines, so what it sees is lit,
        // and across the screen runs along Right.
        const float Yaw = 150f;
        static readonly Vector3 Right = new Vector3(Mathf.Cos(Yaw * Mathf.Deg2Rad), 0f, -Mathf.Sin(Yaw * Mathf.Deg2Rad));
        static readonly Vector3 Ahead = new Vector3(Mathf.Sin(Yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(Yaw * Mathf.Deg2Rad));

        // A small stand of scenery for a spell: four trees, a rock, a wall and its rubble.
        Vector3 Stand()
        {
            var c = Dry(mock, mock.StageCentre, 24f);
            Place("AraTree04", c - Right * 3.5f);
            Place("AraTree05", c);
            Place("AraTree03", c + Right * 3.5f);
            Place("AraTree01", c + Ahead * 3.5f + Right * 1.6f);
            Place("AraRock01", c - Right * 6f - Ahead * 2f);
            Place("AraWall01", c + Right * 6.5f + Ahead * 2f);
            Place("AraWall01b", c + Right * 5.5f - Ahead * 3f);
            return c;
        }

        IEnumerator Scene(string dir, string scene)
        {
            yield return Begin(scene == "forest" ? "mock_marches" : "mock_highlands");
            var frames = new List<string>();
            string sub = Path.Combine(dir, scene);
            Directory.CreateDirectory(sub);
            foreach (var old in Directory.GetFiles(sub, "*.png")) File.Delete(old);
            var fx = root.World.Effects;
            var magic = root.World.Magic;
            var fire = root.World.Fire;
            float clock = 0f;
            // The game played tick by tick to a time in seconds, a frame saved there.
            IEnumerator Until(float seconds, string label = null)
            {
                int ticks = Mathf.Max(0, Mathf.RoundToInt((seconds - clock) * mock.TicksPerSecond));
                for (int t = 0; t < ticks; t++)
                {
                    mock.Advance(1);
                    yield return null;
                }
                clock = seconds;
                string path = Path.Combine(sub, $"{scene}-{frames.Count:D3}-{seconds:00.0}s.png");
                yield return Grab(Camera.main, path);
                frames.Add(path);
            }
            switch (scene)
            {
                case "forest":
                {
                    var c = Dry(mock, mock.StageCentre, 40f);
                    mock.SpreadChance = 0.55f;
                    // The wind blows left to right across the picture.
                    mock.SetWind(Mathf.Atan2(Right.x, Right.z) * Mathf.Rad2Deg, MockBackend.WindMax * 0.8f);
                    var rng = new System.Random(7);
                    for (int gx = 0; gx < 12; gx++)
                        for (int gz = 0; gz < 7; gz++)
                        {
                            var p = c + Right * ((gx - 5.5f) * 2.2f + (float)rng.NextDouble() * 0.8f) + Ahead * ((gz - 3f) * 2.2f + (float)rng.NextDouble() * 0.8f);
                            Place($"AraTree0{1 + rng.Next(5)}", p);
                        }
                    Place("AraHut01", c + Right * 16f);
                    Place("AraRock01", c - Right * 16f - Ahead * 2f);
                    yield return Frame(c, 55f, Yaw, 24f);
                    yield return Until(0.5f);
                    var upwind = c - Right * 12.5f;
                    mock.FireFx("MOCK FIREBALL SPELL", upwind - Right * 7f + Vector3.up * 4f, upwind);
                    for (int k = 1; k <= 60; k++) yield return Until(k * 1.25f);
                    break;
                }
                case "rain":
                {
                    root.World.Atmosphere.SetWeather(WeatherChoice.Rain);
                    var c = Stand();
                    mock.SpreadChance = 0f;
                    yield return Frame(c + Ahead * 4f, 34f, Yaw, 18f);
                    yield return Until(0.3f);
                    mock.FireFx("MOCK FIREBALL SPELL", c - Right * 6f + Vector3.up * 3f, c);
                    for (int k = 1; k <= 12; k++) yield return Until(0.3f + k * 0.6f);
                    // A water spell puts it out in steam.
                    magic.Apply(BlastKind.Water, c, 2.5f, Vector3.zero);
                    fx.Play(BlastKind.Water, c, 2.5f, Vector3.zero);
                    for (int k = 1; k <= 8; k++) yield return Until(clock + 0.5f);
                    break;
                }
                default:
                {
                    var c = Stand();
                    if (scene != "lightning") mock.SceneryBreaks = false;
                    yield return Frame(c + Ahead * 4f, 34f, Yaw, 18f);
                    yield return Until(0.3f);
                    foreach (float t in Cast(scene, c, fx, magic, fire)) yield return Until(t);
                    break;
                }
            }
            Sheet(frames, Path.Combine(dir, $"{scene}-sheet.png"), Mathf.Min(12, frames.Count));
            log.Add($"{scene}: {frames.Count} frames; fire lit {fire.Lit}, spread {fire.Spread}, doused {fire.Doused}, flames {fire.FlamesMade}, smoke {fire.SmokeMade}, steam {fire.SteamMade}, " +
                    $"char marks {fire.CharMarks}; magic marked {string.Join(" ", System.Enum.GetValues(typeof(BlastKind)).Cast<BlastKind>().Where(k => (int)k < magic.Marked.Length && magic.Marked[(int)k] > 0).Select(k => k + " " + magic.Marked[(int)k]))}");
        }

        // A spell of a kind cast at the stand, and the times to save frames at.
        IEnumerable<float> Cast(string scene, Vector3 c, EffectRenderer fx, FxMagic magic, FxFire fire)
        {
            switch (scene)
            {
                case "frost":
                    fx.Play(BlastKind.Frost, c, 3f, Vector3.zero);
                    magic.Apply(BlastKind.Frost, c, 3f, Vector3.zero);
                    foreach (var t in new[] { 0.4f, 0.8f, 1.5f, 3f, 6f, 12f, 25f, 40f, 50f, 63f }) yield return t;
                    break;
                case "dark":
                    fx.Play(BlastKind.Dark, c, 2.5f, Vector3.zero);
                    magic.Apply(BlastKind.Dark, c, 2.5f, Vector3.zero);
                    foreach (var t in new[] { 0.4f, 0.8f, 1.3f, 2f, 2.8f, 3.6f, 5f, 8f, 15f }) yield return t;
                    break;
                case "lightning":
                {
                    // The first bolt splits the middle tree, and the second brings it down.
                    var top = c + Vector3.up * 12f;
                    mock.FireFx("ZONHUNT 1", top, c);
                    foreach (var t in new[] { 0.35f, 0.45f, 0.7f, 1.2f, 2f, 3f }) yield return t;
                    mock.FireFx("ZONHUNT 1", top, c);
                    foreach (var t in new[] { 3.1f, 3.3f, 3.6f, 4.2f, 5.5f, 8f }) yield return t;
                    break;
                }
                case "holy":
                    magic.Apply(BlastKind.Dark, c, 2.5f, Vector3.zero);
                    foreach (var t in new[] { 2.5f }) yield return t;
                    fx.Play(BlastKind.Holy, c, 2.5f, Vector3.zero);
                    magic.Apply(BlastKind.Holy, c, 2.5f, Vector3.zero);
                    foreach (var t in new[] { 2.7f, 2.9f, 3.2f, 3.6f, 4.2f, 5f, 6.5f, 9f }) yield return t;
                    break;
                case "earth":
                    fx.Play(BlastKind.Earth, c, 4f, Vector3.zero);
                    magic.Apply(BlastKind.Earth, c, 4f, Vector3.zero);
                    for (int k = 1; k <= 12; k++) yield return 0.3f + k * 0.12f;
                    foreach (var t in new[] { 2.5f, 4f }) yield return t;
                    break;
                case "water":
                {
                    mock.SceneryBreaks = true;
                    mock.SpreadChance = 0f;
                    mock.FireFx("TARARCH 1", c - Ahead * 6f + Vector3.up * 1.5f, c);
                    foreach (var t in new[] { 1.5f, 3f, 4.5f }) yield return t;
                    fx.Play(BlastKind.Water, c, 2.5f, Vector3.zero);
                    magic.Apply(BlastKind.Water, c, 2.5f, Vector3.zero);
                    foreach (var t in new[] { 4.7f, 5f, 5.5f, 6.5f, 8f, 11f }) yield return t;
                    break;
                }
                case "wind":
                {
                    // A vortex passing along the stand.
                    for (int k = 0; k < 8; k++)
                    {
                        var p = c + Right * (-5f + k * 1.4f) - Ahead * 1.5f;
                        fx.Play(BlastKind.Wind, p, 2f, Right);
                        magic.Apply(BlastKind.Wind, p, 2f, Right);
                        yield return 0.6f + k * 0.4f;
                    }
                    foreach (var t in new[] { 4.5f, 6f }) yield return t;
                    break;
                }
            }
        }

        static IEnumerator Grab(Camera cam, string path)
        {
            var hdr = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            var rt = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var old = cam.targetTexture;
            cam.targetTexture = hdr;
            cam.aspect = W / (float)H;
            cam.Render();
            Graphics.Blit(hdr, rt);
            RenderTexture.ReleaseTemporary(hdr);
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = old;
            // Back to the screen's own shape, so later tests see the view they expect.
            cam.ResetAspect();
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
            yield break;
        }

        // Frames picked evenly through the strip, in a grid four across at half size.
        static void Sheet(List<string> frames, string path, int count)
        {
            if (frames.Count == 0 || count <= 0) return;
            int cols = 4, rows = (count + cols - 1) / cols, w = W / 2, h = H / 2;
            var sheet = new Texture2D(cols * w, rows * h, TextureFormat.RGB24, false);
            var one = new Texture2D(2, 2);
            for (int k = 0; k < count; k++)
            {
                int i = Mathf.Min(frames.Count - 1, Mathf.RoundToInt(k * (frames.Count - 1) / (float)Mathf.Max(1, count - 1)));
                one.LoadImage(File.ReadAllBytes(frames[i]));
                var half = new Color32[w * h];
                var px = one.GetPixels32();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        half[y * w + x] = px[(y * 2) * one.width + x * 2];
                int cx = k % cols, cy = rows - 1 - k / cols;
                sheet.SetPixels32(cx * w, cy * h, w, h, half);
            }
            sheet.Apply();
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.Destroy(sheet);
            Object.Destroy(one);
        }
    }
}
