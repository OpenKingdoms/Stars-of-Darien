// FxFire.cs - burning scenery as the engine reports it. Flames sized to the
// model catch on the side the fire came from and climb over it, a crown's
// leaves burn away, the model chars with embers glowing in its cracks, and
// its charred model fades into its burnt stage. A flickering light from the
// shared pool, a smoke column that bends with the wind, embers drifting
// downwind, sparks thrown at a neighbour as the fire spreads, char left on
// the ground under it, and smoke from what smoulders after. A forest burning
// together shares its smoke and light by patch, and the whole fire keeps to
// its share of the particles. Rain shortens the flames and turns the smoke
// to steam, and water puts a fire out in a burst of it. Nothing goes back to
// the engine, whose fire burns on as it would.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FxFire
    {
        // A patch of forest that shares its smoke column and light, world units.
        public const float CellSize = 6f;
        // The share of the battle's particles fires may keep alive.
        public const float ParticleShare = 0.45f;
        // How long what a fire leaves smoulders, at most.
        public const float SmoulderSeconds = 25f;
        // Hot puffs a second from a burning tree of the mock's size at full
        // burn, and how long each lives; flickering tongues of flame, embers,
        // the glow round a crown, and a patch's smoke the same.
        public const float FlameRate = 10f, FlameLife = 0.6f;
        public const float TongueRate = 24f, TongueLife = 0.18f;
        public const float EmberRate = 2.5f, EmberLife = 3.5f;
        public const float HaloRate = 4f, HaloLife = 0.3f;
        public const float SmokeRate = 1.5f, SmokeLife = 5.5f;
        // Flames in rain and in snow live this share of their dry life.
        public const float RainFlames = 0.55f, SnowFlames = 0.85f;

        sealed class Fire
        {
            public SceneryLook Look;
            public Vector3 At, From, Way;
            public int Def, Seed;
            public float Start, Seconds, SparkAt, OutAt, DousedAt, FanUntil, SmoulderFor = SmoulderSeconds;
            public bool Out, Doused, Sparked, Wisp;
            public float WispSize;
            public int Marks;
            public float FlameDebt, TongueDebt, EmberDebt, HaloDebt;
            // This frame: how hard it burns, and how much it smoulders.
            public float Intensity, Smoulder;
            public bool Seen;
        }

        struct Patch
        {
            public long Key;
            public Vector3 Sum, Low;
            public float Weight, Burn, Smoulder, Width, Height;
            public int Burning, Steaming;
            public bool Seen;
        }

        struct Arc
        {
            public Vector3 From, To;
            public float Width;
        }

        readonly IGameBackend backend;
        readonly EntityRenderer entities;
        readonly List<FeatureEvent> news = new List<FeatureEvent>();
        readonly List<Fire> fires = new List<Fire>();
        readonly Stack<Fire> spare = new Stack<Fire>();
        readonly List<Arc> arcs = new List<Arc>();
        readonly Dictionary<long, int> patchOf = new Dictionary<long, int>();
        readonly Dictionary<long, float> smokeDebt = new Dictionary<long, float>();
        readonly List<long> stale = new List<long>();
        Patch[] patches = new Patch[64];
        int patchCount;
        float now;
        uint rng = 0x6C8E9CF5u;

        // The weather now, from the atmosphere.
        public WeatherChoice Weather;
        // True for a point the player cannot see.
        public System.Func<Vector3, int, bool> Hidden;

        // What it has done, for tests and the log.
        public int Burning { get; private set; }
        public int Smouldering { get; private set; }
        public int Lit { get; private set; }
        public int Spread { get; private set; }
        public int SparkBursts { get; private set; }
        public int CharMarks { get; private set; }
        public int Doused { get; private set; }
        public long FlamesMade { get; private set; }
        public long TonguesMade { get; private set; }
        public long SmokeMade { get; private set; }
        public long SteamMade { get; private set; }
        public long EmbersMade { get; private set; }
        public int LightsAsked { get; private set; }
        public int Patches => patchCount;
        // The share of what the fires asked for that the budget let them make.
        public float Scale { get; private set; } = 1f;
        // How long the last flame was made to live, and the main thread's time last frame.
        public float LastFlameLife { get; private set; }
        public Color32 LastSmoke { get; private set; }
        public double Ms { get; private set; }

        public FxFire(IGameBackend backend, EntityRenderer entities)
        {
            this.backend = backend;
            this.entities = entities;
        }

        // A feature event, as the renderer reads it.
        public void Note(FeatureEvent e)
        {
            switch (e.Kind)
            {
                case FeatureEventKind.Burning:
                case FeatureEventKind.Burnt:
                case FeatureEventKind.Dying:
                case FeatureEventKind.Dead:
                case FeatureEventKind.Swept:
                case FeatureEventKind.Removed:
                    // Unread a long while, with the looks turned off: the oldest have had their moment.
                    if (news.Count >= 4096) news.RemoveRange(0, 2048);
                    news.Add(e);
                    break;
            }
        }

        // Where each fire burning now stands, for tests.
        public void BurningPlaces(List<Vector3> into)
        {
            foreach (var f in fires) if (!f.Out && !f.Wisp) into.Add(f.At);
        }

        public void Clear()
        {
            foreach (var f in fires) if (f.Look != null) f.Look.FireGlow = 0f;
            fires.Clear();
            arcs.Clear();
            news.Clear();
            smokeDebt.Clear();
            patchCount = 0;
        }

        // ── The shape of a fire ───────────────────────────────────────

        // How hard a fire burns through its life, 0 to 1 for t from its
        // catching to its burnt stage: it takes hold, burns, and dies down.
        public static float Intensity(float t)
        {
            if (t <= 0f) return 0f;
            if (t < 0.12f) return Mathf.SmoothStep(0.15f, 1f, t / 0.12f);
            if (t < 0.65f) return 1f;
            return Mathf.Lerp(1f, 0.4f, Mathf.Clamp01((t - 0.65f) / 0.35f));
        }

        // The share of the model alight, from where it caught to all of it.
        public static float Coverage(float t) => Mathf.Lerp(0.25f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.35f)));
        // The char on the model, the embers glowing in it, and a crown's leaves burnt away.
        public static float CharAt(float t) => Mathf.SmoothStep(0f, 0.92f, Mathf.Clamp01((t - 0.04f) / 0.86f));
        public static float GlowAt(float t) => 0.9f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.15f) / 0.4f));
        public static float BareAt(float t) => 0.85f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.25f) / 0.6f));

        // A model's size against the mock's tree, which makes FlameRate a second.
        public static float SizeOf(float width, float height) => Mathf.Clamp(Mathf.Sqrt(width * height / 4.8f), 0.6f, 3f);

        // The share of what fires ask that they may make: all of it while
        // they keep within their share of the particles.
        public static float BudgetScale(float asked, int particles) =>
            asked <= 1e-3f ? 1f : Mathf.Clamp01(ParticleShare * particles / asked);

        // Flames live shorter in rain and a little in snow.
        public static float FlameLifeIn(WeatherChoice w) => w == WeatherChoice.Rain ? RainFlames : w == WeatherChoice.Snow ? SnowFlames : 1f;
        // The share of a fire's smoke that is white steam in the weather.
        public static float SteamIn(WeatherChoice w) => w == WeatherChoice.Rain ? 0.8f : w == WeatherChoice.Snow ? 0.3f : 0f;

        // ── Each frame ────────────────────────────────────────────────

        // now is the battle's clock in seconds and dt the time since the last
        // frame. Lights are asked of the effects' pool.
        public void Update(float now, float dt, FxLights lights)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            this.now = now;
            ReadNews();
            var P = FxParticles.Active;
            Step(P);
            if (P != null && dt > 0f) Emit(P, dt);
            if (P != null && lights != null) Light(lights);
            Ms = clock.Elapsed.TotalMilliseconds;
        }

        void ReadNews()
        {
            float tps = Mathf.Max(1, backend.TicksPerSecond);
            foreach (var e in news)
            {
                switch (e.Kind)
                {
                    case FeatureEventKind.Burning: Catch(e, tps); break;
                    case FeatureEventKind.Burnt: Burnt(e); break;
                    default: Ended(e); break;
                }
            }
            news.Clear();
        }

        Fire FindFire(Vector3 at)
        {
            for (int i = fires.Count - 1; i >= 0; i--)
            {
                var f = fires[i];
                if (f.Wisp) continue;
                float dx = f.At.x - at.x, dz = f.At.z - at.z;
                if (dx * dx + dz * dz < 0.01f) return f;
            }
            return null;
        }

        void Catch(in FeatureEvent e, float tps)
        {
            float start = e.Tick / tps, seconds = e.Ticks > 0 ? e.Ticks / tps : 8f;
            // One read long after it burnt out, by a world built mid-battle, has had its moment.
            if (now > start + seconds + 1f) return;
            var old = FindFire(e.Position);
            if (old != null && !old.Out) return;
            if (old != null) fires.Remove(old);
            var f = spare.Count > 0 ? spare.Pop() : new Fire();
            f.Look = entities?.LookAt(e.Feature, e.Def, e.Position);
            f.At = e.Position;
            f.From = e.From;
            f.Def = e.Def;
            f.Seed = Mathf.RoundToInt(e.Position.x * 64f) * 73856093 ^ Mathf.RoundToInt(e.Position.z * 64f) * 19349663;
            f.Start = Mathf.Min(start, now);
            f.Seconds = seconds;
            f.SparkAt = f.Start + 2.5f + 2.5f * Hash01(f.Seed);
            f.Out = f.Doused = f.Sparked = f.Wisp = false;
            f.OutAt = f.DousedAt = f.FanUntil = 0f;
            f.SmoulderFor = SmoulderSeconds;
            f.Marks = 0;
            f.FlameDebt = f.TongueDebt = f.EmberDebt = f.HaloDebt = 0f;
            var way = e.From - e.Position;
            way.y = 0f;
            if (way.sqrMagnitude < 0.01f)
            {
                float a = Hash01(f.Seed * 7 + 1) * Mathf.PI * 2f;
                way = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }
            f.Way = way.normalized;
            fires.Add(f);
            Lit++;
            // Caught from a neighbour's sparks, with no blast behind it: they
            // fly across from the fire most likely to have thrown them.
            if (e.Blast == 0)
            {
                Spread++;
                var source = SourceOf(f);
                if (source != null)
                {
                    Shape(f, out var mid, out float w, out _);
                    Shape(source, out var from, out _, out _);
                    arcs.Add(new Arc { From = from, To = mid, Width = w });
                }
            }
        }

        // The burning fire nearest a new one, those upwind of it first, as
        // the original's sparks reach three cells round and five downwind.
        Fire SourceOf(Fire f)
        {
            var wind = FxParticles.Active != null ? FxParticles.Active.Wind : Vector3.zero;
            wind.y = 0f;
            float speed = wind.magnitude;
            var way = speed > 1e-3f ? wind / speed : Vector3.zero;
            Fire best = null;
            float bestScore = float.MaxValue;
            foreach (var g in fires)
            {
                if (g == f || g.Wisp || g.Out && now - g.OutAt > 1f) continue;
                var d = f.At - g.At;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist > 7f || dist < 1e-3f) continue;
                float score = dist * (1.2f - 0.6f * Vector3.Dot(d / dist, way));
                if (score < bestScore) { bestScore = score; best = g; }
            }
            return best;
        }

        void Burnt(in FeatureEvent e)
        {
            var f = FindFire(e.Position);
            if (f == null || f.Out) return;
            GoOut(f, SmoulderSeconds);
            // What is left is charred through, its embers dying.
            if (f.Look != null && e.NewDef >= 0)
            {
                f.Look.Char = Mathf.Max(f.Look.Char, 0.85f);
                if (f.Look.Kind == BreakKind.Tree) f.Look.Bare = Mathf.Max(f.Look.Bare, 0.9f);
            }
            else f.Look = null;
        }

        // Dying, swept or gone some other way: no more flames, a little smoke.
        void Ended(in FeatureEvent e)
        {
            var f = FindFire(e.Position);
            if (f == null || f.Out) return;
            GoOut(f, 8f);
        }

        void GoOut(Fire f, float smoulder)
        {
            float t = (now - f.Start) / Mathf.Max(0.1f, f.Seconds);
            float glow = f.Doused ? 0f : GlowAt(t);
            f.Out = true;
            f.OutAt = now;
            f.SmoulderFor = smoulder * Mathf.Max(0.6f, FxQuality.Current.SmokeLife);
            Stamp(f, 1.1f, 1f);
            var look = f.Look;
            if (look == null) return;
            look.FireGlow = 0f;
            look.GlowPeak = Mathf.Max(look.GlowPeak, glow);
            look.GlowAt = now;
            look.GlowFor = smoulder * 0.8f;
            look.Step(now);
        }

        // Steam where water reaches a fire: its flames go out, and what
        // smoulders after steams. Returns how many it put out.
        public int Douse(Vector3 at, float radius)
        {
            int n = 0;
            var P = FxParticles.Active;
            foreach (var f in fires)
            {
                if (f.Wisp || f.Doused || f.Out && now - f.OutAt > 3f) continue;
                Shape(f, out var mid, out float w, out float h);
                float dx = f.At.x - at.x, dz = f.At.z - at.z, reach = radius + w * 0.5f;
                if (dx * dx + dz * dz > reach * reach) continue;
                f.Doused = true;
                f.DousedAt = now;
                n++;
                if (P == null || !Visible(P, f, mid, w, h)) continue;
                int puffs = Mathf.RoundToInt(6 + 6 * SizeOf(w, h));
                for (int i = 0; i < puffs; i++)
                {
                    var o = new Vector3(R(-0.4f, 0.4f) * w, R(-0.3f, 0.3f) * h, R(-0.4f, 0.4f) * w);
                    P.Smoke(mid + o, Vector3.up * R(1.2f, 3f) + o * 0.8f, 0.6f * w * R(0.8f, 1.3f), R(2.5f, 4f), new Color32(236, 238, 242, 200), 0f, 0.9f);
                    SteamMade++;
                }
            }
            Doused += n;
            return n;
        }

        // Smoke curling up from a point for a while, as from a trunk lightning split.
        public void Wisp(Vector3 at, float seconds, float size)
        {
            var f = spare.Count > 0 ? spare.Pop() : new Fire();
            f.Look = null;
            f.At = at;
            f.From = at;
            f.Def = -1;
            f.Seed = Mathf.RoundToInt(at.x * 37f) ^ Mathf.RoundToInt(at.z * 91f) * 7919;
            f.Start = f.OutAt = now;
            f.Seconds = 1f;
            f.Out = f.Wisp = true;
            f.Doused = f.Sparked = false;
            f.WispSize = Mathf.Max(0.3f, size);
            f.SmoulderFor = seconds;
            f.Marks = 3;
            fires.Add(f);
        }

        // Wind that fans the fires in its reach: embers and sparks thrown downwind, and fiercer flames a while.
        public int Fan(Vector3 at, float radius, Vector3 way)
        {
            int n = 0;
            var P = FxParticles.Active;
            foreach (var f in fires)
            {
                if (f.Out || f.Doused || f.Wisp) continue;
                float dx = f.At.x - at.x, dz = f.At.z - at.z;
                if (dx * dx + dz * dz > radius * radius) continue;
                f.FanUntil = now + 2f;
                n++;
                Shape(f, out var mid, out float w, out float h);
                if (P == null || !Visible(P, f, mid, w, h)) continue;
                P.Embers(mid, 8, 0.4f * w, 3f, new Color32(255, 160, 70, 255));
                P.Sparks(mid, (way + Vector3.up * 0.4f).normalized, 10, 5f, new Color32(255, 190, 100, 255));
                EmbersMade += 8;
            }
            return n;
        }

        // Each fire's state this frame, its marks on the model and the
        // ground, and the patches of forest it burns in.
        void Step(FxParticles P)
        {
            patchOf.Clear();
            patchCount = 0;
            int burning = 0, smouldering = 0;
            for (int i = fires.Count - 1; i >= 0; i--)
            {
                var f = fires[i];
                if (f.Look != null && f.Look.Gone) f.Look = null;
                // A feature drawn only after it caught finds its look a little later.
                if (f.Look == null && !f.Out && !f.Wisp && entities != null && (Time.frameCount + f.Seed & 15) == 0) f.Look = entities.LookNear(f.At, 0.3f);
                float t = (now - f.Start) / Mathf.Max(0.1f, f.Seconds);
                // A burnt stage that never came, or a fire left burning: it goes out on its own.
                if (!f.Out && t > 1.6f) GoOut(f, SmoulderSeconds);
                if (f.Out && now - f.OutAt > f.SmoulderFor)
                {
                    if (f.Look != null) f.Look.Step(now);
                    fires.RemoveAt(i);
                    f.Look = null;
                    spare.Push(f);
                    continue;
                }
                f.Intensity = !f.Out && !f.Doused ? Intensity(t) * (now < f.FanUntil ? 1.4f : 1f) : 0f;
                f.Smoulder = f.Out ? 1f - (now - f.OutAt) / f.SmoulderFor : f.Doused ? 0.8f : 0f;
                if (!f.Out) burning++; else smouldering++;
                var look = f.Look;
                if (look != null && !f.Out)
                {
                    look.Char = Mathf.Max(look.Char, CharAt(t));
                    if (look.Kind == BreakKind.Tree) look.Bare = Mathf.Max(look.Bare, BareAt(t));
                    look.FireGlow = f.Doused ? GlowAt(t) * Mathf.Exp(-(now - f.DousedAt) / 1.5f) : GlowAt(t);
                    // Frost cannot hold on a burning tree.
                    look.FrostPeak = 0f;
                    look.Step(now);
                }
                else if (look != null) look.Step(now);
                // Char under it on the ground as it takes hold and burns.
                if (!f.Out && !f.Wisp)
                {
                    if (f.Marks == 0) Stamp(f, 0.5f, 0.45f);
                    if (f.Marks == 1 && t >= 0.5f) Stamp(f, 0.85f, 0.75f);
                }
                Shape(f, out var mid, out float w, out float h);
                f.Seen = P != null && Visible(P, f, mid, w, h);
                AddToPatch(f, mid, w, h);
            }
            Burning = burning;
            Smouldering = smouldering;
        }

        void Stamp(Fire f, float reach, float strength)
        {
            if (f.Wisp) return;
            f.Marks++;
            var map = ScarMap.Current;
            if (map == null) return;
            Shape(f, out _, out float w, out _);
            map.Mark(ScarKind.Fire, f.At, Mathf.Max(0.4f, 0.5f * w * reach + 0.2f), f.Way, strength);
            CharMarks++;
        }

        // A fire's middle, and the width and height it burns over: its model's, or its kind's.
        void Shape(Fire f, out Vector3 mid, out float width, out float height)
        {
            if (f.Wisp)
            {
                mid = f.At;
                width = f.WispSize;
                height = f.WispSize;
                return;
            }
            if (f.Look != null)
            {
                mid = f.Look.Middle;
                width = f.Look.Width;
                height = f.Look.Height;
                return;
            }
            var defs = backend.FeatureDefs;
            var d = f.Def >= 0 && f.Def < defs.Count ? defs[f.Def] : null;
            float cell = backend.Terrain != null ? backend.Terrain.CellSize : 1f;
            width = d != null ? Mathf.Max(1, Mathf.Max(d.Footprint.x, d.Footprint.y)) * cell : 1f;
            height = d != null && d.Height > 0f ? d.Height : 2f;
            mid = f.At + Vector3.up * height * 0.6f;
        }

        bool Visible(FxParticles P, Fire f, Vector3 mid, float w, float h) =>
            (Hidden == null || !Hidden(f.At, -1)) && P.OnScreen(mid + Vector3.up * h, w + h * 2f + 4f);

        void AddToPatch(Fire f, Vector3 mid, float w, float h)
        {
            long key = (long)Mathf.FloorToInt(f.At.x / CellSize) << 32 ^ (uint)Mathf.FloorToInt(-f.At.z / CellSize);
            if (!patchOf.TryGetValue(key, out int k))
            {
                k = patchCount++;
                if (k == patches.Length) System.Array.Resize(ref patches, k * 2);
                patches[k] = new Patch { Key = key, Low = f.At };
                patchOf[key] = k;
            }
            ref var p = ref patches[k];
            float weight = Mathf.Max(0.05f, f.Intensity + f.Smoulder * 0.3f);
            p.Sum += mid * weight;
            p.Weight += weight;
            p.Low.y = Mathf.Min(p.Low.y, f.At.y);
            p.Burn += f.Intensity;
            p.Smoulder += f.Wisp ? f.Smoulder * 0.6f : f.Smoulder;
            p.Width = Mathf.Max(p.Width, w);
            p.Height = Mathf.Max(p.Height, h);
            if (!f.Out) p.Burning++;
            if (f.Doused) p.Steaming++;
            p.Seen |= f.Seen;
        }

        // ── Making the fire ───────────────────────────────────────────

        void Emit(FxParticles P, float dt)
        {
            var q = FxQuality.Current;
            float lifeIn = FlameLifeIn(Weather), steam = SteamIn(Weather);
            bool rain = Weather == WeatherChoice.Rain;
            // What the fires on screen ask for, against their share.
            float asked = 0f;
            foreach (var f in fires)
            {
                if (!f.Seen || f.Intensity <= 0f) continue;
                Shape(f, out var mid, out float w, out float h);
                float near = P.Nearness(mid);
                asked += ((FlameRate * FlameLife + TongueRate * TongueLife) * SizeOf(w, h) * lifeIn + EmberRate * EmberLife + HaloRate * HaloLife) * f.Intensity * near * q.Emission;
            }
            for (int k = 0; k < patchCount; k++)
                if (patches[k].Seen) asked += SmokeRate * Mathf.Pow(patches[k].Burn + patches[k].Smoulder * 0.4f + 0.01f, 0.6f) * SmokeLife * q.SmokeLife * q.Emission * (rain ? 1.5f : 1f);
            Scale = BudgetScale(asked, q.Particles);
            // With fewer flames each grows a little, so a crown stays covered.
            float grow = Mathf.Min(1.35f, Mathf.Pow(1f / Mathf.Max(0.15f, Scale), 0.35f));
            var lean = new Vector3(P.Wind.x, 0f, P.Wind.z) * 0.12f;
            int most = Mathf.Max(32, q.Particles / 20), made = 0;

            foreach (var f in fires)
            {
                if (!f.Seen) continue;
                Shape(f, out var mid, out float w, out float h);
                if (!f.Sparked && !f.Out && now >= f.SparkAt && !f.Wisp)
                {
                    // The original's sparks, thrown once as it burns.
                    f.Sparked = true;
                    SparkBursts++;
                    P.Sparks(mid + Vector3.up * h * 0.25f, (Vector3.up + P.Wind * 0.3f).normalized, Mathf.RoundToInt(14 * SizeOf(w, h)), 5.5f, new Color32(255, 196, 110, 255), 1f);
                    P.Embers(mid, 6, 0.4f * w, 3.5f, new Color32(255, 150, 60, 255));
                    EmbersMade += 6;
                }
                if (f.Intensity <= 0f) continue;
                float near = P.Nearness(mid);
                float k = Scale * near;
                float t = (now - f.Start) / Mathf.Max(0.1f, f.Seconds);
                float cover = Coverage(t), s = SizeOf(w, h);
                f.FlameDebt += FlameRate * s * f.Intensity * k * q.Emission * (rain ? 0.75f : 1f) * dt;
                f.TongueDebt += TongueRate * s * f.Intensity * k * q.Emission * (rain ? 0.6f : 1f) * dt;
                f.HaloDebt += HaloRate * f.Intensity * k * q.Emission * dt;
                f.EmberDebt += EmberRate * Mathf.Sqrt(s) * f.Intensity * k * dt;
                float flameSize = Mathf.Clamp(0.18f * w + 0.05f * h, 0.3f, 1.2f) * grow * (rain ? 0.8f : 1f);
                // Tongues of flame: short lines of light leaning with the wind, each gone in a blink.
                while (f.TongueDebt >= 1f && made < most)
                {
                    f.TongueDebt -= 1f;
                    made++;
                    var p = FlamePoint(f, mid, w, h, cover);
                    float len = flameSize * R(0.7f, 1.5f) * (rain ? 0.6f : 1f);
                    P.Streak(p, p + (Vector3.up + lean) * len, flameSize * R(0.3f, 0.5f),
                        Color32.Lerp(new Color32(255, 96, 24, 255), new Color32(255, 196, 92, 255), R01()), R(0.12f, 0.24f) * lifeIn, R(1.15f, 1.7f));
                    TonguesMade++;
                }
                if (f.TongueDebt > 4f) f.TongueDebt = 4f;
                // Hot puffs that rise and cool into smoke at their tips.
                while (f.FlameDebt >= 1f && made < most)
                {
                    f.FlameDebt -= 1f;
                    made++;
                    var p = FlamePoint(f, mid, w, h, cover);
                    float life = R(0.42f, 0.72f) * lifeIn;
                    LastFlameLife = life;
                    P.Smoke(p, Vector3.up * (R(1.3f, 2.4f) * Mathf.Sqrt(Mathf.Max(1f, h) / 3f)) + new Vector3(R(-0.3f, 0.3f), 0f, R(-0.3f, 0.3f)),
                        flameSize * R(0.55f, 0.9f), life / Mathf.Max(0.1f, q.SmokeLife), new Color32(84, 74, 66, 220), R(0.65f, 0.88f), 2.2f);
                    FlamesMade++;
                    // Rain hisses off the flames as steam.
                    if (rain && R01() < 0.25f)
                    {
                        P.Smoke(p + Vector3.up * 0.3f, Vector3.up * R(1f, 2f), flameSize * 0.8f, R(1.2f, 2f), new Color32(232, 235, 238, 150), 0f, 0.8f);
                        SteamMade++;
                    }
                }
                if (f.FlameDebt > 4f) f.FlameDebt = 4f;
                while (f.HaloDebt >= 1f)
                {
                    f.HaloDebt -= 1f;
                    P.Glow(mid + new Vector3(R(-0.2f, 0.2f) * w, R(-0.15f, 0.2f) * h, R(-0.2f, 0.2f) * w), 0.9f * w * R(0.8f, 1.1f),
                        new Color32(255, 128, 44, 255), HaloLife, (0.25f + 0.15f * f.Intensity) * (rain ? 0.7f : 1f));
                }
                while (f.EmberDebt >= 1f)
                {
                    f.EmberDebt -= 1f;
                    P.Embers(mid + Vector3.up * h * 0.2f, 1, 0.4f * w, EmberLife, new Color32(255, 150, 60, 255));
                    EmbersMade++;
                }
            }

            // Smoke by patch: a column from the forest burning there, bending with the wind.
            stale.Clear();
            foreach (var key in smokeDebt.Keys) if (!patchOf.ContainsKey(key)) stale.Add(key);
            foreach (var key in stale) smokeDebt.Remove(key);
            for (int i = 0; i < patchCount; i++)
            {
                ref var p = ref patches[i];
                if (!p.Seen) continue;
                var mid = p.Sum / Mathf.Max(1e-3f, p.Weight);
                float strength = p.Burn + p.Smoulder * 0.4f;
                if (strength <= 0.01f) continue;
                smokeDebt.TryGetValue(p.Key, out float debt);
                debt += SmokeRate * Mathf.Pow(strength, 0.6f) * Scale * q.Emission * (rain ? 1.5f : 1f) * dt;
                bool burning = p.Burn > 0.05f;
                // Burning wood smokes dark; what smoulders, grey; anything doused or rained on, white with steam.
                float white = p.Steaming > 0 ? 0.9f : steam;
                var dark = burning ? new Color32(90, 84, 78, 165) : new Color32(140, 136, 130, 110);
                var colour = Color32.Lerp(dark, new Color32(226, 229, 233, 150), white);
                float size = Mathf.Min(4f, (0.55f + 0.25f * Mathf.Sqrt(Mathf.Max(1, p.Burning))) * Mathf.Max(0.8f, p.Width) * (burning ? 1f : 0.7f));
                float top = p.Low.y + p.Height * 0.95f;
                float spread = Mathf.Min(CellSize * 0.4f, p.Width * 0.3f + 0.3f * Mathf.Sqrt(Mathf.Max(1, p.Burning)));
                while (debt >= 1f && made < most)
                {
                    debt -= 1f;
                    made++;
                    var at = new Vector3(mid.x + R(-spread, spread), top + R(-0.2f, 0.4f), mid.z + R(-spread, spread));
                    P.Smoke(at, Vector3.up * R(1f, 1.8f), size * R(0.8f, 1.2f), SmokeLife * R(0.8f, 1.2f), colour, burning && white < 0.5f ? 0.1f : 0f, 1.3f);
                    LastSmoke = colour;
                    if (white >= 0.5f) SteamMade++; else SmokeMade++;
                }
                smokeDebt[p.Key] = Mathf.Min(debt, 4f);
            }

            // Sparks flying from a fire to the neighbour it set alight.
            foreach (var a in arcs)
            {
                if (!P.OnScreen(a.To, 6f) || Hidden != null && Hidden(a.To, -1)) continue;
                var gap = a.To - a.From;
                float d = gap.magnitude;
                var way = d > 1e-3f ? gap / d : Vector3.up;
                P.Sparks(a.From, (way + Vector3.up * 0.5f).normalized, 14, 4f + 1.6f * d, new Color32(255, 196, 110, 255), 0.9f);
                for (int k = 1; k <= 3; k++) P.Embers(Vector3.Lerp(a.From, a.To, k / 4f) + Vector3.up * 0.3f, 2, 0.3f, 2.5f, new Color32(255, 160, 70, 255));
                P.Glow(a.To, 1.2f * Mathf.Max(0.6f, a.Width), new Color32(255, 150, 60, 255), 0.35f, 1.2f);
                EmbersMade += 6;
            }
            arcs.Clear();
        }

        // Where a flame rises: in a tree's crown more than on its trunk, and
        // only over the share alight, which spreads from where it caught.
        Vector3 FlamePoint(Fire f, Vector3 mid, float w, float h, float cover)
        {
            var look = f.Look;
            bool tree = look != null && look.Kind == BreakKind.Tree;
            float foot = look != null ? look.Foot.y : f.At.y;
            var centre = look != null ? new Vector3(look.Bounds.center.x, 0f, look.Bounds.center.z) : new Vector3(f.At.x, 0f, f.At.z);
            float u = R01();
            float y01 = tree ? 0.3f + 0.7f * (1f - (1f - u) * (1f - u)) : 0.15f + 0.85f * u;
            // A crown is round, widest a little above its middle.
            float girth = tree ? Mathf.Lerp(0.15f, 0.5f, Mathf.Sin(Mathf.Clamp01((y01 - 0.25f) / 0.75f) * Mathf.PI)) : 0.45f;
            float a = R01() * Mathf.PI * 2f, r = Mathf.Sqrt(R01()) * girth * w;
            var p = new Vector3(centre.x + Mathf.Cos(a) * r, foot + y01 * h * 0.95f, centre.z + Mathf.Sin(a) * r);
            // It caught low on the side the fire came from.
            var caught = new Vector3(centre.x + f.Way.x * 0.35f * w, foot + h * (tree ? 0.4f : 0.25f), centre.z + f.Way.z * 0.35f * w);
            if (cover < 0.999f)
            {
                float reach = (h + w) * cover;
                var off = p - caught;
                if (off.magnitude > reach) p = caught + off.normalized * reach * Mathf.Sqrt(R01());
            }
            return p;
        }

        // A flickering light for each patch burning, from the effects' pool.
        void Light(FxLights lights)
        {
            LightsAsked = 0;
            for (int i = 0; i < patchCount; i++)
            {
                ref var p = ref patches[i];
                if (!p.Seen || p.Burn <= 0.02f) continue;
                var mid = p.Sum / Mathf.Max(1e-3f, p.Weight);
                float range = Mathf.Clamp(4f + 2f * p.Width * Mathf.Sqrt(Mathf.Max(1, p.Burning)), 5f, 18f);
                float reach = range / 1.5f;
                int key = -3000000 - (int)(p.Key * 2654435761L & 0x7FFFF);
                float k = (key & 1023) * 0.37f;
                float flicker = 0.78f + 0.12f * Mathf.Sin(now * 11f + k) + 0.1f * Mathf.Sin(now * 23.3f + k * 1.7f);
                float strength = 0.5f * Mathf.Sqrt(p.Burn) * flicker * (Weather == WeatherChoice.Rain ? 0.7f : 1f);
                lights.Flash(new Vector3(mid.x, p.Low.y + p.Height * 0.6f, mid.z), new Color(1f, 0.52f, 0.2f), reach, strength * (reach * reach + 4f), key);
                LightsAsked++;
            }
        }

        // ── Numbers ───────────────────────────────────────────────────

        float R01()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 16777216f;
        }

        float R(float a, float b) => a + (b - a) * R01();

        static float Hash01(int seed)
        {
            uint h = (uint)seed * 2654435761u;
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
}
