// ContactSheet.cs - one picture of a whole clip: every frame in a grid with
// its number and time underneath, at most 2048 pixels wide. RGBA32 rows from
// the bottom up, as Unity keeps textures, and safe on a worker thread.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.Capture
{
    public static class ContactSheet
    {
        public const int MaxWidth = 2048, Pad = 6, TextScale = 2;
        public static int LabelHeight => Glyphs.Height * TextScale + 6;
        public static readonly Color32 Background = new Color32(22, 22, 24, 255), Ink = new Color32(235, 225, 190, 255);

        public struct Plan
        {
            public int Frames, Cols, Rows, ThumbW, ThumbH, Width, Height;

            // Where frame i goes, in picture pixels from the top left.
            public RectInt Thumb(int i) => new RectInt(Pad + i % Cols * (ThumbW + Pad), Pad + i / Cols * (ThumbH + LabelHeight + Pad), ThumbW, ThumbH);
            public RectInt Label(int i) { var t = Thumb(i); return new RectInt(t.x, t.yMax, ThumbW, LabelHeight); }
        }

        // Near square overall, never wider than maxWidth, and never larger
        // than the frames themselves.
        public static Plan PlanFor(int frames, int frameW, int frameH, int maxWidth = MaxWidth)
        {
            frames = Mathf.Max(1, frames);
            frameW = Mathf.Max(1, frameW);
            frameH = Mathf.Max(1, frameH);
            float aspect = frameW / (float)frameH;
            int cols = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(frames / aspect)), 1, frames);
            int widest = (maxWidth - Pad * (cols + 1)) / cols;
            while (widest < 16 && cols > 1) { cols--; widest = (maxWidth - Pad * (cols + 1)) / cols; }
            int tw = Mathf.Max(1, Mathf.Min(frameW, widest));
            int th = Mathf.Max(1, Mathf.RoundToInt(tw * frameH / (float)frameW));
            int rows = (frames + cols - 1) / cols;
            return new Plan
            {
                Frames = frames, Cols = cols, Rows = rows, ThumbW = tw, ThumbH = th,
                Width = Pad + cols * (tw + Pad), Height = Pad + rows * (th + LabelHeight + Pad),
            };
        }

        // "#07 0.50s": the frame's number from 1 and its time.
        public static string LabelFor(int index, float seconds) => $"#{index + 1:00} {seconds:0.00}s";

        // The sheet's pixels from the frames' shrunk pictures (each ThumbW by
        // ThumbH) and their times in seconds.
        public static byte[] Compose(Plan p, IList<byte[]> thumbs, IList<float> times)
        {
            var px = new byte[p.Width * p.Height * 4];
            for (int i = 0; i < px.Length; i += 4) { px[i] = Background.r; px[i + 1] = Background.g; px[i + 2] = Background.b; px[i + 3] = 255; }
            for (int i = 0; i < p.Frames && i < thumbs.Count; i++)
            {
                var t = p.Thumb(i);
                var src = thumbs[i];
                // A clip cut short leaves its last cells empty.
                if (src == null || src.Length < p.ThumbW * p.ThumbH * 4) continue;
                for (int ty = 0; ty < p.ThumbH; ty++)
                {
                    int row = p.Height - 1 - (t.y + p.ThumbH - 1 - ty);
                    Buffer.BlockCopy(src, ty * p.ThumbW * 4, px, (row * p.Width + t.x) * 4, p.ThumbW * 4);
                }
                var l = p.Label(i);
                float at = times != null && i < times.Count ? times[i] : 0f;
                DrawText(px, p.Width, p.Height, l.x + 2, l.y + 3, LabelFor(i, at), TextScale, Ink);
            }
            return px;
        }

        // A box filter from w by h to tw by th.
        public static byte[] Shrink(byte[] rgba, int w, int h, int tw, int th)
        {
            var dst = new byte[tw * th * 4];
            for (int y = 0; y < th; y++)
            {
                int y0 = y * h / th, y1 = Math.Max(y0 + 1, (y + 1) * h / th);
                for (int x = 0; x < tw; x++)
                {
                    int x0 = x * w / tw, x1 = Math.Max(x0 + 1, (x + 1) * w / tw);
                    int r = 0, g = 0, b = 0, a = 0, n = 0;
                    for (int sy = y0; sy < y1; sy++)
                    {
                        int at = (sy * w + x0) * 4;
                        for (int sx = x0; sx < x1; sx++, at += 4) { r += rgba[at]; g += rgba[at + 1]; b += rgba[at + 2]; a += rgba[at + 3]; n++; }
                    }
                    int o = (y * tw + x) * 4;
                    dst[o] = (byte)(r / n); dst[o + 1] = (byte)(g / n); dst[o + 2] = (byte)(b / n); dst[o + 3] = (byte)(a / n);
                }
            }
            return dst;
        }

        // Rows turned upside down, in place.
        public static void FlipRows(byte[] rgba, int w, int h)
        {
            int stride = w * 4;
            var tmp = new byte[stride];
            for (int y = 0; y < h / 2; y++)
            {
                int a = y * stride, b = (h - 1 - y) * stride;
                Buffer.BlockCopy(rgba, a, tmp, 0, stride);
                Buffer.BlockCopy(rgba, b, rgba, a, stride);
                Buffer.BlockCopy(tmp, 0, rgba, b, stride);
            }
        }

        // Text in the built-in pixel font, x and y from the picture's top left.
        public static void DrawText(byte[] rgba, int w, int h, int x, int y, string text, int scale, Color32 c)
        {
            foreach (char ch in text)
            {
                var g = Glyphs.For(ch);
                for (int gy = 0; gy < Glyphs.Height; gy++)
                    for (int gx = 0; gx < Glyphs.Width; gx++)
                    {
                        if ((g[gy] >> (Glyphs.Width - 1 - gx) & 1) == 0) continue;
                        for (int sy = 0; sy < scale; sy++)
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int px = x + gx * scale + sx, py = y + gy * scale + sy;
                                if (px < 0 || py < 0 || px >= w || py >= h) continue;
                                int o = ((h - 1 - py) * w + px) * 4;
                                rgba[o] = c.r; rgba[o + 1] = c.g; rgba[o + 2] = c.b; rgba[o + 3] = 255;
                            }
                    }
                x += (Glyphs.Width + 1) * scale;
            }
        }

        // Five by seven, rows from the top, the left pixel in the high bit.
        static class Glyphs
        {
            public const int Width = 5, Height = 7;
            static readonly Dictionary<char, byte[]> map = new Dictionary<char, byte[]>
            {
                ['0'] = new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E },
                ['1'] = new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E },
                ['2'] = new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F },
                ['3'] = new byte[] { 0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E },
                ['4'] = new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 },
                ['5'] = new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E },
                ['6'] = new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E },
                ['7'] = new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 },
                ['8'] = new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E },
                ['9'] = new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C },
                ['#'] = new byte[] { 0x0A, 0x0A, 0x1F, 0x0A, 0x1F, 0x0A, 0x0A },
                ['.'] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x0C },
                ['s'] = new byte[] { 0x00, 0x00, 0x0F, 0x10, 0x0E, 0x01, 0x1E },
                [':'] = new byte[] { 0x00, 0x0C, 0x0C, 0x00, 0x0C, 0x0C, 0x00 },
                ['-'] = new byte[] { 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00 },
            };
            static readonly byte[] blank = new byte[Height];

            public static byte[] For(char c) => map.TryGetValue(c, out var g) ? g : blank;
        }
    }
}
