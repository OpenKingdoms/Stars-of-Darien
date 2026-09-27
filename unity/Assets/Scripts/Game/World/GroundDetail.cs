// GroundDetail.cs - the ground's close-up detail, until art supplies a set
// per climate: one tiling picture made once from noise, with lightness in
// red, a bump's slope in green and blue, and a rock pattern in alpha. The
// terrain shader lays the first three over the map's own picture up close,
// and the rock over cliffs, each centred so the ground's mean colour keeps.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public static class GroundDetail
    {
        public const int Size = 256;
        public const float TileUnits = 4f;
        public const float FadeStart = 16f, FadeEnd = 40f;

        static Texture2D tex;

        public static bool On { get; private set; }

        // Sets the shader globals, with the detail on or off.
        public static void Apply(bool on)
        {
            if (tex == null) tex = Make();
            On = on;
            Shader.SetGlobalTexture("_OkuDetail", tex);
            Shader.SetGlobalVector("_OkuDetailParams", new Vector4(TileUnits, FadeStart, FadeEnd, on ? 1f : 0f));
        }

        public static Texture2D Make()
        {
            int n = Size * Size;
            var bump = new float[n];
            var light = new float[n];
            var rock = new float[n];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = (float)x / Size, v = (float)y / Size;
                    int i = y * Size + x;
                    bump[i] = Fbm(u, v, 8, 4, 11);
                    light[i] = Fbm(u, v, 16, 3, 23) * 0.6f + bump[i] * 0.4f;
                    rock[i] = 1f - Mathf.Abs(Fbm(u, v, 4, 5, 37) * 2f - 1f);
                }
            Centre(light);
            Centre(rock);
            var px = new Color32[n];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    float gx = bump[y * Size + (x + 1) % Size] - bump[y * Size + (x + Size - 1) % Size];
                    float gz = bump[((y + 1) % Size) * Size + x] - bump[((y + Size - 1) % Size) * Size + x];
                    px[i] = new Color32(Byte(light[i]), Byte(0.5f + gx * 6f), Byte(0.5f + gz * 6f), Byte(rock[i]));
                }
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true)
            {
                name = "ground detail", hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4,
            };
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        static byte Byte(float f) => (byte)Mathf.Clamp(Mathf.RoundToInt(f * 255f), 0, 255);

        // Mean 0.5, spread to about a quarter either way.
        static void Centre(float[] a)
        {
            double sum = 0, sq = 0;
            foreach (float f in a) { sum += f; sq += f * f; }
            float mean = (float)(sum / a.Length);
            float sd = Mathf.Sqrt(Mathf.Max(1e-6f, (float)(sq / a.Length) - mean * mean));
            for (int i = 0; i < a.Length; i++) a[i] = Mathf.Clamp01(0.5f + (a[i] - mean) / sd * 0.12f);
        }

        // Value noise that tiles: a lattice of period cells across the picture.
        static float Noise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            float a = Hash(x0, y0, period, seed), b = Hash(x0 + 1, y0, period, seed);
            float c = Hash(x0, y0 + 1, period, seed), d = Hash(x0 + 1, y0 + 1, period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Hash(int x, int y, int period, int seed)
        {
            x = ((x % period) + period) % period;
            y = ((y % period) + period) % period;
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144665);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xffff) / 65535f;
        }

        static float Fbm(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            for (int o = 0; o < octaves; o++)
            {
                sum += Noise(u, v, period << o, seed + o * 17) * amp;
                norm += amp;
                amp *= 0.5f;
            }
            return sum / norm;
        }
    }
}
