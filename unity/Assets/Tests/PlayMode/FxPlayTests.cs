// FxPlayTests.cs - the effects in a running game on the mock: each staged
// reference scene draws its families, and two hundred effects at once keep
// the frame time sane.
using System.Collections;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class FxPlayTests
    {
        static IEnumerator Start(MockBackend mock, System.Action<GameRoot> ready)
        {
            var root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_highlands";
            root.Setup.LineOfSight = false;
            root.Setup.MapRevealed = true;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 60f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
            root.Options.GameSpeed = 0;
            ready(root);
        }

        static void Frame(GameRoot root, MockBackend mock)
        {
            var cam = root.World.Camera;
            cam.focus = mock.StageCentre;
            cam.pitch = OpenKingdomsUnity.Game.World.GameCamera.ClassicPitch;
            cam.yaw = 0;
            cam.Zoom(60f);
        }

        [UnityTest]
        public IEnumerator EveryStagedSceneDrawsItsFamilies()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            GameRoot root = null;
            yield return Start(mock, r => root = r);
            try
            {
                Frame(root, mock);
                var fx = root.World.Effects;
                foreach (var scene in MockBackend.FxScenes.Keys)
                {
                    Assert.IsTrue(mock.StageFx(scene, 2f), scene);
                    int peak = 0, beams = 0, models = 0, lights = 0, trails = 0;
                    for (int f = 0; f < 150; f++)
                    {
                        mock.Advance(1);
                        yield return null;
                        peak = Mathf.Max(peak, fx.Count);
                        beams = Mathf.Max(beams, fx.Beams);
                        models = Mathf.Max(models, fx.ModelShots);
                        lights = Mathf.Max(lights, fx.LightsLit);
                        trails = Mathf.Max(trails, fx.TrailCount);
                    }
                    Debug.Log($"Fx scene {scene}: {peak} effects, {beams} beams, {models} model shots, {lights} lights, {trails} trails, {fx.Marks} marks");
                    Assert.Greater(peak, 0, scene + " shows pictures");
                    if (scene == "magic" || scene == "magic2" || scene == "spells" || scene == "bolts") Assert.Greater(beams, 0, scene + " draws beams");
                    if (scene == "ranged" || scene == "bolts") Assert.Greater(models, 0, scene + " flies model shots");
                    if (scene == "magic" || scene == "spells") Assert.Greater(lights, 0, scene + " lights the ground");
                    if (scene == "ranged" || scene == "fire") Assert.Greater(trails, 0, scene + " leaves trails");
                    mock.Advance(400);
                }
                Assert.Greater(fx.Marks, 0, "the blasts scorched the ground");
                Assert.LessOrEqual(fx.LightsLit, OpenKingdomsUnity.Game.World.FxLights.Budget);
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
                Frame(root, mock);
                root.World.Atmosphere.SetWeather(WeatherChoice.Off);
                var hud = root.Screens.Screen("Hud");
                if (hud != null) hud.SetActive(false);
                var fx = root.World.Effects;
                var at = mock.StageCentre + new Vector3(0, 0, 6);
                root.World.Camera.focus = at;
                root.World.Camera.Zoom(28f);
                mock.FireFx("TARNECRO 2", at + new Vector3(0, 1, -4), at + new Vector3(0, 1, 0.5f));
                // Until its blast has flared up and its light is lit.
                for (int f = 0; f < 90 && !(fx.LightsLit > 0 && mock.FxShotCount == 0); f++) { mock.Advance(1); yield return null; }
                mock.Advance(3);
                var cam = Camera.main;
                cam.rect = new Rect(0, 0, 1, 1);
                fx.Lights = true;
                yield return null;
                var lit = Shot(cam);
                fx.Lights = false;
                yield return null;
                var dark = Shot(cam);
                var s = cam.WorldToViewportPoint(at);
                float gain = Region(lit, s) - Region(dark, s);
                Debug.Log($"Fx: the blast's light brightens the ground round it by {gain:0.000}");
                Assert.Greater(gain, 0.01f, "the ground round the blast is lit");
            }
            finally { Object.Destroy(root.gameObject); }
        }

        static Color[] Shot(Camera cam)
        {
            var rt = RenderTexture.GetTemporary(640, 360, 24, RenderTextureFormat.ARGB32);
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
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

        // Mean brightness in a square round a viewport point.
        static float Region(Color[] px, Vector3 v)
        {
            int cx = Mathf.RoundToInt(v.x * 640), cy = Mathf.RoundToInt(v.y * 360), n = 0;
            float sum = 0f;
            for (int y = cy - 60; y <= cy + 60; y++)
                for (int x = cx - 60; x <= cx + 60; x++)
                {
                    if (x < 0 || y < 0 || x >= 640 || y >= 360) continue;
                    var c = px[y * 640 + x];
                    sum += 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
                    n++;
                }
            return n > 0 ? sum / n : 0f;
        }

        [UnityTest]
        public IEnumerator TwoHundredEffectsDrawInBoundedTime()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            GameRoot root = null;
            yield return Start(mock, r => root = r);
            try
            {
                Frame(root, mock);
                var fx = root.World.Effects;
                var c = mock.StageCentre;
                // Rings, rain, blasts, beams and shots, all at once.
                string[] mix = { "ARAKING 3", "TARMAGE 3", "TARNECRO 2", "ARAKING 1", "ARACAN 1", "ZONHUNT 2", "ARADRAG 1", "TARWITCH 1" };
                for (int i = 0; i < 48; i++)
                {
                    float x = (i % 8 - 3.5f) * 4f, z = (i / 8 - 2.5f) * 5f;
                    mock.FireFx(mix[i % mix.Length], c + new Vector3(x, 1, z - 3), c + new Vector3(x + 1, 1, z + 3));
                }
                for (int f = 0; f < 30; f++) { mock.Advance(1); yield return null; }
                Assert.GreaterOrEqual(fx.Count + fx.Shots, 200, "two hundred effects on screen");
                // Keep them coming while the clock runs.
                float start = Time.realtimeSinceStartup;
                const int frames = 120;
                int least = int.MaxValue;
                for (int f = 0; f < frames; f++)
                {
                    if (f % 30 == 0)
                        for (int i = 0; i < 24; i++)
                            mock.FireFx(mix[(i + f) % mix.Length], c + new Vector3((i % 6 - 2.5f) * 5f, 1, (i / 6 - 1.5f) * 6f - 3), c + new Vector3((i % 6 - 2.5f) * 5f + 1, 1, (i / 6 - 1.5f) * 6f + 3));
                    mock.Advance(1);
                    yield return null;
                    least = Mathf.Min(least, fx.Count + fx.Shots);
                }
                float ms = (Time.realtimeSinceStartup - start) * 1000f / frames;
                Debug.Log($"Fx: at least {least} effects and shots a frame, {fx.Drawn} quads, {fx.LightsLit} lights, {ms:0.0} ms a frame");
                Assert.GreaterOrEqual(least, 200);
                Assert.Less(ms, 100f, "a frame with two hundred effects took too long");
                Assert.LessOrEqual(fx.LightsLit, OpenKingdomsUnity.Game.World.FxLights.Budget, "lights stay within the budget");
            }
            finally { Object.Destroy(root.gameObject); }
        }
    }
}
