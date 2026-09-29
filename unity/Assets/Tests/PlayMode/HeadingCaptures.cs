// HeadingCaptures.cs - which way things face on the real engine: a
// lodestone with its card model beside its ghost and the original card, a
// feature with a drop-in model beside its sprite, and a monarch after a
// formation move toward the camera, from the classic camera and a low
// three-quarter view. Runs only when OKU_HEADING_SHOTS names a folder, and
// writes heading-*.png and heading.txt there.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class HeadingCaptures
    {
        GameRoot root;
        readonly List<string> log = new List<string>();

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator LodestoneFeatureAndFormation()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_HEADING_SHOTS");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_HEADING_SHOTS to capture headings");
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            Directory.CreateDirectory(dir);
            var view = GameObject.Find("OpenKingdoms");
            if (view != null) Object.Destroy(view);
            yield return null;
            root = GameRoot.Boot();
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "two castles";
            root.Setup.MapRevealed = true;
            root.Setup.LineOfSight = false;
            root.Setup.StartMana = 5000;
            root.Setup.Seats[0].Side = "ARAMON";
            root.Setup.Seats[1].Side = "TAROS";
            root.Setup.Seats[1].Difficulty = AiDifficulty.Easy;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 240f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
            root.Orders.Frozen = true;
            root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            var b = root.Backend;
            var monarch = Own().First(u => !b.UnitDefs[u.Def].IsBuilding && b.UnitDefs[u.Def].BuildOptions.Length > 0);

            // Which way a walk east reads.
            b.Command(GameCommand.To(CommandKind.Move, monarch.Handle, monarch.Position + new Vector3(6f, 0f, 0f)));
            b.Advance(40);
            var walking = Find(monarch.Handle);
            log.Add($"walking east from {monarch.Position} to {walking.Position}: heading {walking.Heading:0.#}");
            b.Command(GameCommand.To(CommandKind.Stop, monarch.Handle, Vector3.zero));
            b.Advance(5);

            // The lodestone, on the site nearest the monarch.
            var options = b.UnitDefs[monarch.Def].BuildOptions;
            int lode = options.First(o => b.UnitDefs[o].IsBuilding && b.UnitDefs[o].Name.ToUpperInvariant().Contains("LODE"));
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
            log.Add($"lodestone {b.UnitDefs[lode].ObjectName} at {built.Position}: heading {built.Heading:0.#}, facing {built.Facing}, card {(CardOverride.For(b.UnitDefs[lode].ObjectName) != null ? "yes" : "no")}");
            // Out of the picture.
            b.Command(GameCommand.To(CommandKind.Move, monarch.Handle, built.Position + new Vector3(-9f, 0f, 3f)));
            b.Advance(400);
            root.World.Entities.Ghost = new EntityRenderer.GhostState { Def = lode, At = built.Position + new Vector3(4f, 0f, 0f), Ok = true, Facing = 0 };
            yield return Shots(dir, "lode", built.Position + new Vector3(2f, 0f, 0f), 16f);
            root.World.Entities.Ghost = null;
            // The original's card in place of the model.
            var cache = (Dictionary<string, CardOverride>)typeof(CardOverride).GetField("cache", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            string obj = b.UnitDefs[lode].ObjectName;
            cache[obj] = null;
            yield return Shots(dir, "lode-original", built.Position, 12f);
            cache.Remove(obj);

            // A sprite feature with a drop-in model, a hand-made one first.
            // A cart set down beside the lodestone, where the map allows it.
            var cart = b.FeatureDefs.FirstOrDefault(d => string.Equals(d.Name, "Aracart01", System.StringComparison.OrdinalIgnoreCase));
            int placed = cart != null ? b.PlaceFeature(cart.Id, Mathf.FloorToInt(built.Position.x) - 6, Mathf.FloorToInt(-built.Position.z) + 1) : -1;
            log.Add($"cart {(cart != null ? cart.Name : "none")} placed as feature {placed}");
            nf = b.ReadFeatures(features);
            var handMade = new HashSet<string>(Directory.GetFiles(Path.Combine(OverrideLoader.ProjectDir, OverrideIndex.Folder(OverrideKind.Feature)), "*.glb").Select(Path.GetFileNameWithoutExtension), System.StringComparer.OrdinalIgnoreCase);
            FeatureState pick = default;
            int rank = int.MaxValue;
            for (int i = 0; i < nf; i++)
            {
                var f = features[i];
                if (f.Model >= 0 || f.Sprite < 0 || f.Def < 0 || f.Flat) continue;
                var fd = b.FeatureDefs[f.Def];
                var names = new[] { fd.Name, fd.SequenceName, fd.ObjectName };
                if (OverrideLoader.Find(OverrideKind.Feature, null, names) == null) continue;
                bool hand = names.Any(n => !string.IsNullOrEmpty(n) && handMade.Contains(n));
                // A cart or a hut shows plainly which way it faces.
                bool plain = fd.Name.IndexOf("cart", System.StringComparison.OrdinalIgnoreCase) >= 0 || fd.Name.IndexOf("hut", System.StringComparison.OrdinalIgnoreCase) >= 0;
                int r = (hand ? 0 : 2000000) + (plain ? 0 : 1000000) + (int)Mathf.Min(900000f, (f.Position - built.Position).sqrMagnitude);
                if (r < rank) { rank = r; pick = f; }
            }
            if (placed >= 0 && placed < nf) { pick = features[placed]; rank = 0; }
            Assert.Less(rank, int.MaxValue, "a feature with a drop-in model");
            var pd = b.FeatureDefs[pick.Def];
            log.Add($"feature {pd.Name} ({pd.SequenceName}) at {pick.Position}: heading {pick.Heading:0.#}, hand made {rank < 2000000}");
            SavePicture(b.Sprite(pick.Sprite), Path.Combine(dir, "heading-feature-sprite.png"));
            yield return Shots(dir, "feature", pick.Position, 10f);

            // The monarch in formation, told to face the camera.
            monarch = Find(monarch.Handle);
            var to = new Vector2(monarch.Position.x + 2f, monarch.Position.z - 4f);
            Assert.IsTrue(b.MoveFormation(new[] { monarch.Handle }, new[] { to }, 180f, false, false));
            for (int t = 0; t < 60 * 30; t += 30)
            {
                b.Advance(30);
                monarch = Find(monarch.Handle);
                if (Vector2.Distance(new Vector2(monarch.Position.x, monarch.Position.z), to) < 0.3f && Mathf.Abs(Mathf.DeltaAngle(monarch.Heading, 180f)) < 3f) break;
            }
            log.Add($"monarch in formation toward the camera at {monarch.Position}: heading {monarch.Heading:0.#}");
            yield return Shots(dir, "formation", monarch.Position, 12f);

            File.WriteAllLines(Path.Combine(dir, "heading.txt"), log);
            foreach (var line in log) Debug.Log(line);
        }

        UnitState Find(int handle) => All().First(u => u.Handle == handle);

        IEnumerable<UnitState> All()
        {
            var units = new UnitState[4096];
            int n = root.Backend.ReadUnits(units);
            for (int i = 0; i < n; i++) yield return units[i];
        }

        IEnumerable<UnitState> Own() => All().Where(u => u.Player == root.Backend.LocalPlayer && (u.Flags & UnitFlags.Dying) == 0);

        // From the classic camera, then low from the south-east and from the north.
        IEnumerator Shots(string dir, string name, Vector3 at, float distance)
        {
            var cam = root.World.Camera;
            cam.focus = at;
            cam.yaw = 0;
            cam.pitch = GameCamera.ClassicPitch;
            cam.Zoom(distance);
            yield return Shoot(Path.Combine(dir, $"heading-{name}-classic.png"));
            cam.yaw = -35f;
            cam.pitch = 18f;
            cam.Zoom(distance);
            yield return Shoot(Path.Combine(dir, $"heading-{name}-low.png"));
            cam.yaw = 180f;
            cam.Zoom(distance);
            yield return Shoot(Path.Combine(dir, $"heading-{name}-north.png"));
        }

        static IEnumerator Shoot(string path)
        {
            for (int i = 0; i < 4; i++) yield return null;
            const int W = 1280, H = 720;
            var cam = Camera.main;
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

        static void SavePicture(RgbaImage img, string path)
        {
            if (img == null) return;
            var tex = new Texture2D(img.Width, img.Height, TextureFormat.RGBA32, false);
            var px = new Color32[img.Width * img.Height];
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                {
                    int s = ((img.Height - 1 - y) * img.Width + x) * 4;
                    px[y * img.Width + x] = new Color32(img.Pixels[s], img.Pixels[s + 1], img.Pixels[s + 2], img.Pixels[s + 3]);
                }
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
