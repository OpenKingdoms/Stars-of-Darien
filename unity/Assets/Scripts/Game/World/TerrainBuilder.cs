// TerrainBuilder.cs - the ground as meshes from a MapTerrain. The map is
// cut into regions of 16 by 16 blocks, each with one picture of its
// ground at full detail. Up close a region is every height sample, and
// far away a coarser grid with a skirt, both over that picture. A LODGroup
// picks between them.
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

    // A vertex of the ground as the dented regions upload it, interleaved.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct GroundVertex
    {
        public Vector3 Position, Normal;
        public Vector2 Uv;
    }

    // A dented region's mesh, built whole on a worker so the main thread
    // only copies its buffers.
    public sealed class GroundBuffers
    {
        public GroundVertex[] Vertices;
        public int[] Indices;
        public Bounds Bounds;

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
        };

        public static GroundBuffers From(TerrainMeshData d, float roomAbove, float roomBelow)
        {
            var g = new GroundBuffers { Vertices = new GroundVertex[d.Vertices.Count], Indices = d.For(-1).ToArray() };
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            for (int i = 0; i < g.Vertices.Length; i++)
            {
                var p = d.Vertices[i];
                g.Vertices[i] = new GroundVertex { Position = p, Normal = d.Normals[i], Uv = d.Uvs[i] };
                lo = Vector3.Min(lo, p);
                hi = Vector3.Max(hi, p);
            }
            lo.y -= roomBelow;
            hi.y += roomAbove;
            g.Bounds = new Bounds((lo + hi) * 0.5f, hi - lo);
            return g;
        }

        public Mesh ToMesh(string name)
        {
            const MeshUpdateFlags quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;
            var mesh = new Mesh { name = name };
            mesh.SetVertexBufferParams(Vertices.Length, Layout);
            mesh.SetVertexBufferData(Vertices, 0, 0, Vertices.Length, 0, quiet);
            mesh.SetIndexBufferParams(Indices.Length, IndexFormat.UInt32);
            mesh.SetIndexBufferData(Indices, 0, 0, Indices.Length, quiet);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, Indices.Length) { bounds = Bounds, vertexCount = Vertices.Length }, quiet);
            mesh.bounds = Bounds;
            return mesh;
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

        // Texels across a region's picture.
        public static int RegionTexels(MapTerrain t) => RegionBlocks * t.BlockTexels;

        // Every sample of the region as one grid. Its texture coordinates
        // run straight across the region's picture, so neighbouring blocks
        // meet texel to texel with no seam.
        public static TerrainMeshData Detail(MapTerrain t, int rx, int ry)
        {
            var d = new TerrainMeshData();
            int n = SamplesPerBlock(t);
            int s0x = rx * RegionBlocks * n, s0z = ry * RegionBlocks * n;
            int s1x = Mathf.Min(t.BlocksW, (rx + 1) * RegionBlocks) * n, s1z = Mathf.Min(t.BlocksH, (ry + 1) * RegionBlocks) * n;
            float span = RegionBlocks * n;
            for (int sz = s0z; sz <= s1z; sz++)
                for (int sx = s0x; sx <= s1x; sx++)
                {
                    d.Vertices.Add(new Vector3(sx * t.CellSize, t.HeightAt(sx, sz), -sz * t.CellSize));
                    d.Normals.Add(Normal(t, sx, sz));
                    d.Uvs.Add(new Vector2((sx - s0x) / span, 1f - (sz - s0z) / span));
                }
            Quads(d.For(-1), 0, s1x - s0x, s1z - s0z);
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
            Skirts(d, tris, xs.Count, zs.Count, SkirtDepth);
            return d;
        }

        // The region's samples split factor times each way, for the scar
        // map's dips: heights and normals between samples taken bilinearly,
        // and a short skirt round the edge where a neighbour is coarser.
        public static TerrainMeshData Refined(MapTerrain t, int rx, int ry, int factor)
        {
            factor = Mathf.Max(1, factor);
            var d = new TerrainMeshData();
            int n = SamplesPerBlock(t);
            int s0x = rx * RegionBlocks * n, s0z = ry * RegionBlocks * n;
            int s1x = Mathf.Min(t.BlocksW, (rx + 1) * RegionBlocks) * n, s1z = Mathf.Min(t.BlocksH, (ry + 1) * RegionBlocks) * n;
            float span = RegionBlocks * n;
            int sw = s1x - s0x + 1, sh = s1z - s0z + 1;
            var normals = new Vector3[sw * sh];
            for (int z = 0; z < sh; z++)
                for (int x = 0; x < sw; x++)
                    normals[z * sw + x] = Normal(t, s0x + x, s0z + z);
            int cx = (s1x - s0x) * factor, cz = (s1z - s0z) * factor;
            for (int gz = 0; gz <= cz; gz++)
                for (int gx = 0; gx <= cx; gx++)
                {
                    int ix = gx / factor, iz = gz / factor;
                    float fx = (gx - ix * factor) / (float)factor, fz = (gz - iz * factor) / (float)factor;
                    int jx = Mathf.Min(ix + 1, sw - 1), jz = Mathf.Min(iz + 1, sh - 1);
                    float sx = s0x + ix + fx, sz = s0z + iz + fz;
                    float h = Mathf.Lerp(Mathf.Lerp(t.HeightAt(s0x + ix, s0z + iz), t.HeightAt(s0x + jx, s0z + iz), fx),
                        Mathf.Lerp(t.HeightAt(s0x + ix, s0z + jz), t.HeightAt(s0x + jx, s0z + jz), fx), fz);
                    var nrm = Vector3.Lerp(Vector3.Lerp(normals[iz * sw + ix], normals[iz * sw + jx], fx),
                        Vector3.Lerp(normals[jz * sw + ix], normals[jz * sw + jx], fx), fz).normalized;
                    d.Vertices.Add(new Vector3(sx * t.CellSize, h, -sz * t.CellSize));
                    d.Normals.Add(nrm);
                    d.Uvs.Add(new Vector2((sx - s0x) / span, 1f - (sz - s0z) / span));
                }
            Quads(d.For(-1), 0, cx, cz);
            Skirts(d, d.For(-1), cx + 1, cz + 1, 0.4f);
            return d;
        }

        // Each edge ring of a w by h grid copied down by depth, faces outward.
        static void Skirts(TerrainMeshData d, List<int> tris, int w, int h, float depth)
        {
            void Skirt(IList<int> ring)
            {
                int basev = d.Vertices.Count;
                foreach (int i in ring)
                {
                    d.Vertices.Add(d.Vertices[i] + Vector3.down * depth);
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

        // The region's ground at full detail, row 0 on the north edge: each
        // block's square copied from its chunk picture into place. Past the
        // map's edge the last row and column repeat, so smaller mips keep
        // the edge's colour.
        public static RgbaImage RegionPicture(MapTerrain t, int rx, int ry, Func<int, RgbaImage> chunk)
        {
            int bt = t.BlockTexels, size = RegionTexels(t);
            var img = new RgbaImage(size, size);
            int bx0 = rx * RegionBlocks, by0 = ry * RegionBlocks;
            int bx1 = Mathf.Min(t.BlocksW, bx0 + RegionBlocks), by1 = Mathf.Min(t.BlocksH, by0 + RegionBlocks);
            for (int by = by0; by < by1; by++)
                for (int bx = bx0; bx < bx1; bx++)
                {
                    int b = by * t.BlocksW + bx;
                    var c = chunk(t.Blocks[3 * b]);
                    if (c == null) continue;
                    int px = t.Blocks[3 * b + 1], py = t.Blocks[3 * b + 2];
                    int w = Mathf.Min(bt, c.Width - px);
                    if (px < 0 || py < 0 || w <= 0) continue;
                    int ox = (bx - bx0) * bt, oy = (by - by0) * bt;
                    for (int y = 0; y < bt && py + y < c.Height; y++)
                        Buffer.BlockCopy(c.Pixels, ((py + y) * c.Width + px) * 4, img.Pixels, ((oy + y) * size + ox) * 4, w * 4);
                }
            int usedW = (bx1 - bx0) * bt, usedH = (by1 - by0) * bt;
            if (usedW <= 0 || usedH <= 0) return img;
            for (int y = 0; y < usedH; y++)
                for (int x = usedW; x < size; x++)
                    Buffer.BlockCopy(img.Pixels, (y * size + usedW - 1) * 4, img.Pixels, (y * size + x) * 4, 4);
            for (int y = usedH; y < size; y++)
                Buffer.BlockCopy(img.Pixels, (usedH - 1) * size * 4, img.Pixels, y * size * 4, size * 4);
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
