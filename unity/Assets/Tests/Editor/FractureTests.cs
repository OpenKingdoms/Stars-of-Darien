// FractureTests.cs - the splitter cuts a kind the same way every time, keeps
// every triangle, makes as many chunks as its kind asks, and splits a
// field's kinds in slices that stay inside the loading budget.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class FractureTests
    {
        // A box of size, each face cut into n by n quads, in two material slots.
        static FractureSource Box(Vector3 size, int n)
        {
            var src = new FractureSource();
            Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var nrm in normals)
            {
                var a = nrm.x != 0 ? Vector3.up : Vector3.right;
                var b = Vector3.Cross(nrm, a);
                int first = src.Positions.Count;
                for (int j = 0; j <= n; j++)
                    for (int i = 0; i <= n; i++)
                    {
                        var p = nrm * 0.5f + a * (i / (float)n - 0.5f) + b * (j / (float)n - 0.5f);
                        src.Positions.Add(Vector3.Scale(p, size) + Vector3.up * size.y * 0.5f);
                        src.Normals.Add(nrm);
                        src.Uvs.Add(new Vector2(i / (float)n, j / (float)n));
                        src.Colors.Add(new Color32(200, 180, 160, 255));
                    }
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        int v = first + j * (n + 1) + i;
                        int slot = nrm.y > 0 ? 1 : 0;
                        src.Triangles.AddRange(new[] { v, v + n + 1, v + 1 });
                        src.Slots.Add(slot);
                        src.Triangles.AddRange(new[] { v + 1, v + n + 1, v + n + 2 });
                        src.Slots.Add(slot);
                    }
            }
            src.SlotCount = 2;
            return src;
        }

        static float Area(FractureSource s)
        {
            float a = 0f;
            for (int t = 0; t < s.TriangleCount; t++)
            {
                var p0 = s.Positions[s.Triangles[t * 3]];
                a += Vector3.Cross(s.Positions[s.Triangles[t * 3 + 1]] - p0, s.Positions[s.Triangles[t * 3 + 2]] - p0).magnitude * 0.5f;
            }
            return a;
        }

        [Test]
        public void AKindSplitsTheSameWayEveryTime()
        {
            var a = Fracture.Split(Box(new Vector3(4f, 2f, 0.6f), 4), "AraWall01", 16);
            var b = Fracture.Split(Box(new Vector3(4f, 2f, 0.6f), 4), "AraWall01", 16);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Centre, b[i].Centre, "chunk " + i);
                CollectionAssert.AreEqual(a[i].Positions, b[i].Positions, "chunk " + i);
                CollectionAssert.AreEqual(a[i].Normals, b[i].Normals);
                CollectionAssert.AreEqual(a[i].Uvs, b[i].Uvs);
                CollectionAssert.AreEqual(a[i].Slots, b[i].Slots);
                for (int s = 0; s < a[i].Triangles.Length; s++) CollectionAssert.AreEqual(a[i].Triangles[s], b[i].Triangles[s]);
            }
            var other = Fracture.Split(Box(new Vector3(4f, 2f, 0.6f), 4), "AraWall02", 16);
            Assert.IsFalse(other.Count == a.Count && other.Zip(a, (x, y) => x.Centre == y.Centre).All(same => same), "another kind splits another way");
        }

        [Test]
        public void ChunksKeepEveryTriangleAndTheirMaterials()
        {
            var src = Box(new Vector3(3f, 3f, 3f), 3);
            var chunks = Fracture.Split(src, "CreHut01", 20);
            Assert.AreEqual(20, chunks.Count, "a box splits as often as asked");
            Assert.AreEqual(Area(src), chunks.Sum(c => c.Area), Area(src) * 1e-3f, "no surface lost or made");
            var bounds = src.Bounds();
            // Chunks are drawn a hair larger about their middles to close their seams.
            bounds.Expand(bounds.size.magnitude * 0.01f);
            foreach (var c in chunks)
            {
                foreach (var p in c.Positions) Assert.IsTrue(bounds.Contains(p + c.Centre), "inside the model");
                Assert.IsTrue(c.Slots.All(s => s == 0 || s == 1));
                Assert.Greater(c.Support.Length, 3, "points to rest on");
            }
            Assert.IsTrue(chunks.Any(c => c.Slots.Contains(1)), "the top keeps its own material");
        }

        // One box as its own part of a model.
        static void AddBox(FractureSource src, Vector3 centre, Vector3 size, int part)
        {
            Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var n in normals)
            {
                var a = n.x != 0 ? Vector3.up : Vector3.right;
                var b = Vector3.Cross(n, a);
                int first = src.Positions.Count;
                for (int i = 0; i < 4; i++)
                {
                    float sa = i == 1 || i == 2 ? 1 : -1, sb = i >= 2 ? 1 : -1;
                    src.Positions.Add(centre + Vector3.Scale((n + a * sa + b * sb) * 0.5f, size));
                    src.Normals.Add(n);
                    src.Uvs.Add(Vector2.zero);
                    src.Colors.Add(new Color32(120, 90, 60, 255));
                }
                src.Triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
                src.Slots.Add(0);
                src.Slots.Add(0);
                src.Parts.Add(part);
                src.Parts.Add(part);
            }
            src.SlotCount = 1;
            src.PartCount = Mathf.Max(src.PartCount, part + 1);
        }

        [Test]
        public void AModelOfManyPartsBreaksAlongThem()
        {
            // A palisade of forty logs, each its own part.
            var src = new FractureSource();
            for (int k = 0; k < 40; k++) AddBox(src, new Vector3(k * 0.3f, 1f, 0f), new Vector3(0.25f, 2f, 0.25f), k);
            var job = new FractureJob(src, "TarPRWall04", 16);
            while (!job.Step()) { }
            Assert.IsTrue(job.Grouped, "broken along its logs");
            Assert.That(job.Chunks.Count, Is.InRange(12, 16));
            Assert.AreEqual(src.TriangleCount, job.Chunks.Sum(c => c.Triangles.Sum(t => t.Length)) / 3, "no log was cut");
            var cut = new FractureJob(Box(new Vector3(4f, 2f, 0.6f), 4), "AraWall01", 16);
            while (!cut.Step()) { }
            Assert.IsFalse(cut.Grouped, "a model of one piece is cut");
        }

        [Test]
        public void EachKindBreaksIntoItsShareOfChunks()
        {
            for (int i = 0; i < 20; i++)
            {
                string name = "kind" + i;
                int tree = Fracture.ChunkCount(BreakKind.Tree, new Vector3(2f, 4f, 2f), name);
                int wall = Fracture.ChunkCount(BreakKind.Wall, new Vector3(4f, 2f, 1f), name);
                int hut = Fracture.ChunkCount(BreakKind.Hut, new Vector3(3f, 2f, 3f), name);
                int building = Fracture.ChunkCount(BreakKind.Building, new Vector3(8f, 6f, 8f), name);
                Assert.That(tree, Is.InRange(6, 10));
                Assert.That(wall, Is.InRange(12, 24));
                Assert.That(hut, Is.InRange(12, 24));
                Assert.That(building, Is.InRange(16, 40));
                Assert.AreEqual(tree, Fracture.ChunkCount(BreakKind.Tree, new Vector3(2f, 4f, 2f), name), "seeded by the name");
            }
            Assert.Less(Fracture.ChunkCount(BreakKind.Wall, new Vector3(4f, 2f, 1f), "w", 0.5f), Fracture.ChunkCount(BreakKind.Wall, new Vector3(4f, 2f, 1f), "w", 1f), "Low makes fewer");
        }

        [Test]
        public void KindsAreToldApartByTheirData()
        {
            FeatureDef D(string cat, string name = "x", string obj = "") => new FeatureDef { Category = cat, Name = name, ObjectName = obj };
            Assert.AreEqual(BreakKind.Tree, Fracture.KindOf(D("trees")));
            Assert.AreEqual(BreakKind.Wall, Fracture.KindOf(D("walls")));
            Assert.AreEqual(BreakKind.Hut, Fracture.KindOf(D("dwellings", "ZonHut01")));
            Assert.AreEqual(BreakKind.Hut, Fracture.KindOf(D("misc", "CreWell01")));
            Assert.AreEqual(BreakKind.Building, Fracture.KindOf(D("buildings")));
            Assert.AreEqual(BreakKind.Building, Fracture.KindOf(D("towers", "AraTow01")));
            Assert.AreEqual(BreakKind.Building, Fracture.KindOf(D("misc", "CreInvent01")));
            Assert.AreEqual(BreakKind.Body, Fracture.KindOf(D("corpse", "VERMAN_DEAD")));
            Assert.AreEqual(BreakKind.Body, Fracture.KindOf(D("", "araarch_dead", "araarch_dead")), "a unit's wreck has no category");
            Assert.AreEqual(BreakKind.Rock, Fracture.KindOf(D("rocks")));
            Assert.AreEqual(BreakKind.Rock, Fracture.KindOf(D("mana", "AraLode01")));
            Assert.AreEqual(BreakKind.None, Fracture.KindOf(D("waves")));
            Assert.AreEqual(BreakKind.Scatter, Fracture.KindOf(D("rural hooha", "VerFence01")));
            Assert.AreEqual(BreakKind.Scatter, Fracture.KindOf(D("special", "Aracrop01")));
            Assert.AreEqual(Interior.Ice, Fracture.InteriorOf(D("corpses", "ArafrozenAcolyte"), BreakKind.Body));
            Assert.AreEqual(Interior.Stone, Fracture.InteriorOf(D("walls"), BreakKind.Wall));
            Assert.AreEqual(Interior.Wood, Fracture.InteriorOf(D("trees"), BreakKind.Tree));
        }

        // A readable mesh of the box, as a drop-in model's parts would be.
        static Mesh BoxMesh(Vector3 size, int n)
        {
            var src = Box(size, n);
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(src.Positions);
            m.SetNormals(src.Normals);
            m.SetUVs(0, src.Uvs);
            m.SetColors(src.Colors);
            m.SetTriangles(src.Triangles, 0);
            return m;
        }

        [Test]
        public void AFieldOfKindsSplitsInSlicesInsideTheLoadingBudget()
        {
            var mesh = BoxMesh(new Vector3(6f, 5f, 6f), 16);
            var mat = Looks.Model(null);
            try
            {
                var cache = new FractureCache((key, into) =>
                {
                    into.Add(new KindPart { Mesh = mesh, Submesh = 0, Material = mat, Local = Matrix4x4.Translate(Vector3.right * (key % 7)) });
                    return true;
                });
                // The first split pays for compiling the splitter, as the first battle's loading does.
                cache.Ask(100, "warm", BreakKind.Building, Interior.Stone);
                while (!cache.Work(1000.0)) { }
                cache.ResetTimes();
                for (int k = 0; k < 30; k++) cache.Ask(k, "kind" + k, BreakKind.Building, Interior.Stone);
                const double budget = 20.0;
                int calls = 0;
                double worst = 0;
                var clock = new System.Diagnostics.Stopwatch();
                while (true)
                {
                    clock.Restart();
                    bool done = cache.Work(budget);
                    worst = System.Math.Max(worst, clock.Elapsed.TotalMilliseconds);
                    calls++;
                    if (done) break;
                    Assert.Less(calls, 10000);
                }
                Debug.Log($"Splitting 30 kinds of {mesh.triangles.Length / 3} triangles: {calls} slices, {cache.SpentMs:0} ms in all, the slowest slice {worst:0.0} ms and the slowest step {cache.LongestStepMs:0.0} ms");
                Assert.AreEqual(31, cache.Ready);
                Assert.Greater(calls, 1, "spread over frames");
                Assert.Less(cache.LongestStepMs, budget, "no single step takes a whole slice");
                Assert.Less(worst, budget + cache.LongestStepMs + 5.0, "a slice ends at its budget");
                var set = cache.Get(3);
                Assert.That(set.Count, Is.InRange(16, 40));
                Assert.IsFalse(set.Whole);
                Assert.IsTrue(set.Draws.All(d => d.All(x => x.Material.GetFloat("_Interior") > 0.5f)), "chunks show their inside");
                cache.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(mat);
            }
        }
    }
}
