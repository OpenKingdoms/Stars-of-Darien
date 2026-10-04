// FxShock.cs - rings that run out along the ground from a blast: a thin
// bright shock front that is gone in a blink, or a slow ring of holy light
// that lingers. Each is draped over the drawn ground, its heights read once
// on a few circles when it starts and eased between them as it grows.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FxShock
    {
        public const int Max = 64;
        const int Segments = 24, Circles = 3, Spokes = 12;

        sealed class Ring
        {
            public Vector3 At;
            public float Reach, Width, Age, Life, Strength;
            public Color32 Colour;
            // The ground on Circles circles out to the full reach, at Spokes ways round.
            public readonly float[] Heights = new float[Spokes * Circles];
        }

        readonly Ring[] rings = new Ring[Max];
        int count;
        public int Count => count;

        static readonly Vector2[] Around = MakeAround();

        static Vector2[] MakeAround()
        {
            var a = new Vector2[Segments];
            for (int i = 0; i < Segments; i++) a[i] = new Vector2(Mathf.Cos(i * Mathf.PI * 2f / Segments), Mathf.Sin(i * Mathf.PI * 2f / Segments));
            return a;
        }

        // A ring reaching out to reach over seconds, width across, its
        // strength past 1 glowing into the bloom.
        public void Add(Vector3 at, float reach, float width, float seconds, Color32 colour, float strength, System.Func<float, float, float> ground)
        {
            if (reach <= 0.1f || seconds <= 0f) return;
            // Fewer at once on the lower settings.
            int most = Mathf.Clamp(FxQuality.Current.Lights, 4, Max);
            Ring r;
            if (count < most) r = rings[count] ??= new Ring();
            else
            {
                // The oldest gives way.
                int oldest = 0;
                for (int i = 1; i < count; i++) if (rings[i].Age / rings[i].Life > rings[oldest].Age / rings[oldest].Life) oldest = i;
                r = rings[oldest];
                rings[oldest] = rings[count - 1];
                rings[count - 1] = r;
                count--;
            }
            r.At = at; r.Reach = reach; r.Width = width; r.Age = 0f; r.Life = seconds; r.Colour = colour; r.Strength = strength;
            for (int c = 0; c < Circles; c++)
            {
                float d = reach * (c + 1) / Circles;
                for (int k = 0; k < Spokes; k++)
                {
                    var way = Around[k * Segments / Spokes];
                    r.Heights[c * Spokes + k] = ground(at.x + way.x * d, at.z + way.y * d);
                }
            }
            count++;
        }

        public void Step(float dt)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                var r = rings[i];
                r.Age += dt;
                if (r.Age < r.Life) continue;
                rings[i] = rings[count - 1];
                rings[count - 1] = r;
                count--;
            }
        }

        // Each ring as a soft band: dark at both edges, bright in the middle.
        public void Draw(FxMesh into)
        {
            for (int i = 0; i < count; i++)
            {
                var r = rings[i];
                float t = Mathf.Clamp01(r.Age / r.Life);
                // Out fast and slowing, as a front does.
                float reach = r.Reach * (1f - (1f - t) * (1f - t) * (1f - t) * 0.92f - 0.08f * (1f - t));
                float fade = Mathf.Clamp01((1f - t) / 0.6f) * Mathf.Clamp01(r.Age / 0.03f);
                float w = r.Width * (0.6f + 0.4f * t);
                var mid = r.Colour;
                mid.a = (byte)Mathf.RoundToInt(Mathf.Clamp01(fade * Mathf.Min(1f, r.Strength)) * mid.a);
                var edge = mid;
                edge.a = 0;
                var look = new Vector4(1f, Mathf.Max(1f, r.Strength), 0.3f, 0f);
                int first = into.Vertices;
                for (int s = 0; s < Segments; s++)
                {
                    var dir = Around[s];
                    for (int band = 0; band < 3; band++)
                    {
                        float d = Mathf.Max(0.05f, reach + (band - 1) * w * 0.5f);
                        var p = new Vector3(r.At.x + dir.x * d, Height(r, s, d) + 0.12f, r.At.z + dir.y * d);
                        into.Vertex(p, band == 1 ? mid : edge, new Vector2(0.5f, 0.5f), look);
                    }
                }
                for (int s = 0; s < Segments; s++)
                {
                    int a = first + s * 3, b = first + ((s + 1) % Segments) * 3;
                    for (int band = 0; band < 2; band++)
                    {
                        into.Triangle(a + band, b + band, a + band + 1);
                        into.Triangle(b + band, b + band + 1, a + band + 1);
                    }
                }
            }
        }

        // The ground under segment s at distance d, eased between the spokes
        // and circles read.
        static float Height(Ring r, int s, float d)
        {
            float k = s * Spokes / (float)Segments;
            int k0 = (int)k % Spokes, k1 = (k0 + 1) % Spokes;
            float f = k - (int)k;
            float c = d / r.Reach * Circles - 1f;
            if (c <= 0f) return Mathf.Lerp(r.At.y, Mathf.Lerp(r.Heights[k0], r.Heights[k1], f), Mathf.Clamp01(c + 1f));
            int lo = Mathf.Min(Circles - 1, (int)c), hi = Mathf.Min(Circles - 1, lo + 1);
            float a = Mathf.Lerp(r.Heights[lo * Spokes + k0], r.Heights[lo * Spokes + k1], f);
            float b = Mathf.Lerp(r.Heights[hi * Spokes + k0], r.Heights[hi * Spokes + k1], f);
            return Mathf.Lerp(a, b, c - lo);
        }

        public void Clear() => count = 0;
    }
}
