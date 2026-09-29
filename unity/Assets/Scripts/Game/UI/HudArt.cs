// HudArt.cs - the battle HUD's Carolingian skin, painted in code: vellum,
// two- and three-strand interlace, Solomon's knots, a quiet ruled panel,
// jewelled bosses, the crystal ball and the experience shield. Each piece
// is painted at the HUD's scale, so strands and keylines stay crisp from
// 720p to 4K. Button, spell and build pictures are the player's own game
// files and never come from here.
using System;
using UnityEngine;

namespace OpenKingdomsUnity.Game.UI
{
    public static partial class HudArt
    {
        public static readonly Color Vellum = Hex(0xEDE0C4), VellumShade = Hex(0xD9C7A0), VellumEdge = Hex(0xB89A68);
        public static readonly Color Ink = Hex(0x2E2118), Minium = Hex(0xA8291B), Azurite = Hex(0x2F4F9A), Verdigris = Hex(0x2E6346);
        // The inks darkened for small numbers, which thin strokes would fade.
        public static readonly Color MiniumDeep = Hex(0x861E13), AzuriteDeep = Hex(0x213B78);
        public static readonly Color Gold = Hex(0xC8A24A), GoldHi = Hex(0xF3DC8A), GoldShadow = Hex(0x7A5A1E);
        public static readonly Color Purple = Hex(0x3B1F3A), Silver = Hex(0xD5D9DF);
        public static readonly Color Garnet = Hex(0x8E1B2A), Sapphire = Hex(0x1F3E8A), Emerald = Hex(0x1E6B4A), Pearl = Hex(0xEDE6D6);

        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        // Textures are painted at the scale rounded up to a quarter step.
        public static float Density(float s) => Mathf.Ceil(s * 4f - 0.001f) / 4f;

        // A painted picture, rows bottom first as Unity uploads them.
        public sealed class Sheet
        {
            public int W, H;
            public Color[] Px;
            public bool Repeat;
        }

        static Texture2D New(int w, int h, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            var t = new Texture2D(Mathf.Max(1, w), Mathf.Max(1, h), TextureFormat.RGBA32, false)
            { hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear, wrapMode = wrap };
            return t;
        }

        public static Texture2D ToTexture(Sheet s)
        {
            var t = New(s.W, s.H, s.Repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp);
            t.SetPixels(s.Px);
            t.Apply(false);
            return t;
        }

        // Fills a sheet from a function of the texel centre in cp, y down.
        static Sheet Paint(float wCp, float hCp, float s, Func<float, float, Color> px, bool repeat = false)
        {
            int w = Mathf.Max(1, Mathf.RoundToInt(wCp * s)), h = Mathf.Max(1, Mathf.RoundToInt(hCp * s));
            var c = new Color[w * h];
            float sx = wCp / w, sy = hCp / h;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    c[(h - 1 - y) * w + x] = px((x + 0.5f) * sx, (y + 0.5f) * sy);
            return new Sheet { W = w, H = h, Px = c, Repeat = repeat };
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / 16777215f;
            }
        }

