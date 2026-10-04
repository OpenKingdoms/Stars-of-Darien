// BlastCaptures.cs - each kind of blast on the mock as a strip of frames
// from the moment it bursts, today's look above and the new parts below,
// rendered offscreen. Runs only when OKU_BLAST_DIR names a folder.
// OKU_BLAST_KINDS picks kinds ("Gunpowder,Fire"), OKU_BLAST_QUALITY a setting.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.Capture;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class BlastCaptures
    {
        static string Env(string k) => System.Environment.GetEnvironmentVariable(k);
        const int W = 1280, H = 720, ThumbW = 320, ThumbH = 180;
        // Ticks after the burst each frame is taken at, 30 to the second.
        static readonly int[] After = { 1, 3, 6, 12, 24, 45, 90, 180 };

        // Each kind, the mock weapon that makes it, and for the kinds the
        // mock's weapons do not name, the kind played where its spell lands.
        static readonly (BlastKind kind, string weapon, BlastKind play, float radius)[] Kinds =
        {
            (BlastKind.Gunpowder, "ARACAN 1", BlastKind.None, 0f),
            (BlastKind.Siege, "ARAPULT 1", BlastKind.None, 0f),
            (BlastKind.Arrow, "TARARCH 1", BlastKind.None, 0f),
            (BlastKind.Fire, "TARNECRO 2", BlastKind.None, 0f),
            (BlastKind.Breath, "ARADRAG 1", BlastKind.None, 0f),
            (BlastKind.Lightning, "ARAKING 1", BlastKind.None, 0f),
            (BlastKind.Water, "VERMAGE 1", BlastKind.None, 0f),
            (BlastKind.Frost, "MOCK FROST SPELL", BlastKind.None, 0f),
            (BlastKind.Earth, "ARAKING 3", BlastKind.Earth, 6f),
            (BlastKind.Wind, "TARWITCH 1", BlastKind.Wind, 1.4f),
            (BlastKind.Dark, "TARNECRO 3", BlastKind.Dark, 3.5f),
            (BlastKind.Holy, "ARAPRIES 3", BlastKind.None, 0f),
        };

        [UnityTest, Timeout(3600000)]
        public IEnumerator CaptureEachKind()
        {
            string dir = Env("OKU_BLAST_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_BLAST_DIR to capture each kind of blast");
            Directory.CreateDirectory(dir);
            var only = string.IsNullOrEmpty(Env("OKU_BLAST_KINDS")) ? null : new HashSet<string>(Env("OKU_BLAST_KINDS").Split(','));
            var level = System.Enum.TryParse(Env("OKU_BLAST_QUALITY") ?? "", out EffectsQuality q) ? q : EffectsQuality.High;
            var before = FxQuality.Current.Level;
            FxQuality.Use(level);
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            var root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_highlands";
            root.Setup.Seed = 7;
            root.Setup.LineOfSight = false;
            root.Setup.MapRevealed = true;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 120f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
            root.Options.GameSpeed = 0;
            root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            var hud = root.Screens.Screen("Hud");
            if (hud != null) hud.SetActive(false);
            var fx = root.World.Effects;
            fx.FixedClock = true;
            var cam = Camera.main;
            var log = new List<string> { $"blasts on {mock.Name} at {level}" };
            try
            {
                var at = Dry(mock, mock.StageCentre + new Vector3(-14f, 0f, 10f));
                foreach (var k in Kinds)
                {
                    if (only != null && !only.Contains(k.kind.ToString())) continue;
                    var thumbs = new List<byte[]>();
                    var times = new List<float>();
                    int burst = -1;
                    foreach (bool parts in new[] { true, false })
                    {
                        var shots = new List<byte[]>();
                        yield return Run(root, mock, fx, cam, at, k, parts, burst, b => burst = b, shots, log);
                        // Today's look on the top row, the new one below.
                        if (parts) { thumbs.AddRange(shots); }
                        else { thumbs.InsertRange(0, shots); }
                    }
                    foreach (var _ in new[] { 0, 1 }) foreach (int t in After) times.Add(t / 30f);
                    var plan = new ContactSheet.Plan { Frames = thumbs.Count, Cols = After.Length, Rows = 2, ThumbW = ThumbW, ThumbH = ThumbH };
                    plan.Width = ContactSheet.Pad + plan.Cols * (ThumbW + ContactSheet.Pad);
                    plan.Height = ContactSheet.Pad + plan.Rows * (ThumbH + ContactSheet.LabelHeight + ContactSheet.Pad);
                    var sheet = ContactSheet.Compose(plan, thumbs, times);
                    File.WriteAllBytes(Path.Combine(dir, $"{(int)k.kind:00}-{k.kind}.png"), CaptureWriter.Png(sheet, plan.Width, plan.Height));
                }
            }
            finally
            {
                File.WriteAllLines(Path.Combine(dir, "log.txt"), log);
                Object.Destroy(root.gameObject);
                FxQuality.Use(before);
            }
        }

        static Vector3 Dry(IGameBackend b, Vector3 near)
        {
            float sea = b.Terrain.SeaLevel;
            for (float r = 0f; r < 80f; r += 2f)
                for (int a = 0; a < 16; a++)
                {
                    var p = near + new Vector3(Mathf.Cos(a * Mathf.PI / 8f) * r, 0f, Mathf.Sin(a * Mathf.PI / 8f) * r);
                    bool dry = true;
                    for (int k = 0; k < 8 && dry; k++)
                        dry = b.GroundHeight(p.x + Mathf.Cos(k * 0.785f) * 9f, p.z + Mathf.Sin(k * 0.785f) * 9f) > sea + 0.6f;
                    if (dry) { p.y = b.GroundHeight(p.x, p.z); return p; }
                }
            return near;
        }

        // One firing of a kind's weapon, framed close, with a frame at each
        // tick in After counted from the burst. The pass with the parts finds
        // the burst's tick, and the pass without uses the same.
        static IEnumerator Run(GameRoot root, MockBackend mock, EffectRenderer fx, Camera cam, Vector3 at,
            (BlastKind kind, string weapon, BlastKind play, float radius) k, bool parts, int burst, System.Action<int> found,
            List<byte[]> shots, List<string> log)
        {
            fx.Blasts = parts;
            fx.Clear();
            // Whatever the last firing left plays out first.
            for (int i = 0; i < 300; i++) mock.Advance(1);
            yield return null;
            var gc = root.World.Camera;
            gc.focus = at;
            gc.pitch = 50f;
            gc.yaw = 0f;
            gc.Zoom(k.kind == BlastKind.Earth ? 34f : 24f);
            for (int i = 0; i < 4; i++) yield return null;
            var from = at + new Vector3(-2f, 1f, -9f);
            int played = fx.BlastsPlayed;
            int start = (int)mock.Tick;
            mock.FireFx(k.weapon, from, at + Vector3.up * 0.1f);
            if (k.play != BlastKind.None && parts) fx.Play(k.play, at, k.radius, Vector3.forward);
            if (k.play != BlastKind.None && burst < 0) { burst = 0; found(0); }
            var scratch = new EffectState[256];
            int taken = 0;
            for (int tick = 1; taken < After.Length && tick < 600; tick++)
            {
                mock.Advance(1);
                // The wind's whirl rides the wandering spell's picture.
                if (k.kind == BlastKind.Wind && parts && tick % 10 == 0)
                {
                    int n = mock.ReadEffects(scratch);
                    for (int i = 0; i < n; i++)
                        if (scratch[i].Loops && !scratch[i].IsProjectile) { fx.Play(BlastKind.Wind, scratch[i].Position, k.radius, Vector3.zero); break; }
                }
                yield return null;
                if (burst < 0 && fx.BlastsPlayed > played) { burst = tick; found(tick); }
                if (burst < 0) continue;
                if (tick - burst != After[taken]) continue;
                fx.WaitForArt();
                // Two moments of each at full size: the blast at its height, and its smoke.
                string full = After[taken] == 12 || After[taken] == 90
                    ? Path.Combine(Env("OKU_BLAST_DIR"), $"{(int)k.kind:00}-{k.kind}-{(parts ? "new" : "today")}-{After[taken] / 30f:0.0}s.png") : null;
                shots.Add(Shoot(cam, full));
                taken++;
                log.Add($"{k.kind} {(parts ? "new" : "today")} +{tick - burst} ticks: particles {fx.ParticleCount} debris {fx.DebrisCount} rings {fx.RingCount} lights {fx.LightsLit} effects {fx.Count}");
            }
            while (shots.Count < After.Length) shots.Add(null);
            if (parts && burst < 0) log.Add($"{k.kind}: no blast seen from {k.weapon} after {(int)mock.Tick - start} ticks");
        }

        static byte[] Shoot(Camera cam, string full)
        {
            var hdr = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            var rt = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var old = cam.targetTexture;
            cam.targetTexture = hdr;
            cam.Render();
            Graphics.Blit(hdr, rt);
            RenderTexture.ReleaseTemporary(hdr);
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = old;
            RenderTexture.ReleaseTemporary(rt);
            var raw = tex.GetRawTextureData<byte>().ToArray();
            if (full != null) File.WriteAllBytes(full, tex.EncodeToPNG());
            Object.Destroy(tex);
            return ContactSheet.Shrink(raw, W, H, ThumbW, ThumbH);
        }
    }
}
