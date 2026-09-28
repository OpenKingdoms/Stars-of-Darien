// FxArt.cs - an effect strip ready to draw smoothly: each frame in its own
// padded cell with its edge colour bled outward, laid out on a worker, and
// only the texture made and uploaded on the main thread. The shader samples
// it bicubic when it is drawn larger than the art.
using System.Threading.Tasks;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FxArt
    {
        // Clear pixels round each cell, at the strip's own size.
        public const int Pad = 3;
        const int Mips = 3;

        public Texture2D Texture { get; private set; }
        public int Frames { get; }
        public int CellW { get; }
        public int CellH { get; }
        // The art's average colour with its brightest channel 1, the average
        // itself, and how much light it gives, 0 to 1. Known once Ready.
        public Color Glow { get; private set; } = Color.white;
        public Color Mean { get; private set; } = Color.white;
        public float Brightness { get; private set; }
        public bool Ready => Texture != null;
        public bool Pending => work != null;

        readonly int atlasW, atlasH;
        Task<Laid> work;
        bool disposed;

        sealed class Laid
        {
            public byte[] Pixels;
            public Color Glow, Mean;
            public float Brightness;
        }

        FxArt(int frames, int cellW, int cellH)
        {
            Frames = frames;
            CellW = cellW;
            CellH = cellH;
            atlasW = frames * (cellW + 2 * Pad);
            atlasH = cellH + 2 * Pad;
        }

        // A strip of `frames` equal cells side by side, row 0 at the top.
        // Frames 0 or a width that does not split evenly makes one cell. The
        // work runs on a worker, and Poll brings the texture in.
        public static FxArt Build(RgbaImage strip, int frames)
        {
            if (strip == null || strip.Width <= 0 || strip.Height <= 0) return null;
            if (frames <= 0 || strip.Width % frames != 0) frames = 1;
            var art = new FxArt(frames, strip.Width / frames, strip.Height);
            art.work = Task.Run(() => Lay(strip, frames));
            return art;
        }

        static Laid Lay(RgbaImage strip, int frames)
        {
            Measure(strip, out var glow, out var mean, out float bright);
            return new Laid { Pixels = Atlas(strip, frames, out _, out _), Glow = glow, Mean = mean, Brightness = bright };
        }

        // Makes the texture once the worker is done. True when it changed,
        // so a material can take it.
        public bool Poll()
        {
            if (work == null || !work.IsCompleted) return false;
            var t = work;
            work = null;
            if (disposed || t.Status != TaskStatus.RanToCompletion || t.Result == null) return false;
            var r = t.Result;
            Glow = r.Glow;
            Mean = r.Mean;
            Brightness = r.Brightness;
            Texture = MakeTexture(r.Pixels, atlasW, atlasH);
            return true;
        }

        // Blocks until the worker is done, for tests and loading.
        public bool Wait(int milliseconds = 10000)
        {
            if (work != null) work.Wait(milliseconds);
            return Poll() || Ready;
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
            disposed = true;
            work = null;
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
            var edge = new System.Collections.Generic.List<int>();
            var next = new System.Collections.Generic.List<int>();
            for (int i = 0; i < w * h; i++) done[i] = p[i * 4 + 3] != 0;
            // Only the clear pixels next to coloured ones, then their neighbours.
            for (int i = 0; i < w * h; i++) if (!done[i] && Touches(done, i, w, h)) edge.Add(i);
            for (int pass = 0; pass < Pad + 1 && edge.Count > 0; pass++)
            {
                next.Clear();
                foreach (int i in edge)
                {
                    if (done[i]) continue;
                    int x = i % w, y = i / w, r = 0, g = 0, b = 0, n = 0;
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
                edge.Clear();
                foreach (int i in next)
                {
                    int x = i % w, y = i / w;
                    if (x + 1 < w && !done[i + 1]) edge.Add(i + 1);
                    if (x > 0 && !done[i - 1]) edge.Add(i - 1);
                    if (y + 1 < h && !done[i + w]) edge.Add(i + w);
                    if (y > 0 && !done[i - w]) edge.Add(i - w);
                }
            }
        }

        static bool Touches(bool[] done, int i, int w, int h)
        {
            int x = i % w, y = i / w;
            return (x + 1 < w && done[i + 1]) || (x > 0 && done[i - 1]) || (y + 1 < h && done[i + w]) || (y > 0 && done[i - w]);
        }

        static void Measure(RgbaImage img, out Color glow, out Color mean, out float brightness)
        {
            double r = 0, g = 0, b = 0, a = 0, lit = 0;
            var p = img.Pixels;
            int n = img.Width * img.Height;
            for (int i = 0; i < n; i++)
            {
                int al8 = p[i * 4 + 3];
                if (al8 == 0) continue;
                float al = al8 / 255f;
                float pr = p[i * 4] / 255f, pg = p[i * 4 + 1] / 255f, pb = p[i * 4 + 2] / 255f;
                r += pr * al; g += pg * al; b += pb * al; a += al;
                lit += (0.3f * pr + 0.59f * pg + 0.11f * pb) * al;
            }
            if (a <= 0) { glow = mean = Color.white; brightness = 0f; return; }
            float max = (float)System.Math.Max(r, System.Math.Max(g, b));
            glow = max > 0 ? new Color((float)(r / max), (float)(g / max), (float)(b / max), 1f) : Color.white;
            mean = new Color((float)(r / a), (float)(g / a), (float)(b / a), 1f);
            // How bright the art is where it shows, alpha weighted.
            brightness = (float)(lit / a);
        }
    }
}
