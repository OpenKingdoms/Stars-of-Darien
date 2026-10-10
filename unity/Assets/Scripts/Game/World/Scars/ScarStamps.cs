// ScarStamps.cs - what each kind of blast leaves on the ground, and the
// crater's shape. OkuScarStamp.shader draws the same shape into the scar
// map, so the dip the ground shows and the one units sit in agree.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public enum ScarKind : byte
    {
        None,
        Gunpowder,  // a crater with a thrown rim, scorch and earth flung out in rays
        Siege,      // a narrow pit with scattered stone
        Impact,     // a big spell's meteor or fireball: the deepest crater, charred
        Fire,       // char that glows and then darkens
        Breath,     // a scorched swath back along the breath's line
        Lightning,  // a scorched fork round a charred star
        Frost,      // rime that melts from its edges, and cracks
        Dark,       // blight that withers grass grey
        Water,      // wet ground that dries from its edges
        Holy,       // a pale ring of light that fades
        Earth,      // cracks and churned soil along fissures
        Dust,       // a scuff of churned soil
    }

    // One mark on the ground. Strengths run 0 to 1, lasting marks are in
    // seconds, and a swath runs Length back from its centre against Dir.
    public struct ScarStamp
    {
        public ScarKind Kind;
        public float X, Z;
        public float DirX, DirZ;
        public float Reach;         // world units: the farthest any mark goes, a swath's half width
        public float Length;
        public float Dent;          // world units: the crater's radius, 0 for none
        public float Depth, Rim;    // pixels, at the centre and along the rim
        public float Floor;         // the share of the dent that is flat floor
        // The dent's outline: drawn out along Dir by Stretch, narrower across,
        // and pushed in and out round its length by up to Lobes of its radius.
        public float Stretch, Lobes;
        public float Char, Soil, Blight, Stone, Crack;
        public float Frost, Wet, Holy, Heat;
        public int Seed;

        // The farthest the stamp reaches from its centre, swath and all.
        public float Extent => Reach + Length;

        // The farthest the dip and its rim reach from the centre.
        public float DentReach => Dent * 1.5f * (1f + Stretch) * (1f + Lobes);

        // Stretch and Lobes in one number, as the stamp shader reads them.
        public float Outline => Stretch + Mathf.Round(Lobes * 100f);
    }

    public static class ScarStamps
    {
        public const float PixelsPerUnit = 16f;
        // The scar map's range for a dip and a rim, in pixels.
        public const float MaxDepthPx = 12f, MaxRimPx = 4f;
        // The owner's limits: a cannon's crater and the largest spells'.
        public const float CannonDepthPx = 6f, SpellDepthPx = 10f;
        // How long a mark of each lasting kind stays at its strongest point.
        public const float FrostSeconds = 60f, WetSeconds = 60f, HolySeconds = 25f, FireHeatSeconds = 20f;

        // ── Kinds ─────────────────────────────────────────────────────

        // The kind of mark a blast leaves, by the kinds the explosions sort
        // weapons into. A blast on water or one that struck a unit directly
        // leaves none. caster is the firing unit's internal name, if known.
        public static ScarKind KindOf(in BlastEvent b, string caster = null)
        {
            if ((b.Flags & (BlastFlags.Water | BlastFlags.DirectHit)) != 0) return ScarKind.None;
            return KindOf(FxKinds.Of(b, caster), b.Weapon, b.Radius);
        }

        // Fire digs a crater when it is a meteor, a volcanic blast or a spell
        // of 3.5 units or more. A burning arrow chars a spot, and a dust puff
        // or a whirlwind scuffs the soil.
        public static ScarKind KindOf(BlastKind kind, WeaponInfo w, float radius)
        {
            switch (kind)
            {
                case BlastKind.Gunpowder: return ScarKind.Gunpowder;
                case BlastKind.Siege: return ScarKind.Siege;
                case BlastKind.Arrow:
                    if (w != null && (w.Flags & WeaponFlags.FireStarter) != 0) return ScarKind.Fire;
                    return radius > 0.4f ? ScarKind.Dust : ScarKind.None;
                case BlastKind.Fire:
                    bool spell = w != null && (w.Flags & WeaponFlags.Spell) != 0;
                    return Has(Lower(w?.ExplosionClass), "volc") || Has(Lower(w?.Name), "meteor") || spell && radius >= 3.5f ? ScarKind.Impact : ScarKind.Fire;
                case BlastKind.Breath: return ScarKind.Breath;
                case BlastKind.Lightning: return ScarKind.Lightning;
                case BlastKind.Water: return ScarKind.Water;
                case BlastKind.Frost: return ScarKind.Frost;
                case BlastKind.Earth: return ScarKind.Earth;
                case BlastKind.Wind: return radius > 0.4f ? ScarKind.Dust : ScarKind.None;
                case BlastKind.Dark: return ScarKind.Dark;
                case BlastKind.Holy: return ScarKind.Holy;
                default: return ScarKind.None;
            }
        }

        static string Lower(string s) => s ?? "";
        static bool Has(string s, string part) => s.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;

        // ── Stamps ────────────────────────────────────────────────────

        // The stamp a blast leaves, or false for none. A blast well above the
        // ground leaves less, and one high in the air nothing.
        public static bool Make(in BlastEvent b, float ground, out ScarStamp s, string caster = null)
        {
            s = default;
            var kind = KindOf(b, caster);
            if (kind == ScarKind.None) return false;
            float above = b.Position.y - ground;
            float strength = Mathf.Clamp01(1f - (above - 0.4f) / (b.Radius + 0.6f));
            if (strength < 0.05f) return false;
            var dir = new Vector3(b.Direction.x, 0f, b.Direction.z);
            s = Make(kind, b.Position, b.Radius, dir, strength, b.Id);
            if (kind == ScarKind.Water && b.Weapon != null && Has(Lower(b.Weapon.ExplosionClass), "tsunami") && dir.sqrMagnitude > 1e-4f) s.Length = 6f * strength;
            return true;
        }

        // A stamp of a kind at a world point, for a blast of the given
        // radius (world units, half its areaofeffect) facing dir.
        public static ScarStamp Make(ScarKind kind, Vector3 at, float radius, Vector3 dir, float strength = 1f, int seed = 0)
        {
            float r = Mathf.Max(0f, radius);
            var flat = new Vector2(dir.x, dir.z);
            if (flat.sqrMagnitude < 1e-6f)
            {
                float a = Hash01(seed) * Mathf.PI * 2f;
                flat = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }
            flat.Normalize();
            bool aimed = flat.sqrMagnitude >= 1e-6f;
            var s = new ScarStamp { Kind = kind, X = at.x, Z = at.z, DirX = flat.x, DirZ = flat.y, Seed = ((seed % 1024) + 1024) % 1024 };
            // No two craters alike: each blast's own size, depth, floor, rim
            // and outline, drawn out along the shot's flight when it had one.
            float J(int n) => Hash01(seed * 8 + n);
            switch (kind)
            {
                case ScarKind.Gunpowder:
                    if (r < 0.4f)
                    {
                        // A musket ball or a teeny explosion scorches without digging.
                        s.Reach = 0.45f; s.Char = 0.55f; s.Soil = 0.35f; s.Heat = 2f;
                        break;
                    }
                    s.Dent = Mathf.Min(0.8f * r, 4f) * Mathf.Lerp(0.85f, 1.15f, J(1));
                    s.Depth = Mathf.Min(2f + 1.6f * r, CannonDepthPx) * Mathf.Lerp(0.6f, 1f, J(2));
                    s.Rim = Mathf.Lerp(0.28f, 0.55f, J(3)) * s.Depth;
                    s.Floor = Mathf.Lerp(0.1f, 0.42f, J(4));
                    s.Char = Mathf.Lerp(0.6f, 0.9f, J(5)); s.Soil = Mathf.Lerp(0.65f, 0.95f, J(6)); s.Heat = 4f;
                    s.Stretch = aimed ? Mathf.Lerp(0.12f, 0.4f, J(7)) : Mathf.Lerp(0f, 0.18f, J(7));
                    s.Lobes = Mathf.Lerp(0.05f, 0.16f, J(8));
                    s.Reach = 2.4f * s.Dent;
                    break;
                case ScarKind.Siege:
                    s.Dent = Mathf.Clamp(0.45f * r, 0.45f, 2.2f) * Mathf.Lerp(0.85f, 1.2f, J(1));
                    s.Depth = Mathf.Min(2f + 1.2f * r, 5f) * Mathf.Lerp(0.7f, 1f, J(2));
                    s.Rim = Mathf.Lerp(0.15f, 0.35f, J(3)) * s.Depth;
                    s.Floor = Mathf.Lerp(0.04f, 0.2f, J(4));
                    s.Soil = Mathf.Lerp(0.6f, 0.85f, J(6)); s.Stone = 0.85f;
                    s.Stretch = aimed ? Mathf.Lerp(0.15f, 0.45f, J(7)) : Mathf.Lerp(0f, 0.2f, J(7));
                    s.Lobes = Mathf.Lerp(0.08f, 0.2f, J(8));
                    s.Reach = 2.8f * s.Dent;
                    break;
                case ScarKind.Impact:
                    s.Dent = Mathf.Clamp(0.55f * r, 0.8f, 5f) * Mathf.Lerp(0.9f, 1.1f, J(1));
                    s.Depth = Mathf.Min(3f + 1.4f * r, SpellDepthPx) * Mathf.Lerp(0.75f, 1f, J(2));
                    s.Rim = Mathf.Lerp(0.25f, 0.45f, J(3)) * s.Depth;
                    s.Floor = Mathf.Lerp(0.2f, 0.4f, J(4));
                    s.Char = 1f; s.Soil = Mathf.Lerp(0.7f, 0.9f, J(6)); s.Heat = 25f;
                    s.Stretch = Mathf.Lerp(0f, 0.15f, J(7));
                    s.Lobes = Mathf.Lerp(0.04f, 0.1f, J(8));
                    s.Reach = 2.2f * s.Dent;
                    break;
                case ScarKind.Fire:
                    s.Reach = Mathf.Max(r, 0.5f) * 1.1f; s.Char = 0.9f; s.Heat = FireHeatSeconds;
                    break;
                case ScarKind.Breath:
                    s.Reach = Mathf.Max(r, 0.6f) * 0.9f; s.Length = 3.5f; s.Char = 0.85f; s.Heat = 15f;
                    break;
                case ScarKind.Lightning:
                    s.Reach = Mathf.Max(r * 1.4f, 3f); s.Char = 0.95f; s.Heat = 3f;
                    break;
                case ScarKind.Frost:
                    s.Reach = Mathf.Max(r, 1f) * 1.1f; s.Frost = FrostSeconds; s.Crack = 0.6f;
                    break;
                case ScarKind.Dark:
                    s.Reach = Mathf.Max(r, 2f) * 1.5f; s.Blight = 0.9f;
                    break;
                case ScarKind.Water:
                    s.Reach = Mathf.Max(r, 1f) * 1.3f; s.Wet = WetSeconds;
                    break;
                case ScarKind.Holy:
                    s.Reach = Mathf.Max(r, 1.5f); s.Holy = HolySeconds;
                    break;
                case ScarKind.Earth:
                    s.Reach = Mathf.Clamp(r * 0.5f, 3f, 8f); s.Crack = 0.9f; s.Soil = 0.5f;
                    break;
                case ScarKind.Dust:
                    s.Reach = Mathf.Max(r, 0.4f); s.Soil = 0.35f;
                    break;
                default:
                    return s;
            }
            float k = Mathf.Clamp01(strength);
            s.Depth *= k; s.Rim *= k;
            s.Char *= k; s.Soil *= k; s.Blight *= k; s.Stone *= k; s.Crack *= k;
            s.Frost *= k; s.Wet *= k; s.Holy *= k; s.Heat *= k;
            if (s.Depth < 0.25f) s.Dent = s.Depth = s.Rim = 0f;
            // As the shader unpacks them, so both draw the same outline.
            s.Stretch = Mathf.Round(Mathf.Clamp(s.Stretch, 0f, 0.45f) * 1000f) / 1000f;
            s.Lobes = Mathf.Round(Mathf.Clamp(s.Lobes, 0f, 0.25f) * 100f) / 100f;
            return s;
        }

        // Folds b into a, kept where a is: each mark the stronger of the two.
        // A queue past its cap trades b's place for keeping its strength.
        public static void Merge(ref ScarStamp a, in ScarStamp b)
        {
            a.Reach = Mathf.Max(a.Reach, b.Reach);
            a.Length = Mathf.Max(a.Length, b.Length);
            if (b.Dent > 0f && b.Depth > a.Depth) { a.Dent = Mathf.Max(a.Dent, b.Dent); a.Depth = b.Depth; a.Rim = b.Rim; a.Floor = b.Floor; }
            a.Char = Mathf.Max(a.Char, b.Char); a.Soil = Mathf.Max(a.Soil, b.Soil); a.Blight = Mathf.Max(a.Blight, b.Blight);
            a.Stone = Mathf.Max(a.Stone, b.Stone); a.Crack = Mathf.Max(a.Crack, b.Crack);
            a.Frost = Mathf.Max(a.Frost, b.Frost); a.Wet = Mathf.Max(a.Wet, b.Wet); a.Holy = Mathf.Max(a.Holy, b.Holy); a.Heat = Mathf.Max(a.Heat, b.Heat);
        }

        // ── The crater's shape ────────────────────────────────────────

        // How deep a crater is at x, its distance from the centre over its
        // radius, as a share of its depth: a flat floor, then a smooth wall.
        public static float DepthAt(float x, float floor) => 1f - SmoothStep(floor, 1f, x);

        // The dent's radius toward a direction, as a share of Dent: c and s
        // are the cosine and sine of its angle from the stamp's right, so s
        // runs along the stamp's direction. An ellipse drawn out along it,
        // pushed in and out by two and five lobes.
        public static float Outline(float c, float s, float stretch, float lobes, Vector4 phases)
        {
            float ax = c / (1f - 0.4f * stretch), ay = s / (1f + stretch);
            float ell = 1f / Mathf.Sqrt(ax * ax + ay * ay);
            float cos2 = c * c - s * s, sin2 = 2f * s * c;
            float c2 = c * c, s2 = s * s;
            float cos5 = c * (c2 * c2 - 10f * c2 * s2 + 5f * s2 * s2), sin5 = s * (5f * c2 * c2 - 10f * c2 * s2 + s2 * s2);
            float lobe = 0.55f * (cos2 * phases.x - sin2 * phases.y) + 0.45f * (sin5 * phases.z + cos5 * phases.w);
            return ell * (1f + lobes * lobe);
        }

        // How high its thrown rim stands at x and angle a, as a share of the
        // rim's height: a ring just outside the dip, lumpy round its length.
        public static float RimAt(float x, float a, int seed) => RimAt(x, Mathf.Cos(a), Mathf.Sin(a), Phases(seed));

        // The same from the angle's cosine and sine and the seed's phases,
        // with no trigonometry a texel.
        public static float RimAt(float x, float c, float s, Vector4 phases)
        {
            float t = (x - 1.05f) / 0.45f;
            if (t <= -1f || t >= 1f) return 0f;
            float bump = (1f - t * t) * (1f - t * t);
            return bump * Lumps(c, s, phases);
        }

        // 0.75 + 0.25 sin(3a + p) cos(2a + q), p and q from the seed.
        public static float Lumps(float c, float s, Vector4 phases)
        {
            float sin3 = s * (3f - 4f * s * s), cos3 = c * (4f * c * c - 3f);
            float cos2 = c * c - s * s, sin2 = 2f * s * c;
            return 0.75f + 0.25f * (sin3 * phases.x + cos3 * phases.y) * (cos2 * phases.z - sin2 * phases.w);
        }

        // cos p, sin p, cos q, sin q for a seed's lumps.
        public static Vector4 Phases(int seed)
        {
            float p = Frac(seed * 0.1234f) * 6.2831853f, q = Frac(seed * 0.5678f) * 6.2831853f;
            return new Vector4(Mathf.Cos(p), Mathf.Sin(p), Mathf.Cos(q), Mathf.Sin(q));
        }

        // The drawn ground's change in pixels from what the scar map keeps:
        // the deepest dip, and the highest rim where the ground is not dug.
        public static float Height(float depthPx, float rimPx) => rimPx * (1f - Mathf.Clamp01(depthPx * 0.5f)) - depthPx;

        static float SmoothStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static float Frac(float v) => v - Mathf.Floor(v);

        static float Hash01(int n)
        {
            uint h = (uint)n * 2654435761u;
            h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12;
            return (h & 0xffffff) / 16777216f;
        }
    }
}
