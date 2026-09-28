// FxPlayTests.cs - the effects in a running game on the mock: each staged
// reference scene draws its families, added art lands as the original's
// byte sum, the fog hides what the player cannot see, and two hundred
// effects at once keep the effects' own time and garbage small.
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class FxPlayTests
    {
        static IEnumerator Start(MockBackend mock, System.Action<GameRoot> ready, bool lineOfSight = false, bool revealed = true)
        {
            var root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_highlands";
            root.Setup.LineOfSight = lineOfSight;
            root.Setup.MapRevealed = revealed;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 60f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
            root.Options.GameSpeed = 0;
            ready(root);
        }

        static void Frame(GameRoot root, Vector3 at, float distance = 60f)
        {
            var cam = root.World.Camera;
            cam.focus = at;
            cam.pitch = GameCamera.ClassicPitch;
            cam.yaw = 0;
            cam.Zoom(distance);
        }

        [UnityTest]
        public IEnumerator EveryStagedSceneDrawsItsFamilies()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            GameRoot root = null;
            yield return Start(mock, r => root = r);
            try
            {
                Frame(root, mock.StageCentre);
                var fx = root.World.Effects;
                Assert.IsTrue(fx.WaitForArt(), "the strips warmed while loading are made");
                foreach (var scene in MockBackend.FxScenes.Keys)
                {
                    Assert.IsTrue(mock.StageFx(scene, 2f), scene);
                    int peak = 0, beams = 0, models = 0, lights = 0, trails = 0;
                    for (int f = 0; f < 150; f++)
                    {
                        mock.Advance(1);
                        yield return null;
                        peak = Mathf.Max(peak, fx.Pictures);
                        beams = Mathf.Max(beams, fx.Beams);
                        models = Mathf.Max(models, fx.ModelShots);
                        lights = Mathf.Max(lights, fx.LightsLit);
                        trails = Mathf.Max(trails, fx.TrailCount);
                    }
                    Debug.Log($"Fx scene {scene}: {peak} pictures, {beams} beams, {models} model shots, {lights} lights, {trails} trails, {fx.Marks} marks");
                    Assert.Greater(peak, 0, scene + " shows pictures");
                    if (scene == "magic" || scene == "magic2" || scene == "spells" || scene == "bolts") Assert.Greater(beams, 0, scene + " draws beams");
                    if (scene == "ranged" || scene == "bolts") Assert.Greater(models, 0, scene + " flies model shots");
                    if (scene == "magic" || scene == "spells") Assert.Greater(lights, 0, scene + " lights the ground");
                    if (scene == "ranged" || scene == "fire") Assert.Greater(trails, 0, scene + " leaves trails");
                    mock.Advance(400);
                }
                Assert.Greater(fx.Marks, 0, "the blasts scorched the ground");
            }
            finally { Object.Destroy(root.gameObject); }
        }

        [UnityTest]
        public IEnumerator ABrightBlastLightsTheGroundRoundIt()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            GameRoot root = null;
            yield return Start(mock, r => root = r);
            try
            {
                root.World.Atmosphere.SetWeather(WeatherChoice.Off);
                var hud = root.Screens.Screen("Hud");
                if (hud != null) hud.SetActive(false);
                var fx = root.World.Effects;
                var at = mock.StageCentre + new Vector3(0, 0, 6);
                Frame(root, at, 28f);
                mock.FireFx("TARNECRO 2", at + new Vector3(0, 1, -4), at + new Vector3(0, 1, 0.5f));
                // Until its blast has flared up and its light is lit.
                for (int f = 0; f < 90 && !(fx.LightsLit > 0 && mock.FxShotCount == 0); f++) { mock.Advance(1); yield return null; }
                mock.Advance(3);
                var cam = Camera.main;
                cam.rect = new Rect(0, 0, 1, 1);
                // Lights fade in and out, so each shot waits for the fade.
                fx.Lights = true;
                yield return Wait(0.4f);
                var lit = Shot(cam);
                fx.Lights = false;
                yield return Wait(0.4f);
                var dark = Shot(cam);
                var s = cam.WorldToViewportPoint(at);
                float gain = Luma(Region(lit, s, 60)) - Luma(Region(dark, s, 60));
                Debug.Log($"Fx: the blast's light brightens the ground round it by {gain:0.000}");
                Assert.Greater(gain, 0.01f, "the ground round the blast is lit");
            }
            finally { Object.Destroy(root.gameObject); }
        }

        // Added art on the screen is the ground as shown plus the art's own
        // bytes, capped at white, as the original adds them: a middling
        // colour, and a bright one whose sum runs into white.
        [UnityTest]
        public IEnumerator AddedArtLandsAsTheOriginalsByteSum()
        {
            if (Looks.Urp == null) Assert.Ignore("the sum reads URP's copy of the picture");
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            GameRoot root = null;
            yield return Start(mock, r => root = r);
            try
            {
                root.World.Atmosphere.SetWeather(WeatherChoice.Off);
                var hud = root.Screens.Screen("Hud");
                if (hud != null) hud.SetActive(false);
                var fx = root.World.Effects;
                fx.Lights = false;
                fx.Glow = false;
                var at = DryFlat(mock, mock.StageCentre);
                Frame(root, at, 14f);
                var cam = Camera.main;
                cam.rect = new Rect(0, 0, 1, 1);
                for (int f = 0; f < 5; f++) yield return null;
                var bare = Shot(cam);
                foreach (var (name, art) in new[] { ("flat", new Color(0.30f, 0.12f, 0.02f)), ("flatbright", new Color(0.60f, 0.55f, 0.10f)) })
                {
                    Assert.IsTrue(mock.ShowFx(name, at, 8));
                    mock.Advance(1);
                    yield return null;
                    fx.WaitForArt();
                    for (int f = 0; f < 5; f++) yield return null;
                    Assert.Greater(fx.Pictures, 0, name + " is drawn");
                    var shown = Shot(cam);
                    mock.Advance(20);
                    for (int f = 0; f < 3; f++) yield return null;
                    // The art's middle, clear of its soft foot and its edges.
                    var v = cam.WorldToViewportPoint(at + Vector3.up * 1.3f);
                    Color sum = default, got = default;
                    int n = 0, cx = Mathf.RoundToInt(v.x * 640), cy = Mathf.RoundToInt(v.y * 360);
                    for (int y = cy - 5; y <= cy + 5; y++)
                        for (int x = cx - 5; x <= cx + 5; x++)
                        {
                            var g = bare[y * 640 + x];
                            sum += new Color(Mathf.Min(1f, g.r + art.r), Mathf.Min(1f, g.g + art.g), Mathf.Min(1f, g.b + art.b));
                            got += shown[y * 640 + x];
                            n++;
                        }
                    sum /= n;
                    got /= n;
                    Color.RGBToHSV(sum, out float hs, out float ss, out float vs);
                    Color.RGBToHSV(got, out float hg, out float sg, out float vg);
                    float dh = Mathf.Abs(Mathf.DeltaAngle(hs * 360f, hg * 360f));
                    Debug.Log($"Fx sum of {name}: ground {(Color32)Region(bare, v, 5)} plus art {(Color32)art} should show {(Color32)sum} (hue {hs * 360:0.0} sat {ss:0.000}), shows {(Color32)got} (hue {hg * 360:0.0} sat {sg:0.000})");
                    Assert.Less(dh, 3f, name + " hue");
                    Assert.AreEqual(ss, sg, 0.04f, name + " saturation");
                    Assert.AreEqual(vs, vg, 0.04f, name + " brightness");
                }
            }
            finally { Object.Destroy(root.gameObject); }
        }

        [UnityTest]
        public IEnumerator AnEnemysFireOutOfSightShowsNothingUntilItIsInSight()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            GameRoot root = null;
            yield return Start(mock, r => root = r, lineOfSight: true, revealed: false);
            try
            {
                var fx = root.World.Effects;
                var fog = root.World.Fog;
                var units = new UnitState[EntityRenderer.MaxUnits];
                int count = mock.ReadUnits(units);
                int enemy = -1, mine = -1;
                for (int i = 0; i < count; i++)
                {
                    if (units[i].Player == mock.LocalPlayer) { if (mine < 0) mine = i; }
                    else if (enemy < 0 && !mock.Allied(units[i].Player, mock.LocalPlayer)) enemy = i;
                }
                Assert.GreaterOrEqual(enemy, 0, "an enemy unit");
                Assert.GreaterOrEqual(mine, 0, "a unit of ours");
                var spot = units[enemy].Position;
                fog.Update(true);
                Assert.IsFalse(fog.InSight(spot), "the enemy's ground starts out of sight");
                Frame(root, spot, 30f);

                int drawn = 0, lit = 0;
                mock.FireFx("TARDRAG 2", Above(mock, spot, 0, -4), Above(mock, spot, 0, 4), shooter: units[enemy].Handle);
                for (int f = 0; f < 120; f++)
                {
                    mock.Advance(1);
                    yield return null;
                    drawn = Mathf.Max(drawn, fx.Drawn);
                    lit = Mathf.Max(lit, fx.LightsLit);
                }
                Assert.AreEqual(0, drawn, "nothing drawn of the enemy's fire");
                Assert.AreEqual(0, lit, "nor lit");
                Assert.AreEqual(0, fx.Marks, "nor scorched");
                Assert.AreEqual(0, fx.TrailCount, "nor trailed");

                // A shot of our own shows wherever it flies, but not what it strikes.
                bool own = false;
                mock.FireFx("TARDRAG 2", Above(mock, spot, 3, -4), Above(mock, spot, 3, 4), shooter: units[mine].Handle);
                for (int f = 0; f < 120; f++) { mock.Advance(1); yield return null; own |= fx.Pictures > 0; }
                Assert.IsTrue(own, "our own shot shows out of sight");
                Assert.AreEqual(0, fx.Marks, "its blast out of sight leaves no scorch");
                mock.Advance(300);

                // A unit of ours goes there, and the same fire shows.
                int def = -1;
                for (int d = 0; d < mock.UnitDefs.Count && def < 0; d++)
                    if (!mock.UnitDefs[d].IsBuilding && !mock.UnitDefs[d].CanFly) def = d;
                mock.SpawnFrame(def, spot + new Vector3(2, 0, 0), 1f);
                float until = Time.realtimeSinceStartup + 10f;
                while (!fog.InSight(spot) && Time.realtimeSinceStartup < until) { mock.Advance(1); yield return null; }
                Assert.IsTrue(fog.InSight(spot), "our unit sees the spot");
                drawn = lit = 0;
                mock.FireFx("TARDRAG 2", Above(mock, spot, 0, -4), Above(mock, spot, 0, 4), shooter: units[enemy].Handle);
                for (int f = 0; f < 120; f++)
                {
                    mock.Advance(1);
                    yield return null;
                    drawn = Mathf.Max(drawn, fx.Drawn);
                    lit = Mathf.Max(lit, fx.LightsLit);
                }
                Assert.Greater(drawn, 0, "the enemy's fire in sight is drawn");
                Assert.Greater(lit, 0, "and lights the ground");
                Assert.Greater(fx.Marks, 0, "and scorches it");
            }
            finally { Object.Destroy(root.gameObject); }
        }

        [UnityTest]
        public IEnumerator TwoHundredEffectsCostLittleTimeAndNoGarbage()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            GameRoot root = null;
            yield return Start(mock, r => root = r);
            try
            {
                var c = mock.StageCentre;
                Frame(root, c);
                var fx = root.World.Effects;
                var cam = Camera.main;
                // Rings, rain, blasts, beams and shots, all at once.
                string[] mix = { "ARAKING 3", "TARMAGE 3", "TARNECRO 2", "ARAKING 1", "ARACAN 1", "ZONHUNT 2", "ARADRAG 1", "TARWITCH 1" };
                void Volley(int shift, int count, float spread)
                {
                    for (int i = 0; i < count; i++)
                    {
                        float x = (i % 8 - 3.5f) * spread, z = (i / 8 - 2.5f) * 5f;
                        mock.FireFx(mix[(i + shift) % mix.Length], c + new Vector3(x, 1, z - 3), c + new Vector3(x + 1, 1, z + 3));
                    }
                }
                Volley(0, 48, 4f);
                for (int f = 0; f < 30; f++) { mock.Advance(1); yield return null; }
                Assert.IsTrue(fx.WaitForArt());
                Assert.GreaterOrEqual(fx.Count + fx.Shots, 200, "two hundred effects on screen");

                // The effects' own time, keeping them coming while the clock runs.
                var times = new List<double>();
                var watch = new System.Diagnostics.Stopwatch();
                int least = int.MaxValue, toggles = fx.LightToggles;
                for (int f = 0; f < 120; f++)
                {
                    if (f % 30 == 0) Volley(f, 24, 5f);
                    mock.Advance(1);
                    yield return null;
                    watch.Restart();
                    fx.Render(cam);
                    watch.Stop();
                    times.Add(watch.Elapsed.TotalMilliseconds);
                    least = Mathf.Min(least, fx.Count + fx.Shots);
                }
                times.Sort();
                double median = times[times.Count / 2];
                Debug.Log($"Fx: at least {least} effects and shots a frame, {fx.Drawn} quads, {fx.LightsLit} lights, {fx.LightToggles - toggles} light switches, Effects.Render median {median:0.00} ms, worst {times[times.Count - 1]:0.00} ms");
                Assert.GreaterOrEqual(least, 200);
                Assert.Less(median, 12.0, "Effects.Render with two hundred effects");

                using (var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
                {
                    yield return null;
                    yield return null;
                    if (!rec.Valid) Assert.Ignore("no allocation counter here");
                    var known = new byte[65536];
                    yield return null;
                    if (rec.LastValue < known.Length) Assert.Ignore("the allocation counter does not count here");
                    // Frames of the game's own, and ones that also render the effects thirty times more.
                    var quiet = new List<long>();
                    var busy = new List<long>();
                    const int renders = 30;
                    for (int round = 0; round < 3; round++)
                    {
                        yield return null;
                        quiet.Add(rec.LastValue);
                        for (int i = 0; i < renders; i++) fx.Render(cam);
                        yield return null;
                        busy.Add(rec.LastValue);
                    }
                    quiet.Sort();
                    busy.Sort();
                    long bytes = System.Math.Max(0, busy[1] - quiet[1]) / renders;
                    Debug.Log($"Fx: Effects.Render with {fx.Count} effects allocates {bytes} bytes");
                    Assert.Less(bytes, 4096, "Effects.Render throws little away");
                }
            }
            finally { Object.Destroy(root.gameObject); }
        }

        // A point beside another, a unit and a half over the ground there.
        static Vector3 Above(MockBackend mock, Vector3 p, float dx, float dz)
        {
            float x = p.x + dx, z = p.z + dz;
            return new Vector3(x, mock.GroundHeight(x, z) + 1.5f, z);
        }

        // The nearest dry, level ground to a point.
        static Vector3 DryFlat(MockBackend mock, Vector3 near)
        {
            float sea = mock.Terrain.SeaLevel;
            for (float r = 0f; r < 60f; r += 2f)
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    float x = near.x + Mathf.Cos(a) * r, z = near.z + Mathf.Sin(a) * r;
                    float h = mock.GroundHeight(x, z), lo = h, hi = h;
                    for (int j = 0; j < 8; j++)
                    {
                        float g = mock.GroundHeight(x + Mathf.Cos(j * 0.785f) * 3f, z + Mathf.Sin(j * 0.785f) * 3f);
                        lo = Mathf.Min(lo, g); hi = Mathf.Max(hi, g);
                    }
                    if (lo > sea + 1f && hi - lo < 0.4f) return new Vector3(x, h, z);
                }
            Assert.Fail("no dry level ground near the stage");
            return near;
        }

        static IEnumerator Wait(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        // The camera drawn as the screen shows it. URP draws into a camera's
        // own target, so the target is HDR as the screen's buffer is, then
        // copied to bytes.
        static Color[] Shot(Camera cam)
        {
            var hdr = RenderTexture.GetTemporary(640, 360, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            var rt = RenderTexture.GetTemporary(640, 360, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var old = cam.targetTexture;
            cam.targetTexture = hdr;
            cam.Render();
            Graphics.Blit(hdr, rt);
            RenderTexture.ReleaseTemporary(hdr);
            RenderTexture.active = rt;
            var tex = new Texture2D(640, 360, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 640, 360), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = old;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels();
            Object.Destroy(tex);
            return px;
        }

        // Mean colour in a square round a viewport point.
        static Color Region(Color[] px, Vector3 v, int half)
        {
            int cx = Mathf.RoundToInt(v.x * 640), cy = Mathf.RoundToInt(v.y * 360), n = 0;
            Color sum = default;
            for (int y = cy - half; y <= cy + half; y++)
                for (int x = cx - half; x <= cx + half; x++)
                {
                    if (x < 0 || y < 0 || x >= 640 || y >= 360) continue;
                    sum += px[y * 640 + x];
                    n++;
                }
            return n > 0 ? sum / n : default;
        }

        static float Luma(Color c) => 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
    }
}