        static float Cover(float d, float r, float aa) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r - aa, r + aa, d));

        // Straight-alpha "over": over with coverage a on top of under.
        static Color Over(Color under, Color over, float a)
        {
            float ao = Mathf.Clamp01(a * over.a), au = under.a * (1f - ao), out_ = ao + au;
            if (out_ <= 0f) return Color.clear;
            var c = (over * ao + under * au) / out_;
            c.a = out_;
            return c;
        }

        // ---- Vellum ----

        static float Noise(float x, float y, int cellsX, int cellsY, float periodX, float periodY, int seed)
        {
            float fx = x / periodX * cellsX, fy = y / periodY * cellsY;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            float H(int a, int b) => Hash(((a % cellsX) + cellsX) % cellsX, ((b % cellsY) + cellsY) % cellsY, seed);
            return Mathf.Lerp(Mathf.Lerp(H(x0, y0), H(x0 + 1, y0), tx), Mathf.Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), tx), ty);
        }

        public const float VellumTileCp = 96f;

        // Fibre noise without stains, tiling every VellumTileCp.
        public static Sheet VellumTile(Color baseColour)
        {
            const int n = 256;
            const float p = n;
            var c = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fibre = Noise(x, y, 5, 42, p, p, 11) - 0.5f;
                    float cloud = Noise(x, y, 6, 6, p, p, 12) - 0.5f;
                    float grain = Hash(x, y, 13) - 0.5f;
                    float k = 1f + fibre * 0.05f + cloud * 0.06f + grain * 0.025f;
                    var v = baseColour * k;
                    v.b *= 1f - cloud * 0.03f;
                    v.a = 1f;
                    c[y * n + x] = v;
                }
            return new Sheet { W = n, H = n, Px = c, Repeat = true };
        }

        // ---- Interlace ----

        struct Strand { public float Offset, Slope, Depth; }

        // Strands along u (cp), each at an offset across the band with a
        // depth: where two cross, the deeper passes under. Gold cores, ink
        // outlines, and a shadow on a strand where one crosses over it.
        static Color Weave(float v, Strand[] st, float core, float outline, float aa, Color ground)
        {
            var c = ground;
            Array.Sort(st, (a, b) => a.Depth.CompareTo(b.Depth));
            for (int i = 0; i < st.Length; i++)
            {
                float d = Mathf.Abs(v - st[i].Offset) / Mathf.Sqrt(1f + st[i].Slope * st[i].Slope);
                float outer = Cover(d, core + outline, aa);
                if (outer <= 0f) continue;
                float shade = (v - st[i].Offset) / Mathf.Max(0.01f, core);
                var gold = shade < 0 ? Color.Lerp(Gold, GoldHi, Mathf.Clamp01(-shade) * 0.85f) : Color.Lerp(Gold, GoldShadow, Mathf.Clamp01(shade) * 0.75f);
                var sc = Color.Lerp(Ink, gold, Cover(d, core, aa));
                float shadow = 0f;
                for (int j = i + 1; j < st.Length; j++)
                {
                    float dj = Mathf.Abs(v - st[j].Offset) / Mathf.Sqrt(1f + st[j].Slope * st[j].Slope);
                    shadow = Mathf.Max(shadow, Cover(dj, core + outline + 0.9f, 0.6f));
                }
                sc = Color.Lerp(sc, sc * 0.62f, shadow);
                sc.a = 1f;
                c = Over(c, sc, outer);
            }
            return c;
        }

        // A two-strand twist along a band of width wCp, one period of pCp.
        // Vertical bands run their strands down the texture.
        public static Sheet TwistTile(float wCp, float pCp, float s, bool vertical, Color ground)
        {
            float core = wCp * 0.14f, outline = Mathf.Max(wCp * 0.06f, 1f / s);
            float amp = Mathf.Max(0.2f, (wCp - 2f * (core + outline)) / 2f - 0.15f);
            float aa = 0.6f / s;
            var st = new Strand[2];
            Color Px(float u, float v)
            {
                for (int i = 0; i < 2; i++)
                {
                    float a = 2f * Mathf.PI * u / pCp + i * Mathf.PI;
                    st[i] = new Strand { Offset = wCp / 2f + amp * Mathf.Sin(a), Slope = amp * 2f * Mathf.PI / pCp * Mathf.Cos(a), Depth = Mathf.Cos(a) };
                }
                return Weave(v, st, core, outline, aa, ground);
            }
            return vertical
                ? Paint(wCp, pCp, s, (x, y) => Px(y, x), true)
                : Paint(pCp, wCp, s, (x, y) => Px(x, y), true);
        }

        // A three-strand plait filling a vertical panel, with a gold frame.
        public static Sheet PlaitPanel(float wCp, float hCp, float s, Color ground)
        {
            float frame = 1.25f;
            float inner = wCp - 2 * frame;
            float core = inner * 0.12f, outline = Mathf.Max(inner * 0.035f, 1f / s);
            float amp = inner / 2f - core - outline - 0.4f;
            float period = inner * 1.9f;
            float aa = 0.6f / s;
            var st = new Strand[3];
            return Paint(wCp, hCp, s, (x, y) =>
            {
                var f = Frame(x, y, wCp, hCp, frame, s);
                if (f.a > 0.99f) return f;
                float u = y - hCp / 2f, v = x;
                for (int i = 0; i < 3; i++)
                {
                    float a = 2f * Mathf.PI * u / period + i * 2f * Mathf.PI / 3f;
                    st[i] = new Strand { Offset = wCp / 2f + amp * Mathf.Sin(a), Slope = amp * 2f * Mathf.PI / period * Mathf.Cos(a), Depth = Mathf.Sin(2f * a) };
                }
                return Over(Weave(v, st, core, outline, aa, ground), f, f.a);
            });
        }

        // A thin gold frame with ink keylines, clear inside.
        static Color Frame(float x, float y, float w, float h, float t, float s)
        {
            float d = Mathf.Min(Mathf.Min(x, w - x), Mathf.Min(y, h - y));
            if (d > t + 1f / s) return Color.clear;
            float ink = 0.9f / s;
            if (d < ink || d > t - ink * 0.5f) return Ink;
            float mid = (d - ink) / Mathf.Max(0.01f, t - 1.5f * ink);
            return Color.Lerp(GoldHi, GoldShadow, mid);
        }

        // Coverage of a band from a to b along d.
        static float Band(float d, float a, float b, float aa) =>
            Mathf.Clamp01(Mathf.Min((d - a) / aa + 0.5f, (b - d) / aa + 0.5f));

        // The empty slot under the minimap: vellum in a thin ink, minium and
        // gold ruled frame, with one small closed knot in a ruled roundel,
        // all in muted inks so it never pulls the eye from the map.
        public static Sheet FillerPanel(float wCp, float hCp, float s)
        {
            float aa = 0.6f / s, hair = Mathf.Max(0.5f, 1f / s);
            var keyline = new Color(Ink.r, Ink.g, Ink.b, 0.45f);
            var red = new Color(Minium.r, Minium.g, Minium.b, 0.6f);
            var gold = new Color(Gold.r, Gold.g, Gold.b, 0.8f);
            float size = Mathf.Min(26f, Mathf.Min(wCp, hCp) * 0.28f), k = size / 12f;
            float len = 2.6f * k, rad = 1.85f * k, core = 0.72f * k, outline = Mathf.Max(0.36f * k, hair);
            float ring = size * 0.72f;
            var strand = Color.Lerp(Vellum, GoldShadow, 0.38f);
            var edge = Color.Lerp(Vellum, Ink, 0.4f);
            float Loop(float a, float b) { float q = Mathf.Max(0f, Mathf.Abs(a) - len); return Mathf.Abs(Mathf.Sqrt(q * q + b * b) - rad); }
            return Paint(wCp, hCp, s, (x, y) =>
            {
                float d = Mathf.Min(Mathf.Min(x, wCp - x), Mathf.Min(y, hCp - y));
                var c = Color.clear;
                c = Over(c, keyline, Band(d, 0f, hair, aa));
                c = Over(c, red, Band(d, 1.5f, 1.5f + hair * 1.4f, aa));
                c = Over(c, gold, Band(d, 3f, 3.9f, aa));
                float px = x - wCp / 2f, py = y - hCp / 2f, r = Mathf.Sqrt(px * px + py * py);
                c = Over(c, red, Band(r, ring - hair * 0.7f, ring + hair * 0.7f, aa));
                c = Over(c, keyline, Band(r, ring + 1.6f, ring + 1.6f + hair, aa));
                float dh = Loop(px, py), dv = Loop(py, px);
                bool hOver = px * py > 0;
                for (int pass = 0; pass < 2; pass++)
                {
                    bool isH = (pass == 1) == hOver;
                    float dd = isH ? dh : dv;
                    float outer = Cover(dd, core + outline, aa);
                    if (outer <= 0f) continue;
                    var sc = Color.Lerp(edge, strand, Cover(dd, core, aa));
                    if (pass == 0) sc = Color.Lerp(sc, Color.Lerp(sc, edge, 0.5f), Cover(isH ? dv : dh, core + outline + 0.7f * k, 0.5f * k));
                    sc.a = 1f;
                    c = Over(c, sc, outer);
                }
                return c;
            });
        }

        // Two interlaced loops, a Solomon's knot, on purple in a keyline.
        public static Sheet SolomonKnot(float sizeCp, float s)
        {
            float half = sizeCp / 2f, k = sizeCp / 12f;
            float len = 2.6f * k, rad = 1.85f * k, core = 0.72f * k, outline = Mathf.Max(0.36f * k, 1f / s), aa = 0.6f / s;
            float Loop(float a, float b) { float q = Mathf.Max(0f, Mathf.Abs(a) - len); return Mathf.Abs(Mathf.Sqrt(q * q + b * b) - rad); }
            return Paint(sizeCp, sizeCp, s, (x, y) =>
            {
                float px = x - half, py = y - half;
                var c = Purple;
                float edge = Mathf.Min(Mathf.Min(x, sizeCp - x), Mathf.Min(y, sizeCp - y));
                if (edge < 0.9f / s) return Ink;
                if (edge < 0.9f / s + 0.8f * k) c = Color.Lerp(Gold, GoldShadow, 0.3f);
                float dh = Loop(px, py), dv = Loop(py, px);
                bool hOver = px * py > 0;
                for (int pass = 0; pass < 2; pass++)
                {
                    bool isH = (pass == 1) == hOver;
                    float d = isH ? dh : dv;
                    float outer = Cover(d, core + outline, aa);
                    if (outer <= 0f) continue;
                    var sc = Color.Lerp(Ink, Color.Lerp(GoldHi, Gold, 0.4f), Cover(d, core, aa));
                    if (pass == 0) sc = Color.Lerp(sc, sc * 0.62f, Cover(isH ? dv : dh, core + outline + 0.7f * k, 0.5f * k));
                    sc.a = 1f;
                    c = Over(c, sc, outer);
                }
                return c;
            });
        }

        // A circle of twist on azurite in a gold ring: the strip's end cap.
        public static Sheet Roundel(float diamCp, float s)
        {
            float r = diamCp / 2f, ring = 2f, mid = r * 0.66f, bandW = r * 0.42f;
            int periods = Mathf.Max(6, Mathf.RoundToInt(2f * Mathf.PI * mid / 8f));
            float pCp = 2f * Mathf.PI * mid / periods;
            float core = bandW * 0.14f, outline = Mathf.Max(bandW * 0.06f, 1f / s), aa = 0.6f / s;
            float amp = (bandW - 2f * (core + outline)) / 2f - 0.15f;
            var st = new Strand[2];
            return Paint(diamCp, diamCp, s, (x, y) =>
            {
                float dx = x - r, dy = y - r, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > r + 0.5f / s) return Color.clear;
                float edgeA = Cover(d, r, 0.5f / s);
                Color c;
                if (d > r - 0.9f / s || (d > r - ring - 0.9f / s && d < r - ring)) c = Ink;
                else if (d > r - ring) c = Color.Lerp(GoldHi, GoldShadow, Mathf.Clamp01(0.5f + (dx + dy) / (2f * r)));
                else
                {
                    c = Azurite;
                    float u = (Mathf.Atan2(dy, dx) + Mathf.PI) * mid, v = d - (mid - bandW / 2f);
                    for (int i = 0; i < 2; i++)
                    {
                        float a = 2f * Mathf.PI * u / pCp + i * Mathf.PI;
                        st[i] = new Strand { Offset = bandW / 2f + amp * Mathf.Sin(a), Slope = amp * 2f * Mathf.PI / pCp * Mathf.Cos(a), Depth = Mathf.Cos(a) };
                    }
                    if (v > -1f && v < bandW + 1f) c = Weave(v, st, core, outline, aa, c);
                    if (d < r * 0.34f) c = Purple;
                }
                c.a = edgeA;
                return c;
            });
        }

        // ---- Jewels, orb and shield ----

        // A cabochon in a gold bezel with an ink shadow below right.
        public static Sheet Boss(float diamCp, float s, Color stone)
        {
            float size = diamCp + 1f, r = diamCp / 2f, bezel = Mathf.Min(1.5f, r * 0.42f), aa = 0.6f / s;
            return Paint(size, size, s, (x, y) =>
            {
                float cx = r, cy = r;
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                float ds = Vector2.Distance(new Vector2(x, y), new Vector2(cx + 0.7f, cy + 0.7f));
                var c = Ink;
                c.a = Cover(ds, r, aa) * 0.8f;
                float body = Cover(d, r, aa);
                if (body <= 0f) return c;
                float lx = (x - cx) / r, ly = (y - cy) / r;
                var g = Color.Lerp(GoldHi, GoldShadow, Mathf.Clamp01(0.5f + 0.55f * (lx + ly)));
                g = Color.Lerp(Ink, g, Cover(d, r - 0.35f / s, aa));
                float rs = r - bezel;
                if (d < rs + aa)
                {
                    float t = d / rs;
                    var st = Color.Lerp(stone * 1.35f, stone * 0.55f, t * t);
                    float hl = Vector2.Distance(new Vector2(lx, ly), new Vector2(-0.3f, -0.32f));
                    st = Color.Lerp(st, Color.white, Mathf.Clamp01(1f - hl / 0.28f) * 0.85f);
                    st.a = 1f;
                    g = Color.Lerp(g, st, Cover(d, rs, aa));
                }
                g.a = 1f;
                return Over(c, g, body);
            });
        }

        public const float BallCp = 36f;

        // How much of the ball, from its foot, the liquid of a pool this
        // full covers: the painted full ball cropped to it shows the level.
        public static float BallLiquidCp(float fill)
        {
            float r = BallCp / 2f, glass = r - 2.4f;
            return r - glass + 2f * glass * Mathf.Clamp01(fill);
        }

        // The crystal ball: azurite rising in dark glass, a meniscus and a
        // highlight, in a 2 cp gold bezel.
        public static Sheet Ball(float fill, float s)
        {
            float r = BallCp / 2f, glass = r - 2.4f, aa = 0.6f / s;
            float level = glass - 2f * glass * Mathf.Clamp01(fill);
            return Paint(BallCp, BallCp, s, (x, y) =>
            {
                float dx = x - r, dy = y - r, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > r + aa) return Color.clear;
                Color c;
                if (d > glass)
                {
                    c = Color.Lerp(GoldHi, GoldShadow, Mathf.Clamp01(0.5f + (dx + dy) / (2.4f * r)));
                    if (d > r - 0.8f / s || d < glass + 0.8f / s) c = Ink;
                }
                else
                {
                    float t = d / glass;
                    c = Color.Lerp(Purple * 0.8f, Purple * 0.45f, t * t);
                    if (dy > level)
                    {
                        var liq = Color.Lerp(Azurite * 1.25f, Azurite * 0.55f, t * t);
                        if (dy - level < 1.1f) liq = Color.Lerp(liq, Color.Lerp(Azurite, Silver, 0.55f), 1f - (dy - level) / 1.1f);
                        c = liq;
                    }
                    float hl = Vector2.Distance(new Vector2(dx, dy), new Vector2(-glass * 0.38f, -glass * 0.42f));
                    c = Color.Lerp(c, Color.white, Mathf.Clamp01(1f - hl / (glass * 0.3f)) * 0.55f);
                    float rim = Mathf.Clamp01((t - 0.86f) / 0.14f);
                    c = Color.Lerp(c, Silver * 0.7f, rim * 0.25f);
                }
                c.a = Cover(d, r, aa);
                return c;
            });
        }

        // A heater shield: rank 0 plain vellum, 1 half gold, 2 all gold.
        public static Sheet Shield(int rank, float s)
        {
            const float w = 11f, h = 22f;
            float aa = 0.6f / s, ink = Mathf.Max(0.75f, 1f / s);
            float Inside(float x, float y)
            {
                float u = Mathf.Abs(x - w / 2f) / (w / 2f), v = y / h;
                float half = v < 0.45f ? 1f : Mathf.Max(0f, 1f - Mathf.Pow((v - 0.45f) / 0.55f, 1.7f));
                return Mathf.Min((half - u) * w / 2f, y, h - y);
            }
            return Paint(w, h, s, (x, y) =>
            {
                float d = Inside(x, y);
                if (d < -aa) return Color.clear;
                Color fillC = rank >= 2 || (rank == 1 && x < w / 2f)
                    ? Color.Lerp(GoldHi, GoldShadow, Mathf.Clamp01(y / h * 0.8f + (x / w) * 0.3f))
                    : Vellum;
                var c = d < ink ? Ink : fillC;
                c.a = Mathf.Clamp01((d + aa) / (2f * aa));
                return c;
            });
        }

        // A lozenge button face: vellum inside a gold bezel, pointed ends.
        public static Sheet Lozenge(float wCp, float hCp, float s, bool lit)
        {
            float aa = 0.6f / s, ink = Mathf.Max(0.6f, 1f / s), bezel = 1.2f;
            return Paint(wCp, hCp, s, (x, y) =>
            {
                float half = hCp / 2f, dy = Mathf.Abs(y - half);
                float tip = Mathf.Min(x, wCp - x);
                float d = Mathf.Min(half - dy, tip - dy * 0.6f);
                if (d < -aa) return Color.clear;
                Color c = d < ink ? Ink : d < ink + bezel ? (lit ? GoldHi : Gold) : Vellum;
                c.a = Mathf.Clamp01((d + aa) / (2f * aa));
                return c;
            });
        }

        // ---- Sharp upscaling of the game's small pictures ----

        // Nearest-neighbour growth to the next whole number, so the GPU's
        // bilinear only ever shrinks what it draws, a little.
        public static Texture2D Sharp(Texture2D src, float factor)
        {
            if (src == null) return null;
            int k = Mathf.Max(1, Mathf.CeilToInt(factor - 0.05f));
            if (k == 1) { src.filterMode = FilterMode.Bilinear; return src; }
            int w = src.width, h = src.height;
            var from = src.GetPixels32();
            var to = new Color32[w * k * h * k];
            for (int y = 0; y < h * k; y++)
                for (int x = 0; x < w * k; x++)
                    to[y * w * k + x] = from[(y / k) * w + x / k];
            var t = New(w * k, h * k);
            t.SetPixels32(to);
            t.Apply(false);
            return t;
        }
    }
}
