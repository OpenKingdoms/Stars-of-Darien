// FractureModelsTests.cs - every hand-built and painted feature model split
// as a battle's loading splits them, a slice at a time, to measure the
// budget on real models. Runs only when OKU_SPLIT_MODELS=1.
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class FractureModelsTests
    {
        static BreakKind Guess(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("tree")) return BreakKind.Tree;
            if (n.Contains("wall") || n.Contains("wl")) return BreakKind.Wall;
            if (n.Contains("hut") || n.Contains("shed") || n.Contains("well")) return BreakKind.Hut;
            if (n.Contains("build") || n.Contains("house") || n.Contains("tow") || n.Contains("ruin") || n.Contains("dev")) return BreakKind.Building;
            return BreakKind.Scatter;
        }

        [Test, Timeout(1800000)]
        public void EveryFeatureModelSplitsInsideTheLoadingBudget()
        {
            if (System.Environment.GetEnvironmentVariable("OKU_SPLIT_MODELS") != "1") Assert.Ignore("set OKU_SPLIT_MODELS=1 to split every feature model");
            string dir = Path.Combine(OverrideLoader.ProjectDir, "Assets", "Overrides", "Features");
            var files = Directory.GetFiles(dir, "*.glb");
            var templates = new List<GameObject>();
            var parts = new Dictionary<long, List<KindPart>>();
            int triangles = 0;
            var load = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < files.Length; i++)
            {
                var go = GlbLoader.Load(files[i], out _);
                if (go == null) continue;
                templates.Add(go);
                var model = OverrideModel.From(go, null, files[i]);
                var list = new List<KindPart>();
                foreach (var p in model.Parts)
                {
                    list.Add(new KindPart { Mesh = p.Mesh, Submesh = p.Submesh, Material = p.Material, Local = p.NodeToRoot, Flat = p.Flat });
                    if (!p.Flat && p.Mesh != null && p.Submesh < p.Mesh.subMeshCount) triangles += (int)p.Mesh.GetIndexCount(p.Submesh) / 3;
                }
                parts[i] = list;
            }
            double loadMs = load.Elapsed.TotalMilliseconds;
            var cache = new FractureCache((key, into) => { into.AddRange(parts[key]); return true; });
            foreach (var kv in parts)
            {
                string name = Path.GetFileNameWithoutExtension(files[kv.Key]);
                cache.Ask(kv.Key, name, Guess(name), Interior.Stone);
            }
            const double budget = 20.0;
            int slices = 0;
            double worst = 0;
            var clock = new System.Diagnostics.Stopwatch();
            bool done;
            do
            {
                clock.Restart();
                done = cache.Work(budget);
                worst = System.Math.Max(worst, clock.Elapsed.TotalMilliseconds);
                slices++;
            } while (!done && slices < 100000);
            int chunks = 0;
            foreach (var kv in parts) chunks += cache.Get(kv.Key)?.Count ?? 0;
            Debug.Log($"Split {cache.Ready} of {files.Length} feature models, {triangles} triangles, into {chunks} chunks: {cache.SpentMs:0} ms in {slices} slices of {budget} ms, " +
                      $"the slowest slice {worst:0.0} ms and the slowest step {cache.LongestStepMs:0.0} ms, {cache.LongestStep}, {cache.Grouped} kinds broken along their parts (models read in {loadMs:0} ms)");
            cache.Dispose();
            foreach (var go in templates) Object.DestroyImmediate(go);
            Assert.IsTrue(done);
            Assert.Less(cache.LongestStepMs, 25.0, "no single step stalls a loading frame");
        }
    }
}
