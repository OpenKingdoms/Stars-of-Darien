// DustPuffs.cs - soft puffs of dust, smoke and spray where scenery breaks
// and thrown pieces land: discs turned to the camera that rise, grow and
// fade. A stand-in for the effects' own particles until they land.
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class DustPuffs
    {
        struct Mote
        {
            public Vector3 P, V;
            public float Size, Grow, Age, Life;
            public Color Colour;
        }

        public const int Max = 192;
        readonly Mote[] puffs = new Mote[Max];
        int count;
        uint seed = 12345;
        readonly Matrix4x4[] matrices = new Matrix4x4[Max];
        readonly Vector4[] colours = new Vector4[Max];
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        Mesh disc;
        Material mat;
        static readonly int ColourId = Shader.PropertyToID("_Color");

        public int Count => count;
        // The way the air carries smoke, in world units a second.
        public Vector3 Wind;
        // Colour of dust from stone, and from wood and earth.
        public static readonly Color StoneDust = new Color(0.62f, 0.58f, 0.52f, 0.55f), EarthDust = new Color(0.45f, 0.37f, 0.28f, 0.5f);

        float Rand() => Fracture.Rand01(ref seed);

        public void Puff(Vector3 at, Vector3 v, float size, float grow, float life, Color colour)
        {
            if (count == Max) return;
            puffs[count++] = new Mote { P = at, V = v, Size = size, Grow = grow, Life = life, Colour = colour };
        }

        // A ring of dust where something heavy comes down.
        public void Land(Vector3 at, float radius, float speed)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(radius * 4f + speed * 0.3f), 2, 8);
            for (int i = 0; i < n; i++)
            {
                float a = (i + Rand()) * Mathf.PI * 2f / n;
                var out_ = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Puff(at + out_ * radius * 0.5f + Vector3.up * 0.1f, out_ * (0.6f + 0.6f * Rand()) + Vector3.up * 0.35f,
                    radius * 0.8f + 0.25f, 0.6f + radius * 0.4f, 1.6f + Rand(), EarthDust);
            }
        }

        // A cloud rolling out from a collapse.
        public void Burst(Vector3 at, float radius, Color colour, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float a = Rand() * Mathf.PI * 2f;
                var out_ = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Puff(at + out_ * radius * Rand() + Vector3.up * (0.2f + Rand() * radius * 0.5f),
                    out_ * (0.8f + 1.2f * Rand()) + Vector3.up * (0.3f + 0.4f * Rand()), radius * 0.5f + 0.3f, 0.5f + radius * 0.5f, 2f + 1.5f * Rand(), colour);
            }
        }

        // Smoke or flame behind a thrown piece.
        public void Trail(Vector3 at, PieceExplode how, float dt)
        {
            if (Rand() > dt / 0.05f) return;
            bool fire = (how & PieceExplode.Fire) != 0;
            var c = fire ? new Color(1f, 0.55f, 0.15f, 0.7f) : new Color(0.18f, 0.17f, 0.16f, 0.5f);
            Puff(at, Vector3.up * 0.4f, fire ? 0.25f : 0.3f, fire ? 0.3f : 0.8f, fire ? 0.35f : 1.4f, c);
        }

        public void Splash(Vector3 at, float radius) =>
            Puff(at, Vector3.up * 1.5f, radius + 0.2f, 0.8f, 0.8f, new Color(0.85f, 0.9f, 0.95f, 0.5f));

        public void Step(float dt)
        {
            if (dt <= 0f) return;
            for (int i = 0; i < count; i++)
            {
                ref var p = ref puffs[i];
                p.Age += dt;
                if (p.Age >= p.Life) { puffs[i--] = puffs[--count]; continue; }
                p.P += (p.V + Wind) * dt;
                p.V *= 1f - Mathf.Min(1f, 1.5f * dt);
                p.Size += p.Grow * dt;
            }
        }

        public void Draw(Camera cam)
        {
            if (count == 0 || cam == null) return;
            if (disc == null) Make();
            if (mat == null) return;
            var turn = cam.transform.rotation;
            for (int i = 0; i < count; i++)
            {
                var p = puffs[i];
                float t = p.Age / p.Life;
                // In fast, out slow.
                float a = Mathf.Clamp01(t * 8f) * (1f - t) * (1f - t);
                matrices[i] = Matrix4x4.TRS(p.P, turn, Vector3.one * p.Size);
                colours[i] = new Vector4(p.Colour.r, p.Colour.g, p.Colour.b, p.Colour.a * a);
            }
            block.SetVectorArray(ColourId, colours);
            var rp = new RenderParams(mat)
            {
                matProps = block, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false,
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 100000f),
            };
            Graphics.RenderMeshInstanced(rp, disc, 0, matrices, count);
        }

        // A disc solid in the middle and clear at the rim.
        void Make()
        {
            const int N = 16;
            var v = new Vector3[N + 1];
            var c = new Color32[N + 1];
            var t = new int[N * 3];
            c[0] = new Color32(255, 255, 255, 255);
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                v[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.5f;
                c[i + 1] = new Color32(255, 255, 255, 0);
                t[i * 3] = 0; t[i * 3 + 1] = 1 + (i + 1) % N; t[i * 3 + 2] = 1 + i;
            }
            disc = new Mesh { name = "dust puff", hideFlags = HideFlags.DontSave, vertices = v, colors32 = c, triangles = t };
            disc.RecalculateBounds();
            mat = Looks.Overlay(Color.white);
        }

        public void Clear() => count = 0;

        public void Dispose()
        {
            Looks.Release(disc);
            Looks.Release(mat);
            disc = null;
            mat = null;
            count = 0;
        }
    }
}
