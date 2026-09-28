// MockFxArt.cs - stand-in effect pictures painted from shapes and noise at the
// original art's sizes, frame counts, timing and blending, for the mock.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public static class MockFxArt
    {
        public sealed class Spec
        {
            public string Name;
            public int W, H, Frames, Duration = 2;
            public bool Additive;
            // The point the picture stands on, as fractions of the frame
            // from the left and from the top.
            public float AnchorX = 0.5f, AnchorY = 0.5f;
            // The first frames drawn small, growing to full size as the
            // retail blasts' trimmed frames do.
            public bool Grows;
            // Colour and alpha at x, y in -1..1 (y down) and phase t in 0..1.
            public Func<float, float, float, Color> Paint;
        }

        static readonly Dictionary<string, Spec> specs = new Dictionary<string, Spec>();

        public static IEnumerable<string> Names { get { Init(); return specs.Keys; } }

        public static Spec Find(string name)
        {
            Init();
            return name != null && specs.TryGetValue(name, out var s) ? s : null;
        }

        // Every frame of a spec side by side, row 0 at the top.
        public static RgbaImage Draw(Spec s)
        {
            var img = new RgbaImage(s.W * s.Frames, s.H);
            for (int f = 0; f < s.Frames; f++)
            {
                float t = f / (float)s.Frames;
                for (int y = 0; y < s.H; y++)
                    for (int x = 0; x < s.W; x++)
                    {
                        float nx = (x + 0.5f) / s.W * 2f - 1f, ny = (y + 0.5f) / s.H * 2f - 1f;
                        var c = s.Paint(nx, ny, t);
                        int i = (y * s.W * s.Frames + f * s.W + x) * 4;
                        img.Pixels[i] = To8(c.r); img.Pixels[i + 1] = To8(c.g); img.Pixels[i + 2] = To8(c.b);
                        img.Pixels[i + 3] = To8(c.a);
                    }
            }
            return img;
        }

        static byte To8(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        static void Add(string name, int w, int h, int frames, bool additive, Func<float, float, float, Color> paint, float ax = 0.5f, float ay = 0.5f, int duration = 2) =>
            specs[name] = new Spec { Name = name, W = w, H = h, Frames = frames, Additive = additive, Paint = paint, AnchorX = ax, AnchorY = ay, Duration = duration };

        static void Init()
        {
            if (specs.Count > 0) return;
            var fire = new[] { new Color(0.5f, 0.05f, 0f), new Color(0.95f, 0.35f, 0.02f), new Color(1f, 0.75f, 0.2f), new Color(1f, 0.97f, 0.75f) };
            var blue = new[] { new Color(0.05f, 0.1f, 0.5f), new Color(0.2f, 0.4f, 1f), new Color(0.55f, 0.8f, 1f), new Color(0.95f, 0.98f, 1f) };
            var cyan = new[] { new Color(0f, 0.25f, 0.35f), new Color(0.05f, 0.6f, 0.75f), new Color(0.4f, 0.95f, 1f), new Color(0.9f, 1f, 1f) };
            var violet = new[] { new Color(0.25f, 0.05f, 0.4f), new Color(0.55f, 0.2f, 0.85f), new Color(0.85f, 0.55f, 1f), new Color(1f, 0.9f, 1f) };
            var magenta = new[] { new Color(0.4f, 0f, 0.3f), new Color(0.8f, 0.15f, 0.7f), new Color(1f, 0.5f, 0.9f), new Color(1f, 0.92f, 1f) };
            var ice = new[] { new Color(0.1f, 0.2f, 0.3f), new Color(0.45f, 0.65f, 0.75f), new Color(0.75f, 0.92f, 1f), new Color(1f, 1f, 1f) };
            var water = new[] { new Color(0.05f, 0.15f, 0.3f), new Color(0.3f, 0.55f, 0.75f), new Color(0.7f, 0.88f, 0.95f), new Color(1f, 1f, 1f) };
            var green = new[] { new Color(0f, 0.25f, 0.1f), new Color(0.1f, 0.65f, 0.3f), new Color(0.5f, 1f, 0.55f), new Color(0.92f, 1f, 0.9f) };
            var yellow = new[] { new Color(0.4f, 0.3f, 0f), new Color(0.9f, 0.75f, 0.1f), new Color(1f, 0.95f, 0.45f), new Color(1f, 1f, 0.9f) };

            // Shots.
            Add("FireballA", 39, 39, 10, true, (x, y, t) => Ball(x, y, t, 0.8f, 0.35f, fire));
            Add("FireballB", 65, 65, 10, true, (x, y, t) => Ball(x, y, t, 0.8f, 0.45f, fire));
            Add("FireballCa", 34, 34, 10, true, (x, y, t) => Ball(x, y, t, 0.75f, 0.3f, violet));
            Add("FireballD", 34, 34, 10, true, (x, y, t) => Ball(x, y, t, 0.75f, 0.3f, magenta));
            Add("LtngBall_1a", 128, 128, 15, true, (x, y, t) => Crackle(x, y, t, 0.55f, cyan));
            Add("LtngBall_2a", 34, 32, 8, true, (x, y, t) => Crackle(x, y, t, 0.7f, blue));
            Add("WaterBall", 23, 25, 10, true, (x, y, t) => Bubble(x, y, t, water));
            Add("TsunamiSprite", 23, 25, 10, true, (x, y, t) => Bubble(x, y, t, water));
            Add("iceburst", 60, 66, 16, true, (x, y, t) => Ball(x, y, t, 0.7f, 0.5f, ice));
            Add("iceballspin", 18, 19, 8, true, (x, y, t) => Ball(x, y, t, 0.8f, 0.15f, ice));
            Add("meteor", 28, 73, 21, true, (x, y, t) => Streak(x, y, t, fire), 0.5f, 0.4f);
            Add("flame", 36, 34, 12, true, (x, y, t) => Flame(x, y, t, fire), 0.17f, 0.24f);
            Add("cannbsm", 6, 6, 4, false, (x, y, t) => Cannon(x, y, t));
            Add("cannbmed", 9, 9, 4, false, (x, y, t) => Cannon(x, y, t));
            Add("cannblg", 13, 12, 4, false, (x, y, t) => Cannon(x, y, t));
            // Impacts, standing on the ground.
            Add("explodeb", 74, 59, 31, true, (x, y, t) => Blast(x, y, t, fire), 0.5f, 0.8f, 1);
            specs["explodeb"].Grows = true;
            Add("VBlast", 61, 63, 21, true, (x, y, t) => Blast(x, y, t, fire), 0.5f, 0.8f);
            Add("flamestrike", 68, 85, 14, true, (x, y, t) => Column(x, y, t, fire), 0.5f, 0.9f);
            Add("teeny", 40, 44, 18, true, (x, y, t) => Blast(x, y, t, fire), 0.5f, 0.8f);
            Add("med", 56, 44, 22, true, (x, y, t) => Blast(x, y, t, fire), 0.5f, 0.8f);
            Add("large", 91, 71, 19, true, (x, y, t) => Blast(x, y, t, fire), 0.5f, 0.8f);
            Add("blue_shockring", 60, 57, 18, true, (x, y, t) => Shock(x, y, t, blue), 0.5f, 0.55f);
            Add("green_shockring", 60, 57, 18, true, (x, y, t) => Shock(x, y, t, green), 0.5f, 0.55f);
            Add("lightning1", 95, 72, 30, true, (x, y, t) => Sparks(x, y, t, blue), 0.5f, 0.75f);
            Add("Lodeexplode", 128, 128, 15, true, (x, y, t) => Blast(x, y, t, magenta), 0.5f, 0.7f);
            Add("iceballexp", 69, 63, 15, true, (x, y, t) => Blast(x, y, t, ice), 0.5f, 0.8f);
            Add("WaterBallExplode", 106, 108, 19, true, (x, y, t) => Splash(x, y, t, water), 0.5f, 0.75f);
            Add("TsunamiExplode", 102, 109, 12, false, (x, y, t) => Splash(x, y, t, water), 0.5f, 0.75f);
            Add("dustlg", 60, 48, 20, false, (x, y, t) => Dust(x, y, t), 0.5f, 0.8f);
            Add("DirtClodMed", 40, 34, 20, false, (x, y, t) => Dust(x, y, t), 0.5f, 0.8f);
            // Rings, rain and the rest.
            Add("ring_fx_red", 62, 66, 11, true, (x, y, t) => Blast(x, y, (t + 0.3f) % 1f, yellow), 0.5f, 0.7f);
            Add("ring_fx_white", 61, 61, 12, false, (x, y, t) => Blast(x, y, (t + 0.3f) % 1f, fire), 0.5f, 0.7f);
            Add("tornadoloop", 57, 100, 15, true, (x, y, t) => Funnel(x, y, t), 0.5f, 0.92f);
            Add("nimbus_aramon", 52, 47, 11, true, (x, y, t) => Nimbus(x, y, t, yellow), 0.5f, 0.6f, 3);
            Add("nimbus_taros", 52, 47, 11, true, (x, y, t) => Nimbus(x, y, t, fire), 0.5f, 0.6f, 3);
            Add("nimbus_veruna", 52, 47, 11, true, (x, y, t) => Nimbus(x, y, t, water), 0.5f, 0.6f, 3);
            // One flat colour of added light standing on its point, for tests.
            Add("flat", 32, 32, 1, true, (x, y, t) => new Color(0.30f, 0.12f, 0.02f, 1f), 0.5f, 1f);
            Add("flatbright", 32, 32, 1, true, (x, y, t) => new Color(0.60f, 0.55f, 0.10f, 1f), 0.5f, 1f);
            Add("nimbus_zhon", 52, 47, 11, true, (x, y, t) => Nimbus(x, y, t, green), 0.5f, 0.6f, 3);
        }

        // ── Painting ──────────────────────────────────────────────────

        static Color Ramp(Color[] r, float k)
        {
            k = Mathf.Clamp01(k) * (r.Length - 1);
            int i = Mathf.Min(r.Length - 2, (int)k);
            return Color.Lerp(r[i], r[i + 1], k - i);
        }

        static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xffff) / 65535f;
        }

        static float Noise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Mathf.Lerp(Hash(ix, iy), Hash(ix + 1, iy), fx);
            float b = Mathf.Lerp(Hash(ix, iy + 1), Hash(ix + 1, iy + 1), fx);
            return Mathf.Lerp(a, b, fy);
        }

        // Two octaves, looping once round as t goes 0 to 1.
        static float Fbm(float x, float y, float t)
        {
            float cx = Mathf.Cos(t * 6.2831853f) * 0.8f, cy = Mathf.Sin(t * 6.2831853f) * 0.8f;
            return 0.65f * Noise(x * 3f + cx, y * 3f + cy) + 0.35f * Noise(x * 7f - cy * 2f, y * 7f + cx * 2f);
        }

        static Color Glow(Color c, float a) => new Color(c.r, c.g, c.b, Mathf.Clamp01(a));

        static Color Ball(float x, float y, float t, float radius, float rough, Color[] ramp)
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float n = Fbm(x, y, t);
            float r = radius * (1f - rough * 0.5f + rough * n);
            if (d >= r) return Color.clear;
            float k = 1f - d / r;
            return Glow(Ramp(ramp, k * 1.2f + n * 0.3f), Mathf.SmoothStep(0f, 0.35f, k) * (0.65f + 0.35f * n));
        }

        static Color Crackle(float x, float y, float t, float radius, Color[] ramp)
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float n = Fbm(x * 1.5f, y * 1.5f, t);
            float ridge = 1f - Mathf.Abs(n * 2f - 1f);
            ridge = Mathf.Pow(ridge, 6f);
            float body = Mathf.Clamp01(1f - d / radius);
            float a = Mathf.Max(body * body * 0.8f, ridge * Mathf.Clamp01(1.2f - d));
            return a <= 0.01f ? Color.clear : Glow(Ramp(ramp, body + ridge * 0.6f), a);
        }

        static Color Bubble(float x, float y, float t, Color[] ramp)
        {
            float d = Mathf.Sqrt(x * x + y * y);
            if (d > 0.9f) return Color.clear;
            float rim = Mathf.SmoothStep(0.55f, 0.88f, d) * (1f - Mathf.SmoothStep(0.88f, 0.9f, d));
            float hx = x + 0.3f + 0.1f * Mathf.Sin(t * 6.28f), hy = y + 0.35f;
            float spot = Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy) / 0.25f);
            float a = 0.18f + rim * 0.7f + spot;
            return Glow(Ramp(ramp, 0.5f + rim * 0.3f + spot), a);
        }

        static Color Streak(float x, float y, float t, Color[] ramp)
        {
            // A head low down and its tail streaming up.
            float head = Mathf.Clamp01(1f - Mathf.Sqrt(x * x * 4f + (y - 0.55f) * (y - 0.55f) * 9f));
            float along = Mathf.Clamp01((0.7f - y) / 1.6f);
            float width = 0.15f + 0.35f * (1f - along);
            float tail = Mathf.Clamp01(1f - Mathf.Abs(x) / width) * (1f - along) * (y < 0.6f ? 1f : 0f);
            float n = Fbm(x * 2f, y * 0.5f - t * 2f, t);
            float a = Mathf.Max(head, tail * (0.5f + 0.5f * n));
            return a <= 0.01f ? Color.clear : Glow(Ramp(ramp, head + tail * 0.5f), a);
        }

        static Color Flame(float x, float y, float t, Color[] ramp)
        {
            // A tongue of fire streaming right and up from its root.
            float u = (x + 0.66f) / 1.6f, v = (y + 0.5f);
            float n = Fbm(x * 1.5f - t, y * 1.5f, t);
            float width = 0.55f * Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI) * (0.7f + 0.5f * n);
            float d = Mathf.Abs(v - 0.1f - u * 0.2f);
            if (u < 0f || u > 1f || d > width) return Color.clear;
            float k = 1f - d / Mathf.Max(0.01f, width);
            return Glow(Ramp(ramp, k * (1.1f - u * 0.6f)), Mathf.SmoothStep(0f, 0.5f, k) * (1f - u * 0.5f));
        }

        static Color Cannon(float x, float y, float t)
        {
            float d = Mathf.Sqrt(x * x + y * y);
            if (d > 1f) return Color.clear;
            float z = Mathf.Sqrt(Mathf.Max(0f, 1f - d * d));
            float a = t * 6.2831853f;
            float lit = Mathf.Max(0f, x * Mathf.Cos(a) * 0.5f - y * 0.6f + z * 0.6f);
            float g = 0.12f + 0.4f * lit;
            return new Color(g, g, g * 1.05f, Mathf.SmoothStep(1f, 0.8f, d));
        }

        static Color Blast(float x, float y, float t, Color[] ramp)
        {
            // A ball of fire rising and swelling from the ground, cooling as it goes.
            float r = 0.25f + 0.7f * Mathf.Sqrt(t);
            float cy = 0.55f - 0.6f * t;
            float dx = x, dy = (y - cy) * 1.1f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float n = Fbm(x * 1.2f, y * 1.2f + t, t);
            float edge = r * (0.75f + 0.45f * n);
            if (d >= edge) return Color.clear;
            float k = 1f - d / edge;
            float heat = (1f - t) * (0.6f + 0.6f * k);
            return Glow(Ramp(ramp, heat), Mathf.SmoothStep(0f, 0.4f, k) * (1f - t * t) * (0.7f + 0.3f * n));
        }

        static Color Column(float x, float y, float t, Color[] ramp)
        {
            float rise = Mathf.Clamp01(t * 2.5f), fade = 1f - Mathf.Clamp01((t - 0.5f) * 2f);
            float top = 0.95f - 1.9f * rise;
            if (y < top) return Color.clear;
            float n = Fbm(x * 2f, y - t * 3f, t);
            float w = 0.25f + 0.35f * (y + 1f) * 0.5f;
            float k = Mathf.Clamp01(1f - Mathf.Abs(x) / (w * (0.6f + 0.6f * n)));
            float a = k * fade * Mathf.Clamp01((y - top) * 3f);
            return a <= 0.01f ? Color.clear : Glow(Ramp(ramp, k * 0.8f + 0.3f * (1f - t)), a);
        }

        static Color Shock(float x, float y, float t, Color[] ramp)
        {
            float d = Mathf.Sqrt(x * x + y * y * 2.2f);
            float r = 0.15f + 0.8f * t;
            float th = 0.12f + 0.1f * t;
            float k = Mathf.Exp(-((d - r) * (d - r)) / (th * th));
            float core = Mathf.Clamp01(1f - d / 0.3f) * (1f - t) * (1f - t);
            float a = Mathf.Max(k * (1f - t), core);
            return a <= 0.01f ? Color.clear : Glow(Ramp(ramp, 0.4f + k * 0.5f + core), a);
        }

        static Color Sparks(float x, float y, float t, Color[] ramp)
        {
            float ang = Mathf.Atan2(y, x), d = Mathf.Sqrt(x * x + y * y);
            int frame = Mathf.FloorToInt(t * 30f);
            int bin = Mathf.FloorToInt((ang + Mathf.PI) / (2f * Mathf.PI) * 9f);
            float wob = (Hash(bin, frame) - 0.5f) * 0.5f + Mathf.Sin(d * 12f + frame) * 0.08f;
            float mid = (bin + 0.5f) / 9f * 2f * Mathf.PI - Mathf.PI + wob;
            float off = Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, mid * Mathf.Rad2Deg)) * Mathf.Deg2Rad * d;
            float ray = Mathf.Clamp01(1f - off / 0.05f) * Mathf.Clamp01(1f - d / (0.4f + 0.5f * Hash(bin, frame + 7)));
            float core = Mathf.Clamp01(1f - d / 0.25f);
            float a = Mathf.Max(ray, core) * (1f - t * 0.8f);
            return a <= 0.01f ? Color.clear : Glow(Ramp(ramp, 0.5f + ray * 0.5f + core), a);
        }

        static Color Splash(float x, float y, float t, Color[] ramp)
        {
            float d = Mathf.Sqrt(x * x + (y - 0.3f) * (y - 0.3f) * 1.5f);
            float r = 0.2f + 0.75f * t;
            float n = Fbm(x * 2f, y * 2f, t);
            float ring = Mathf.Clamp01(1f - Mathf.Abs(d - r) / (0.18f + 0.1f * n));
            float spray = Mathf.Clamp01(1f - d / r) * (n > 0.55f ? 1f : 0f) * 0.7f;
            float a = Mathf.Max(ring, spray) * (1f - t);
            return a <= 0.01f ? Color.clear : Glow(Ramp(ramp, 0.5f + ring * 0.5f), a);
        }

        static Color Dust(float x, float y, float t)
        {
            float r = 0.3f + 0.65f * Mathf.Sqrt(t);
            float cy = 0.45f - 0.35f * t;
            float d = Mathf.Sqrt(x * x + (y - cy) * (y - cy) * 1.4f);
            float n = Fbm(x, y, t);
            float edge = r * (0.7f + 0.5f * n);
            if (d >= edge) return Color.clear;
            float k = 1f - d / edge;
            float s = 0.35f + 0.25f * n;
            return new Color(s * 1.1f, s * 0.92f, s * 0.7f, Mathf.SmoothStep(0f, 0.5f, k) * (1f - t) * 0.9f);
        }

        static Color Funnel(float x, float y, float t)
        {
            float v = (y + 1f) * 0.5f;
            float w = 0.12f + 0.75f * (1f - v) * (1f - v);
            float n = Fbm(x * 3f + t * 4f, y * 2f, t);
            float k = Mathf.Clamp01(1f - Mathf.Abs(x) / w);
            float a = k * (0.35f + 0.65f * n) * Mathf.SmoothStep(0f, 0.15f, v);
            float g = 0.55f + 0.35f * n;
            return a <= 0.01f ? Color.clear : new Color(g, g * 0.97f, g * 0.9f, a * 0.8f);
        }

        static Color Nimbus(float x, float y, float t, Color[] ramp)
        {
            float d = Mathf.Sqrt(x * x + y * y * 1.6f);
            float ang = Mathf.Atan2(y, x) + t * 6.2831853f;
            float band = Mathf.Exp(-(d - 0.7f) * (d - 0.7f) / 0.02f);
            float sparks = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(ang * 5f)), 8f);
            float a = band * (0.35f + 0.65f * sparks);
            return a <= 0.01f ? Color.clear : Glow(Ramp(ramp, 0.6f + sparks * 0.4f), a);
        }
    }
}
