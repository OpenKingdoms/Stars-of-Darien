// BreakCaptures.cs - frame strips and contact sheets of scenery breaking
// on the mock, rendered offscreen: a tree losing its crown and toppling, a
// wall coming down in two stages, a hut caving in, a stone body shattering
// and a routed army's pieces. Runs only when OKU_BREAK_DIR names a folder.
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
    public class BreakCaptures
    {
        static string Env(string k) => System.Environment.GetEnvironmentVariable(k);
        const int W = 960, H = 540;

        GameRoot root;
        MockBackend mock;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        [UnityTest, Timeout(3600000)]
        public IEnumerator CaptureBreaking()
        {
            string dir = Env("OKU_BREAK_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_BREAK_DIR to capture scenery breaking");
            Directory.CreateDirectory(dir);
            string only = Env("OKU_BREAK_SCENES");
            foreach (var scene in new[] { "tree", "wall", "hut", "body", "pieces" })
            {
                if (!string.IsNullOrEmpty(only) && !only.Split(',').Contains(scene)) continue;
                yield return Scene(dir, scene);
                Object.Destroy(root.gameObject);
                root = null;
                for (int i = 0; i < 5; i++) yield return null;
            }
        }

        IEnumerator Begin(bool war = false)
        {
            mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = war ? "mock_highlands" : "mock_frost";
            // A third kingdom, so the war goes on when one is routed.
            if (war && root.Setup.Seats.Count > 2)
            {
                root.Setup.Seats[2].Kind = SeatKind.Computer;
                root.Setup.Seats[2].Side = mock.Sides[2 % mock.Sides.Count].Id;
            }
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
        }

        int Def(string name) => mock.FeatureDefs.First(d => d.Name == name).Id;

        Vector3 At(int index)
        {
            var fs = new FeatureState[EntityRenderer.MaxFeatures];
            mock.ReadFeatures(fs);
            return fs[index].Position;
        }

        int Place(string kind, int dx = 0, int dz = 0)
        {
            var c = mock.StageCentre;
            return mock.PlaceFeature(Def(kind), Mathf.RoundToInt(c.x) + dx, Mathf.RoundToInt(-c.z) + dz);
        }

        void Shoot(Vector3 at) => mock.FireFx("ARACAN 1", at + new Vector3(0f, 1f, -8f), at);

        IEnumerator Frame(Vector3 focus, float distance, float yaw, float pitch)
        {
            var gc = root.World.Camera;
            gc.focus = focus;
            gc.yaw = yaw;
            gc.pitch = pitch;
            gc.Zoom(distance);
            for (int i = 0; i < 40; i++) yield return null;
        }

        IEnumerator Scene(string dir, string scene)
        {
            yield return Begin(scene == "pieces");
            var frames = new List<string>();
            string sub = Path.Combine(dir, scene);
            Directory.CreateDirectory(sub);
            foreach (var old in Directory.GetFiles(sub, "*.png")) File.Delete(old);
            int shot = 0;
            // Each tick drawn and saved, the shots fired on the ticks given.
            IEnumerator Roll(int ticks, System.Action<int> on = null)
            {
                for (int t = 0; t < ticks; t++)
                {
                    on?.Invoke(t);
                    mock.Advance(1);
                    yield return null;
                    string path = Path.Combine(sub, $"{scene}-{shot++:D4}.png");
                    yield return Grab(Camera.main, path);
                    frames.Add(path);
                }
            }
            switch (scene)
            {
                case "tree":
                {
                    int tree = Place("mock_tree");
                    var at = At(tree);
                    yield return Frame(at + Vector3.up, 9f, -90f, 25f);
                    yield return Roll(150, t => { if (t == 0) Shoot(at); });
                    // The crown sinks away while the dead tree stands, then it falls.
                    for (int t = 0; t < 30 * 15; t++) { mock.Advance(1); if (t % 30 == 0) yield return null; }
                    yield return Roll(120, t => { if (t == 0) Shoot(at); });
                    break;
                }
                case "wall":
                {
                    int wall = Place("mock_wall");
                    var at = At(wall);
                    yield return Frame(at + Vector3.up, 11f, -60f, 22f);
                    // Two hits weaken it out of view of the strip, the third breaks it.
                    Shoot(at);
                    for (int t = 0; t < 60; t++) { mock.Advance(1); yield return null; }
                    Shoot(at);
                    for (int t = 0; t < 60; t++) { mock.Advance(1); yield return null; }
                    yield return Roll(110, t => { if (t == 0) Shoot(at); });
                    Shoot(at);
                    for (int t = 0; t < 60; t++) { mock.Advance(1); yield return null; }
                    yield return Roll(110, t => { if (t == 0) Shoot(at); });
                    break;
                }
                case "hut":
                {
                    int hut = Place("mock_hut");
                    var at = At(hut);
                    yield return Frame(at + Vector3.up, 10f, -50f, 25f);
                    yield return Roll(150, t => { if (t == 0) Shoot(at); });
                    break;
                }
                case "body":
                {
                    int body = Place("mock_stone_body");
                    var at = At(body);
                    yield return Frame(at, 7f, -70f, 28f);
                    yield return Roll(90, t => { if (t == 0) Shoot(at); });
                    break;
                }
                case "pieces":
                {
                    int foe = mock.Players.First(p => !p.IsLocal).Index;
                    var units = new UnitState[EntityRenderer.MaxUnits];
                    int n = mock.ReadUnits(units);
                    var theirs = units.Take(n).Where(u => u.Player == foe).ToList();
                    var mid = theirs.Aggregate(Vector3.zero, (s, u) => s + u.Position) / Mathf.Max(1, theirs.Count);
                    yield return Frame(mid, 16f, -40f, 30f);
                    yield return Roll(120, t => { if (t == 6) mock.Rout(foe); });
                    break;
                }
            }
            Sheet(frames, Path.Combine(dir, $"{scene}-sheet.png"), 12);
            File.WriteAllText(Path.Combine(dir, $"{scene}-log.txt"),
                $"{scene}: {frames.Count} frames at 30 a second, {root.World.Entities.Fractures.Ready} kinds split, last break {root.World.Entities.Falls.LastKind} " +
                $"with {root.World.Entities.Falls.LastFlying} chunks flying and {root.World.Entities.Falls.LastHeld} waiting for the swap\n");
        }

        static IEnumerator Grab(Camera cam, string path)
        {
            var hdr = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            var rt = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var old = cam.targetTexture;
            float aspect = cam.aspect;
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
            cam.aspect = aspect;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
            yield break;
        }

        // Frames picked evenly through the strip, in a grid four across at half size.
        static void Sheet(List<string> frames, string path, int count)
        {
            if (frames.Count == 0) return;
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
