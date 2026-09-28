// FxCaptures.cs - pictures of the effects reference scenes in the classic
// view, on the mock or on an engine built with the stage. Runs only when
// OKU_FX_DIR names a folder, docs/ITERATE.md has the settings.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class FxCaptures
    {
        static string Env(string k) => System.Environment.GetEnvironmentVariable(k);
        static int W => int.TryParse(Env("OKU_CAPTURE_W"), out int w) ? w : 1280;
        static int H => int.TryParse(Env("OKU_CAPTURE_H"), out int h) ? h : 720;

        [UnityTest]
        public IEnumerator CaptureFxScenes()
        {
            string dir = Env("OKU_FX_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_FX_DIR to capture the effects scenes");
            Directory.CreateDirectory(dir);
            bool engine = Env("OKU_CAPTURE_BACKEND") == "engine";
            if (engine && GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            var scenes = new List<string>(string.IsNullOrEmpty(Env("OKU_FX_SCENES")) ? MockBackend.FxScenes.Keys : Env("OKU_FX_SCENES").Split(','));
            foreach (var sc in scenes)
            {
                if (engine) System.Environment.SetEnvironmentVariable("OK_FXSTAGE", sc);
                yield return CaptureScene(dir, sc, engine);
                for (int i = 0; i < 5; i++) yield return null;
            }
        }

        IEnumerator CaptureScene(string dir, string scene, bool engine)
        {
            string at = Env("OK_FXSTAGE_AT");
            string shots = Env("OK_FXSHOTS") ?? "30,60,90";
            string map = Env("OKU_CAPTURE_MAP");
            var view = GameObject.Find("OpenKingdoms");
            if (view != null) Object.Destroy(view);
            yield return null;
            yield return null;
            var mock = engine ? null : new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            var root = engine ? GameRoot.Boot() : GameRoot.Boot(mock);
            root.Options.GameSpeed = 0;
            yield return null;
            var cam = Camera.main;
            if (cam == null) { cam = new GameObject("Main Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; }
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            if (!string.IsNullOrEmpty(map))
                foreach (var m in root.Backend.Maps)
                    if (string.Equals(m.Id, map, System.StringComparison.OrdinalIgnoreCase) || string.Equals(m.Name, map, System.StringComparison.OrdinalIgnoreCase)) root.Setup.MapId = m.Id;
            if (!engine && string.IsNullOrEmpty(map)) root.Setup.MapId = "mock_highlands";
            root.Setup.Seed = 7;
            root.Setup.LineOfSight = false;
            root.Setup.MapRevealed = true;
            yield return null;
            root.Screens.Show(FlowState.Skirmish);
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 300f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, "the game loaded: " + root.LastError);
            root.Options.GameSpeed = 0;
            root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            // The whole screen for the world, as the classic captures show it.
            var hud = root.Screens.Screen("Hud");
            if (hud != null) hud.SetActive(false);
            var fx = root.World.Effects;
            if (Env("OKU_FX_LOOK") == "original") fx.Smooth = fx.Glow = fx.Soft = fx.Lights = fx.Trails = fx.Scorch = false;
            var b = root.Backend;
            var log = new List<string> { $"scene {scene} on {b.Name} map {root.Setup.MapId} look {Env("OKU_FX_LOOK") ?? "remastered"}" };
            Vector3 centre;
            if (engine)
            {
                float cx = b.Terrain.Size.x * 0.5f, cz = -b.Terrain.Size.y * 0.5f;
                if (!string.IsNullOrEmpty(at)) { var p = at.Split(','); cx = float.Parse(p[0]) / 16f; cz = -float.Parse(p[1]) / 16f; }
                centre = new Vector3(cx, b.GroundHeight(cx, cz), cz);
            }
            else
            {
                Assert.IsTrue(mock.StageFx(scene, 2.5f), scene);
                centre = mock.StageCentre;
            }
            var gc = root.World.Camera;
            foreach (var s in shots.Split(','))
            {
                int t = int.Parse(s);
                // A tick at a time, each read as a frame of play reads it.
                var scratch = new EffectState[1];
                while (b.Tick < t) { b.Advance(1); b.ReadEffects(scratch); }
                gc.focus = centre;
                gc.pitch = OpenKingdomsUnity.Game.World.GameCamera.ClassicPitch;
                gc.yaw = 0;
                // One world pixel to one screen pixel across the stage, as the classic view draws it.
                cam.rect = new Rect(0, 0, 1, 1);
                cam.aspect = W / (float)H;
                float d = W / 16f * 0.5f / (Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.aspect);
                gc.maxDistance = Mathf.Max(gc.maxDistance, d);
                gc.Zoom(d);
                for (int i = 0; i < 3; i++) yield return null;
                // The smooth copies of new art are made on a worker, and wait for it.
                float wait = Time.realtimeSinceStartup + 10f;
                while (fx.ArtPending > 0 && Time.realtimeSinceStartup < wait) yield return null;
                yield return Shoot(cam, Path.Combine(dir, $"{scene}-{t:D4}.png"));
                log.Add($"tick {t} drawn {b.Tick} effects {fx.Count} shots {fx.Shots} beams {fx.Beams} models {fx.ModelShots} lights {fx.LightsLit} marks {fx.Marks} trails {fx.TrailCount}");
                if (Env("OKU_FX_DUMP") == "1")
                {
                    var all = new EffectState[4096];
                    int ne = Mathf.Min(b.ReadEffects(all), all.Length);
                    for (int i = 0; i < ne; i++)
                        log.Add($"  effect {all[i].Id} strip {all[i].Strip} frame {all[i].Frame} age {all[i].Age} loops {all[i].Loops} shot {all[i].IsProjectile} add {all[i].Additive} at {all[i].Position} h {all[i].Top - all[i].Bottom:0.0}");
                    var shotsNow = new ProjectileState[1024];
                    int ns = Mathf.Min(b.ReadProjectiles(shotsNow), shotsNow.Length);
                    for (int i = 0; i < ns; i++)
                        log.Add($"  shot {shotsNow[i].Id} kind {shotsNow[i].Kind} beam {shotsNow[i].Beam} model {shotsNow[i].Model} at {shotsNow[i].Position} from {shotsNow[i].Source} v {shotsNow[i].Velocity} age {shotsNow[i].Age}");
                }
            }
            File.WriteAllLines(Path.Combine(dir, $"{scene}-log.txt"), log);
            Object.Destroy(root.gameObject);
        }

        static IEnumerator Shoot(Camera cam, string path)
        {
            yield return null;
            var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
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
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
