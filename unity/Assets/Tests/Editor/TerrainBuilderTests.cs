// TerrainBuilderTests.cs - the ground meshes sit on the height grid, point
// into the right chunk squares, and the coarse level and its baked picture
// agree with the detail.
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
            var t = Small(out var chunks);
            var d = TerrainBuilder.Detail(t, 0, 0, c => new Vector2Int(chunks[c].Width, chunks[c].Height));
            // 16 by 12 blocks in region 0, each a 3 by 3 grid of vertices.
            Assert.AreEqual(16 * 12 * 9, d.Vertices.Count);
            foreach (var v in d.Vertices)
            {
                int x = Mathf.RoundToInt(v.x), z = Mathf.RoundToInt(-v.z);
                Assert.AreEqual(t.HeightAt(x, z), v.y, 1e-5f);
                Assert.LessOrEqual(v.z, 0f, "the map lies at z <= 0");
            }
            foreach (var uv in d.Uvs) { Assert.That(uv.x, Is.InRange(0f, 1f)); Assert.That(uv.y, Is.InRange(0f, 1f)); }
            int tris = 0;
            foreach (var kv in d.Triangles)
            {
                Assert.That(kv.Key, Is.InRange(0, t.ChunkCount - 1));
                foreach (int i in kv.Value) Assert.That(i, Is.InRange(0, d.Vertices.Count - 1));
                tris += kv.Value.Count / 3;
            }
            Assert.AreEqual(16 * 12 * 8, tris);
        }

        [Test]
        public void DetailTrianglesFaceUp()
        {
            var t = Small(out var chunks);
            var d = TerrainBuilder.Detail(t, 1, 0, c => new Vector2Int(32, 32));
            foreach (var kv in d.Triangles)
                for (int i = 0; i < kv.Value.Count; i += 3)
                {
                    var a = d.Vertices[kv.Value[i]]; var b = d.Vertices[kv.Value[i + 1]]; var c = d.Vertices[kv.Value[i + 2]];
                    // Unity's front faces are clockwise, so the cross product points up.
                    Assert.Greater(Vector3.Cross(b - a, c - a).y, 0f);
                }
        }

        [Test]
        public void ABlockSamplesItsOwnSquare()
        {
            var t = Small(out var chunks);
            var d = TerrainBuilder.Detail(t, 0, 0, c => new Vector2Int(32, 32));
            // Block (5, 6) is in chunk (1, 1) at texel (8, 16); its first vertex
            // is the square's north-west corner.
            int blockIndexInRegion = 6 * 16 + 5;
            var uv = d.Uvs[blockIndexInRegion * 9];
            Assert.AreEqual((8 + 0.5f) / 32f, uv.x, 1e-5f);
            Assert.AreEqual(1f - (16 + 0.5f) / 32f, uv.y, 1e-5f);
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
        public void TheBakedPictureTakesEachBlocksChunk()
        {
            var t = Small(out var chunks);
            var img = TerrainBuilder.BakeRegion(t, 1, 0, 32, c => chunks[c]);
            // Region 1 starts at block 16, chunk column 4. Pixel (1, 1) is
            // block (16, 0), chunk 4; pixel (9, 17) is block (20, 8), off the map.
            Assert.AreEqual(40, img.Pixels[(1 * 32 + 1) * 4]);
            Assert.AreEqual(0, img.Pixels[(17 * 32 + 9) * 4 + 3], "past the map edge stays clear");
            Assert.AreEqual(4 * 10 + 5 * 10 * 1, img.Pixels[(9 * 32 + 1) * 4], "block (16, 4) is chunk 9");
        }
    }
}
