// FxMagic.cs - what each kind of magic leaves on the scenery it reaches,
// past its blast and its mark on the ground. Frost rimes trees and rocks and
// melts over a minute. Dark magic withers what grows, its leaves greying and
// some falling. Lightning chars and splits the tree it strikes, and smoke
// curls from the split. Holy light leaves a golden sheen and heals what dark
// magic withered. Earth shakes trees and shakes loose rubble. Water wets
// what it reaches and puts fires out in steam. Wind sways trees, strips
// their leaves and fans fires. Fire scorches what will not burn. Read from
// the backend's blasts, drawn through the model shader's per-instance marks.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FxMagic
    {
        readonly IGameBackend backend;
        readonly EntityRenderer entities;
        readonly FxFire fire;
        readonly BlastEvent[] read = new BlastEvent[128];
        readonly Dictionary<WeaponInfo, BlastKind> kinds = new Dictionary<WeaponInfo, BlastKind>();
        readonly Dictionary<WeaponInfo, bool> divine = new Dictionary<WeaponInfo, bool>();
        readonly List<SceneryLook> near = new List<SceneryLook>(128);
        readonly List<SceneryLook> moving = new List<SceneryLook>();
        readonly HashSet<SceneryLook> movingSet = new HashSet<SceneryLook>();
        int since = -1;
        float now;
        uint rng = 0x1B873593u;

        // Leaves that fall a while after the withering takes them.
        struct Later
        {
            public float At;
            public SceneryLook Look;
        }

        readonly List<Later> later = new List<Later>();

        // True for a point the player cannot see.
        public System.Func<Vector3, int, bool> Hidden;
        // Scenery marked by each kind since the start, by BlastKind, and the looks still changing.
        public readonly int[] Marked = new int[16];
        public int Moving => moving.Count;
        public double Ms { get; private set; }

        public FxMagic(IGameBackend backend, EntityRenderer entities, FxFire fire)
        {
            this.backend = backend;
            this.entities = entities;
            this.fire = fire;
        }

        public void Clear()
        {
            moving.Clear();
            movingSet.Clear();
            later.Clear();
        }

        // Reads the frame's blasts, marks what each reaches, and steps the marks still changing.
        public void Update(float now, float dt)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            this.now = now;
            ReadBlasts();
            var P = FxParticles.Active;
            for (int i = later.Count - 1; i >= 0; i--)
            {
                var l = later[i];
                if (now < l.At) continue;
                later.RemoveAt(i);
                if (P == null || l.Look.Gone || !Shows(P, l.Look.Middle, l.Look.Width)) continue;
                P.Flakes(l.Look.Middle, Mathf.RoundToInt(4 + 2 * l.Look.Width), 0.5f * l.Look.Width, new Color32(92, 82, 74, 255), 4f);
            }
            for (int i = moving.Count - 1; i >= 0; i--)
            {
                var look = moving[i];
                if (!look.Gone && look.Step(now)) continue;
                moving[i] = moving[moving.Count - 1];
                moving.RemoveAt(moving.Count - 1);
                movingSet.Remove(look);
            }
            Ms = clock.Elapsed.TotalMilliseconds;
        }

        void ReadBlasts()
        {
            int tps = Mathf.Max(1, backend.TicksPerSecond);
            if (since < 0) since = 0;
            int n;
            do
            {
                n = backend.ReadBlasts(since, read);
                for (int i = 0; i < n; i++)
                {
                    var b = read[i];
                    since = Mathf.Max(since, b.Id);
                    // One read long after it burst, by a world built mid-battle, has had its moment.
                    if (backend.Tick > b.Tick + (uint)(tps * 2)) continue;
                    if ((b.Flags & (BlastFlags.Water | BlastFlags.DirectHit)) != 0) continue;
                    var kind = KindOf(b, out bool holy);
                    if (kind == BlastKind.None) continue;
                    Apply(kind, b.Position, b.Radius, b.Direction, holy, b.Id);
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
                var defs = backend.UnitDefs;
                string caster = w.Def >= 0 && w.Def < defs.Count ? defs[w.Def].Name : b.Def >= 0 && b.Def < defs.Count ? defs[b.Def].Name : null;
                kinds[w] = kind = FxKinds.Of(w, caster);
                string first = w.Name ?? "";
                int space = first.IndexOf(' ');
                divine[w] = FxKinds.IsDivine(caster) || FxKinds.IsDivine(space > 0 ? first.Substring(0, space) : first);
            }
            holy = divine[w];
            return kind;
        }

        // How far each kind's marks reach for a blast of a radius, world units.
        public static float ReachOf(BlastKind kind, float radius)
        {
            switch (kind)
            {
                case BlastKind.Frost: return Mathf.Max(1.5f, radius * 1.2f);
                case BlastKind.Dark: return Mathf.Max(2f, radius * 1.5f);
                case BlastKind.Lightning: return Mathf.Max(0.8f, radius);
                case BlastKind.Holy: return Mathf.Max(1.5f, radius * 1.3f);
                case BlastKind.Earth: return Mathf.Clamp(radius * 1.2f, 3f, 12f);
                case BlastKind.Water: return Mathf.Max(1.5f, radius * 1.3f);
                case BlastKind.Wind: return Mathf.Max(2f, radius * 1.5f);
                case BlastKind.Fire: case BlastKind.Breath: return Mathf.Max(0.8f, radius * 1.1f);
                default: return 0f;
            }
        }

        // Marks what a blast of a kind reaches. Returns how many it marked.
        public int Apply(BlastKind kind, Vector3 at, float radius, Vector3 direction, bool holy = false, int seed = 0)
        {
            float reach = ReachOf(kind, radius);
            if (holy && kind != BlastKind.Holy) Divine(at, Mathf.Max(1.5f, radius), seed);
            if (reach <= 0f || entities == null) return 0;
            var P = FxParticles.Active;
            near.Clear();
            int touched = 0;
            switch (kind)
            {
                case BlastKind.Lightning:
                {
                    // The bolt takes the tallest thing where it lands.
                    var struck = entities.LookNear(at, reach);
                    if (struck != null) touched += Strike(struck, direction, seed, P);
                    entities.LooksNear(at, Mathf.Max(radius, 0.5f), near);
                    foreach (var l in near)
                    {
                        if (l == struck) continue;
                        l.Char = Mathf.Max(l.Char, 0.25f);
                        Glow(l, 0.5f, 4f);
                        touched++;
                    }
                    break;
                }
                case BlastKind.Water:
                    fire?.Douse(at, reach);
                    goto default;
                default:
                    entities.LooksNear(at, reach, near);
                    foreach (var l in near)
                    {
                        float d = Mathf.Clamp01(Distance(l, at) / reach);
                        if (Touch(kind, l, at, d, direction, seed, P)) touched++;
                    }
                    break;
            }
            if (kind == BlastKind.Earth)
                entities.Debris.Kick(at, reach, 4.5f, seed);
            if (kind == BlastKind.Wind)
            {
                var way = direction.sqrMagnitude > 1e-4f ? direction.normalized : (fire != null && P != null ? P.Wind.normalized : Vector3.right);
                fire?.Fan(at, reach, way);
            }
            if ((int)kind < Marked.Length) Marked[(int)kind] += touched;
            return touched;
        }

        static float Distance(SceneryLook l, Vector3 at)
        {
            float dx = l.Foot.x - at.x, dz = l.Foot.z - at.z;
            return Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) - l.Width * 0.4f);
        }

        bool Shows(FxParticles P, Vector3 at, float size) => P != null && P.OnScreen(at, size + 3f) && (Hidden == null || !Hidden(at, -1));

        void Move(SceneryLook l)
        {
            l.Step(now);
            if (movingSet.Add(l)) moving.Add(l);
        }

        void Glow(SceneryLook l, float peak, float seconds)
        {
            l.GlowPeak = Mathf.Max(l.Glow, peak);
            l.GlowAt = now;
            l.GlowFor = seconds;
            Move(l);
        }

        // One piece of scenery a kind of magic reaches, d from 0 at the
        // blast's heart to 1 at its reach.
        bool Touch(BlastKind kind, SceneryLook l, Vector3 at, float d, Vector3 direction, int seed, FxParticles P)
        {
            float w = l.Width, h = l.Height;
            var mid = l.Middle;
            switch (kind)
            {
                case BlastKind.Frost:
                {
                    if (l.FireGlow > 0f)
                    {
                        // A burning tree only steams.
                        if (Shows(P, mid, w)) P.Smoke(mid, Vector3.up * 1.5f, 0.7f * w, 2.5f, new Color32(236, 238, 242, 180), 0f, 0.8f);
                        return false;
                    }
                    l.FrostPeak = Mathf.Max(l.Frost, 1f - 0.45f * d * d);
                    l.FrostAt = now;
                    l.WetPeak = 0f;
                    Move(l);
                    if (Shows(P, mid, w))
                    {
                        // Cold mist round its foot, and glints of ice in it.
                        for (int i = 0; i < 2; i++)
                            P.Smoke(l.Foot + new Vector3(R(-0.5f, 0.5f) * w, 0.2f, R(-0.5f, 0.5f) * w), new Vector3(R(-0.3f, 0.3f), 0.15f, R(-0.3f, 0.3f)),
                                0.6f * w + 0.3f, 3f, new Color32(226, 236, 246, 120), 0f, 0.1f);
                        P.Glow(mid + new Vector3(R(-0.3f, 0.3f) * w, R(-0.2f, 0.3f) * h, R(-0.3f, 0.3f) * w), 0.5f, new Color32(220, 236, 255, 255), 0.25f, 1.4f);
                    }
                    return true;
                }
                case BlastKind.Dark:
                {
                    float to = l.Plant ? 1f - 0.35f * d : 0.3f * (1f - d);
                    if (to <= l.WitherTo + 0.02f) return false;
                    l.WitherTo = to;
                    l.WitherAt = now;
                    if (l.Plant && l.Kind == BreakKind.Tree) l.Bare = Mathf.Max(l.Bare, 0.35f * to);
                    Move(l);
                    if (l.Plant) later.Add(new Later { At = now + R(1.2f, 2.2f), Look = l });
                    if (Shows(P, l.Foot, w)) P.Smoke(l.Foot + Vector3.up * 0.3f, Vector3.up * 0.4f, 0.6f * w + 0.3f, 2.5f, new Color32(70, 40, 92, 110), 0.25f, 0.3f, true);
                    return true;
                }
                case BlastKind.Holy:
                {
                    l.SheenPeak = Mathf.Max(l.Sheen, 1f - 0.5f * d);
                    l.SheenAt = now;
                    // Holy light heals what dark magic withered.
                    if (l.WitherTo > 0f) { l.WitherTo *= 0.4f; l.WitherAt = float.NegativeInfinity; }
                    Move(l);
                    if (Shows(P, mid, w)) P.Embers(mid, 3, 0.5f * w, 2.5f, new Color32(255, 220, 140, 255));
                    return true;
                }
                case BlastKind.Earth:
                {
                    float amp = l.Plant ? 0.16f * h : l.Kind == BreakKind.Rock || l.Kind == BreakKind.Body ? 0f : 0.03f * h;
                    amp *= 1f - 0.6f * d;
                    if (amp > l.ShakeAmp * Mathf.Exp(-(now - l.ShakeAt) / SceneryLook.ShakeSeconds))
                    {
                        var away = new Vector2(l.Foot.x - at.x, l.Foot.z - at.z);
                        if (away.sqrMagnitude < 1e-4f) { float a = R(0f, Mathf.PI * 2f); away = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); }
                        l.ShakeAmp = amp;
                        l.ShakeAt = now;
                        l.ShakeWay = away.normalized;
                        Move(l);
                    }
                    if (Shows(P, l.Foot, w))
                    {
                        if (l.Loose)
                        {
                            // Rubble shaken loose tumbles off it.
                            P.Debris(l.Foot + Vector3.up * 0.2f, Vector3.up, 4, 3.5f, 0.1f + 0.05f * w, DebrisLook.Stone, 10f);
                            P.Dust(l.Foot, 0.6f * w + 0.4f, 6, new Color32(160, 146, 124, 150));
                        }
                        else if (l.Plant && l.Kind == BreakKind.Tree) P.Flakes(mid, 4, 0.4f * w, new Color32(78, 104, 48, 255), 3f);
                        else if (amp > 0f) P.Dust(l.Foot, 0.5f * w + 0.3f, 3, new Color32(160, 146, 124, 120));
                    }
                    return amp > 0f || l.Loose;
                }
                case BlastKind.Water:
                {
                    l.WetPeak = Mathf.Max(l.Wet, 1f - 0.5f * d);
                    l.WetAt = now;
                    l.FrostPeak = 0f;
                    Move(l);
                    if (Shows(P, mid, w)) P.Spray(mid + Vector3.up * 0.3f * h, 6, 0.3f, new Color32(214, 230, 242, 200), 0.5f * w);
                    return true;
                }
                case BlastKind.Wind:
                {
                    if (!l.Plant) return false;
                    // Pushed round the vortex and a little out from it.
                    var off = new Vector2(l.Foot.x - at.x, l.Foot.z - at.z);
                    if (off.sqrMagnitude < 1e-4f) off = new Vector2(1f, 0f);
                    off.Normalize();
                    var way = (new Vector2(-off.y, off.x) * 0.8f + off * 0.45f).normalized;
                    l.GustAmp = Mathf.Max(l.GustAmp * Mathf.Exp(-(now - l.GustAt) / SceneryLook.GustSeconds), 0.2f * h * (1f - 0.5f * d));
                    l.GustAt = now;
                    l.GustWay = way;
                    if (l.Kind == BreakKind.Tree) l.Bare = Mathf.Min(0.6f, Mathf.Max(l.Bare, l.Bare + 0.12f * (1f - d)));
                    Move(l);
                    if (Shows(P, mid, w)) P.Flakes(mid, 6, 0.5f * w, new Color32(78, 104, 48, 255), 2.5f);
                    return true;
                }
                case BlastKind.Fire:
                case BlastKind.Breath:
                {
                    // What will not burn is scorched, and frost and wet steam off it.
                    if (l.FireGlow > 0f) return false;
                    if (l.Frost > 0f || l.Wet > 0f)
                    {
                        l.FrostPeak = l.WetPeak = 0f;
                        if (Shows(P, mid, w)) P.Smoke(mid, Vector3.up * 1.2f, 0.7f * w, 2.5f, new Color32(236, 238, 242, 170), 0f, 0.8f);
                    }
                    l.Char = Mathf.Max(l.Char, 0.3f * (1f - d) + 0.05f);
                    Glow(l, 0.5f * (1f - d), 4f);
                    return true;
                }
            }
            return false;
        }

        // A bolt's strike: a tree splits down its trunk, charred, with smoke
        // curling out of it; anything else is scorched.
        int Strike(SceneryLook l, Vector3 direction, int seed, FxParticles P)
        {
            l.Char = Mathf.Max(l.Char, l.Kind == BreakKind.Tree ? 0.6f : 0.35f);
            Glow(l, 0.6f, 6f);
            var mid = l.Middle;
            if (l.Kind == BreakKind.Tree && l.Split <= 0f)
            {
                // Split across the way the bolt came, or any way for a bolt from straight above.
                var flat = new Vector2(direction.x, direction.z);
                float angle = flat.sqrMagnitude > 1e-4f ? Mathf.Atan2(flat.y, flat.x) + Mathf.PI * 0.5f : Hash01(seed) * Mathf.PI;
                l.Split = Mathf.Clamp(0.06f + 0.09f * l.Width, 0.08f, 0.5f);
                l.SplitAngle = angle;
                l.Bare = Mathf.Max(l.Bare, 0.3f);
                fire?.Wisp(l.Foot + Vector3.up * l.Height * 0.6f, 10f, 0.4f * l.Width);
            }
            if (Shows(P, mid, l.Width))
            {
                P.Sparks(l.Foot + Vector3.up * l.Height * 0.8f, Vector3.up, 16, 6f, new Color32(200, 222, 255, 255));
                P.Debris(l.Foot + Vector3.up * l.Height * 0.4f, Vector3.up, 4, 4f, 0.08f, DebrisLook.Charred, 8f);
            }
            return 1;
        }

        // A divine caster's elemental spell leaves a little of its light too.
        void Divine(Vector3 at, float reach, int seed)
        {
            if (entities == null) return;
            near.Clear();
            entities.LooksNear(at, reach, near);
            foreach (var l in near)
            {
                l.SheenPeak = Mathf.Max(l.Sheen, 0.5f);
                l.SheenAt = now;
                Move(l);
            }
        }

        float R(float a, float b)
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return a + (b - a) * ((rng & 0xFFFFFF) / 16777216f);
        }

        static float Hash01(int seed)
        {
            uint h = (uint)seed * 2654435761u;
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
}
