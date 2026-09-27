// TerrainBuilderTests.cs - the ground meshes sit on the height grid, the
// region's picture takes each block's chunk square, and neighbouring
// blocks meet with no seam.
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class TerrainBuilderTests
    {
        // A 40 by 24 cell map: 2 cells to a block, 8 texels to a block,
        // 4 by 4 blocks to a 32 texel chunk.
        static MapTerrain Small(out RgbaImage[] chunks)
        {
            var t = new MapTerrain { HeightsW = 41, HeightsH = 25, CellSize = 1, SeaLevel = 1, BlockSize = 2, BlockTexels = 8 };
            t.Heights = new float[t.HeightsW * t.HeightsH];
            for (int z = 0; z < t.HeightsH; z++)
                for (int x = 0; x < t.HeightsW; x++)
                    t.Heights[z * t.HeightsW + x] = x * 0.1f + z * 0.05f;
            t.BlocksW = 20; t.BlocksH = 12;
            int cw = 5, ch = 3;
            t.ChunkCount = cw * ch;
            t.Blocks = new int[t.BlocksW * t.BlocksH * 3];
            chunks = new RgbaImage[t.ChunkCount];
            for (int c = 0; c < t.ChunkCount; c++)
            {
                chunks[c] = new RgbaImage(32, 32);
                for (int i = 0; i < 32 * 32; i++) { chunks[c].Pixels[i * 4] = (byte)(c * 10); chunks[c].Pixels[i * 4 + 3] = 255; }
            }
            for (int by = 0; by < t.BlocksH; by++)
                for (int bx = 0; bx < t.BlocksW; bx++)
                {
                    int b = by * t.BlocksW + bx;
                    t.Blocks[3 * b] = (by / 4) * cw + bx / 4;
                    t.Blocks[3 * b + 1] = (bx % 4) * 8;
                    t.Blocks[3 * b + 2] = (by % 4) * 8;
                }
            return t;
        }

        [Test]
        public void RegionsCoverTheMap()
        {
            var t = Small(out _);
            Assert.AreEqual(2, TerrainBuilder.RegionsW(t));
            Assert.AreEqual(1, TerrainBuilder.RegionsH(t));
            Assert.AreEqual(2, TerrainBuilder.SamplesPerBlock(t));
        }

        [Test]
        public void DetailVerticesSitOnTheHeights()
        {
            var t = Small(out _);
            var d = TerrainBuilder.Detail(t, 0, 0);
            // Region 0 is 16 by 12 blocks, samples 0..32 by 0..24.
            Assert.AreEqual(33 * 25, d.Vertices.Count);
            foreach (var v in d.Vertices)
            {
                int x = Mathf.RoundToInt(v.x), z = Mathf.RoundToInt(-v.z);
                Assert.AreEqual(t.HeightAt(x, z), v.y, 1e-5f);
                Assert.LessOrEqual(v.z, 0f, "the map lies at z <= 0");
            }
            foreach (var uv in d.Uvs) { Assert.That(uv.x, Is.InRange(0f, 1f)); Assert.That(uv.y, Is.InRange(0f, 1f)); }
            Assert.AreEqual(1, d.Triangles.Count, "one picture for the region");
            foreach (int i in d.Triangles[-1]) Assert.That(i, Is.InRange(0, d.Vertices.Count - 1));
            Assert.AreEqual(32 * 24 * 2, d.Triangles[-1].Count / 3);
        }

        [Test]
        public void DetailTrianglesFaceUp()
        {
            var t = Small(out _);
            var d = TerrainBuilder.Detail(t, 1, 0);
            foreach (var kv in d.Triangles)
                for (int i = 0; i < kv.Value.Count; i += 3)
                {
                    var a = d.Vertices[kv.Value[i]]; var b = d.Vertices[kv.Value[i + 1]]; var c = d.Vertices[kv.Value[i + 2]];
                    // Unity's front faces are clockwise, so the cross product points up.
                    Assert.Greater(Vector3.Cross(b - a, c - a).y, 0f);
                }
        }

        [Test]
        public void TextureCoordinatesRunStraightAcrossBlocks()
        {
            var t = Small(out _);
            var d = TerrainBuilder.Detail(t, 0, 0);
            // A region is 16 blocks of 2 samples, 32 samples across. Sample
            // (10, 12) sits on a block corner, at texel (40, 48) of 128.
            int row = 33;
            var uv = d.Uvs[12 * row + 10];
            Assert.AreEqual(10f / 32f, uv.x, 1e-6f);
            Assert.AreEqual(1f - 12f / 32f, uv.y, 1e-6f);
            Assert.AreEqual(40f / TerrainBuilder.RegionTexels(t), uv.x, 1e-6f, "a block edge falls on a texel edge");
        }

        [Test]
        public void CoarseLevelHasSkirtsAndMatchesHeights()
        {
            var t = Small(out _);
            var d = TerrainBuilder.Coarse(t, 0, 0, 4);
            // Region 0 spans samples 0..32 by 0..24: 9 by 7 grid, plus a skirt ring.
            int grid = 9 * 7, ring = 2 * 9 + 2 * 7;
            Assert.AreEqual(grid + ring, d.Vertices.Count);
            for (int i = 0; i < grid; i++)
            {
                var v = d.Vertices[i];
                Assert.AreEqual(t.HeightAt(Mathf.RoundToInt(v.x), Mathf.RoundToInt(-v.z)), v.y, 1e-5f);
            }
            for (int i = grid; i < d.Vertices.Count; i++)
                Assert.Less(d.Vertices[i].y, t.HeightAt(0, 0) + 10f - TerrainBuilder.SkirtDepth + 0.01f);
            Assert.IsTrue(d.Triangles.ContainsKey(-1));
        }

        [Test]
        public void TheRegionPictureTakesEachBlocksSquare()
        {
            var t = Small(out var chunks);
            var img = TerrainBuilder.RegionPicture(t, 1, 0, c => chunks[c]);
            int size = TerrainBuilder.RegionTexels(t);
            Assert.AreEqual(16 * 8, size);
            Assert.AreEqual(size, img.Width);
            // Region 1 starts at block 16, chunk column 4. Texel (1, 1) is
            // block (16, 0), chunk 4, and texel (1, 33) is block (16, 4), chunk 9.
            Assert.AreEqual(40, img.Pixels[(1 * size + 1) * 4]);
            Assert.AreEqual(90, img.Pixels[(33 * size + 1) * 4]);
            // Region 1 holds only 4 blocks across. Past them the edge repeats.
            Assert.AreEqual(img.Pixels[(1 * size + 31) * 4], img.Pixels[(1 * size + 100) * 4]);
            Assert.AreEqual(255, img.Pixels[(100 * size + 100) * 4 + 3], "past the map is never clear");
        }

        [Test]
        public void NeighbouringSquaresJoinWithNoSeam()
        {
            // Two blocks side by side from neighbouring squares of one chunk
            // whose red counts texels: the picture counts straight through.
            var t = new MapTerrain { HeightsW = 5, HeightsH = 3, CellSize = 1, BlockSize = 2, BlockTexels = 8, BlocksW = 2, BlocksH = 1, ChunkCount = 1 };
            t.Heights = new float[t.HeightsW * t.HeightsH];
            t.Blocks = new[] { 0, 8, 0, 0, 16, 0 };
            var chunk = new RgbaImage(32, 8);
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 32; x++) { chunk.Pixels[(y * 32 + x) * 4] = (byte)x; chunk.Pixels[(y * 32 + x) * 4 + 3] = 255; }
            var img = TerrainBuilder.RegionPicture(t, 0, 0, c => chunk);
            for (int x = 0; x < 16; x++) Assert.AreEqual(8 + x, img.Pixels[(3 * img.Width + x) * 4], $"texel {x}");
        }
    }
}
