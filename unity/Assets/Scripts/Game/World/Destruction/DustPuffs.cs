// DustPuffs.cs - dust, smoke and spray where scenery breaks and thrown
// pieces land, made in the battle's shared particles (FxParticles), so they
// are lit, sorted, capped and blown by the wind with the rest. Without a
// battle's particles, as in a test of the debris alone, it makes nothing.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class DustPuffs
    {
        uint seed = 12345;

        // Puffs asked of the particles since the start.
        public int Count { get; private set; }
        // The wind now comes from the particles' own; kept for callers that set it.
        public Vector3 Wind;
        // Colour of dust from stone, and from wood and earth.
        public static readonly Color StoneDust = new Color(0.62f, 0.58f, 0.52f, 0.55f), EarthDust = new Color(0.45f, 0.37f, 0.28f, 0.5f);

        float Rand() => Fracture.Rand01(ref seed);

        static Color32 Of(Color c) => new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), (byte)(Mathf.Clamp01(c.a + 0.05f) * 255f));

        public void Puff(Vector3 at, Vector3 v, float size, float grow, float life, Color colour)
        {
            var P = FxParticles.Active;
            if (P == null) return;
            P.Smoke(at, v, size, life, Of(colour), 0f, 0.3f);
            Count++;
        }

        // A ring of dust where something heavy comes down.
        public void Land(Vector3 at, float radius, float speed)
        {
            var P = FxParticles.Active;
            if (P == null || !P.OnScreen(at, radius + 2f)) return;
            int n = Mathf.Clamp(Mathf.RoundToInt(radius * 6f + speed * 0.45f), 3, 12);
            P.Dust(at, Mathf.Max(0.35f, radius * 1.2f), n, Of(EarthDust), 1.8f);
            Count += n;
        }

        // A cloud rolling out from a collapse, billowing up where it is thick.
        public void Burst(Vector3 at, float radius, Color colour, int n)
        {
            var P = FxParticles.Active;
            if (P == null || !P.OnScreen(at, radius * 2f + 3f)) return;
            var c = Of(colour);
            P.Dust(at, Mathf.Max(0.4f, radius * 1.2f), Mathf.RoundToInt(n * 1.5f), c, 2.4f);
            for (int i = 0; i < n / 3; i++)
            {
                float a = Rand() * Mathf.PI * 2f;
                var out_ = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                P.Smoke(at + out_ * radius * Rand() + Vector3.up * (0.2f + Rand() * radius * 0.4f), out_ * (0.6f + Rand()) + Vector3.up * (0.4f + 0.5f * Rand()),
                    radius * 0.6f + 0.3f, 2.6f + Rand(), c, 0f, 0.35f);
            }
            Count += n + n / 3;
        }

        // Smoke or flame behind a thrown piece.
        public void Trail(Vector3 at, PieceExplode how, float dt)
        {
            if (Rand() > dt / 0.05f) return;
            var P = FxParticles.Active;
            if (P == null) return;
            if ((how & PieceExplode.Fire) != 0) P.Smoke(at, Vector3.up * 0.5f, 0.28f, 0.45f, new Color32(80, 66, 56, 210), 0.95f, 0.8f);
            else P.Smoke(at, Vector3.up * 0.4f, 0.3f, 1.4f, new Color32(46, 43, 41, 130), 0f, 0.5f);
            Count++;
        }

        public void Splash(Vector3 at, float radius)
        {
            var P = FxParticles.Active;
            if (P == null || !P.OnScreen(at, radius + 2f)) return;
            P.Spray(at, 10, 0.8f + radius, new Color32(214, 230, 242, 200), 0.5f + radius);
            P.Smoke(at, Vector3.up * 1.2f, radius + 0.3f, 1f, new Color32(220, 230, 240, 120), 0f, 0.5f);
            Count += 11;
        }

        // The particles step and draw themselves.
        public void Step(float dt) { }
        public void Draw(Camera cam) { }
        public void Clear() { }
        public void Dispose() { }
    }
}
