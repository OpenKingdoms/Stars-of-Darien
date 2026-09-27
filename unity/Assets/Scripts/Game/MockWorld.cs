// MockWorld.cs - the procedural parts of MockBackend: value noise, map
// terrain with its ground pictures, and boxy models with simple animation.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public static class MockNoise
    {
        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / 16777215f;
            }
        }

        public static float Value(float x, float y, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            tx = tx * tx * (3 - 2 * tx);
            ty = ty * ty * (3 - 2 * ty);
            float a = Mathf.Lerp(Hash(x0, y0, seed), Hash(x0 + 1, y0, seed), tx);
            float b = Mathf.Lerp(Hash(x0, y0 + 1, seed), Hash(x0 + 1, y0 + 1, seed), tx);
            return Mathf.Lerp(a, b, ty);
        }

        public static float Fbm(float x, float y, int seed, int octaves = 4)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value(x, y, seed + i * 31) * amp;
                norm += amp;
                x *= 2.03f;
                y *= 2.03f;
                amp *= 0.5f;
            }
            return sum / norm;
        }
    }

    public sealed class MockTerrainGen
    {
        public const float BlockSize = 4f;
        public const int BlockTexels = 32, ChunkBlocks = 8;

        readonly MapInfo map;
        readonly int seed;
        public readonly float SeaLevel;

        public MockTerrainGen(MapInfo map)
        {
            this.map = map;
            unchecked
            {
                int s = 17;
                foreach (char c in map.Id) s = s * 31 + c;
                seed = s & 0xffff;
            }
            SeaLevel = map.Id == "mock_isles" ? 2.2f : map.Id == "mock_highlands" ? 1.2f : map.Id == "mock_dunes" ? 0.8f : -1f;
        }

        public float Height(float wx, float wz)
        {
            float u = wx / map.Size.x, v = -wz / map.Size.y;
            float n = MockNoise.Fbm(u * 5f, v * 5f, seed);
            switch (map.Id)
            {
                case "mock_isles":
                {
                    float a = Blob(u, v, 0.28f, 0.7f, 0.3f), b = Blob(u, v, 0.72f, 0.3f, 0.3f);
                    return Mathf.Max(a, b) * 7f + n * 4f - 0.8f;
                }
                case "mock_highlands":
                    return n * 11f + 2.5f - Blob(u, v, 0.5f, 0.5f, 0.16f) * 5f;
                case "mock_frost":
                {
                    float ridge = Mathf.Pow(Mathf.Abs(u - 0.5f) * 2f, 2.5f);
                    return ridge * 14f + n * 3f;
                }
                default:
                    return (Mathf.Sin(u * 22f + n * 4f) * 0.5f + 0.5f) * 2.5f + n * 3f + 1.2f - Blob(u, v, 0.55f, 0.45f, 0.1f) * 3.5f;
            }
        }

        static float Blob(float u, float v, float cu, float cv, float r)
        {
            float d = ((u - cu) * (u - cu) + (v - cv) * (v - cv)) / (r * r);
            return Mathf.Clamp01(1f - d);
        }

        public Color32 Colour(float wx, float wz, float h)
        {
            float d = MockNoise.Value(wx * 0.9f, wz * 0.9f, seed + 7) * 0.5f + MockNoise.Hash(Mathf.FloorToInt(wx * 8), Mathf.FloorToInt(wz * 8), seed) * 0.5f;
            Color c;
            float above = h - Mathf.Max(0, SeaLevel);
            switch (map.Climate)
            {
                case "snow":
                    c = h > 5f ? Color.Lerp(new Color(0.85f, 0.88f, 0.92f), Color.white, d)
                      : Color.Lerp(new Color(0.55f, 0.58f, 0.6f), new Color(0.78f, 0.8f, 0.84f), d);
                    break;
                case "desert":
                    c = Color.Lerp(new Color(0.72f, 0.5f, 0.32f), new Color(0.86f, 0.66f, 0.42f), d);
                    if (SeaLevel > 0 && above < 0.7f) c = Color.Lerp(new Color(0.35f, 0.5f, 0.25f), c, Mathf.Clamp01(above / 0.7f));
                    break;
                default:
                    if (SeaLevel > 0 && above < 0.5f) c = Color.Lerp(new Color(0.7f, 0.64f, 0.46f), new Color(0.82f, 0.76f, 0.56f), d);
                    else if (h > 9f) c = Color.Lerp(new Color(0.45f, 0.43f, 0.4f), new Color(0.6f, 0.58f, 0.55f), d);
                    else c = Color.Lerp(new Color(0.24f, 0.4f, 0.16f), new Color(0.4f, 0.55f, 0.24f), d);
                    break;
            }
            if (SeaLevel > 0 && h < SeaLevel) c *= Mathf.Lerp(0.55f, 0.9f, Mathf.Clamp01(1f + (h - SeaLevel) / 3f));
            c.a = 1;
            return c;
        }

        public TerrainData Build()
        {
            int w = Mathf.RoundToInt(map.Size.x) + 1, h = Mathf.RoundToInt(map.Size.y) + 1;
            var t = new TerrainData { HeightsW = w, HeightsH = h, CellSize = 1f, Heights = new float[w * h], SeaLevel = SeaLevel };
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    t.Heights[z * w + x] = Mathf.Max(0f, Height(x, -z));
            t.BlockSize = BlockSize;
            t.BlockTexels = BlockTexels;
            t.BlocksW = Mathf.CeilToInt(map.Size.x / BlockSize);
            t.BlocksH = Mathf.CeilToInt(map.Size.y / BlockSize);
            int cw = (t.BlocksW + ChunkBlocks - 1) / ChunkBlocks, ch = (t.BlocksH + ChunkBlocks - 1) / ChunkBlocks;
            t.ChunkCount = cw * ch;
            t.Blocks = new int[t.BlocksW * t.BlocksH * 3];
            for (int by = 0; by < t.BlocksH; by++)
                for (int bx = 0; bx < t.BlocksW; bx++)
                {
                    int b = by * t.BlocksW + bx;
                    t.Blocks[3 * b] = (by / ChunkBlocks) * cw + bx / ChunkBlocks;
                    t.Blocks[3 * b + 1] = (bx % ChunkBlocks) * BlockTexels;
                    t.Blocks[3 * b + 2] = (by % ChunkBlocks) * BlockTexels;
                }
            return t;
        }

        public RgbaImage Chunk(TerrainData t, int chunk)
        {
            int cw = (t.BlocksW + ChunkBlocks - 1) / ChunkBlocks;
            int cx = chunk % cw, cy = chunk / cw;
            int size = ChunkBlocks * BlockTexels;
            float perPx = BlockSize / BlockTexels;
            var img = new RgbaImage(size, size);
            for (int py = 0; py < size; py++)
                for (int px = 0; px < size; px++)
                {
                    float wx = cx * ChunkBlocks * BlockSize + (px + 0.5f) * perPx;
                    float wz = -(cy * ChunkBlocks * BlockSize + (py + 0.5f) * perPx);
                    MockBackend.Put(img, px, py, Colour(wx, wz, t.Sample(wx, wz)));
                }
            return img;
        }
    }

    public static class MockModels
    {
        sealed class Builder
        {
            public readonly List<Vector3> P = new List<Vector3>(), N = new List<Vector3>();
            public readonly List<Vector2> U = new List<Vector2>();
            public readonly List<Color32> C = new List<Color32>();
            public readonly List<int> Piece = new List<int>();
            public readonly List<int> Plain = new List<int>(), Textured = new List<int>();
            public readonly List<PieceInfo> Pieces = new List<PieceInfo>();

            public int AddPiece(string name, int parent, Vector3 offset)
            {
                Pieces.Add(new PieceInfo { Name = name, Parent = parent, Offset = offset });
                return Pieces.Count - 1;
            }

            // A box centred at c in piece space.
            public void Box(int piece, Vector3 c, Vector3 size, Color32 col, bool textured = false)
            {
                var h = size * 0.5f;
                Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                foreach (var n in normals)
                {
                    Vector3 a = n.x != 0 ? Vector3.up : Vector3.right;
                    Vector3 b = Vector3.Cross(n, a);
                    int basev = P.Count;
                    for (int i = 0; i < 4; i++)
                    {
                        float sa = i == 1 || i == 2 ? 1 : -1, sb = i >= 2 ? 1 : -1;
                        var corner = n + a * sa + b * sb;
                        P.Add(c + Vector3.Scale(corner, h));
                        N.Add(n);
                        U.Add(new Vector2((sa + 1) * 0.5f * Mathf.Max(1, size.x), (sb + 1) * 0.5f * Mathf.Max(1, size.y)));
                        C.Add(col);
                        Piece.Add(piece);
                    }
                    var list = textured ? Textured : Plain;
                    // Clockwise seen from outside, as Unity wants.
                    list.Add(basev); list.Add(basev + 1); list.Add(basev + 2);
                    list.Add(basev); list.Add(basev + 2); list.Add(basev + 3);
                }
            }

            public ModelData Finish(string name)
            {
                var idx = new List<int>(Plain);
                var batches = new List<Batch>();
                if (Plain.Count > 0) batches.Add(new Batch { FirstIndex = 0, IndexCount = Plain.Count, Texture = -1 });
                if (Textured.Count > 0) batches.Add(new Batch { FirstIndex = idx.Count, IndexCount = Textured.Count, Texture = 0 });
                idx.AddRange(Textured);
                var bounds = new Bounds(Vector3.zero, Vector3.zero);
                for (int i = 0; i < P.Count; i++)
                {
                    var world = P[i];
                    for (int p = Piece[i]; p >= 0; p = Pieces[p].Parent) world += Pieces[p].Offset;
                    if (i == 0) bounds = new Bounds(world, Vector3.zero); else bounds.Encapsulate(world);
                }
                return new ModelData
                {
                    Name = name, Positions = P.ToArray(), Normals = N.ToArray(), Uvs = U.ToArray(), Colors = C.ToArray(),
                    VertexPiece = Piece.ToArray(), Indices = idx.ToArray(), Pieces = Pieces.ToArray(), Batches = batches.ToArray(),
                    Bounds = bounds, Scale = 1f,
                };
            }
        }

        static readonly Color32 Skin = new Color32(225, 185, 150, 255), Steel = new Color32(170, 175, 185, 255),
            Leather = new Color32(110, 75, 45, 255), Wood = new Color32(95, 65, 40, 255), Leaf = new Color32(45, 110, 40, 255);

        public static ModelData Build(string obj, Color32 team)
        {
            var b = new Builder();
            if (obj == "mocktree")
            {
                int root = b.AddPiece("base", -1, Vector3.zero);
                b.Box(root, new Vector3(0, 0.9f, 0), new Vector3(0.35f, 1.8f, 0.35f), Wood);
                int top = b.AddPiece("canopy", root, new Vector3(0, 2.2f, 0));
                b.Box(top, Vector3.zero, new Vector3(1.6f, 1.2f, 1.6f), Leaf);
                b.Box(top, new Vector3(0, 0.8f, 0), new Vector3(1f, 0.8f, 1f), Leaf);
                return b.Finish(obj);
            }
            if (obj.EndsWith("lodge"))
            {
                int root = b.AddPiece("base", -1, Vector3.zero);
                b.Box(root, new Vector3(0, 0.9f, 0), new Vector3(2.8f, 1.8f, 2.8f), Steel, true);
                int roof = b.AddPiece("roof", root, new Vector3(0, 1.8f, 0));
                b.Box(roof, new Vector3(0, 0.4f, 0), new Vector3(3.1f, 0.8f, 3.1f), Wood);
                int flag = b.AddPiece("flag", roof, new Vector3(1.2f, 0.8f, 1.2f));
                b.Box(flag, new Vector3(0, 0.8f, 0), new Vector3(0.08f, 1.6f, 0.08f), Wood);
                b.Box(flag, new Vector3(0.35f, 1.35f, 0), new Vector3(0.6f, 0.4f, 0.04f), team);
                return b.Finish(obj);
            }
            bool monarch = obj.EndsWith("monarch"), archer = obj.EndsWith("archer"), knight = obj.EndsWith("knight");
            if (!monarch && !archer && !knight) return null;
            float s = monarch ? 1.25f : 1f;
            int pelvis = b.AddPiece("pelvis", -1, new Vector3(0, 0.7f * s, 0));
            int torso = b.AddPiece("torso", pelvis, new Vector3(0, 0.1f * s, 0));
            b.Box(torso, new Vector3(0, 0.3f * s, 0), new Vector3(0.5f, 0.6f, 0.3f) * s, team);
            int head = b.AddPiece("head", torso, new Vector3(0, 0.6f * s, 0));
            b.Box(head, new Vector3(0, 0.15f * s, 0), new Vector3(0.26f, 0.28f, 0.26f) * s, knight ? Steel : Skin);
            if (monarch) b.Box(head, new Vector3(0, 0.34f * s, 0), new Vector3(0.3f, 0.1f, 0.3f) * s, new Color32(230, 190, 60, 255));
            int armL = b.AddPiece("larm", torso, new Vector3(-0.32f * s, 0.55f * s, 0));
            int armR = b.AddPiece("rarm", torso, new Vector3(0.32f * s, 0.55f * s, 0));
            b.Box(armL, new Vector3(0, -0.25f * s, 0), new Vector3(0.14f, 0.5f, 0.14f) * s, knight ? Steel : Leather);
            b.Box(armR, new Vector3(0, -0.25f * s, 0), new Vector3(0.14f, 0.5f, 0.14f) * s, knight ? Steel : Leather);
            if (knight) b.Box(armR, new Vector3(0, -0.5f, 0.35f), new Vector3(0.05f, 0.05f, 0.8f), Steel);
            if (knight) b.Box(armL, new Vector3(-0.08f, -0.3f, 0.1f), new Vector3(0.06f, 0.45f, 0.35f), team);
            if (archer) b.Box(armL, new Vector3(0, -0.45f, 0.1f), new Vector3(0.04f, 0.9f, 0.04f), Wood);
            if (monarch) b.Box(armR, new Vector3(0, -0.45f * s, 0.2f), new Vector3(0.05f, 0.05f, 0.6f), new Color32(230, 190, 60, 255));
            int legL = b.AddPiece("lleg", pelvis, new Vector3(-0.13f * s, 0, 0));
            int legR = b.AddPiece("rleg", pelvis, new Vector3(0.13f * s, 0, 0));
            b.Box(legL, new Vector3(0, -0.35f * s, 0), new Vector3(0.16f, 0.7f, 0.16f) * s, Leather);
            b.Box(legR, new Vector3(0, -0.35f * s, 0), new Vector3(0.16f, 0.7f, 0.16f) * s, Leather);
            return b.Finish(obj);
        }

        // A piece rotation for a named animation at a time in seconds.
        public static Quaternion Animate(string piece, string anim, float t)
        {
            float swing = 0;
            switch (anim)
            {
                case "walk":
                {
                    float w = Mathf.Sin(t * 2f * Mathf.PI) * 30f;
                    if (piece == "lleg" || piece == "rarm") swing = w;
                    else if (piece == "rleg" || piece == "larm") swing = -w;
                    else if (piece == "flag") return Quaternion.Euler(0, w * 0.5f, 0);
                    break;
                }
                case "attack":
                    if (piece == "rarm") swing = -Mathf.Abs(Mathf.Sin(t * Mathf.PI)) * 110f;
                    else if (piece == "torso") return Quaternion.Euler(0, Mathf.Sin(t * Mathf.PI) * 15f, 0);
                    break;
                default:
                    if (piece == "torso") return Quaternion.Euler(Mathf.Sin(t * 1.3f) * 2f, 0, 0);
                    if (piece == "flag") return Quaternion.Euler(0, Mathf.Sin(t * 2f) * 20f, 0);
                    break;
            }
            return Quaternion.Euler(swing, 0, 0);
        }
    }
}
