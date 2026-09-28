// WaterTextures.cs - the sea's textures, made in code once a session from
// fixed seeds: wave slopes from a wind-driven spectrum of lattice waves,
// foam lace, caustic nets and slow noise, each tiling seamlessly. Per map,
// the sea's depth and shore distance, baked from the height grid.
using System;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public static class WaterTextures
    {
        public const int NormalSize = 256, FoamSize = 256, CausticSize = 256, NoiseSize = 128;
        public const int SeaTexelsPerCell = 2, SeaMargin = 16;

        static Texture2D normals, foam, caustics, noise;

        public static Texture2D Normals => normals != null ? normals : normals = Make("sea waves", NormalSize, WaveSlopes(NormalSize, 96, 11), TextureFormat.RGBA32);
        public static Texture2D Foam => foam != null ? foam : foam = Make("sea foam", FoamSize, FoamPixels(FoamSize, 23), TextureFormat.RGBA32);
        public static Texture2D Caustics => caustics != null ? caustics : caustics = Make("sea caustics", CausticSize, CausticPixels(CausticSize, 37), TextureFormat.RGBA32);
        public static Texture2D Noise => noise != null ? noise : noise = Make("sea noise", NoiseSize, NoisePixels(NoiseSize, 5), TextureFormat.RGBA32);

        static Texture2D Make(string name, int size, byte[] rgba, TextureFormat format)
        {
            var t = new Texture2D(size, size, format, true, true)
            {
                name = name, hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear, anisoLevel = 4,
            };
            t.SetPixelData(rgba, 0);
            t.Apply(true, false);
            return t;
        }

        // Heights as a sum of cosines whose wave vectors are whole cycles
        // per tile, so the tile repeats seamlessly, mostly within 70 degrees
        // of the wind (+x here). Amplitude falls as frequency to the 1.6, so
        // the slope is spread over many waves and none of them stands out.
        // R and G hold the slopes, A the height, and B the height with the
        // short waves raised (by frequency to the 0.8), for crests that are
        // not all one long wave.
        public static byte[] WaveSlopes(int n, int waves, int seed)
        {
            var rng = new System.Random(seed);
            var cosT = new double[n];
            var sinT = new double[n];
            for (int i = 0; i < n; i++) { cosT[i] = Math.Cos(2 * Math.PI * i / n); sinT[i] = Math.Sin(2 * Math.PI * i / n); }
            var h = new double[n * n];
            var crest = new double[n * n];
            var sx = new double[n * n];
            var sy = new double[n * n];
            var used = new System.Collections.Generic.HashSet<(int, int)>();
            int made = 0, tries = 0;
            while (made < waves && tries++ < waves * 200)
            {
                double f = 2 + Math.Pow(rng.NextDouble(), 1.6) * 26;
                bool across = rng.NextDouble() < 0.15;
                double a = (rng.NextDouble() * 2 - 1) * (across ? Math.PI : 70 * Math.PI / 180);
                int kx = (int)Math.Round(f * Math.Cos(a)), ky = (int)Math.Round(f * Math.Sin(a));
                if ((kx == 0 && ky == 0) || !used.Add((kx, ky)) || used.Contains((-kx, -ky))) continue;
                double k = Math.Sqrt(kx * kx + ky * ky);
                double amp = (0.5 + rng.NextDouble() * 0.5) / Math.Pow(k, 1.6) * (across ? 0.4 : 1);
                double lift = Math.Pow(k, 0.8);
                double phase = rng.NextDouble() * 2 * Math.PI;
                double cp = Math.Cos(phase), sp = Math.Sin(phase);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int m = ((kx * x + ky * y) % n + n) % n;
                        double c = cosT[m] * cp - sinT[m] * sp, s = sinT[m] * cp + cosT[m] * sp;
                        int o = y * n + x;
                        h[o] += amp * c;
                        crest[o] += amp * lift * c;
                        // d/dx of cos(2 pi (kx x + ky y) / n + phase), per tile width.
                        sx[o] -= amp * 2 * Math.PI * kx * s;
                        sy[o] -= amp * 2 * Math.PI * ky * s;
                    }
                made++;
            }
            double maxS = 1e-9, lo = double.MaxValue, hi = double.MinValue, clo = double.MaxValue, chi = double.MinValue;
            for (int i = 0; i < n * n; i++)
            {
                maxS = Math.Max(maxS, Math.Max(Math.Abs(sx[i]), Math.Abs(sy[i])));
                lo = Math.Min(lo, h[i]); hi = Math.Max(hi, h[i]);
                clo = Math.Min(clo, crest[i]); chi = Math.Max(chi, crest[i]);
            }
            var px = new byte[n * n * 4];
            for (int i = 0; i < n * n; i++)
            {
                px[4 * i] = ToByte(sx[i] / maxS * 0.5 + 0.5);
                px[4 * i + 1] = ToByte(sy[i] / maxS * 0.5 + 0.5);
                px[4 * i + 2] = ToByte((crest[i] - clo) / Math.Max(chi - clo, 1e-9));
                px[4 * i + 3] = ToByte((h[i] - lo) / Math.Max(hi - lo, 1e-9));
            }
            return px;
        }

        static byte ToByte(double v) => (byte)Math.Max(0, Math.Min(255, Math.Round(v * 255)));

        // Worley distances on a jittered grid of cells that wraps: the
        // nearest and second nearest point to every texel, in cells. The
        // sample point moves by warp (in tiles), so the cells are not polygons.
        static void Worley(int n, int cells, int seed, float[] f1, float[] f2, float[] warpX = null, float[] warpY = null, float warp = 0f)
        {
            var rng = new System.Random(seed);
            var pts = new Vector2[cells * cells];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
            float scale = (float)cells / n;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float px = (x + 0.5f) * scale, py = (y + 0.5f) * scale;
                    if (warpX != null) { px += warpX[y * n + x] * warp * cells; py += warpY[y * n + x] * warp * cells; }
                    int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py);
                    float a = 9, b = 9;
                    for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                        {
                            int gx = cx + i, gy = cy + j;
                            var p = pts[((gy % cells + cells) % cells) * cells + ((gx % cells + cells) % cells)];
                            float dx = gx + p.x - px, dy = gy + p.y - py;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            if (d < a) { b = a; a = d; } else if (d < b) b = d;
                        }
                    f1[y * n + x] = a;
                    f2[y * n + x] = b;
                }
        }

        // Tiling value noise, several octaves, 0 to 1.
        static float[] Fbm(int n, int baseCells, int octaves, int seed)
        {
            var sum = new float[n * n];
            float amp = 0.5f, norm = 0;
            for (int o = 0; o < octaves; o++)
            {
                int cells = baseCells << o;
                var rng = new System.Random(seed + o * 101);
                var lat = new float[cells * cells];
                for (int i = 0; i < lat.Length; i++) lat[i] = (float)rng.NextDouble();
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float fx = (float)x * cells / n, fy = (float)y * cells / n;
                        int x0 = (int)fx, y0 = (int)fy;
                        float tx = fx - x0, ty = fy - y0;
                        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
                        int x1 = (x0 + 1) % cells, y1 = (y0 + 1) % cells;
                        float a = Mathf.Lerp(lat[y0 * cells + x0], lat[y0 * cells + x1], tx);
                        float b = Mathf.Lerp(lat[y1 * cells + x0], lat[y1 * cells + x1], tx);
                        sum[y * n + x] += Mathf.Lerp(a, b, ty) * amp;
                    }
                norm += amp;
                amp *= 0.5f;
            }
            for (int i = 0; i < sum.Length; i++) sum[i] /= norm;
            return sum;
        }

        // R: foam, the walls between bubbles at two scales, bent by noise and
        // of varying thickness, filled in soft patches here and there. The
        // shader shows more of it as a threshold falls, so thin foam is lace
        // and thick foam is solid. G: soft blobs of value noise. B: fine lace.
        public static byte[] FoamPixels(int n, int seed)
        {
            float[] a1 = new float[n * n], a2 = new float[n * n], b1 = new float[n * n], b2 = new float[n * n];
            var wx = Fbm(n, 4, 3, seed + 68);
            var wy = Fbm(n, 4, 3, seed + 69);
            for (int i = 0; i < wx.Length; i++) { wx[i] -= 0.5f; wy[i] -= 0.5f; }
            Worley(n, 9, seed, a1, a2, wx, wy, 0.09f);
            Worley(n, 21, seed + 1, b1, b2, wy, wx, 0.05f);
            var thick = Fbm(n, 8, 3, seed + 70);
            var fill = Fbm(n, 16, 3, seed + 71);
            var blobs = Fbm(n, 4, 4, seed + 2);
            var px = new byte[n * n * 4];
            for (int i = 0; i < n * n; i++)
            {
                float wallA = 1 - Mathf.Clamp01((a2[i] - a1[i]) / (0.08f + 0.18f * thick[i]));
                float wallB = 1 - Mathf.Clamp01((b2[i] - b1[i]) / (0.1f + 0.2f * thick[i]));
                float bubble = 1 - Mathf.Clamp01(b1[i] / 0.22f);
                float patch = Mathf.Clamp01((fill[i] - 0.45f) * 2.5f);
                float walls = Mathf.Pow(wallA, 1.5f) * 0.8f + Mathf.Pow(wallB, 1.3f) * 0.5f;
                float blob = Mathf.Clamp01((blobs[i] - 0.3f) * 2.2f);
                float lace = Mathf.Clamp01(Mathf.Max(walls, patch * 0.85f) + bubble * 0.15f) * (0.45f + 0.7f * blob);
                px[4 * i] = ToByte(Mathf.Clamp01(lace));
                px[4 * i + 1] = ToByte(blob);
                px[4 * i + 2] = ToByte(Mathf.Pow(wallB, 1.3f));
                px[4 * i + 3] = 255;
            }
            return px;
        }

        // The bright net of light a wavy surface focuses onto the floor:
        // thin rims between Worley cells, sharpened.
        public static byte[] CausticPixels(int n, int seed)
        {
            float[] f1 = new float[n * n], f2 = new float[n * n], g1 = new float[n * n], g2 = new float[n * n];
            Worley(n, 9, seed, f1, f2);
            Worley(n, 5, seed + 3, g1, g2);
            var px = new byte[n * n * 4];
            for (int i = 0; i < n * n; i++)
            {
                float a = Mathf.Pow(1 - Mathf.Clamp01((f2[i] - f1[i]) / 0.35f), 3f);
                float b = Mathf.Pow(1 - Mathf.Clamp01((g2[i] - g1[i]) / 0.3f), 3f);
                byte v = ToByte(Mathf.Clamp01(a * 0.85f + b * 0.35f));
                px[4 * i] = v; px[4 * i + 1] = v; px[4 * i + 2] = v; px[4 * i + 3] = 255;
            }
            return px;
        }

        public static byte[] NoisePixels(int n, int seed)
        {
            var f = Fbm(n, 4, 3, seed);
            var px = new byte[n * n * 4];
            for (int i = 0; i < n * n; i++)
            {
                byte v = ToByte(Mathf.Clamp01((f[i] - 0.5f) * 1.8f + 0.5f));
                px[4 * i] = v; px[4 * i + 1] = v; px[4 * i + 2] = v; px[4 * i + 3] = 255;
            }
            return px;
        }

        // ---- The sea over one map ----

        // Where the sea data lies: the map grown by the margin on each side,
        // as x, z of the north-west corner, width and depth.
        public static Vector4 SeaRect(Vector2 size, float cell) =>
            new Vector4(-SeaMargin * cell, SeaMargin * cell, size.x + 2 * SeaMargin * cell, size.y + 2 * SeaMargin * cell);

        // Depth under the sea (R, over 8 units) and signed distance to the
        // shore (G, 0.5 + cells / 16, negative on land), row 0 north, from
        // ground heights sampled at texel centres.
        public static Color32[] SeaData(Vector4 rect, float cell, float sea, Func<float, float, float> ground, out int w, out int h)
        {
            w = Mathf.Max(2, Mathf.RoundToInt(rect.z / cell * SeaTexelsPerCell));
            h = Mathf.Max(2, Mathf.RoundToInt(rect.w / cell * SeaTexelsPerCell));
            return SeaData(rect, cell, sea, ground, new RectInt(0, 0, w, h));
        }

        // The same for a window of the whole rect's texels (x east, y south
        // from its north-west texel). Every texel centre is placed from the
        // whole rect, so a window bakes what the whole bake does wherever the
        // nearest shore lies inside the window.
        public static Color32[] SeaData(Vector4 rect, float cell, float sea, Func<float, float, float> ground, RectInt window)
        {
            int w = window.width, h = window.height, n = w * h;
            float step = cell / SeaTexelsPerCell;
            var depth = new float[n];
            var wet = new bool[n];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float wx = rect.x + (window.x + x + 0.5f) * step, wz = rect.y - (window.y + y + 0.5f) * step;
                    float d = sea - ground(wx, wz);
                    depth[y * w + x] = d;
                    wet[y * w + x] = d > 0;
                }
            var toLand = Distance(wet, w, h, false);
            var toWater = Distance(wet, w, h, true);
            var px = new Color32[n];
            float perTexel = 1f / SeaTexelsPerCell;
            for (int i = 0; i < n; i++)
            {
                // Half a texel either side of the line where the ground crosses the sea.
                float shore = wet[i] ? (toLand[i] - 0.5f) * perTexel : -(toWater[i] - 0.5f) * perTexel;
                byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(0, depth[i]) / 8f * 255f), 0, 255);
                byte g = (byte)Mathf.Clamp(Mathf.RoundToInt((0.5f + shore / 16f) * 255f), 0, 255);
                px[i] = new Color32(r, g, 0, 255);
            }
            return px;
        }

        // Exact Euclidean distance, in texels, from each texel to the
        // nearest texel whose wet flag equals `target`, by Felzenszwalb and
        // Huttenlocher's two passes.
        public static float[] Distance(bool[] wet, int w, int h, bool target)
        {
            const double Far = 1e12;
            var g = new double[w * h];
            for (int i = 0; i < g.Length; i++) g[i] = wet[i] == target ? 0 : Far;
            int m = Mathf.Max(w, h);
            var f = new double[m];
            var d = new double[m];
            var v = new int[m];
            var z = new double[m + 1];
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++) f[y] = g[y * w + x];
                Pass(f, h, d, v, z);
                for (int y = 0; y < h; y++) g[y * w + x] = d[y];
            }
            var result = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++) f[x] = g[y * w + x];
                Pass(f, w, d, v, z);
                for (int x = 0; x < w; x++) result[y * w + x] = (float)Math.Sqrt(Math.Min(d[x], Far));
            }
            return result;
        }

        static void Pass(double[] f, int n, double[] d, int[] v, double[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = double.NegativeInfinity;
            z[1] = double.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                double s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = double.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                double dq = q - v[k];
                d[q] = dq * dq + f[v[k]];
            }
        }
    }
}
