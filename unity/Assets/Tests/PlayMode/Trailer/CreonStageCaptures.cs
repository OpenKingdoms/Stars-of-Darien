// CreonStageCaptures.cs - the Creon stages on the real engine, filmed
// offscreen. Runs only when OKU_CREON_DIR names a folder:
//   OKU_CREON_SCENES  stills, palace, buildings, houses, trees, small, or all
// stills/ has each stage set down alone, in the classic view and turned,
// and shots/ a film of each group shelled from whole to its last stage.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OpenKingdomsUnity.Tests.Trailer
{
    public class CreonStageCaptures
    {
        TrailerDirector director;

        [TearDown]
        public void CleanUp()
        {
            director?.TearDown();
            director = null;
        }

        [UnityTest, Timeout(int.MaxValue)]
        public IEnumerator FilmTheCreonStages()
        {
            string dir = Environment.GetEnvironmentVariable("OKU_CREON_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_CREON_DIR to film the Creon stages");
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            LogAssert.ignoreFailingMessages = true;
            string ffmpeg = Environment.GetEnvironmentVariable("OKU_TRAILER_FFMPEG");
            director = new TrailerDirector(dir, string.IsNullOrEmpty(ffmpeg) ? "ffmpeg" : ffmpeg);
            yield return director.CreonStages(Environment.GetEnvironmentVariable("OKU_CREON_SCENES"));
        }
    }

    public sealed partial class TrailerDirector
    {
        const string CreonField = "edmont's field";

        public static readonly string[] CreonStageNames =
        {
            "CreBuild01a", "CreBuild01b", "CreBuild02a", "CreBuild02b", "CreBuild03a", "CreBuild03b",
            "CreBuild04a", "CreBuild04b", "CreBuild05a", "CreBuild05b", "CreBuild06a", "CreBuild06b",
            "CreBuild07a", "CreBuild07b", "CreBuild08a", "CreBuild08b", "CreBuild09a", "CreBuild09b",
            "CreHouse01a", "CreHouse02a", "CreHouse03a", "CreHouse04a", "CreHouse05a", "CreHouse06a", "CreHouse07a",
            "CreTree01a", "CreTree03a", "CreTree04a", "CreTree05a", "CreTree06a",
            "CreTreesmudge01", "CreTreesmudge02", "CreTreesmudge03", "CreTreesmudge04", "CreTreesmudge05", "CreTreesmudge06", "CreTreesmudge07",
            "CreInvent01a", "CreInvent02a", "CreInvent03a", "CreInvent04a",
            "CreFence01a", "CreFence02a", "CreFence03a", "CreFence04a",
            "CrePlant01a", "CrePlant02a", "CrePlant03a", "CreCart01a", "CreShed01a", "CreWell01a",
        };

        public IEnumerator CreonStages(string which)
        {
            Directory.CreateDirectory(OutDir);
            var want = new HashSet<string>((which ?? "all").Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0));
            yield return Boot();
            var scenes = new (string name, Func<IEnumerator> run)[]
            {
                ("stills", CreonStills),
                ("palace", () => CreonFall("creon-palace", 50f, "CreBuild01")),
                ("buildings", () => CreonFall("creon-senate-observatory", 50f, "CreBuild03", "CreBuild06")),
                ("houses", () => CreonFall("creon-houses", 34f, "CreHouse01", "CreHouse03", "CreHouse06")),
                ("trees", () => CreonFall("creon-trees", 30f, "CreTree01", "CreTree03", "CreTree04", "CreTree05", "CreTree06")),
                ("small", () => CreonFall("creon-fence-cart-well", 20f, "CreFence01", "CreCart01", "CreWell01")),
            };
            foreach (var (name, run) in scenes)
            {
                if (!want.Contains("all") && !want.Contains(name)) continue;
                Note($"scene {name}");
                float began = Time.realtimeSinceStartup;
                yield return Safe(name, run());
                EndShot();
                Lift = 0f;
                Note($"scene {name} done in {Time.realtimeSinceStartup - began:0}s");
            }
            Time.captureFramerate = 0;
            if (rt != null) { rt.Release(); Object.Destroy(rt); rt = null; }
            if (tex != null) { Object.Destroy(tex); tex = null; }
        }

        FeatureDef FeatureNamed(string name) => B.FeatureDefs.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

        float CellSize => B.Terrain != null && B.Terrain.CellSize > 0f ? B.Terrain.CellSize : 1f;

        // A kind set down with its footprint's middle at a point: its index, or -1.
        int SetDown(FeatureDef d, Vector3 at)
        {
            float cell = CellSize;
            int cx = Mathf.RoundToInt(at.x / cell - d.Footprint.x * 0.5f), cz = Mathf.RoundToInt(-at.z / cell - d.Footprint.y * 0.5f);
            return B.PlaceFeature(d.Id, cx, cz);
        }

        // Every feature within reach of a point taken away, for a clear stage.
        void ClearAround(Vector3 at, float reach)
        {
            int n = B.ReadFeatures(featBuf);
            for (int i = n - 1; i >= 0; i--)
                if (Mathf.Abs(featBuf[i].Position.x - at.x) < reach && Mathf.Abs(featBuf[i].Position.z - at.z) < reach) B.RemoveFeature(i);
        }

        // The drop-in model's bounds in its own space.
        static Bounds ModelBounds(FeatureDef d)
        {
            var o = OverrideLoader.Find(OverrideKind.Feature, null, d.Name, d.SequenceName, d.ObjectName);
            var b = new Bounds(Vector3.up * 0.5f, Vector3.one);
            if (o == null) return b;
            bool first = true;
            foreach (var p in o.Parts)
            {
                if (p.Mesh == null) continue;
                var mb = p.Mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var c = p.NodeToRoot.MultiplyPoint3x4(corner);
                    if (first) { b = new Bounds(c, Vector3.zero); first = false; }
                    else b.Encapsulate(c);
                }
            }
            return b;
        }

        // Each stage alone on cleared grass, framed whole, in the classic view and turned.
        IEnumerator CreonStills()
        {
            yield return Battle(CreonField, WeatherChoice.Off, 60000, Seat.You("CREON", 0), Seat.Ai("TAROS", 1, FarStart(CreonField, 0)));
            var site = FlatGround(MapCentre, 40f, 16f);
            ClearAround(site, 26f);
            string dir = Path.Combine(OutDir, "stills");
            Directory.CreateDirectory(dir);
            float half = Mathf.Sin(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            foreach (string name in CreonStageNames)
            {
                var d = FeatureNamed(name);
                if (d == null) { Note($"stills: no feature {name}"); continue; }
                int index = SetDown(d, site);
                if (index < 0) { Note($"stills: {name} would not stand at {site}"); continue; }
                for (int i = 0; i < 4; i++) yield return null;
                B.ReadFeatures(featBuf);
                var f = featBuf[index];
                var bounds = ModelBounds(d);
                var middle = f.Position + EntityRenderer.ModelTurn(f.Heading) * bounds.center;
                float r = Mathf.Max(0.6f, bounds.extents.magnitude);
                Lift = middle.y - Gc.SmoothGround(middle);
                foreach (var (tag, pitch, yaw) in new[] { ("classic", GameCamera.ClassicPitch, 0f), ("turned", 30f, 35f) })
                {
                    Pose(Look(middle, r * 1.1f / half, pitch, yaw));
                    for (int i = 0; i < 4; i++) yield return null;
                    GrabWorld();
                    File.WriteAllBytes(Path.Combine(dir, $"{name}_{tag}.png"), tex.EncodeToPNG());
                }
                Lift = 0f;
                B.RemoveFeature(index);
                for (int i = 0; i < 3; i++) yield return null;
                Note($"stills: {name} radius {r:0.0} at {middle}");
            }
        }

        // The kind standing nearest a spot, within two cells, or null.
        FeatureDef StandingAt(Vector3 at, int n)
        {
            FeatureDef best = null;
            float near = 2f * CellSize;
            for (int i = 0; i < n; i++)
            {
                var p = featBuf[i].Position;
                float dist = new Vector2(p.x - at.x, p.z - at.z).magnitude;
                if (dist < near && featBuf[i].Def >= 0 && featBuf[i].Def < B.FeatureDefs.Count) { near = dist; best = B.FeatureDefs[featBuf[i].Def]; }
            }
            return best;
        }

        // Kinds set down in a row on open grass and shelled by cannon until
        // each stands as its last stage, filmed past the guns.
        IEnumerator CreonFall(string shot, float distance, params string[] names)
        {
            yield return Battle(CreonField, WeatherChoice.Off, 60000, Seat.You("CREON", 0), Seat.Ai("TAROS", 1, FarStart(CreonField, 0)));
            int me = PlayerOf(0);
            var kinds = names.Select(FeatureNamed).Where(d => d != null).ToList();
            float cell = CellSize;
            float span = kinds.Sum(d => d.Footprint.x + 2f) * cell;
            var site = FlatGround(Vector3.Lerp(Start(0), MapCentre, 0.45f), 24f, Mathf.Max(8f, span * 0.5f + 4f));
            ClearAround(site, span * 0.5f + 14f);
            var targets = new List<Vector3>();
            float x = site.x - span * 0.5f;
            foreach (var d in kinds)
            {
                float w = (d.Footprint.x + 2f) * cell;
                var at = Ground(x + w * 0.5f, site.z);
                if (SetDown(d, at) >= 0) targets.Add(at);
                else Note($"{shot}: {d.Name} would not stand at {at}");
                x += w;
            }
            Note($"{shot}: {targets.Count} of {kinds.Count} set down round {site}");
            if (targets.Count == 0) yield break;
            for (int f = 0; f < 1800 && Root.World.Entities.Fractures.Waiting > 0; f++) yield return null;

            var army = Place(me, "ARACAN", 10);
            var back = Start(0) - site;
            back.y = 0f;
            back = back.sqrMagnitude > 0.01f ? back.normalized : Vector3.back;
            var spot = FlatGround(site + back * (span * 0.5f + 14f), 8f, 5f);
            Aggro(army, 0);
            March(army, spot, YawOf(site - spot), 5, 1.8f);
            yield return WaitArrive(army, spot, 4f, B.TicksPerSecond * 150);
            Aggro(army, 2);

            float yaw = YawOf(site - spot) + 30f;
            var from = Look(site, distance, 30f, yaw);
            var to = Look(site, distance * 0.88f, 33f, yaw + 10f);
            Lift = Mathf.Min(4f, kinds.Max(d => ModelBounds(d).size.y) * 0.3f);
            List<int> aimed = null;
            bool Done(int f)
            {
                if (f % 30 != 0) return false;
                int n = B.ReadFeatures(featBuf);
                var left = new List<int>();
                for (int t = 0; t < targets.Count; t++)
                {
                    var d = StandingAt(targets[t], n);
                    if (d != null && d.Breakable) left.Add(t);
                }
                if (left.Count == 0)
                {
                    foreach (int h in army) B.Command(GameCommand.To(CommandKind.Stop, h, Vector3.zero));
                    Note($"{shot}: every kind stands as its last stage at frame {f}");
                    return true;
                }
                if (aimed == null || !left.SequenceEqual(aimed))
                {
                    for (int i = 0; i < army.Count; i++)
                        B.Command(GameCommand.To(CommandKind.AttackGround, army[i], targets[left[i % left.Count]]));
                    aimed = left;
                }
                return false;
            }
            yield return ShotUntil(shot, 50f, 5f, TrailerKit.Move(from, to), Done);
            int m = B.ReadFeatures(featBuf);
            Note($"{shot}: standing at the end " + string.Join(", ", targets.Select(t => StandingAt(t, m)?.Name ?? "nothing")));
        }

        // A shot that runs until `done` holds and `tail` seconds more, at most `most` seconds.
        IEnumerator ShotUntil(string name, float most, float tail, Func<float, ShotPose> path, Func<int, bool> done)
        {
            int frames = Mathf.Max(2, Mathf.RoundToInt(most * Fps)), end = frames;
            Pose(path(0));
            for (int i = 0; i < 6; i++) yield return null;
            BeginShot(name);
            for (int f = 0; f < end; f++)
            {
                Frame = f;
                if (end == frames && done(f)) end = Mathf.Min(frames, f + Mathf.RoundToInt(tail * Fps));
                Pose(path(f / (float)(frames - 1)));
                yield return null;
                if (!Playing) break;
                Cam.rect = new Rect(0, 0, 1, 1);
                GrabWorld();
                sink.Write(tex.GetRawTextureData<byte>());
                Tap?.Frame(f);
                if (f % Fps == 0) Still(f);
            }
            EndShot();
        }
    }
}
