// FxArt.cs - an effect strip ready to draw smoothly: each frame in its own
// padded cell with its edge colour bled outward, then a copy at twice the
// size through a bicubic filter, made on a worker and swapped in.
using System.Threading.Tasks;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FxArt
    {
        // Clear pixels round each cell, at the strip's own size.
        public const int Pad = 3;
        public const int Upscale = 2;
        const int Mips = 3;

        public Texture2D Texture { get; private set; }
        public int Frames { get; }
        public int CellW { get; }
        public int CellH { get; }
        public int Scale { get; private set; } = 1;
        // The art's average colour, brightest channel 1, and how much light
        // it gives, 0 to 1, for the light it casts.
        public Color Glow { get; }
        public float Brightness { get; }
        public bool Pending => upscaling != null;

        readonly int atlasW, atlasH;
        Task<byte[]> upscaling;

        FxArt(int frames, int cellW, int cellH, Color glow, float brightness)
        {
            Frames = frames;
            CellW = cellW;
            CellH = cellH;
            Glow = glow;
            Brightness = brightness;
            atlasW = frames * (cellW + 2 * Pad);
            atlasH = cellH + 2 * Pad;
        }

        // A strip of `frames` equal cells side by side, row 0 at the top.
        // Frames 0 or a width that does not split evenly makes one cell.
        public static FxArt Build(RgbaImage strip, int frames, bool upscale)
        {
            if (strip == null || strip.Width <= 0 || strip.Height <= 0) return null;
            if (frames <= 0 || strip.Width % frames != 0) frames = 1;
            int cw = strip.Width / frames, ch = strip.Height;
            Measure(strip, out var glow, out float bright);
            var art = new FxArt(frames, cw, ch, glow, bright);
            var cells = Atlas(strip, frames, out _, out _);
            art.Texture = MakeTexture(cells, art.atlasW, art.atlasH);
            if (upscale)
            {
                int w = art.atlasW, h = art.atlasH;
                art.upscaling = Task.Run(() => Bicubic2x(cells, w, h));
            }
            return art;
        }

        // Swaps in the smooth copy once the worker has made it. True when
        // the texture changed, so a material holding it can take the new one.
        public bool Poll()
        {
            if (upscaling == null || !upscaling.IsCompleted) return false;
            var t = upscaling;
            upscaling = null;
            if (t.Status != TaskStatus.RanToCompletion || t.Result == null) return false;
            var old = Texture;
            Texture = MakeTexture(t.Result, atlasW * Upscale, atlasH * Upscale);
            Scale = Upscale;
            Looks.Release(old);
            return true;
        }

        // The rectangle to sample for part of a frame given in strip UVs
        // (u across the whole strip, v down from the top), in the texture's
        // UVs: x, y the bottom left and z, w the top right.
        public Vector4 Rect(int frame, Vector2 uvMin, Vector2 uvMax)
        {
            frame = Mathf.Clamp(frame, 0, Frames - 1);
            float stripW = (float)CellW * Frames;
            float x0 = uvMin.x * stripW - frame * CellW, x1 = uvMax.x * stripW - frame * CellW;
            float y0 = uvMin.y * CellH, y1 = uvMax.y * CellH;
            float cx = frame * (CellW + 2 * Pad) + Pad;
            return new Vector4((cx + x0) / atlasW, 1f - (Pad + y1) / atlasH, (cx + x1) / atlasW, 1f - (Pad + y0) / atlasH);
        }

        public void Dispose()
        {
            Looks.Release(Texture);
            Texture = null;
        }

        // Each frame in its own padded cell, bottom row first as Unity
        // stores textures, with colour bled into the clear pixels.
        public static byte[] Atlas(RgbaImage strip, int frames, out int w, out int h)
        {
            int cw = strip.Width / frames, ch = strip.Height;
            w = frames * (cw + 2 * Pad);
            h = ch + 2 * Pad;
            var o = new byte[w * h * 4];
            for (int f = 0; f < frames; f++)
                for (int y = 0; y < ch; y++)
                {
                    int src = (y * strip.Width + f * cw) * 4;
                    int dst = ((h - 1 - (Pad + y)) * w + f * (cw + 2 * Pad) + Pad) * 4;
                    System.Buffer.BlockCopy(strip.Pixels, src, o, dst, cw * 4);
                }
            Bleed(o, w, h);
            return o;
        }

        static Texture2D MakeTexture(byte[] rgba, int w, int h)
        {
            // Linear: the shader decides per frame how the bytes are read.
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, Mips, true)
            {
                hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear, anisoLevel = 4, name = "effect strip",
            };
            tex.SetPixelData(rgba, 0);
            tex.Apply(true, true);
            return tex;
        }

        // Colour for the clear pixels from their coloured neighbours, a few
        // pixels out, so filtering at an edge blends toward the edge's own
        // colour and not toward black. Alpha stays 0.
        static void Bleed(byte[] p, int w, int h)
        {
            var done = new bool[w * h];
            for (int i = 0; i < w * h; i++) done[i] = p[i * 4 + 3] != 0;
            var next = new System.Collections.Generic.List<int>();
            for (int pass = 0; pass < Pad + 1; pass++)
            {
                next.Clear();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (done[i]) continue;
                        int r = 0, g = 0, b = 0, n = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int j = ny * w + nx;
                            if (!done[j]) continue;
                            r += p[j * 4]; g += p[j * 4 + 1]; b += p[j * 4 + 2]; n++;
                        }
                        if (n == 0) continue;
                        p[i * 4] = (byte)(r / n); p[i * 4 + 1] = (byte)(g / n); p[i * 4 + 2] = (byte)(b / n);
                        next.Add(i);
                    }
                foreach (int i in next) done[i] = true;
                if (next.Count == 0) break;
            }
        }

        static void Measure(RgbaImage img, out Color glow, out float brightness)
        {
            double r = 0, g = 0, b = 0, a = 0, lit = 0;
            var p = img.Pixels;
            int n = img.Width * img.Height;
            for (int i = 0; i < n; i++)
            {
                float al = p[i * 4 + 3] / 255f;
                if (al <= 0f) continue;
                float pr = p[i * 4] / 255f, pg = p[i * 4 + 1] / 255f, pb = p[i * 4 + 2] / 255f;
                r += pr * al; g += pg * al; b += pb * al; a += al;
                lit += (0.3f * pr + 0.59f * pg + 0.11f * pb) * al;
            }
            if (a <= 0) { glow = Color.white; brightness = 0f; return; }
            float max = (float)System.Math.Max(r, System.Math.Max(g, b));
            glow = max > 0 ? new Color((float)(r / max), (float)(g / max), (float)(b / max), 1f) : Color.white;
            // How bright the art is where it shows, alpha weighted.
            brightness = (float)(lit / a);
        }

        // Twice the size through a Catmull-Rom filter, one axis at a time,
        // clamped to the byte range. Runs on a worker thread.
        static byte[] Bicubic2x(byte[] src, int w, int h)
        {
            int w2 = w * 2, h2 = h * 2;
            var mid = new float[w2 * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w2; x++)
                {
                    float sx = (x + 0.5f) * 0.5f - 0.5f;
                    int ix = Mathf.FloorToInt(sx);
                    float t = sx - ix;
                    Weights(t, out float k0, out float k1, out float k2, out float k3);
                    for (int c = 0; c < 4; c++)
                        mid[(y * w2 + x) * 4 + c] =
                            k0 * At(src, w, h, ix - 1, y, c) + k1 * At(src, w, h, ix, y, c) +
                            k2 * At(src, w, h, ix + 1, y, c) + k3 * At(src, w, h, ix + 2, y, c);
                }
            var o = new byte[w2 * h2 * 4];
            for (int y = 0; y < h2; y++)
            {
                float sy = (y + 0.5f) * 0.5f - 0.5f;
                int iy = Mathf.FloorToInt(sy);
                float t = sy - iy;
                Weights(t, out float k0, out float k1, out float k2, out float k3);
                int y0 = Mathf.Clamp(iy - 1, 0, h - 1), y1 = Mathf.Clamp(iy, 0, h - 1), y2 = Mathf.Clamp(iy + 1, 0, h - 1), y3 = Mathf.Clamp(iy + 2, 0, h - 1);
                for (int x = 0; x < w2; x++)
                    for (int c = 0; c < 4; c++)
                    {
                        float v = k0 * mid[(y0 * w2 + x) * 4 + c] + k1 * mid[(y1 * w2 + x) * 4 + c] +
                                  k2 * mid[(y2 * w2 + x) * 4 + c] + k3 * mid[(y3 * w2 + x) * 4 + c];
                        o[(y * w2 + x) * 4 + c] = (byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255);
                    }
            }
            return o;
        }

        static float At(byte[] p, int w, int h, int x, int y, int c) =>
            p[(y * w + Mathf.Clamp(x, 0, w - 1)) * 4 + c];

        static void Weights(float t, out float k0, out float k1, out float k2, out float k3)
        {
            float t2 = t * t, t3 = t2 * t;
            k0 = 0.5f * (-t3 + 2f * t2 - t);
            k1 = 0.5f * (3f * t3 - 5f * t2 + 2f);
            k2 = 0.5f * (-3f * t3 + 4f * t2 + t);
            k3 = 0.5f * (t3 - t2);
        }
    }
}
