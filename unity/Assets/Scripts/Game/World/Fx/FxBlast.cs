// FxBlast.cs - each blast's parts round the original's picture, which stays
// at its heart: a flash with a real light, a shock front and a ring of dust
// along the ground, debris thrown with simple physics and sized to the
// blast, smoke that rises and drifts with the wind, sparks, embers and a
// shake of the camera, each kind of attack in its own way (FxKinds). Read
// from the backend's blasts each frame. Nothing goes back to the engine.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FxBlasts
    {
        readonly IGameBackend backend;
        readonly FxParticles particles;
        readonly FxShock shock;
        readonly BlastEvent[] read = new BlastEvent[128];
        readonly Dictionary<WeaponInfo, BlastKind> kinds = new Dictionary<WeaponInfo, BlastKind>();
        readonly Dictionary<WeaponInfo, bool> divine = new Dictionary<WeaponInfo, bool>();
        int since = -1;

        // A light held over its life: up at once, then down.
        struct Held
        {
            public Vector3 At;
            public Color Colour;
            public float Reach, Strength, Age, Life, Hold;
            public bool Flicker;
            public int Key;
        }

        readonly Held[] held = new Held[128];
        int heldCount, nextKey;

        struct Quake
        {
            public Vector3 At;
            public float Strength, Age, Life;
        }

        readonly Quake[] jolts = new Quake[16];
        int joltCount;
        float shakeClock;

        // Blasts given a look since the start, lights asked this frame, and
        // where the camera is thrown this frame.
        public int Played { get; private set; }
        public int LightsAsked { get; private set; }
        public Vector3 Shake { get; private set; }

        // True for a point the player cannot see.
        public System.Func<Vector3, int, bool> Hidden;
        // Where arrows landed on the ground this frame, in sight.
        public readonly List<Vector3> ArrowLandings = new List<Vector3>(64);

        public FxBlasts(IGameBackend backend, FxParticles particles, FxShock shock)
        {
            this.backend = backend;
            this.particles = particles;
            this.shock = shock;
        }

        // Reads the blasts since the last frame and plays each in sight,
        // then holds the lights and the shake over their lives.
        public void Update(float dt, Vector3 focus, FxLights lights)
        {
            ArrowLandings.Clear();
            ReadBlasts();
            LightsAsked = 0;
            for (int i = heldCount - 1; i >= 0; i--)
            {
                ref var h = ref held[i];
                h.Age += dt;
                if (h.Age >= h.Life) { held[i] = held[--heldCount]; continue; }
                float t = Mathf.Clamp01((h.Age / h.Life - h.Hold) / Mathf.Max(0.01f, 1f - h.Hold));
                float level = (1f - t) * (1f - t);
                if (h.Flicker) level *= 0.8f + 0.2f * Mathf.Sin(h.Age * 37f + h.Key) * Mathf.Sin(h.Age * 13f + h.Key * 0.7f);
                lights.Flash(h.At, h.Colour, h.Reach, h.Strength * level * (h.Reach * h.Reach + 4f), h.Key);
                LightsAsked++;
            }
            StepShake(dt, focus);
        }

        void ReadBlasts()
        {
            int tps = Mathf.Max(1, backend.TicksPerSecond);
            bool first = since < 0;
            if (first) since = 0;
            int n, budget = 0;
            do
            {
                n = backend.ReadBlasts(since, read);
                for (int i = 0; i < n; i++)
                {
                    var b = read[i];
                    since = Mathf.Max(since, b.Id);
                    // A blast read late, by a world built mid-battle or parts
                    // turned back on, has had its moment.
                    if (backend.Tick > b.Tick + (uint)(first ? tps : tps / 2)) continue;
                    if ((b.Flags & BlastFlags.OutOfSight) != 0 || Hidden != null && Hidden(b.Position, -1)) continue;
                    var kind = KindOf(b, out bool holy);
                    if (kind == BlastKind.None) continue;
                    if (kind == BlastKind.Arrow && b.Unit < 0 && ArrowLandings.Count < 64) ArrowLandings.Add(b.Position);
                    // A crowd of blasts in one frame: the later ones get less.
                    float share = budget++ < 24 ? 1f : 0.3f;
                    if (Play(kind, b.Position, b.Radius, b.Direction, b.Flags, b.Weapon, holy, b.Id, share, b.Feature >= 0)) Played++;
                }
            } while (n == read.Length);
        }

        BlastKind KindOf(in BlastEvent b, out bool holy)
        {
            holy = false;
            var w = b.Weapon;
            if (w == null) return FxKinds.Of(b);
            if (!kinds.TryGetValue(w, out var kind))
            {
                string caster = w.Def >= 0 && w.Def < backend.UnitDefs.Count ? backend.UnitDefs[w.Def].Name : b.Def >= 0 && b.Def < backend.UnitDefs.Count ? backend.UnitDefs[b.Def].Name : null;
                kinds[w] = kind = FxKinds.Of(w, caster);
                divine[w] = FxKinds.IsDivine(caster) || FxKinds.IsDivine(FirstWord(w.Name));
            }
            holy = divine[w];
            return kind;
        }

        static string FirstWord(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOf(' ');
            return i > 0 ? s.Substring(0, i) : s;
        }

        public void Clear()
        {
            heldCount = 0;
            joltCount = 0;
            Shake = Vector3.zero;
        }

        // ── Each kind ─────────────────────────────────────────────────

        // The least and most reach a kind's parts are drawn for, world units.
        static void Reach(BlastKind kind, out float least, out float most)
        {
            switch (kind)
            {
                case BlastKind.Gunpowder: least = 0.6f; most = 6f; break;
                case BlastKind.Siege: least = 0.8f; most = 5f; break;
                case BlastKind.Arrow: least = 0.3f; most = 0.6f; break;
                case BlastKind.Breath: least = 0.8f; most = 2f; break;
                case BlastKind.Frost: least = 1f; most = 2.5f; break;
                case BlastKind.Earth: least = 3f; most = 7f; break;
                case BlastKind.Wind: least = 1.2f; most = 4f; break;
                default: least = 1f; most = 4.5f; break;
            }
        }

        static Color32 C(int r, int g, int b, int a = 255) => new Color32((byte)r, (byte)g, (byte)b, (byte)a);

        // Plays a kind of blast at a point: radius is the blast's reach in
        // world units, direction the way its shot flew. False when it was
        // off screen. The scars and magic streams can call this too.
        public bool Play(BlastKind kind, Vector3 at, float radius, Vector3 direction, BlastFlags flags = BlastFlags.None, WeaponInfo weapon = null,
            bool holy = false, int seed = 0, float share = 1f, bool wood = false)
        {
            if (kind == BlastKind.None) return false;
            Reach(kind, out float least, out float most);
            float r = Mathf.Clamp(radius, least, most);
            if (!particles.OnScreen(at, r * 3f + 4f)) return false;
            particles.Seed(seed != 0 ? seed : ++nextKey * 7919);
            float n = particles.Nearness(at) * share;
            float g = particles.Ground(at.x, at.z);
            var floor = new Vector3(at.x, g, at.z);
            var up = Vector3.up;
            int key = -2000000 - (seed != 0 ? seed : nextKey);
            bool onWater = (flags & BlastFlags.Water) != 0;
            // A small shot that struck a unit bursts on it and leaves the ground be.
            bool onUnit = (flags & BlastFlags.DirectHit) != 0;
            var P = particles;

            if (onWater && kind != BlastKind.Lightning && kind != BlastKind.Holy && kind != BlastKind.Dark && kind != BlastKind.Wind)
            {
                Splash(at, floor, r, n, kind == BlastKind.Fire || kind == BlastKind.Gunpowder, key);
                if (holy) Divine(floor, r, n);
                return true;
            }

            switch (kind)
            {
                case BlastKind.Gunpowder:
                {
                    P.Glow(at + up * (0.4f + 0.3f * r), 2.6f * r, C(255, 232, 190), 0.12f, 2.2f);
                    Light(at + up * (1f + 0.5f * r), new Color(1f, 0.85f, 0.6f), 2f * r, 0.4f, 0.9f, key, false);
                    int body = Count(3 + 2f * r, n);
                    for (int i = 0; i < body; i++)
                    {
                        var o = Random(0.45f * r);
                        P.Smoke(at + o, (o * 2.2f + up * Random01(1.2f, 2.6f) * r), 0.55f * r + 0.3f, 1.6f, C(96, 88, 80, 200), Random01(0.55f, 1f), 0.8f);
                    }
                    P.Sparks(at + up * 0.3f, up, Count(8 + 4 * r, n), 7f + 2f * r, C(255, 196, 120));
                    P.Embers(at, Count(3 + r, n), 0.6f * r, 2.5f, C(255, 150, 60));
                    if (onUnit) break;
                    shock.Add(floor, 2.6f * r, 0.25f * r + 0.25f, 0.16f, C(255, 240, 220, 50), 1f, P.Ground);
                    P.Dust(floor, 1.1f * r, Count(8 + 3 * r, n), C(152, 132, 106, 150));
                    P.Debris(floor, up, Count(6 + 5 * r, n), 4f + 2.2f * r, 0.07f + 0.035f * r, DebrisLook.Dirt, 10f);
                    if (wood) P.Debris(floor, up, Count(4 + 2 * r, n), 5f + 1.5f * r, 0.12f + 0.03f * r, DebrisLook.Wood, 10f);
                    P.Flakes(floor, Count(5 + 3 * r, n), r, C(72, 58, 44), 1.6f);
                    P.Column(floor + up * (0.4f * r), 15f + Mathf.Min(5f, 2f * r), 0.8f * r + 0.6f, C(178, 172, 162, 120), 0.05f);
                    if (r >= 2.4f) Jolt(at, 0.035f * r, 0.4f);
                    break;
                }
                case BlastKind.Siege:
                {
                    int burst = Count(5 + 2.5f * r, n);
                    for (int i = 0; i < burst; i++)
                    {
                        var o = Random(0.5f * r);
                        o.y = Mathf.Abs(o.y) * 0.4f;
                        P.Smoke(floor + o + up * 0.3f, o * 1.8f + up * Random01(0.6f, 2f) * r, 0.6f * r + 0.3f, 2.6f, C(156, 134, 106, 170), 0f, 0.3f);
                    }
                    P.Dust(floor, 1.3f * r, Count(10 + 4 * r, n), C(164, 144, 116, 160), 3f);
                    P.Debris(floor, -direction, Count(8 + 5 * r, n), 3.5f + 1.5f * r, 0.06f + 0.03f * r, DebrisLook.Stone, 12f);
                    // The stone itself lands, bounces and stays.
                    P.Debris(floor + up * 0.4f, direction, 1, 2.5f, 0.32f + 0.08f * r, DebrisLook.Stone, 40f, true);
                    P.Column(floor, 4f, r, C(170, 152, 124, 90));
                    if (r >= 2.4f) Jolt(at, 0.03f * r, 0.35f);
                    break;
                }
                case BlastKind.Arrow:
                {
                    for (int i = 0; i < Count(onUnit ? 0 : 2, n); i++)
                        P.Smoke(floor + Random(0.15f) + up * 0.15f, Random(0.6f) + up * 0.4f, 0.35f, 0.9f, C(156, 138, 112, 130), 0f, 0.2f);
                    P.Flakes(floor, Count(3, n), 0.4f, C(84, 66, 48), 0.9f);
                    if ((flags & BlastFlags.FireStarter) != 0)
                    {
                        P.Smoke(at + up * 0.2f, up * 0.8f, 0.4f, 0.8f, C(80, 72, 66, 180), 0.9f, 0.6f);
                        P.Embers(at, Count(5, n), 0.25f, 1.6f, C(255, 140, 50));
                        Light(at + up * 0.6f, new Color(1f, 0.55f, 0.2f), 2f, 0.5f, 0.6f, key, true);
                    }
                    break;
                }
                case BlastKind.Fire:
                {
                    P.Glow(at + up * 0.5f * r, 2f * r, C(255, 190, 110), 0.16f, 1.8f);
                    Light(at + up * (0.8f + 0.5f * r), new Color(1f, 0.55f, 0.2f), 2.2f * r, 1.3f, 0.9f, key, true);
                    int body = Count(6 + 3 * r, n);
                    for (int i = 0; i < body; i++)
                    {
                        var o = Random(0.55f * r);
                        P.Smoke(at + o, o * Random01(1.2f, 2.6f) + up * Random01(0.8f, 2.6f), 0.7f * r + 0.25f, 2f, C(74, 66, 60, 210), Random01(0.65f, 1f), 1.4f);
                    }
                    P.Embers(at, Count(10 + 6 * r, n), 0.8f * r, 4.5f, C(255, 140, 50));
                    P.Sparks(at, up, Count(6 + 2 * r, n), 5f + r, C(255, 170, 80));
                    P.Dust(floor, 0.8f * r, Count(5 + 2 * r, n), C(132, 116, 98, 120));
                    P.Debris(floor, up, Count(3 + r, n), 3f + r, 0.07f + 0.02f * r, DebrisLook.Charred, 8f);
                    P.Column(floor + up * 0.5f * r, 10f, 0.7f * r + 0.5f, C(72, 66, 62, 150), 0.3f);
                    break;
                }
                case BlastKind.Breath:
                {
                    Light(at + up * 0.8f, new Color(1f, 0.55f, 0.2f), 2.5f * r, 0.5f, 0.8f, key, true);
                    var along = direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.zero;
                    int body = Count(3 + 2 * r, n);
                    for (int i = 0; i < body; i++)
                        P.Smoke(at + Random(0.4f * r), along * Random01(1f, 3f) + up * Random01(0.8f, 1.8f), 0.45f * r + 0.2f, 1.1f, C(84, 76, 70, 190), Random01(0.7f, 1f), 1.2f);
                    P.Embers(at, Count(6 + 3 * r, n), 0.5f * r, 3f, C(255, 150, 60));
                    P.Smoke(floor + up * 0.5f, up, 0.8f * r, 3f, C(104, 98, 92, 110), 0.15f, 0.8f);
                    break;
                }
                case BlastKind.Lightning:
                {
                    // The field lights blue-white for two of the original's frames.
                    Light(at + up * 4f, new Color(0.72f, 0.82f, 1f), Mathf.Max(9f, 6f * r), 0.1f, 1.4f, key, false, 0.67f);
                    P.Glow(at + up * 0.3f, 2.2f * r, C(200, 222, 255), 0.1f, 3f);
                    Branches(floor, r, n);
                    P.Sparks(at + up * 0.2f, up, Count(12 + 6 * r, n), 8f, C(190, 215, 255));
                    shock.Add(floor, 2.2f * r, 0.2f * r + 0.2f, 0.2f, C(160, 200, 255, 90), 1.2f, P.Ground);
                    P.Smoke(floor + up * 0.3f, up * 0.6f, 0.6f * r, 2f, C(126, 128, 136, 100), 0f, 0.5f);
                    break;
                }
                case BlastKind.Water:
                {
                    Splash(at, floor, r, n, false, key);
                    break;
                }
                case BlastKind.Frost:
                {
                    P.Glow(at + up * 0.4f, 1.6f * r, C(220, 240, 255), 0.12f, 1.8f);
                    Light(at + up * 1.2f, new Color(0.7f, 0.85f, 1f), 1.6f * r, 0.3f, 0.6f, key, false);
                    P.Debris(floor, up, Count(6 + 4 * r, n), 3f + r, 0.06f + 0.03f * r, DebrisLook.Ice, 6f);
                    P.Dust(floor, 1.2f * r, Count(8 + 4 * r, n), C(225, 238, 250, 120), 3f);
                    P.Embers(at, Count(6 + 3 * r, n), 0.6f * r, 1.5f, C(200, 230, 255));
                    shock.Add(floor, 1.8f * r, 0.25f * r + 0.2f, 0.3f, C(210, 235, 255, 60), 1f, P.Ground);
                    break;
                }
                case BlastKind.Earth:
                {
                    P.Dust(floor, 1.6f * r, Count(20 + 6 * r, n), C(156, 136, 110, 170), 3.5f);
                    P.Debris(floor, up, Count(8 + 3 * r, n), 4f + r, 0.12f + 0.03f * r, DebrisLook.Stone, 14f);
                    P.Debris(floor, up, Count(6 + 2 * r, n), 3.5f + r, 0.08f + 0.02f * r, DebrisLook.Dirt, 12f);
                    // Dust bursts up along a few cracks running out from it.
                    for (int c = 0; c < 4; c++)
                    {
                        float a = c * Mathf.PI * 0.5f + seed % 7 * 0.3f;
                        var way = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        for (int s = 1; s <= Count(4, n); s++)
                        {
                            var p = floor + way * (r * 0.35f * s);
                            p.y = P.Ground(p.x, p.z) + 0.2f;
                            P.Smoke(p, up * Random01(1f, 2.4f), 0.5f + 0.15f * r, 2.4f, C(146, 126, 100, 170), 0f, 0.3f);
                        }
                    }
                    float strength = weapon != null && weapon.Shake > 0f ? weapon.Shake * 0.12f : 0.25f;
                    float seconds = weapon != null && weapon.ShakeSeconds > 0f ? weapon.ShakeSeconds : 1.5f;
                    Jolt(at, strength, seconds);
                    break;
                }
                case BlastKind.Wind:
                {
                    P.Whirl(floor, Mathf.Max(1.2f, r * 1.5f), Count(20 + 6 * r, n), C(150, 134, 108, 160), 2.8f, true);
                    P.Dust(floor, r, Count(6 + 2 * r, n), C(166, 152, 128, 100));
                    break;
                }
                case BlastKind.Dark:
                {
                    int body = Count(8 + 3 * r, n);
                    for (int i = 0; i < body; i++)
                    {
                        var o = Random(0.6f * r);
                        o.y = Mathf.Abs(o.y) * 0.3f;
                        P.Smoke(floor + o + up * 0.3f, o * 0.6f + up * Random01(0.2f, 0.8f), 0.8f * r, 3f, C(70, 40, 92, 190), 0.35f, 0.3f, true);
                    }
                    P.Embers(at, Count(8 + 3 * r, n), 0.8f * r, 3f, C(170, 80, 255));
                    shock.Add(floor, 1.8f * r, 0.4f * r, 0.6f, C(150, 70, 220, 80), 1f, P.Ground);
                    Light(at + up * 1.2f, new Color(0.55f, 0.25f, 0.9f), 1.8f * r, 0.6f, 0.5f, key, true);
                    P.Column(floor, 6f, 0.7f * r, C(62, 38, 76, 140), 0.15f);
                    break;
                }
                case BlastKind.Holy:
                {
                    Pillar(floor, r, key);
                    break;
                }
            }
            if (holy && kind != BlastKind.Holy) Divine(floor, r, n);
            return true;
        }

        // Spray thrown up, falling back, and mist rolling out; steam for fire.
        void Splash(Vector3 at, Vector3 floor, float r, float n, bool steam, int key)
        {
            var P = particles;
            var surface = new Vector3(at.x, Mathf.Max(floor.y, at.y - 0.2f), at.z);
            P.Spray(surface, Count(30 + 10 * r, n), 2.5f + 1.2f * r, C(214, 230, 242, 220), 0.6f + 0.3f * r);
            // The column itself: white water thrown straight up, falling back as mist.
            int column = Count(4 + 2 * r, n);
            for (int i = 0; i < column; i++)
                P.Spray(surface + Random(0.2f * r), 1, 3f + 1.6f * r, C(232, 240, 248, 200), 0.3f, 0.35f + 0.12f * r);
            int mist = Count(5 + 2 * r, n);
            for (int i = 0; i < mist; i++)
                P.Smoke(surface + Random(0.4f * r), Random(0.8f) + Vector3.up * 0.6f, 0.8f * r, 2f, steam ? C(232, 234, 236, 170) : C(220, 232, 242, 140), 0f, 0.4f);
            P.Dust(surface, 1.2f * r, Count(8 + 3 * r, n), C(214, 228, 238, 120), 1.6f);
            shock.Add(surface, 2f * r, 0.3f * r + 0.2f, 0.5f, C(180, 215, 240, 50), 1f, P.Ground);
            Light(surface + Vector3.up, new Color(0.6f, 0.8f, 1f), 1.5f * r, 0.25f, 0.35f, key, false);
        }

        // Forked bolts running out over the ground for a blink.
        void Branches(Vector3 floor, float r, float n)
        {
            int forks = Count(4 + r, n);
            for (int f = 0; f < forks; f++)
            {
                float a = f * Mathf.PI * 2f / Mathf.Max(1, forks) + Random01(-0.4f, 0.4f);
                var way = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var p = floor + Vector3.up * 0.15f;
                int steps = 3 + (int)Random01(0f, 2f);
                for (int s = 0; s < steps; s++)
                {
                    var side = new Vector3(-way.z, 0f, way.x) * Random01(-0.5f, 0.5f);
                    var q = p + (way + side) * (r * Random01(0.4f, 0.8f));
                    q.y = particles.Ground(q.x, q.z) + 0.15f;
                    particles.Streak(p, q, 0.1f, C(190, 215, 255), 0.18f, 2.5f);
                    p = q;
                }
            }
        }

        // A golden pillar rising, a ring of light that lingers, motes, no scorch.
        void Pillar(Vector3 floor, float r, int key)
        {
            var P = particles;
            var top = floor + Vector3.up * (8f + 2f * r);
            P.Streak(floor, top, 1.2f + 0.35f * r, C(255, 214, 130), 1.3f, 2.2f);
            P.Streak(floor, top, 0.5f + 0.1f * r, C(255, 246, 214), 1f, 3f);
            P.Glow(floor + Vector3.up, 2.5f * r, C(255, 226, 150), 0.45f, 2.5f);
            shock.Add(floor, 2.2f * r, 0.8f * r, 3f, C(255, 214, 130, 140), 1.3f, P.Ground);
            P.Embers(floor + Vector3.up * 0.5f, Count(10 + 4 * r, 1f), r, 3f, C(255, 220, 140));
            Light(floor + Vector3.up * 2f, new Color(1f, 0.85f, 0.5f), 2.5f * r, 1.2f, 0.9f, key, false);
        }

        // A divine caster's elemental spell carries a little holy light.
        void Divine(Vector3 floor, float r, float n)
        {
            particles.Embers(floor + Vector3.up * 0.5f, Count(6 + 2 * r, n), r, 2.5f, C(255, 220, 140));
            shock.Add(floor, 1.6f * r, 0.6f * r, 1.6f, C(255, 214, 130, 70), 1f, particles.Ground);
        }

        // ── Lights and shake ──────────────────────────────────────────

        // hold is the share of its life a light stays at full strength.
        void Light(Vector3 at, Color colour, float reach, float seconds, float strength, int key, bool flicker, float hold = 0f)
        {
            if (heldCount >= held.Length) return;
            held[heldCount++] = new Held { At = at, Colour = colour, Reach = reach, Life = seconds, Strength = strength, Key = key, Flicker = flicker, Hold = hold };
        }

        void Jolt(Vector3 at, float strength, float seconds)
        {
            if (joltCount >= jolts.Length) { System.Array.Copy(jolts, 1, jolts, 0, jolts.Length - 1); joltCount--; }
            jolts[joltCount++] = new Quake { At = at, Strength = strength, Life = seconds };
        }

        // The camera thrown about by every jolt near what it looks at.
        void StepShake(float dt, Vector3 focus)
        {
            shakeClock += dt;
            float amount = 0f;
            for (int i = joltCount - 1; i >= 0; i--)
            {
                ref var j = ref jolts[i];
                j.Age += dt;
                if (j.Age >= j.Life) { jolts[i] = jolts[--joltCount]; continue; }
                float t = j.Age / j.Life;
                float near = Mathf.Clamp01(1f - (Vector3.Distance(j.At, focus) - 12f) / 40f);
                amount += j.Strength * (1f - t) * (1f - t) * near;
            }
            amount = Mathf.Min(amount, 0.6f);
            if (amount <= 1e-4f) { Shake = Vector3.zero; return; }
            float s = shakeClock * 24f;
            Shake = new Vector3(Mathf.PerlinNoise(s, 0.3f) - 0.5f, (Mathf.PerlinNoise(0.7f, s) - 0.5f) * 0.6f, Mathf.PerlinNoise(s + 5.1f, 3.3f) - 0.5f) * (2f * amount);
        }

        // ── Numbers ───────────────────────────────────────────────────

        static int Count(float count, float share) => Mathf.Max(0, Mathf.RoundToInt(count * share));

        uint rng = 0x2545F491u;

        float Random01(float a, float b)
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return a + (b - a) * ((rng & 0xFFFFFF) / 16777216f);
        }

        Vector3 Random(float r) => new Vector3(Random01(-r, r), Random01(-r, r), Random01(-r, r));
    }
}
