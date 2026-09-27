// TerrainBuilder.cs - the ground as meshes from a MapTerrain. The map is
// cut into regions of 16 by 16 blocks. Up close a region is every height
// sample, with each block textured from its square of a chunk picture.
// Far away it is a coarser grid with a skirt, textured from one small
// picture baked from those squares. A LODGroup picks between them.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class TerrainMeshData
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        // Triangles by chunk picture, or under -1 for a baked region picture.
        public readonly SortedDictionary<int, List<int>> Triangles = new SortedDictionary<int, List<int>>();

        public List<int> For(int chunk)
        {
            if (!Triangles.TryGetValue(chunk, out var list)) Triangles[chunk] = list = new List<int>();
            return list;
        }
    }

    public static class TerrainBuilder
    {
        public const int RegionBlocks = 16;
        public const float SkirtDepth = 2f;

        public static int RegionsW(MapTerrain t) => (t.BlocksW + RegionBlocks - 1) / RegionBlocks;
        public static int RegionsH(MapTerrain t) => (t.BlocksH + RegionBlocks - 1) / RegionBlocks;
        public static int SamplesPerBlock(MapTerrain t) => Mathf.Max(1, Mathf.RoundToInt(t.BlockSize / t.CellSize));

        public static Vector3 Normal(MapTerrain t, int x, int z)
        {
            float dx = (t.HeightAt(x + 1, z) - t.HeightAt(x - 1, z)) / (2f * t.CellSize);
            // Row z grows south, which is world -z.
            float dz = (t.HeightAt(x, z - 1) - t.HeightAt(x, z + 1)) / (2f * t.CellSize);
            return new Vector3(-dx, 1f, -dz).normalized;
        }

        // Every sample of every block in the region. chunkSize gives each
        // chunk picture's size in pixels, for the texture coordinates.
        public static TerrainMeshData Detail(MapTerrain t, int rx, int ry, Func<int, Vector2Int> chunkSize)
        {
            var d = new TerrainMeshData();
            int n = SamplesPerBlock(t);
            int bx1 = Mathf.Min(t.BlocksW, (rx + 1) * RegionBlocks), by1 = Mathf.Min(t.BlocksH, (ry + 1) * RegionBlocks);
            for (int by = ry * RegionBlocks; by < by1; by++)
            {
                for (int bx = rx * RegionBlocks; bx < bx1; bx++)
                {
                    int b = by * t.BlocksW + bx;
                    int chunk = t.Blocks[3 * b], px = t.Blocks[3 * b + 1], py = t.Blocks[3 * b + 2];
                    var size = chunkSize(chunk);
                    if (size.x <= 0 || size.y <= 0) continue;
                    int basev = d.Vertices.Count;
                    for (int gz = 0; gz <= n; gz++)
                        for (int gx = 0; gx <= n; gx++)
                        {
                            int sx = bx * n + gx, sz = by * n + gz;
                            d.Vertices.Add(new Vector3(sx * t.CellSize, t.HeightAt(sx, sz), -sz * t.CellSize));
                            d.Normals.Add(Normal(t, sx, sz));
                            // Half a texel in, so filtering never reads the next square.
                            float u = (px + 0.5f + (t.BlockTexels - 1) * (float)gx / n) / size.x;
                            float row = (py + 0.5f + (t.BlockTexels - 1) * (float)gz / n) / size.y;
                            d.Uvs.Add(new Vector2(u, 1f - row));
                        }
                    Quads(d.For(chunk), basev, n, n);
                }
            }
            return d;
        }

        // A grid every step samples over the whole region, with a skirt
        // hanging from its edges to hide cracks against a finer neighbour.
        public static TerrainMeshData Coarse(MapTerrain t, int rx, int ry, int step)
        {
            var d = new TerrainMeshData();
            int n = SamplesPerBlock(t);
            int s0x = rx * RegionBlocks * n, s0z = ry * RegionBlocks * n;
            int s1x = Mathf.Min(t.BlocksW, (rx + 1) * RegionBlocks) * n, s1z = Mathf.Min(t.BlocksH, (ry + 1) * RegionBlocks) * n;
            float spanX = RegionBlocks * n, spanZ = RegionBlocks * n;
            var xs = Steps(s0x, s1x, step);
            var zs = Steps(s0z, s1z, step);
            foreach (int sz in zs)
                foreach (int sx in xs)
                {
                    d.Vertices.Add(new Vector3(sx * t.CellSize, t.HeightAt(sx, sz), -sz * t.CellSize));
                    d.Normals.Add(Normal(t, sx, sz));
                    d.Uvs.Add(new Vector2((sx - s0x) / spanX, 1f - (sz - s0z) / spanZ));
                }
            var tris = d.For(-1);
            Quads(tris, 0, xs.Count - 1, zs.Count - 1);

            // Skirts: each edge ring copied down, faces pointing outward.
            int w = xs.Count, h = zs.Count;
            void Skirt(IList<int> ring)
            {
                int basev = d.Vertices.Count;
                foreach (int i in ring)
                {
                    d.Vertices.Add(d.Vertices[i] + Vector3.down * SkirtDepth);
                    d.Normals.Add(d.Normals[i]);
                    d.Uvs.Add(d.Uvs[i]);
                }
                for (int k = 0; k + 1 < ring.Count; k++)
                {
                    int a = ring[k], b = ring[k + 1], a2 = basev + k, b2 = basev + k + 1;
                    tris.Add(a); tris.Add(b); tris.Add(b2);
                    tris.Add(a); tris.Add(b2); tris.Add(a2);
                }
            }
            var north = new List<int>(); for (int x = w - 1; x >= 0; x--) north.Add(x);
            var south = new List<int>(); for (int x = 0; x < w; x++) south.Add((h - 1) * w + x);
            var west = new List<int>(); for (int z = 0; z < h; z++) west.Add(z * w);
            var east = new List<int>(); for (int z = h - 1; z >= 0; z--) east.Add(z * w + w - 1);
            Skirt(north); Skirt(south); Skirt(west); Skirt(east);
            return d;
        }

        static List<int> Steps(int from, int to, int step)
        {
            var list = new List<int>();
            for (int s = from; s < to; s += Mathf.Max(1, step)) list.Add(s);
            list.Add(to);
            return list;
        }

        // A w by h grid of vertices from basev, row by row, as triangles
        // clockwise seen from above.
        static void Quads(List<int> tris, int basev, int cellsX, int cellsZ)
        {
            int row = cellsX + 1;
            for (int gz = 0; gz < cellsZ; gz++)
                for (int gx = 0; gx < cellsX; gx++)
                {
                    int nw = basev + gz * row + gx, ne = nw + 1, sw = nw + row, se = sw + 1;
                    tris.Add(nw); tris.Add(ne); tris.Add(se);
                    tris.Add(nw); tris.Add(se); tris.Add(sw);
                }
        }

        // One picture of a region, size pixels square, read from the chunk
        // squares its blocks point at. Row 0 is the north edge.
        public static RgbaImage BakeRegion(MapTerrain t, int rx, int ry, int size, Func<int, RgbaImage> chunk)
        {
            var img = new RgbaImage(size, size);
            float blocksPerPx = (float)RegionBlocks / size;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fbx = rx * RegionBlocks + (x + 0.5f) * blocksPerPx, fby = ry * RegionBlocks + (y + 0.5f) * blocksPerPx;
                    int bx = (int)fbx, by = (int)fby;
                    if (bx >= t.BlocksW || by >= t.BlocksH) continue;
                    int b = by * t.BlocksW + bx;
                    var c = chunk(t.Blocks[3 * b]);
                    if (c == null) continue;
                    int cx = t.Blocks[3 * b + 1] + (int)((fbx - bx) * t.BlockTexels);
                    int cy = t.Blocks[3 * b + 2] + (int)((fby - by) * t.BlockTexels);
                    cx = Mathf.Clamp(cx, 0, c.Width - 1);
                    cy = Mathf.Clamp(cy, 0, c.Height - 1);
                    Buffer.BlockCopy(c.Pixels, (cy * c.Width + cx) * 4, img.Pixels, (y * size + x) * 4, 4);
                }
            return img;
        }

        public static Mesh ToMesh(TerrainMeshData d, string name, IList<int> order)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(d.Vertices);
            mesh.SetNormals(d.Normals);
            mesh.SetUVs(0, d.Uvs);
            mesh.subMeshCount = order.Count;
            for (int i = 0; i < order.Count; i++) mesh.SetTriangles(d.Triangles[order[i]], i, false);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
