// Debris.cs - chunks in flight and on the ground: a pool simulated with
// gravity, spin, bounce and friction against the drawn ground, for the look
// only. A chunk can wait in place until it goes, or ride a toppling trunk
// until it lands. Settled chunks lie a while and then sink away, and
// nothing is allocated once the pool is made.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    // What the Battle effects setting allows breaking: chunks moving and
    // lying at once, the scale on how many a kind breaks into, and how long
    // a chunk lies before it sinks.
    public struct BreakBudget
    {
        public EffectsQuality Level;
        public int Flying, Rubble;
        public float Chunks, Rest;

        public static BreakBudget For(EffectsQuality level) => From(FxQuality.For(level));

        public static BreakBudget From(FxQuality q)
        {
            var b = new BreakBudget { Level = q.Level, Flying = q.FlyingChunks, Rubble = q.RubbleKept };
            switch (q.Level)
            {
                case EffectsQuality.Low: b.Chunks = 0.5f; b.Rest = 3f; break;
                case EffectsQuality.Medium: b.Chunks = 0.75f; b.Rest = 6f; break;
                case EffectsQuality.Ultra: b.Chunks = 1.25f; b.Rest = 24f; break;
                default: b.Chunks = 1f; b.Rest = 12f; break;
            }
            return b;
        }
    }

    public sealed class Debris
    {
        public enum State : byte { Free, Waiting, Riding, Flying, Resting, Sinking, Fading }

        struct Piece
        {
            public ChunkDraw[] Draws;
            public Matrix4x4 Shape;     // mesh space to world, about the pivot, before its turn
            public Vector3 P, V, W;     // the pivot's place and speed, and the spin
            public Quaternion Q;
            public float Start, Age, Fade, Calm, Radius;
            public int Hold, Group, Support, SupportCount;
            public State State;
            public PieceExplode Trail;
            public bool Landed, ExplodeOnHit, Fine, Still;
            public Matrix4x4 M;         // where it lies, kept once it is still
        }

        struct Group
        {
            public Vector3 Pivot, Axis;
            public Quaternion Turn;
            public float Angle, Speed, Length;
            public bool Live, Down;
        }

        public const float Gravity = 9.8f, FadeSeconds = 0.5f, SinkSeconds = 1.5f, MaxFlight = 10f, HoldLimit = 30f;
        const int MaxSupport = 14, MaxGroups = 64;

        readonly Piece[] pieces;
        readonly Vector3[] support;
        readonly int[] free;
        int freeCount, high;
        readonly Group[] groups = new Group[MaxGroups];
        int nextHold = 1;

        public BreakBudget Budget;
        public int Capacity => pieces.Length;
        public float Now { get; private set; }
        // Chunks moving (flying or riding a fall), lying, and in use at all.
        public int Moving { get; private set; }
        public int Lying { get; private set; }
        public int Active { get; private set; }
        public int Drawn { get; private set; }

        // The drawn ground's height, and the sea's surface, NaN for none.
        public System.Func<float, float, float> Ground;
        public float Sea = float.NaN;
        // Where to raise dust and trails, a stand-in until the effects' particles land.
        public DustPuffs Dust;

        public Debris(BreakBudget budget, int extra = 512)
        {
            Budget = budget;
            int cap = budget.Flying + budget.Rubble + extra;
            pieces = new Piece[cap];
            support = new Vector3[cap * MaxSupport];
            free = new int[cap];
            for (int i = 0; i < cap; i++) free[i] = cap - 1 - i;
            freeCount = cap;
        }

        public float GroundAt(float x, float z) => Ground != null ? Ground(x, z) : ScarMap.GroundOffset(x, z);

        // Whether n more chunks may fly now.
        public bool Room(int n) => n <= freeCount && Moving + n <= Budget.Flying;

        public int NewHold() => nextHold++;

        // A trunk that swings down about pivot round axis, as a rod of the length.
        public int BeginFall(Vector3 pivot, Vector3 axis, float length, float speed)
        {
            for (int g = 0; g < MaxGroups; g++)
            {
                if (groups[g].Live) continue;
                groups[g] = new Group { Pivot = pivot, Axis = axis.normalized, Length = Mathf.Max(0.5f, length), Speed = speed, Live = true, Turn = Quaternion.identity };
                return g;
            }
            return -1;
        }

        // A chunk drawn by world (mesh space to world at rest) that turns about
        // pivot and rests on support. It waits in place until start, or until
        // its hold is let go, or rides a fall, and then flies with v and spin w.
        public int Spawn(ChunkDraw[] draws, in Matrix4x4 world, Vector3 pivot, Vector3[] rests, Vector3 v, Vector3 w,
            float start = 0f, int hold = 0, int group = -1, PieceExplode trail = PieceExplode.None, bool explodeOnHit = false, bool fine = false)
        {
            if (freeCount == 0 || draws == null) return -1;
            int i = free[--freeCount];
            if (i >= high) high = i + 1;
            var shape = world;
            shape.m03 = shape.m13 = shape.m23 = 0f;
            shape *= Matrix4x4.Translate(-pivot);
            int n = Mathf.Min(MaxSupport, rests?.Length ?? 0);
            float radius = 0.05f;
            for (int k = 0; k < n; k++)
            {
                var o = shape.MultiplyPoint3x4(rests[k]);
                support[i * MaxSupport + k] = o;
                radius = Mathf.Max(radius, o.magnitude);
            }
            pieces[i] = new Piece
            {
                Draws = draws, Shape = shape, P = world.MultiplyPoint3x4(pivot), V = v, W = w, Q = Quaternion.identity,
                Start = Now + start, Hold = hold, Group = group, Support = i * MaxSupport, SupportCount = n, Radius = radius,
                State = group >= 0 && group < MaxGroups && groups[group].Live ? State.Riding : State.Waiting,
                Trail = trail, ExplodeOnHit = explodeOnHit, Fine = fine,
            };
            if (hold == 0) Moving++;
            Active++;
            return i;
        }

        // Lets the chunks waiting on a hold go: fading away where the next
        // stage takes their place, or falling.
        public void Release(int hold, bool fade)
        {
            if (hold == 0) return;
            for (int i = 0; i < high; i++)
            {
                ref var p = ref pieces[i];
                if (p.State != State.Waiting || p.Hold != hold) continue;
                p.Hold = 0;
                if (fade) { p.State = State.Fading; p.Fade = 0f; }
                else p.Start = Now;
            }
        }

        // Sends a chunk into the ground, as deep as it reaches.
        public void Sink(int i, float depth)
        {
            if (i < 0 || i >= high || pieces[i].State == State.Free) return;
            pieces[i].State = State.Sinking;
            pieces[i].Fade = 0f;
            pieces[i].Radius = Mathf.Max(pieces[i].Radius, depth);
        }

        // Throws the chunks lying within reach of a point up again, as an
        // earthquake shakes rubble loose. Returns how many it threw.
        public int Kick(Vector3 at, float radius, float speed, int seed)
        {
            uint s = (uint)seed * 2654435761u | 1u;
            int room = Mathf.Max(0, Budget.Flying - Moving), n = 0;
            for (int i = 0; i < high && n < room; i++)
            {
                ref var p = ref pieces[i];
                if (p.State != State.Resting || p.Fine) continue;
                float dx = p.P.x - at.x, dz = p.P.z - at.z;
                if (dx * dx + dz * dz > radius * radius) continue;
                float a = Fracture.Rand01(ref s) * Mathf.PI * 2f, k = 0.4f + 0.6f * Fracture.Rand01(ref s);
                p.State = State.Flying;
                p.V = new Vector3(Mathf.Cos(a) * speed * 0.35f, speed * k, Mathf.Sin(a) * speed * 0.35f);
                p.W = new Vector3(Fracture.Rand01(ref s) - 0.5f, Fracture.Rand01(ref s) - 0.5f, Fracture.Rand01(ref s) - 0.5f) * 14f;
                p.Age = 0f;
                p.Calm = 0f;
                p.Landed = false;
                p.Still = false;
                n++;
            }
            Moving += n;
            return n;
        }

        public State StateOf(int i) => i >= 0 && i < high ? pieces[i].State : State.Free;
        public Vector3 PositionOf(int i) => pieces[i].P;
        // Grit and chips, rather than a chunk of a model.
        public bool IsFine(int i) => i >= 0 && i < high && pieces[i].Fine;

        // Where a chunk is drawn now, mesh space to world.
        public Matrix4x4 MatrixOf(int i)
        {
            ref var p = ref pieces[i];
            if (p.State == State.Riding && p.Group >= 0)
            {
                ref var g = ref groups[p.Group];
                var r = g.Turn;
                return Matrix4x4.TRS(g.Pivot + r * (p.P - g.Pivot), r * p.Q, Vector3.one) * p.Shape;
            }
            return Matrix4x4.TRS(p.P, p.Q, Vector3.one) * p.Shape;
        }

        public void Step(float dt)
        {
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, 0.1f);
            Now += dt;
            for (int g = 0; g < MaxGroups; g++) if (groups[g].Live) StepFall(g, dt);
            int lying = 0, moving = 0;
            float rest = Budget.Rest;
            // Over the cap, the oldest lying go sooner.
            if (Lying > Budget.Rubble && Lying > 0) rest *= (float)Budget.Rubble / Lying;
            for (int i = 0; i < high; i++)
            {
                ref var p = ref pieces[i];
                switch (p.State)
                {
                    case State.Free: continue;
                    case State.Waiting:
                        if (p.Hold == 0 && Now >= p.Start) { p.State = State.Flying; moving++; }
                        // A hold never let go, its feature gone some other way, fades.
                        else if (p.Hold != 0 && Now - p.Start > HoldLimit) { p.State = State.Fading; p.Fade = 0f; }
                        break;
                    case State.Riding:
                        moving++;
                        if (p.Group >= 0) Touch(ref p, ref groups[p.Group]);
                        break;
                    case State.Flying:
                        Fly(ref p, dt);
                        if (p.State == State.Flying) moving++;
                        break;
                    case State.Resting:
                        p.Age += dt;
                        // Grit lies a shorter while than chunks.
                        if (p.Age > (p.Fine ? rest * 0.3f : rest)) { p.State = State.Sinking; p.Fade = 0f; p.Still = false; }
                        lying++;
                        break;
                    case State.Sinking:
                        p.Fade += dt / SinkSeconds;
                        p.P.y -= p.Radius * 1.2f * dt / SinkSeconds;
                        if (p.Fade >= 1f) Free(i);
                        break;
                    case State.Fading:
                        p.Fade += dt / FadeSeconds;
                        if (p.Fade >= 1f) Free(i);
                        break;
                }
            }
            for (int g = 0; g < MaxGroups; g++) if (groups[g].Live && groups[g].Down) Land(g);
            Moving = moving;
            Lying = lying;
        }

        void Free(int i)
        {
            pieces[i].State = State.Free;
            pieces[i].Draws = null;
            free[freeCount++] = i;
            Active--;
            while (high > 0 && pieces[high - 1].State == State.Free) high--;
        }

        float Lowest(ref Piece p, Quaternion turn)
        {
            float low = 0f;
            for (int k = 0; k < p.SupportCount; k++)
            {
                float y = (turn * support[p.Support + k]).y;
                if (k == 0 || y < low) low = y;
            }
            return p.SupportCount > 0 ? low : -p.Radius;
        }

        void Fly(ref Piece p, float dt)
        {
            p.Age += dt;
            p.V.y -= Gravity * dt;
            p.V *= 1f - 0.15f * dt;
            p.P += p.V * dt;
            float spin = p.W.magnitude;
            if (spin > 1e-4f) p.Q = Quaternion.AngleAxis(spin * dt * Mathf.Rad2Deg, p.W / spin) * p.Q;
            if (!float.IsNaN(Sea) && p.P.y < Sea && GroundAt(p.P.x, p.P.z) < Sea)
            {
                // Into the sea, where it sinks out of sight.
                p.State = State.Sinking;
                p.Fade = 0f;
                Dust?.Splash(p.P, p.Radius);
                return;
            }
            if ((p.Trail & (PieceExplode.Smoke | PieceExplode.Fire)) != 0 && !p.Landed) Dust?.Trail(p.P, p.Trail, dt);
            float ground = GroundAt(p.P.x, p.P.z);
            float low = Lowest(ref p, p.Q);
            if (p.P.y + low >= ground)
            {
                p.Calm = 0f;
                if (p.Age > MaxFlight) Settle(ref p);
                return;
            }
            p.P.y = ground - low;
            float hit = -p.V.y;
            if (!p.Landed)
            {
                p.Landed = true;
                if (hit > 2f) Dust?.Land(p.P + Vector3.up * low, p.Radius, hit);
                if (p.ExplodeOnHit) { p.State = State.Fading; p.Fade = 0f; Dust?.Land(p.P, p.Radius * 2f, 8f); return; }
            }
            if (p.V.y < 0f) p.V.y = hit * 0.3f;
            p.V.x *= 1f - Mathf.Min(1f, 6f * dt);
            p.V.z *= 1f - Mathf.Min(1f, 6f * dt);
            // Lying on a point or an edge, it tips over onto a side.
            var contact = Vector3.zero;
            int touching = 0;
            for (int k = 0; k < p.SupportCount; k++)
            {
                var o = p.Q * support[p.Support + k];
                if (o.y <= low + 0.02f) { contact += o; touching++; }
            }
            if (touching > 0) contact /= touching;
            float reach = Mathf.Max(0.05f, p.Radius);
            p.W += Vector3.Cross(contact, Vector3.up) * (Gravity / (reach * reach) * dt);
            p.W *= 1f - Mathf.Min(1f, 5f * dt);
            p.Calm = p.V.sqrMagnitude < 0.2f && p.W.sqrMagnitude < 0.6f ? p.Calm + dt : 0f;
            if (p.Calm > 0.25f || p.Age > MaxFlight) Settle(ref p);
        }

        static void Settle(ref Piece p)
        {
            p.State = State.Resting;
            p.V = p.W = Vector3.zero;
            p.Age = 0f;
            p.Still = false;
        }

        // A trunk swings faster as it leans, as a rod falls.
        void StepFall(int g, float dt)
        {
            ref var f = ref groups[g];
            f.Speed += 1.5f * Gravity / f.Length * Mathf.Sin(f.Angle + 0.12f) * dt;
            f.Angle += f.Speed * dt;
            f.Turn = Quaternion.AngleAxis(f.Angle * Mathf.Rad2Deg, f.Axis);
            f.Down = f.Angle > 1.75f;
        }

        // It breaks where it meets the ground. The foot turns about the
        // pivot, so only chunks further up can land.
        void Touch(ref Piece p, ref Group f)
        {
            if (f.Down || (p.P - f.Pivot).sqrMagnitude < f.Length * f.Length * 0.16f) return;
            var at = f.Pivot + f.Turn * (p.P - f.Pivot);
            if (at.y + Lowest(ref p, f.Turn * p.Q) < GroundAt(at.x, at.z) - 0.05f) f.Down = true;
        }

        // The trunk breaks on landing: each chunk goes on as it was moving.
        void Land(int g)
        {
            ref var f = ref groups[g];
            var r = Quaternion.AngleAxis(f.Angle * Mathf.Rad2Deg, f.Axis);
            var spin = f.Axis * f.Speed;
            for (int i = 0; i < high; i++)
            {
                ref var p = ref pieces[i];
                if (p.State != State.Riding || p.Group != g) continue;
                var at = f.Pivot + r * (p.P - f.Pivot);
                p.V = Vector3.Cross(spin, at - f.Pivot) * 0.55f;
                p.W = spin * 0.6f + new Vector3(Mathf.Sin(i * 1.7f), Mathf.Cos(i * 2.3f), Mathf.Sin(i * 3.1f)) * 1.5f;
                p.P = at;
                p.Q = r * p.Q;
                p.Group = -1;
                p.State = State.Flying;
                p.Age = 0f;
                float low = Lowest(ref p, p.Q);
                float ground = GroundAt(p.P.x, p.P.z);
                if (p.P.y + low < ground) p.P.y = ground - low;
            }
            Dust?.Land(f.Pivot + (r * Vector3.up) * f.Length * 0.6f, f.Length * 0.3f, 6f);
            f.Live = false;
        }

        // Every chunk in the camera's view, faded as it goes.
        // Every chunk in the camera's view, faded as it goes. Grit and the
        // smallest chunks go to small, which casts no shadows.
        public void Draw(InstancedDraws into, InstancedDraws small, Plane[] frustum)
        {
            int drawn = 0;
            for (int i = 0; i < high; i++)
            {
                ref var p = ref pieces[i];
                if (p.State == State.Free) continue;
                Matrix4x4 m;
                if (p.State == State.Resting)
                {
                    if (!p.Still) { p.M = MatrixOf(i); p.Still = true; }
                    m = p.M;
                }
                else m = MatrixOf(i);
                if (frustum != null && !InView(frustum, new Vector3(m.m03, m.m13, m.m23), p.Radius + 0.5f)) continue;
                float fade = p.State == State.Sinking || p.State == State.Fading ? Mathf.Clamp(p.Fade, 0.001f, 1f) : 0f;
                var to = small != null && (p.Fine || p.Radius < SmallRadius) ? small : into;
                var draws = p.Draws;
                for (int k = 0; k < draws.Length; k++)
                    if (draws[k].Mesh != null && draws[k].Material != null) to.Add(draws[k].Mesh, draws[k].Submesh, draws[k].Material, m, 0f, fade);
                drawn++;
            }
            Drawn = drawn;
        }

        public void Draw(InstancedDraws into, Plane[] frustum) => Draw(into, null, frustum);

        // Chunks smaller than this cast no shadow.
        public const float SmallRadius = 0.15f;

        static bool InView(Plane[] frustum, Vector3 at, float reach)
        {
            for (int i = 0; i < frustum.Length; i++) if (frustum[i].GetDistanceToPoint(at) < -reach) return false;
            return true;
        }

        public void Clear()
        {
            for (int i = 0; i < high; i++) if (pieces[i].State != State.Free) Free(i);
            for (int g = 0; g < MaxGroups; g++) groups[g].Live = false;
            Moving = Lying = 0;
        }
    }
}
