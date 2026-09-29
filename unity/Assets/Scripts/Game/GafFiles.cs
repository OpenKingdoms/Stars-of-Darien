// GafFiles.cs - reads the original's interface art from loose game files:
// a frame of an entry in anims/<name>.gaf, in the palette of the .pcx
// beside it. The backends use it where the player's own files sit
// unpacked. Nothing it reads is ever stored in the project.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    // A frame of the original's art. Origin is the frame's anchor from its
    // top left, as the game places it.
    public sealed class ArtFrame
    {
        public RgbaImage Image;
        public Vector2Int Origin;
        public int Frames;          // how many the entry has
    }

    public static class GafFiles
    {
        static readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Color32[]> palettes = new Dictionary<string, Color32[]>(StringComparer.OrdinalIgnoreCase);

        // dir holds the .gaf and .pcx files (the game's anims folder).
        public static ArtFrame Read(string dir, string gaf, string entry, int frame)
        {
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(gaf)) return null;
            try
            {
                var data = Load(Path.Combine(dir, gaf));
                var pal = Palette(Path.Combine(dir, Path.ChangeExtension(gaf, ".pcx")));
                if (data == null || pal == null) return null;
                return Decode(data, pal, entry, frame);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{gaf} {entry}: {e.Message}");
                return null;
            }
        }

        public static void Forget()
        {
            files.Clear();
            palettes.Clear();
        }

        static byte[] Load(string path)
        {
            if (files.TryGetValue(path, out var d)) return d;
            d = File.Exists(path) ? File.ReadAllBytes(path) : null;
            files[path] = d;
            return d;
        }

        static Color32[] Palette(string path)
        {
            if (palettes.TryGetValue(path, out var p)) return p;
            p = null;
            if (File.Exists(path))
            {
                var d = File.ReadAllBytes(path);
                if (d.Length >= 769)
                {
                    p = new Color32[256];
                    int o = d.Length - 768;
                    for (int i = 0; i < 256; i++) p[i] = new Color32(d[o + 3 * i], d[o + 3 * i + 1], d[o + 3 * i + 2], 255);
                }
            }
            palettes[path] = p;
            return p;
        }

        static int I16(byte[] d, int o) => (short)(d[o] | d[o + 1] << 8);
        static int U16(byte[] d, int o) => d[o] | d[o + 1] << 8;
        static int I32(byte[] d, int o) => d[o] | d[o + 1] << 8 | d[o + 2] << 16 | d[o + 3] << 24;

        public static ArtFrame Decode(byte[] d, Color32[] pal, string entry, int frame)
        {
            int count = I32(d, 4);
            for (int e = 0; e < count; e++)
            {
                int ep = I32(d, 12 + 4 * e);
                int frames = U16(d, ep);
                int n = 0;
                while (n < 32 && d[ep + 8 + n] != 0) n++;
                string name = System.Text.Encoding.ASCII.GetString(d, ep + 8, n);
                if (!string.Equals(name, entry, StringComparison.OrdinalIgnoreCase)) continue;
                if (frame < 0 || frame >= frames) return null;
                int fp = I32(d, ep + 40 + 8 * frame);
                int w = U16(d, fp), h = U16(d, fp + 2);
                var img = new RgbaImage(w, h);
                Blit(d, pal, fp, img, 0, 0);
                return new ArtFrame { Image = img, Origin = new Vector2Int(I16(d, fp + 4), I16(d, fp + 6)), Frames = frames };
            }
            return null;
        }

        // A frame's pixels into img with its top left at (ox, oy). A frame
        // made of subframes draws each at its own anchor.
        static void Blit(byte[] d, Color32[] pal, int fp, RgbaImage img, int ox, int oy)
        {
            int w = U16(d, fp), h = U16(d, fp + 2);
            int ax = I16(d, fp + 4), ay = I16(d, fp + 6);
            int trans = d[fp + 8], compressed = d[fp + 9], subs = U16(d, fp + 10);
            int px = I32(d, fp + 16);
            if (subs > 0)
            {
                for (int s = 0; s < subs; s++)
                {
                    int sp = I32(d, px + 4 * s);
                    Blit(d, pal, sp, img, ox + ax - I16(d, sp + 4), oy + ay - I16(d, sp + 6));
                }
                return;
            }
            void Put(int x, int y, int c)
            {
                x += ox; y += oy;
                if (x < 0 || y < 0 || x >= img.Width || y >= img.Height) return;
                var col = pal[c];
                int i = (y * img.Width + x) * 4;
                img.Pixels[i] = col.r; img.Pixels[i + 1] = col.g; img.Pixels[i + 2] = col.b; img.Pixels[i + 3] = 255;
            }
            if (compressed == 0)
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int c = d[px + y * w + x];
                        if (c != trans) Put(x, y, c);
                    }
                return;
            }
            int p = px;
            for (int y = 0; y < h; y++)
            {
                int len = U16(d, p), q = p + 2, end = q + len, x = 0;
                while (q < end && x < w)
                {
                    int m = d[q++];
                    if ((m & 1) != 0) x += m >> 1;
                    else if ((m & 2) != 0)
                    {
                        int c = d[q++];
                        for (int k = 0; k <= m >> 2; k++) Put(x++, y, c);
                    }
                    else
                        for (int k = 0; k <= m >> 2; k++) Put(x++, y, d[q++]);
                }
                p = end;
            }
        }
    }
}
