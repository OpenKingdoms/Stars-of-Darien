// FxParticles.cs - the battle's shared particles: smoke and dust that rise
// and drift with the wind, fire that cools into smoke, sparks, embers,
// spray, and debris thrown with simple physics that bounces, settles and
// sinks. Everything is pooled and capped by the Battle effects setting.
// The particles draw in one call, far to near, with added light and alpha
// in one premultiplied blend; the debris draws instanced and lit.
//
// ── For the other streams ─────────────────────────────────────────────
// FxParticles.Active is the pool the battle draws, null outside a battle.
// Every call takes world positions and units, returns at once, and costs
// nothing off screen or where the fog hides the point. The pool scales
// each count by the Battle effects setting, and when it is full the oldest
// give way. Colours are as the eye sees them in daylight; the sun and the
// sky light smoke, and heat makes it glow.
//
//   Smoke(at, velocity, size, seconds, colour, heat)  one soft puff that grows, rises and drifts downwind
//   Column(at, seconds, size, colour, heat)           a column of smoke puffing from one point for a while
//   Dust(at, radius, count, colour)                   a low cloud rolling out along the ground
//   Sparks(at, direction, count, speed, colour)       hot streaks that fly, fall and die within a second
//   Embers(at, count, spread, seconds, colour)        glowing motes that float up and away downwind
//   Spray(at, count, height, colour)                  water or ice thrown up that falls back
//   Flakes(at, count, spread, colour)                 leaves, ash or chips that flutter down
//   Glow(at, size, colour, seconds, strength)         a soft flash on the screen
//   Streak(from, to, width, colour, seconds, strength) a line of light: a bolt's branch, a pillar
//   Debris(at, direction, count, speed, size, look, seconds)  3D chunks that tumble, bounce, settle and sink
//   OnScreen(at, margin)                              whether a point is worth emitting at
//
// Ground is the drawn ground: the engine's height plus ScarMap's dents.
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Game.World
{
    // How a particle is drawn. Puffs are smoke and dust, lit by the sun and
    // glowing with heat. Glows and streaks add light. Flakes and drops are
    // small lit bits.
    public enum ParticleLook : byte { Puff = 0, Glow = 4, Streak = 5, Flake = 6, Drop = 7 }

    // What a chunk of debris is made of.
    public enum DebrisLook : byte { Dirt, Stone, Wood, Charred, Ice, Scrap }

    public sealed class FxParticles
    {
        public static FxParticles Active { get; private set; }

        // World units a second the wind carries smoke along the ground.
        public Vector3 Wind;

        // The particle as the shader reads it, 48 bytes.
        [StructLayout(LayoutKind.Sequential)]
        public struct Gpu
        {
            public Vector3 Pos;
            public float Size;
            public Vector3 Axis;    // a streak's length and way in world units, zero for round
            public float Angle;
            public uint Colour;     // rgba, 8 bits each
            public float Heat;      // 0 cold to 1 white hot
            public float Alpha;
            public uint Look;       // ParticleLook, plus TintHeat
        }

        // A puff whose heat glows in its own colour rather than as fire.
        public const uint TintHeat = 8;

        struct Particle
        {
            public Vector3 Pos, Vel, Axis;
            public float Size, Grow, Age, Life, Angle, Spin, Drag, Rise, Gravity, Heat, Cool, Alpha, Carry, GroundY;
            public Color32 Colour;
            public byte Look, Flags;
        }

        const byte Hug = 1, Bounce = 2, DiesOnGround = 4, Tinted = 8, Swirl = 16, Rooted = 32;

        struct Chunk
        {
            public Vector3 Pos, Vel, Spin, Scale;
            public Quaternion Rot;
            public float Age, Life, Rest;
            public byte Look, Shape, State;     // State: 0 flying, 1 at rest, 2 sinking
            public Matrix4x4 Matrix;
        }

        struct Plume
        {
            public Vector3 At;
            public float Left, Seconds, Size, Debt, Heat;
            public Color32 Colour;
        }

        readonly System.Func<float, float, float> height;
        Particle[] ps = new Particle[0];
        Chunk[] cs = new Chunk[0];
        readonly List<Plume> columns = new List<Plume>();
        int count, chunks, replace, replaceChunk;
        Gpu[] gpu = new Gpu[0];
        float[] keys = new float[0];
        ushort[] near = new ushort[0];
        int[] order = new int[0], scratch = new int[0];
        readonly int[] bins = new int[256];
        GraphicsBuffer buffer;
        MaterialPropertyBlock props;
        Material material;
        // Debris matrices by look and shape, drawn a group at a time.
        readonly Matrix4x4[][] chunkGroups = new Matrix4x4[6 * 8][];
        readonly int[] chunkCounts = new int[6 * 8];
        Mesh[] shapes;
        Material[] chunkMats;
        readonly Plane[] frustum = new Plane[6];
        bool haveFrustum;
        Vector3 eye;
        uint rng = 0x9E3779B9u;
        int frame;

        // Particles alive, chunks of debris in flight or at rest, and those
        // drawn in the last frame.
        public int Count => count;
        public int ChunkCount => chunks;
        public int Drawn { get; private set; }
        public int ColumnCount => columns.Count;
        // The main thread's time in the last frame for stepping the
        // particles, the debris, and sorting and sending the particles.
        public float StepMs { get; private set; }
        public float ChunkMs { get; private set; }
        public float SendMs { get; private set; }
        // Particles and chunks made since the pool began.
        public int Made { get; private set; }

        public FxParticles(System.Func<float, float, float> groundHeight)
        {
            height = groundHeight;
            Fit(FxQuality.Current);
        }

        // Makes this the pool the other streams emit into.
        public void MakeActive() => Active = this;

        // The drawn ground at a point.
        public float Ground(float x, float z) => height(x, z) + ScarMap.GroundOffset(x, z);

        void Fit(FxQuality q)
        {
            int pn = Mathf.Max(64, q.Particles), cn = Mathf.Max(16, q.Debris);
            if (ps.Length != pn)
            {
                var keep = ps;
                ps = new Particle[pn];
                count = Mathf.Min(count, pn);
                System.Array.Copy(keep, ps, Mathf.Min(keep.Length, count));
                gpu = new Gpu[pn];
                keys = new float[pn];
                near = new ushort[pn];
                order = new int[pn];
                scratch = new int[pn];
                buffer?.Release();
                buffer = null;
                replace = 0;
            }
            if (cs.Length != cn)
            {
                var keep = cs;
                cs = new Chunk[cn];
                chunks = Mathf.Min(chunks, cn);
                System.Array.Copy(keep, cs, Mathf.Min(keep.Length, chunks));
                replaceChunk = 0;
            }
        }

        // ── Random ────────────────────────────────────────────────────

        // Seeds the pool's numbers, so a blast throws the same way each time.
        public void Seed(int seed) => rng = (uint)seed * 2654435761u ^ 0x9E3779B9u | 1u;

        float R01()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 16777216f;
        }

        float R(float a, float b) => a + (b - a) * R01();

        Vector3 InCircle(float r)
        {
            float a = R01() * Mathf.PI * 2f, d = Mathf.Sqrt(R01()) * r;
            return new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
        }

        Vector3 OnSphere()
        {
            float z = R(-1f, 1f), a = R01() * Mathf.PI * 2f, r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
        }

        // A count scaled by the setting, rounded by chance so small counts still show.
        int Scaled(float n)
        {
            n *= FxQuality.Current.Emission;
            int whole = (int)n;
            return whole + (R01() < n - whole ? 1 : 0);
        }

        // ── Seeing ────────────────────────────────────────────────────

        public bool OnScreen(Vector3 at, float margin)
        {
            if (!haveFrustum) return true;
            var b = new Bounds(at, Vector3.one * (margin * 2f));
            return GeometryUtility.TestPlanesAABB(frustum, b);
        }

        // 1 near the eye, falling to a quarter far off.
        public float Nearness(Vector3 at)
        {
            if (!haveFrustum) return 1f;
            float d = Vector3.Distance(at, eye);
            return Mathf.Clamp(1f - (d - 60f) / 160f, 0.25f, 1f);
        }

        // ── Emitting ──────────────────────────────────────────────────

        int Take()
        {
            Made++;
            if (count < ps.Length) return count++;
            // Full: the next in turn gives way, which is near enough the oldest.
            replace = (replace + 1) % ps.Length;
            return replace;
        }

        void Add(in Particle p)
        {
            int i = Take();
            ps[i] = p;
        }

        public void Smoke(Vector3 at, Vector3 velocity, float size, float seconds, Color32 colour, float heat = 0f, float rise = 0.6f, bool tinted = false)
        {
            // A puff no bigger than a few men, so a big blast reads as many puffs, not one cloud.
            size = Mathf.Min(size, MaxPuff);
            Add(new Particle
            {
                Pos = at, Vel = velocity, Size = size * R(0.8f, 1.2f), Grow = size * R(0.35f, 0.6f), Life = seconds * R(0.8f, 1.2f) * FxQuality.Current.SmokeLife,
                Angle = R(0f, 360f), Spin = R(-25f, 25f), Drag = 1.2f, Rise = rise, Heat = heat, Cool = heat > 0f ? R(1.1f, 1.8f) : 0f,
                Alpha = colour.a / 255f, Carry = 1f, Colour = colour, Look = (byte)(R01() * 4f), GroundY = Ground(at.x, at.z),
                Flags = tinted ? Tinted : (byte)0,
            });
        }

        // A column puffing for seconds, each puff living about half as long again.
        // The widest a puff of smoke starts, world units. A man stands about two.
        public const float MaxPuff = 2.4f;

        public void Column(Vector3 at, float seconds, float size, Color32 colour, float heat = 0f)
        {
            if (seconds <= 0f) return;
            size = Mathf.Min(size, MaxPuff);
            // As many columns as lights at a time, the oldest giving way.
            if (columns.Count >= Mathf.Max(6, FxQuality.Current.Lights)) columns.RemoveAt(0);
            columns.Add(new Plume { At = at, Left = seconds * FxQuality.Current.SmokeLife, Seconds = seconds, Size = size, Colour = colour, Heat = heat });
        }

        public void Dust(Vector3 at, float radius, int count, Color32 colour, float seconds = 2.2f)
        {
            int n = Scaled(count);
            float g = Ground(at.x, at.z);
            for (int i = 0; i < n; i++)
            {
                var dir = InCircle(1f);
                if (dir.sqrMagnitude < 0.01f) dir = new Vector3(1f, 0f, 0f) * 0.1f;
                float speed = radius * R(1.2f, 2.4f);
                float size = Mathf.Max(0.5f, radius * R(0.3f, 0.5f));
                Add(new Particle
                {
                    Pos = new Vector3(at.x + dir.x * radius * 0.2f, g + size * 0.25f, at.z + dir.z * radius * 0.2f),
                    Vel = dir.normalized * speed + Vector3.up * R(0.1f, 0.6f), Size = size, Grow = size * R(0.5f, 0.9f),
                    Life = seconds * R(0.7f, 1.3f) * FxQuality.Current.SmokeLife, Angle = R(0f, 360f), Spin = R(-15f, 15f), Drag = 1.6f, Rise = 0.15f,
                    Alpha = colour.a / 255f, Carry = 0.6f, Colour = colour, Look = (byte)(R01() * 4f), GroundY = g, Flags = Hug,
                });
            }
        }

        public void Sparks(Vector3 at, Vector3 direction, int count, float speed, Color32 colour, float seconds = 0.6f)
        {
            int n = Scaled(count);
            float g = Ground(at.x, at.z);
            for (int i = 0; i < n; i++)
            {
                var d = OnSphere();
                if (d.y < 0f) d.y = -d.y * 0.5f;
                d = (d + direction * 0.6f).normalized;
                Add(new Particle
                {
                    Pos = at, Vel = d * speed * R(0.4f, 1.1f), Size = R(0.05f, 0.1f), Life = seconds * R(0.5f, 1.2f), Drag = 0.6f, Gravity = 9.8f,
                    Heat = 1f, Cool = R(0.8f, 1.6f), Alpha = 1f, Colour = colour, Look = (byte)ParticleLook.Streak, GroundY = g, Flags = Bounce,
                });
            }
        }

        public void Embers(Vector3 at, int count, float spread, float seconds, Color32 colour)
        {
            int n = Scaled(count);
            for (int i = 0; i < n; i++)
            {
                var p = at + InCircle(spread) + Vector3.up * R(0f, spread * 0.6f);
                Add(new Particle
                {
                    Pos = p, Vel = InCircle(1.2f) + Vector3.up * R(0.8f, 2.2f), Size = R(0.06f, 0.14f), Life = seconds * R(0.6f, 1.3f), Drag = 0.9f,
                    Rise = R(0.4f, 1.2f), Heat = 1f, Cool = R(0.15f, 0.4f), Alpha = 1f, Carry = 1.2f, Colour = colour,
                    Look = (byte)ParticleLook.Glow, GroundY = Ground(p.x, p.z), Flags = Tinted,
                });
            }
        }

        // size is the drops' size, 0 for fine spray.
        public void Spray(Vector3 at, int count, float height, Color32 colour, float spread = 1f, float size = 0f)
        {
            int n = size > 0f ? count : Scaled(count);
            float g = Ground(at.x, at.z);
            float up = Mathf.Sqrt(2f * 9.8f * Mathf.Max(0.5f, height));
            for (int i = 0; i < n; i++)
            {
                var side = InCircle(spread);
                Add(new Particle
                {
                    Pos = at + side * 0.3f, Vel = side * R(0.6f, 1.6f) + Vector3.up * up * R(0.55f, 1.05f),
                    Size = size > 0f ? size * R(0.8f, 1.2f) : R(0.14f, 0.32f), Grow = size > 0f ? size * 0.8f : 0.15f, Life = R(0.9f, 1.6f),
                    Drag = 0.4f, Gravity = 9.8f, Alpha = colour.a / 255f, Colour = colour,
                    Look = size > 0f ? (byte)(R01() * 4f) : (byte)ParticleLook.Drop, GroundY = g, Flags = DiesOnGround,
                });
            }
        }

        public void Flakes(Vector3 at, int count, float spread, Color32 colour, float seconds = 3f)
        {
            int n = Scaled(count);
            float g = Ground(at.x, at.z);
            for (int i = 0; i < n; i++)
            {
                var side = InCircle(1f);
                Add(new Particle
                {
                    Pos = at + side * spread * 0.3f + Vector3.up * R(0.2f, 1.2f), Vel = side * spread * R(1f, 2.5f) + Vector3.up * R(2f, 5f),
                    Size = R(0.12f, 0.22f), Life = seconds * R(0.7f, 1.3f), Angle = R(0f, 360f), Spin = R(-400f, 400f), Drag = 2.2f, Gravity = 2.2f,
                    Alpha = colour.a / 255f, Carry = 0.8f, Colour = colour, Look = (byte)ParticleLook.Flake, GroundY = g, Flags = Bounce,
                });
            }
        }

        // A soft flash, strength past 1 going into the bloom.
        public void Glow(Vector3 at, float size, Color32 colour, float seconds, float strength = 1f)
        {
            Add(new Particle
            {
                Pos = at, Size = size, Life = Mathf.Max(0.02f, seconds), Heat = Mathf.Clamp01(strength - 1f), Alpha = Mathf.Clamp01(strength),
                Colour = colour, Look = (byte)ParticleLook.Glow, Flags = Tinted | Rooted,
            });
        }

        public void Streak(Vector3 from, Vector3 to, float width, Color32 colour, float seconds, float strength = 1f)
        {
            Add(new Particle
            {
                Pos = (from + to) * 0.5f, Axis = to - from, Size = width, Life = Mathf.Max(0.02f, seconds), Heat = Mathf.Clamp01(strength - 1f),
                Alpha = Mathf.Clamp01(strength), Colour = colour, Look = (byte)ParticleLook.Streak, Flags = Tinted | Rooted,
            });
        }

        // Dust and leaves turning round a point, rising as they go.
        public void Whirl(Vector3 at, float radius, int count, Color32 colour, float seconds, bool flakes)
        {
            int n = Scaled(count);
            float g = Ground(at.x, at.z);
            for (int i = 0; i < n; i++)
            {
                float a = R01() * Mathf.PI * 2f, r = radius * R(0.3f, 1f);
                var p = new Vector3(at.x + Mathf.Cos(a) * r, g + R(0.1f, radius * 0.8f), at.z + Mathf.Sin(a) * r);
                var tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                bool flake = flakes && R01() < 0.4f;
                Add(new Particle
                {
                    Pos = p, Vel = tangent * radius * R(2f, 3.5f) + Vector3.up * R(1f, 3f), Axis = new Vector3(at.x, radius, at.z),
                    Size = flake ? R(0.12f, 0.2f) : radius * R(0.25f, 0.45f), Grow = flake ? 0f : radius * 0.2f,
                    Life = seconds * R(0.7f, 1.2f), Angle = R(0f, 360f), Spin = flake ? R(-500f, 500f) : R(-60f, 60f), Drag = 0.8f, Rise = 0.4f,
                    Alpha = colour.a / 255f, Carry = 0.3f, Colour = flake ? new Color32(96, 112, 52, 255) : colour,
                    Look = flake ? (byte)ParticleLook.Flake : (byte)(R01() * 4f), GroundY = g, Flags = Swirl,
                });
            }
        }

        // exact keeps the count whatever the setting, for one stone that must land.
        public void Debris(Vector3 at, Vector3 direction, int count, float speed, float size, DebrisLook look, float seconds = 12f, bool exact = false)
        {
            int n = exact ? count : Scaled(count);
            for (int i = 0; i < n; i++)
            {
                var d = OnSphere();
                d.y = Mathf.Abs(d.y) * 1.4f + 0.35f;
                d = (d.normalized + direction * 0.35f).normalized;
                float s = size * R(0.5f, 1.3f);
                int k;
                Made++;
                if (chunks < cs.Length) k = chunks++;
                else { replaceChunk = (replaceChunk + 1) % cs.Length; k = replaceChunk; }
                cs[k] = new Chunk
                {
                    Pos = at + Vector3.up * 0.2f + InCircle(size), Vel = d * speed * R(0.45f, 1.05f),
                    Spin = OnSphere() * R(180f, 720f), Scale = new Vector3(s * R(0.7f, 1.3f), s * R(0.6f, 1.1f), s * R(0.7f, 1.3f)),
                    Rot = Quaternion.Euler(R(0f, 360f), R(0f, 360f), R(0f, 360f)), Life = seconds * R(0.7f, 1.3f),
                    Look = (byte)look, Shape = (byte)(R01() * 2f),
                };
            }
        }

        // ── Each frame ────────────────────────────────────────────────

        public void Step(float dt, Camera cam)
        {
            Fit(FxQuality.Current);
            frame++;
            if (cam != null)
            {
                GeometryUtility.CalculateFrustumPlanes(cam, frustum);
                haveFrustum = true;
                eye = cam.transform.position;
            }
            if (dt <= 0f) { StepMs = ChunkMs = 0f; return; }
            dt = Mathf.Min(dt, 0.1f);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            StepColumns(dt);
            StepParticles(dt);
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            StepChunks(dt);
            StepMs = Ms(t0, t1);
            ChunkMs = Ms(t1, System.Diagnostics.Stopwatch.GetTimestamp());
        }

        static float Ms(long from, long to) => (to - from) * 1000f / System.Diagnostics.Stopwatch.Frequency;

        void StepColumns(float dt)
        {
            for (int i = columns.Count - 1; i >= 0; i--)
            {
                var c = columns[i];
                c.Left -= dt;
                if (c.Left <= 0f) { columns.RemoveAt(i); continue; }
                // Thick at first, thinning out as it burns down.
                float strength = Mathf.Clamp01(c.Left / c.Seconds);
                c.Debt += dt * (1f + 2.2f * strength) * FxQuality.Current.Emission;
                while (c.Debt >= 1f)
                {
                    c.Debt -= 1f;
                    var at = c.At + InCircle(c.Size * 0.3f);
                    var col = c.Colour;
                    col.a = (byte)(col.a * (0.5f + 0.5f * strength));
                    Smoke(at, Vector3.up * R(0.6f, 1.4f), c.Size * R(0.7f, 1.05f), 3.5f + 2f * strength, col, c.Heat * strength, 0.9f);
                }
                columns[i] = c;
            }
        }

        void StepParticles(float dt)
        {
            var wind = Wind;
            for (int i = 0; i < count; i++)
            {
                ref var p = ref ps[i];
                p.Age += dt;
                if (p.Age >= p.Life)
                {
                    ps[i] = ps[--count];
                    i--;
                    continue;
                }
                if ((p.Flags & Rooted) != 0) continue;
                float t = p.Age / p.Life;
                var v = p.Vel;
                float k = Mathf.Min(1f, p.Drag * dt);
                if ((p.Flags & Swirl) != 0)
                {
                    // Axis holds the whirl's centre and radius.
                    var off = new Vector3(p.Pos.x - p.Axis.x, 0f, p.Pos.z - p.Axis.z);
                    float r = Mathf.Max(0.1f, off.magnitude);
                    var tangent = new Vector3(-off.z, 0f, off.x) / r;
                    var want = tangent * p.Axis.y * 3f - off / r * (r - p.Axis.y * 0.6f) + Vector3.up * 1.2f;
                    v += (want - v) * Mathf.Min(1f, 3f * dt);
                }
                else
                {
                    v.x += (wind.x * p.Carry - v.x) * k;
                    v.z += (wind.z * p.Carry - v.z) * k;
                    v.y -= v.y * k * 0.6f;
                }
                v.y += (p.Rise * (1f - t) - p.Gravity) * dt;
                p.Pos += v * dt;
                p.Vel = v;
                p.Size += p.Grow * dt * (1f - 0.6f * t);
                p.Angle += p.Spin * dt;
                if (p.Heat > 0f) p.Heat = Mathf.Max(0f, p.Heat - p.Cool * dt);

                // The ground, read now and then, and every frame near it.
                bool low = p.Pos.y < p.GroundY + 1.5f;
                if (((i + frame) & 7) == 0 || low && (p.Flags & (Bounce | DiesOnGround)) != 0) p.GroundY = Ground(p.Pos.x, p.Pos.z);
                if ((p.Flags & Hug) != 0)
                {
                    float floor = p.GroundY + p.Size * 0.22f;
                    if (p.Pos.y < floor) { p.Pos.y = floor; if (p.Vel.y < 0f) p.Vel.y = 0f; }
                }
                else if (p.Pos.y < p.GroundY)
                {
                    if ((p.Flags & DiesOnGround) != 0) { p.Life = p.Age; continue; }
                    p.Pos.y = p.GroundY;
                    if ((p.Flags & Bounce) != 0) { p.Vel = new Vector3(p.Vel.x * 0.5f, -p.Vel.y * 0.3f, p.Vel.z * 0.5f); p.Spin *= 0.3f; }
                    else if (p.Vel.y < 0f) p.Vel.y = 0f;
                }
            }
        }

        const float Gravity = 11f;

        void StepChunks(float dt)
        {
            for (int i = 0; i < chunks; i++)
            {
                ref var c = ref cs[i];
                c.Age += dt;
                if (c.State == 0)
                {
                    c.Vel.y -= Gravity * dt;
                    c.Pos += c.Vel * dt;
                    float m = c.Spin.magnitude;
                    if (m > 1e-3f) c.Rot = Quaternion.AngleAxis(m * dt, c.Spin / m) * c.Rot;
                    float g = Ground(c.Pos.x, c.Pos.z), half = c.Scale.y * 0.35f;
                    if (c.Pos.y - half < g)
                    {
                        c.Pos.y = g + half;
                        if (c.Vel.y > -2.2f && new Vector2(c.Vel.x, c.Vel.z).sqrMagnitude < 1.5f || c.Age > 6f)
                        {
                            c.State = 1;
                            c.Rest = c.Pos.y;
                            c.Matrix = Matrix4x4.TRS(c.Pos, c.Rot, c.Scale);
                        }
                        else
                        {
                            c.Vel = new Vector3(c.Vel.x * 0.55f, -c.Vel.y * 0.32f, c.Vel.z * 0.55f);
                            c.Spin *= 0.55f;
                        }
                    }
                }
                else if (c.State == 1 && c.Age >= c.Life) c.State = 2;
                if (c.State == 2)
                {
                    // Settled debris sinks into the ground and is gone.
                    c.Pos.y -= dt * Mathf.Max(0.08f, c.Scale.y * 0.5f);
                    c.Matrix = Matrix4x4.TRS(c.Pos, c.Rot, c.Scale);
                    if (c.Pos.y < c.Rest - c.Scale.y * 1.2f) { cs[i] = cs[--chunks]; i--; }
                }
            }
        }

        // ── Drawing ───────────────────────────────────────────────────

        public void Draw(Camera cam)
        {
            Drawn = 0;
            SendMs = 0f;
            if (cam == null) return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            DrawChunks();
            ChunkMs += Ms(t0, System.Diagnostics.Stopwatch.GetTimestamp());
            if (count == 0 || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsComputeShaders) return;
            EnsureMaterial();
            if (material == null) return;
            t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var at = cam.transform.position;
            SortFarToNear(at);
            for (int k = 0; k < count; k++) gpu[k] = Pack(ps[order[k]]);
            if (buffer == null || buffer.count < ps.Length)
            {
                buffer?.Release();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, ps.Length, Marshal.SizeOf<Gpu>());
            }
            buffer.SetData(gpu, 0, 0, count);
            props ??= new MaterialPropertyBlock();
            props.SetBuffer(ParticlesId, buffer);
            SetSky();
            var rp = new RenderParams(material)
            {
                matProps = props, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false,
                worldBounds = new Bounds(at, Vector3.one * 4000f),
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, count * 6);
            Drawn = count;
            SendMs = Ms(t0, System.Diagnostics.Stopwatch.GetTimestamp());
        }

        // Far to near by distance from the eye in 65536 steps: two passes of
        // a radix sort, which costs the same however the order moved.
        void SortFarToNear(Vector3 eye)
        {
            float far = 1e-3f;
            for (int i = 0; i < count; i++)
            {
                float d = (ps[i].Pos - eye).magnitude;
                keys[i] = d;
                if (d > far) far = d;
            }
            float k = 65535f / far;
            for (int i = 0; i < count; i++) near[i] = (ushort)(65535 - (int)(keys[i] * k));
            System.Array.Clear(bins, 0, 256);
            for (int i = 0; i < count; i++) bins[near[i] & 255]++;
            for (int b = 0, sum = 0; b < 256; b++) { int c = bins[b]; bins[b] = sum; sum += c; }
            for (int i = 0; i < count; i++) scratch[bins[near[i] & 255]++] = i;
            System.Array.Clear(bins, 0, 256);
            for (int i = 0; i < count; i++) bins[near[i] >> 8]++;
            for (int b = 0, sum = 0; b < 256; b++) { int c = bins[b]; bins[b] = sum; sum += c; }
            for (int j = 0; j < count; j++)
            {
                int i = scratch[j];
                order[bins[near[i] >> 8]++] = i;
            }
        }

        Gpu Pack(in Particle p)
        {
            float t = p.Age / p.Life;
            float fadeIn, fadeOut;
            var look = (ParticleLook)(p.Look & 7);
            if (look == ParticleLook.Glow || look == ParticleLook.Streak)
            {
                fadeIn = Mathf.Clamp01(p.Age / 0.02f);
                fadeOut = (p.Flags & Rooted) != 0 ? (1f - t) * (1f - t) : 1f - t;
            }
            else
            {
                fadeIn = Mathf.Clamp01(p.Age / 0.12f);
                fadeOut = Mathf.Clamp01((1f - t) / 0.5f);
            }
            var axis = p.Axis;
            if (look == ParticleLook.Streak && (p.Flags & Rooted) == 0) axis = p.Vel * 0.05f;
            uint flags = (p.Flags & Tinted) != 0 ? TintHeat : 0u;
            if ((p.Flags & Swirl) != 0) axis = Vector3.zero;
            return new Gpu
            {
                Pos = p.Pos, Size = p.Size, Axis = axis, Angle = p.Angle * Mathf.Deg2Rad,
                Colour = (uint)p.Colour.r | (uint)p.Colour.g << 8 | (uint)p.Colour.b << 16 | (uint)p.Colour.a << 24,
                Heat = p.Heat, Alpha = p.Alpha * fadeIn * fadeOut, Look = (uint)(p.Look & 7) | flags,
            };
        }

        void DrawChunks()
        {
            if (chunks == 0) return;
            EnsureChunkLooks();
            System.Array.Clear(chunkCounts, 0, chunkCounts.Length);
            for (int i = 0; i < chunks; i++)
            {
                ref var c = ref cs[i];
                int g = c.Look * 8 + ShapeOf((DebrisLook)c.Look) * 2 + c.Shape;
                var list = chunkGroups[g];
                if (list == null || list.Length == chunkCounts[g])
                {
                    System.Array.Resize(ref list, Mathf.Max(64, chunkCounts[g] * 2));
                    chunkGroups[g] = list;
                }
                list[chunkCounts[g]++] = c.State == 0 ? Matrix4x4.TRS(c.Pos, c.Rot, c.Scale) : c.Matrix;
            }
            for (int g = 0; g < chunkCounts.Length; g++)
            {
                int n = chunkCounts[g];
                if (n == 0) continue;
                var rp = new RenderParams(chunkMats[g / 8])
                {
                    shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, worldBounds = new Bounds(Vector3.zero, Vector3.one * 100000f),
                };
                for (int start = 0; start < n; start += 1023)
                    Graphics.RenderMeshInstanced(rp, shapes[g % 8], 0, chunkGroups[g], Mathf.Min(1023, n - start), start);
            }
        }

        static int ShapeOf(DebrisLook look)
        {
            switch (look)
            {
                case DebrisLook.Dirt: return 1;
                case DebrisLook.Wood: case DebrisLook.Charred: return 2;
                case DebrisLook.Ice: return 3;
                default: return 0;
            }
        }

        // What the last frame drew: mean place of the smoke, for tests.
        public Vector3 SmokeCentre
        {
            get
            {
                var sum = Vector3.zero;
                int n = 0;
                for (int i = 0; i < count; i++)
                    if ((ps[i].Look & 7) < 4 && (ps[i].Flags & (Hug | Swirl)) == 0) { sum += ps[i].Pos; n++; }
                return n > 0 ? sum / n : Vector3.zero;
            }
        }

        public int SmokeCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < count; i++) if ((ps[i].Look & 7) < 4) n++;
                return n;
            }
        }

        public void Clear()
        {
            count = chunks = 0;
            columns.Clear();
        }

        // ── Resources ─────────────────────────────────────────────────

        static readonly int ParticlesId = Shader.PropertyToID("_Particles");
        static readonly int AmbientId = Shader.PropertyToID("_OkuFxAmbient");
        static readonly int SunWayId = Shader.PropertyToID("_OkuFxSunWay");
        static readonly int SunId = Shader.PropertyToID("_OkuFxSun");

        // The sun and the sky as the scene sets them, for lighting smoke:
        // the way toward the sun, its light, and the sky's light from above
        // and the horizon. A scene with neither gets a plain daylight.
        static void SetSky()
        {
            var sun = RenderSettings.sun;
            bool up = sun != null && sun.isActiveAndEnabled;
            var way = up ? -sun.transform.forward : new Vector3(0.3f, 0.85f, 0.2f).normalized;
            var light = up ? sun.color.linear * sun.intensity : new Color(0.95f, 0.92f, 0.85f);
            Shader.SetGlobalVector(SunWayId, way);
            Shader.SetGlobalVector(SunId, new Vector4(light.r, light.g, light.b, 1f));
            var sky = (RenderSettings.ambientSkyColor.linear * 0.6f + RenderSettings.ambientEquatorColor.linear * 0.4f) * Mathf.Max(0.5f, RenderSettings.ambientIntensity);
            Shader.SetGlobalVector(AmbientId, new Vector4(Mathf.Max(sky.r, 0.1f), Mathf.Max(sky.g, 0.1f), Mathf.Max(sky.b, 0.11f), 1f));
        }
        static Texture2D sharedAtlas;

        void EnsureMaterial()
        {
            if (material != null) return;
            var shader = Looks.Find("OkuParticles", "Sprites/Default");
            if (shader == null || !shader.isSupported || shader.name != "OpenKingdoms/Presentation/Particles") return;
            if (sharedAtlas == null) sharedAtlas = FxPuffs.Atlas();
            material = new Material(shader) { hideFlags = HideFlags.DontSave, mainTexture = sharedAtlas };
        }

        void EnsureChunkLooks()
        {
            if (shapes != null) return;
            shapes = new Mesh[8];
            for (int v = 0; v < 2; v++)
            {
                shapes[0 + v] = FxPuffs.Rock(11 + v, 1f, 1f);
                shapes[2 + v] = FxPuffs.Rock(23 + v, 1.15f, 0.6f);
                shapes[4 + v] = FxPuffs.Splinter(37 + v);
                shapes[6 + v] = FxPuffs.Shard(41 + v);
            }
            var colours = new[]
            {
                new Color(0.34f, 0.26f, 0.18f), new Color(0.5f, 0.48f, 0.45f), new Color(0.55f, 0.4f, 0.24f),
                new Color(0.11f, 0.09f, 0.08f), new Color(0.78f, 0.9f, 1f), new Color(0.33f, 0.32f, 0.31f),
            };
            chunkMats = new Material[colours.Length];
            for (int i = 0; i < colours.Length; i++)
            {
                var m = Looks.Model(Texture2D.whiteTexture);
                m.color = colours[i];
                m.SetFloat("_Rim", 0.15f);
                m.SetFloat("_Glossiness", i == (int)DebrisLook.Ice ? 0.85f : i == (int)DebrisLook.Scrap ? 0.5f : 0.08f);
                chunkMats[i] = m;
            }
        }

        public void Dispose()
        {
            if (Active == this) Active = null;
            buffer?.Release();
            buffer = null;
            Looks.Release(material);
            material = null;
            if (shapes != null) foreach (var s in shapes) Looks.Release(s);
            if (chunkMats != null) foreach (var m in chunkMats) Looks.Release(m);
            shapes = null;
            chunkMats = null;
            Clear();
        }
    }
}
