// DentField.cs - the scar map's dips and rims kept on the CPU, texel for
// texel as the shape texture holds them and sampled the way the GPU
// filters it, so what stands on the ground sits on what the ground shows.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class DentField
    {
        // Texels across and down, row 0 on the south edge as the texture's.
        public readonly int W, H;
        // The map's size in world units, from x 0 east and z 0 south.
        public readonly float SizeX, SizeZ;
        readonly byte[] depth, rim;
        // A tile of texels is marked when a texel in it, or just past its far
        // edges, holds a dip or rim: one read tells a filter there is none.
        const int Tile = 8;
        readonly int tilesW;
        readonly bool[] tiles;

        // Texels that hold a dip or a rim, so an undented map answers fast.
        public int Dented { get; private set; }

        public DentField(int w, int h, float sizeX, float sizeZ)
        {
            W = Mathf.Max(1, w);
            H = Mathf.Max(1, h);
            SizeX = Mathf.Max(1e-3f, sizeX);
            SizeZ = Mathf.Max(1e-3f, sizeZ);
            depth = new byte[W * H];
            rim = new byte[W * H];
            tilesW = W / Tile + 1;
            tiles = new bool[tilesW * (H / Tile + 1)];
        }

        public long Bytes => depth.LongLength + rim.LongLength;

        // A texel's centre in the world.
        public float TexelX(int i) => (i + 0.5f) / W * SizeX;
        public float TexelZ(int j) => ((j + 0.5f) / H - 1f) * SizeZ;

        public byte DepthAt(int i, int j) => depth[j * W + i];
        public byte RimAt(int i, int j) => rim[j * W + i];

        // Writes a crater's dip and rim, keeping the deeper dip and the
        // higher rim where it meets another. Returns the texels it visited.
        public int Stamp(in ScarStamp s)
        {
            if (s.Dent <= 0f || s.Depth <= 0f && s.Rim <= 0f) return 0;
            float reach = s.Dent * 1.5f;
            int i0 = Mathf.Max(0, Mathf.FloorToInt((s.X - reach) / SizeX * W - 0.5f));
            int i1 = Mathf.Min(W - 1, Mathf.CeilToInt((s.X + reach) / SizeX * W - 0.5f));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((1f + (s.Z - reach) / SizeZ) * H - 0.5f));
            int j1 = Mathf.Min(H - 1, Mathf.CeilToInt((1f + (s.Z + reach) / SizeZ) * H - 0.5f));
            float d01 = s.Depth / ScarStamps.MaxDepthPx, r01 = s.Rim / ScarStamps.MaxRimPx;
            var phases = ScarStamps.Phases(s.Seed);
            float reach2 = reach * reach, invDent = 1f / s.Dent, stepX = SizeX / W;
            int visited = 0;
            for (int j = j0; j <= j1; j++)
            {
                float dz = TexelZ(j) - s.Z, dx = TexelX(i0) - s.X - stepX;
                for (int i = i0; i <= i1; i++)
                {
                    dx += stepX;
                    float r2 = dx * dx + dz * dz;
                    if (r2 >= reach2) continue;
                    visited++;
                    float r = Mathf.Sqrt(r2), x = r * invDent;
                    byte qd = 0, qr = 0;
                    if (x < 1f) qd = Quantise(d01 * ScarStamps.DepthAt(x, s.Floor));
                    if (x > 0.6f)
                    {
                        // The stamp's own frame, as the stamp shader sees it.
                        float inv = 1f / r, c = (dx * s.DirZ - dz * s.DirX) * inv, sn = (dx * s.DirX + dz * s.DirZ) * inv;
                        qr = Quantise(r01 * ScarStamps.RimAt(x, c, sn, phases));
                    }
                    if (qd == 0 && qr == 0) continue;
                    int k = j * W + i;
                    bool was = depth[k] != 0 || rim[k] != 0;
                    if (qd > depth[k]) depth[k] = qd;
                    if (qr > rim[k]) rim[k] = qr;
                    if (!was) Mark(i, j);
                }
            }
            return visited;
        }

        void Mark(int i, int j)
        {
            Dented++;
            int a = Mathf.Max(0, i - 1) / Tile, b = i / Tile, c = Mathf.Max(0, j - 1) / Tile, d = j / Tile;
            tiles[c * tilesW + a] = tiles[c * tilesW + b] = tiles[d * tilesW + a] = tiles[d * tilesW + b] = true;
        }

        public static byte Quantise(float v) => v <= 0f ? (byte)0 : v >= 1f ? (byte)255 : (byte)(v * 255f + 0.5f);

        // The drawn change at a world point in world units, filtered as the
        // GPU filters the texture: below 0 in a dip, above on a rim.
        public float Height(float x, float z)
        {
            if (Dented == 0) return 0f;
            float tu = x / SizeX * W - 0.5f, tv = (1f + z / SizeZ) * H - 0.5f;
            int i0 = Mathf.FloorToInt(tu), j0 = Mathf.FloorToInt(tv);
            float fu = tu - i0, fv = tv - j0;
            int ia = Mathf.Clamp(i0, 0, W - 1), ib = Mathf.Clamp(i0 + 1, 0, W - 1);
            int ja = Mathf.Clamp(j0, 0, H - 1), jb = Mathf.Clamp(j0 + 1, 0, H - 1);
            if (!tiles[(ja / Tile) * tilesW + ia / Tile]) return 0f;
            float d = Bilinear(depth, ia, ib, ja, jb, fu, fv), r = Bilinear(rim, ia, ib, ja, jb, fu, fv);
            if (d == 0f && r == 0f) return 0f;
            return ScarStamps.Height(d / 255f * ScarStamps.MaxDepthPx, r / 255f * ScarStamps.MaxRimPx) / ScarStamps.PixelsPerUnit;
        }

        float Bilinear(byte[] a, int ia, int ib, int ja, int jb, float fu, float fv)
        {
            float top = Mathf.Lerp(a[ja * W + ia], a[ja * W + ib], fu);
            float bottom = Mathf.Lerp(a[jb * W + ia], a[jb * W + ib], fu);
            return Mathf.Lerp(top, bottom, fv);
        }

        // Takes the dips and rims from a copy of the shape texture, red and
        // green, after the scar map was drawn at another size.
        public void Load(Color32[] pixels)
        {
            Dented = 0;
            System.Array.Clear(tiles, 0, tiles.Length);
            int n = Mathf.Min(pixels.Length, depth.Length);
            for (int k = 0; k < n; k++)
            {
                depth[k] = pixels[k].r;
                rim[k] = pixels[k].g;
                if (depth[k] != 0 || rim[k] != 0) Mark(k % W, k / W);
            }
        }

        // Whether any dip or rim lies within a world rectangle.
        public bool AnyIn(float x0, float z0, float x1, float z1)
        {
            if (Dented == 0) return false;
            int i0 = Mathf.Clamp(Mathf.FloorToInt(x0 / SizeX * W), 0, W - 1), i1 = Mathf.Clamp(Mathf.CeilToInt(x1 / SizeX * W), 0, W - 1);
            int j0 = Mathf.Clamp(Mathf.FloorToInt((1f + z0 / SizeZ) * H), 0, H - 1), j1 = Mathf.Clamp(Mathf.CeilToInt((1f + z1 / SizeZ) * H), 0, H - 1);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    if (depth[j * W + i] != 0 || rim[j * W + i] != 0) return true;
            return false;
        }
    }
}
