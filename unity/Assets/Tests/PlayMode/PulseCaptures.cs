// PulseCaptures.cs - a lodestone breathing on the real engine, for the
// owner to judge: one full breath in twelve frames from the classic camera,
// from a low three-quarter view and from the classic camera in a dim scene,
// each as a strip of frames. Runs only when OKU_PULSE_DIR names a folder;
// OKU_PULSE_SIDES lists the sides whose lodestone is built.
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
    public class PulseCaptures
    {
        const int Frames = 12;
        GameRoot root;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        IEnumerable<UnitState> All()
        {
            var units = new UnitState[4096];
            int n = root.Backend.ReadUnits(units);
            for (int i = 0; i < n; i++) yield return units[i];
        }

        IEnumerable<UnitState> Own() => All().Where(u => u.Player == root.Backend.LocalPlayer && (u.Flags & UnitFlags.Dying) == 0);

        [UnityTest, Timeout(3600000)]
        public IEnumerator OneBreathFramed()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_PULSE_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_PULSE_DIR to capture a lodestone breathing");
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            Directory.CreateDirectory(dir);
            string sides = System.Environment.GetEnvironmentVariable("OKU_PULSE_SIDES") ?? "ARAMON;VERUNA";
            var view = GameObject.Find("OpenKingdoms");
            if (view != null) Object.Destroy(view);
            yield return null;
            foreach (var side in sides.Split(';'))
            {
                yield return Breathe(dir, side);
                if (root != null) Object.Destroy(root.gameObject);
                root = null;
                yield return null;
                yield return null;
            }
        }

        IEnumerator Breathe(string dir, string side)
        {
            root = GameRoot.Boot();
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "two castles";
            root.Setup.MapRevealed = true;
            root.Setup.LineOfSight = false;
            root.Setup.StartMana = 5000;
            root.Setup.Seats[0].Side = side;
            root.Setup.Seats[1].Side = side == "TAROS" ? "ARAMON" : "TAROS";
            root.Setup.Seats[1].Difficulty = AiDifficulty.Easy;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 240f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
            root.Orders.Frozen = true;
            root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            root.Screens.Screen("Hud").SetActive(false);
            var b = root.Backend;
            var monarch = Own().First(u => !b.UnitDefs[u.Def].IsBuilding && b.UnitDefs[u.Def].BuildOptions.Length > 0);

            // A lodestone on the site nearest the monarch.
            var options = b.UnitDefs[monarch.Def].BuildOptions;
            int lode = options.First(o => b.UnitDefs[o].IsBuilding && LodestonePulse.IsLodestone(b.UnitDefs[o].ObjectName));
            var features = new FeatureState[8192];
            int nf = b.ReadFeatures(features);
            Vector3 site = default;
            float best = float.MaxValue;
            for (int i = 0; i < nf; i++)
            {
                if (b.FeatureDefs[features[i].Def].Name.IndexOf("Mana", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                float d = (features[i].Position - monarch.Position).sqrMagnitude;
                if (d < best && b.CanBuildAt(lode, features[i].Position, 0, out var snapped)) { best = d; site = snapped; }
            }
            Assert.Less(best, float.MaxValue, "a lodestone site");
            Assert.IsTrue(b.Command(new GameCommand { Kind = CommandKind.Build, Unit = monarch.Handle, Target = site, TargetUnit = -1, BuildDef = lode, Facing = 0 }));
            UnitState built = default;
            for (int t = 0; t < 30000 && built.MaxHealth == 0; t += 120)
            {
                b.Advance(120);
                built = Own().FirstOrDefault(u => u.Def == lode && u.BuildProgress >= 1f);
                if (t % 1200 == 0) yield return null;
            }
            Assert.Greater(built.MaxHealth, 0, "the lodestone stands");
            var card = CardOverride.For(b.UnitDefs[lode].ObjectName);
            Assert.IsNotNull(card?.Glow, $"{b.UnitDefs[lode].ObjectName} has a card model that glows");
            b.Command(GameCommand.To(CommandKind.Move, monarch.Handle, built.Position + new Vector3(-9f, 0f, 6f)));
            b.Advance(400);
            yield return null;

            string obj = b.UnitDefs[lode].ObjectName.ToUpperInvariant();
            var crystal = built.Position + Vector3.up * (root.World.Entities.SiteLift(built.Position) + card.Glow.Centre.y);
            float phase = LodestonePulse.Phase(built.Position);
            var sun = root.World.Atmosphere.Sun;
            float sunWas = sun.intensity, skyWas = RenderSettings.ambientIntensity;
            var lines = new List<string> { $"{obj} at {built.Position}, crystal colour {card.Glow.Colour}, radius {card.Glow.Radius:F2}, phase {phase:F3}" };
            foreach (var (name, pitch, yaw, zoom, w, h, cropW, cropH, dim) in new[]
            {
                ("classic", GameCamera.ClassicPitch, 0f, 34f, 1920, 1080, 640, 540, false),
                ("low", 30f, 35f, 13f, 1280, 720, 960, 640, false),
                ("night", GameCamera.ClassicPitch, 0f, 34f, 1920, 1080, 640, 540, true),
            })
            {
                sun.intensity = dim ? sunWas * 0.18f : sunWas;
                RenderSettings.ambientIntensity = dim ? 0.3f : skyWas;
                var gc = root.World.Camera;
                var cam = gc.GetComponent<Camera>();
                gc.focus = crystal - Vector3.up * card.Glow.Centre.y * 0.5f;
                gc.pitch = pitch;
                gc.yaw = yaw;
                gc.Zoom(zoom);
                for (int i = 0; i < 30; i++) yield return null;
                var rt = RenderTexture.GetTemporary(w, h, 24);
                var strip = new Texture2D(cropW * 6, cropH * 2, TextureFormat.RGB24, false);
                for (int k = 0; k < Frames; k++)
                {
                    // Frame 0 at the ebb, frame 6 at the swell.
                    root.World.Entities.PulseSeconds = ((float)k / Frames - phase + 1f) * LodestonePulse.Period;
                    yield return null;
                    int halos = root.World.Entities.HalosDrawn;
                    cam.targetTexture = rt;
                    var s = cam.WorldToScreenPoint(crystal);
                    cam.Render();
                    RenderTexture.active = rt;
                    int x0 = Mathf.Clamp(Mathf.RoundToInt(s.x) - cropW / 2, 0, w - cropW);
                    int y0 = Mathf.Clamp(Mathf.RoundToInt(s.y) - cropH / 2, 0, h - cropH);
                    var frame = new Texture2D(cropW, cropH, TextureFormat.RGB24, false);
                    frame.ReadPixels(new Rect(x0, y0, cropW, cropH), 0, 0);
                    frame.Apply();
                    RenderTexture.active = null;
                    cam.targetTexture = null;
                    File.WriteAllBytes(Path.Combine(dir, $"{obj}-{name}-{k:00}.png"), frame.EncodeToPNG());
                    strip.SetPixels(k % 6 * cropW, (1 - k / 6) * cropH, cropW, cropH, frame.GetPixels());
                    Object.Destroy(frame);
                    float time = (float)k / Frames * LodestonePulse.Period;
                    lines.Add($"{name} frame {k:00} at {time:F1} s: crystal x{LodestonePulse.Gain(root.World.Entities.PulseSeconds, phase, LodestonePulse.LightCalm(LodestonePulse.SceneLight())):F3}, rings {halos}");
                }
                strip.Apply();
                File.WriteAllBytes(Path.Combine(dir, $"{obj}-{name}-strip.png"), strip.EncodeToPNG());
                Object.Destroy(strip);
                RenderTexture.ReleaseTemporary(rt);
            }
            sun.intensity = sunWas;
            RenderSettings.ambientIntensity = skyWas;
            root.World.Entities.PulseSeconds = float.NaN;
            File.WriteAllLines(Path.Combine(dir, $"{obj}.txt"), lines);
        }
    }
}
