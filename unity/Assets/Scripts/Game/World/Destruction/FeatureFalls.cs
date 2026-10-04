// FeatureFalls.cs - how each kind of scenery breaks when the engine says it
// is dying. A tree loses its crown and then topples away from the blow, a
// wall crumbles from the top to its half stage and then to rubble, a hut or
// building caves in, a body shatters, and smaller things break apart. What
// the next stage keeps waits in place and fades into it at the swap.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FeatureFalls
    {
        readonly IGameBackend backend;
        readonly FractureCache cache;
        readonly Debris debris;
        readonly DustPuffs dust;
        uint seed;
        Mesh chip;

        // The share of a tree's height under its crown, and of a building's under its roof.
        public const float CrownLine = 0.42f, RoofLine = 0.62f;

        // The last break, for tests: its kind, how many chunks flew and
        // waited for the swap, and the lowest that flew and highest that
        // waited, in the kind's space.
        public BreakKind LastKind;
        public int LastFlying, LastHeld;
        public float LastLowestFlying, LastHighestHeld;

        public FeatureFalls(IGameBackend backend, FractureCache cache, Debris debris, DustPuffs dust)
        {
            this.backend = backend;
            this.cache = cache;
            this.debris = debris;
            this.dust = dust;
        }

        float Rand() => Fracture.Rand01(ref seed);
        Vector3 Jitter(float s) => new Vector3(Rand() - 0.5f, Rand() - 0.5f, Rand() - 0.5f) * (2f * s);
        Vector3 Spin(float lo, float hi) => Jitter(1f).normalized * Mathf.Lerp(lo, hi, Rand());

        FeatureDef Def(int def) => def >= 0 && def < backend.FeatureDefs.Count ? backend.FeatureDefs[def] : null;

        // The chunks to break a kind with: split if ready, its parts whole if not,
        // and none when the pool has no room.
        public ChunkSet SetFor(int def, out BreakKind kind, bool far = false)
        {
            var d = Def(def);
            kind = Fracture.KindOf(d);
            if (d == null || kind == BreakKind.None || kind == BreakKind.Rock) return null;
            var interior = Fracture.InteriorOf(d, kind);
            // Far off, a break shows its parts whole, fewer to draw.
            var set = far ? null : cache.Get(def);
            if (set == null)
            {
                cache.Ask(def, d.Name, kind, interior);
                set = cache.Whole(def, kind, interior);
            }
            if (set != null && !debris.Room(set.Count)) set = set.Whole ? null : cache.Whole(def, kind, interior);
            if (set != null && !debris.Room(set.Count)) set = null;
            return set;
        }

        // How tall the next stage stands against this one, or -1 when there is none.
        float NextShare(FeatureDef d, int next)
        {
            var n = Def(next);
            if (n == null) return -1f;
            if (d.Height > 0f && n.Height > 0f) return Mathf.Clamp01(n.Height / d.Height);
            return 0.5f;
        }

        // Starts a feature breaking. basis takes the kind's space to the world,
        // away is the way the blow pushes along the ground, seconds the death's
        // length. Returns the hold its remains wait on, 0 when nothing waits.
        public int Begin(int def, in Matrix4x4 basis, Vector3 at, Vector3 away, float seconds, out bool shown, bool far = false)
        {
            shown = false;
            var d = Def(def);
            var set = SetFor(def, out var kind, far);
            if (set == null) return 0;
            seed = Fracture.Seed(d.Name) ^ (uint)Mathf.RoundToInt(at.x * 131f) * 73856093u ^ (uint)Mathf.RoundToInt(at.z * 131f) * 19349663u;
            if (seed == 0) seed = 1;
            away.y = 0f;
            away = away.sqrMagnitude > 1e-6f ? away.normalized : new Vector3(Mathf.Sin(Rand() * 6.283f), 0f, Mathf.Cos(Rand() * 6.283f));
            seconds = Mathf.Clamp(seconds, 0.3f, 3f);
            shown = true;
            int hold = debris.NewHold();
            LastKind = kind;
            LastFlying = LastHeld = 0;
            LastLowestFlying = float.MaxValue;
            LastHighestHeld = float.MinValue;
            switch (kind)
            {
                case BreakKind.Tree:
                    if (KeepsTrunk(d)) Crown(set, basis, away, hold);
                    else Topple(set, basis, away);
                    break;
                case BreakKind.Wall: Crumble(set, basis, d, away, seconds, hold); break;
                case BreakKind.Hut:
                case BreakKind.Building: CaveIn(set, basis, d, away, seconds, hold); break;
                case BreakKind.Body: Shatter(set, basis, at - away * 0.5f); break;
                default: Scatter(set, basis, away); break;
            }
            Fines(set, basis, away, Mathf.RoundToInt((kind == BreakKind.Building || kind == BreakKind.Wall ? 10 : 5) * cache.CountScale));
            return hold;
        }

        // A tree whose dead stage still stands as a tree keeps its trunk, and a dead tree falls.
        bool KeepsTrunk(FeatureDef d)
        {
            var n = Def(d.DeadDef);
            if (n == null || Fracture.KindOf(n) != BreakKind.Tree) return false;
            return d.Height > 0f && n.Height > 0f ? n.Height >= d.Height * 0.5f : n.DeadDef >= 0;
        }

        void Spawn(ChunkSet set, int i, in Matrix4x4 basis, Vector3 v, Vector3 w, float start = 0f, int hold = 0, int group = -1)
        {
            if (debris.Spawn(set.Draws[i], basis * set.Place[i], set.Pivot[i], set.Support[i], v, w, start, hold, group) < 0) return;
            float y = set.Centre[i].y;
            if (hold != 0) { LastHeld++; LastHighestHeld = Mathf.Max(LastHighestHeld, y); }
            else { LastFlying++; LastLowestFlying = Mathf.Min(LastLowestFlying, y); }
        }

        static float Top(ChunkSet s) => s.Bounds.max.y;
        static float Bottom(ChunkSet s) => Mathf.Min(0f, s.Bounds.min.y);

        // The crown bursts away from the blow in leaves and branches, and the trunk stays.
        void Crown(ChunkSet set, in Matrix4x4 basis, Vector3 away, int hold)
        {
            float bottom = Bottom(set), line = bottom + CrownLine * (Top(set) - bottom);
            var crown = basis.MultiplyPoint3x4(new Vector3(set.Bounds.center.x, line, set.Bounds.center.z));
            for (int i = 0; i < set.Count; i++)
            {
                if (set.Centre[i].y < line) { Spawn(set, i, basis, Vector3.zero, Vector3.zero, 0f, hold); continue; }
                var c = basis.MultiplyPoint3x4(set.Centre[i]);
                var outward = c - crown;
                outward.y = 0f;
                var v = away * (1.5f + 2.5f * Rand()) + Vector3.up * (2f + 2.5f * Rand()) + outward.normalized * (1.2f + 1.8f * Rand()) + Jitter(1.2f);
                Spawn(set, i, basis, v, Spin(2f, 7f), 0.06f * Rand());
            }
            dust.Burst(crown + Vector3.up * 0.5f, 0.8f, new Color(0.33f, 0.42f, 0.2f, 0.5f), 5);
        }

        // The trunk snaps at its foot and swings down away from the blow,
        // breaking where it meets the ground.
        void Topple(ChunkSet set, in Matrix4x4 basis, Vector3 away)
        {
            float bottom = Bottom(set), height = Top(set) - bottom;
            var foot = basis.MultiplyPoint3x4(new Vector3(set.Bounds.center.x, bottom, set.Bounds.center.z));
            float girth = Mathf.Min(set.Bounds.extents.x, set.Bounds.extents.z) * 0.5f;
            int fall = debris.BeginFall(foot + away * girth, Vector3.Cross(Vector3.up, away), height, 0.35f + 0.3f * Rand());
            if (fall < 0) { Scatter(set, basis, away); return; }
            for (int i = 0; i < set.Count; i++) Spawn(set, i, basis, Vector3.zero, Vector3.zero, 0f, 0, fall);
        }

        // From the top down, chunks fall mostly straight with a little push to
        // either side, and what the next stage keeps stays. Into rubble, the
        // last stage, it all comes down.
        void Crumble(ChunkSet set, in Matrix4x4 basis, FeatureDef d, Vector3 away, float seconds, int hold)
        {
            float share = NextShare(d, d.DeadDef);
            var next = Def(d.DeadDef);
            bool down = share < 0.3f || next == null || Fracture.KindOf(next) != BreakKind.Wall || !next.Breakable;
            float bottom = Bottom(set), top = Top(set), cut = down ? bottom : bottom + share * (top - bottom);
            var size = set.Bounds.size;
            var across = basis.MultiplyVector(size.x >= size.z ? Vector3.forward : Vector3.right).normalized;
            float side = Vector3.Dot(across, away) >= 0f ? 1f : -1f;
            for (int i = 0; i < set.Count; i++)
            {
                float y = set.Centre[i].y;
                if (!down && y < cut) { Spawn(set, i, basis, Vector3.zero, Vector3.zero, 0f, hold); continue; }
                float from = Mathf.Clamp01((top - y) / Mathf.Max(0.01f, top - cut));
                float start = from * (down ? 0.4f : 0.55f) * seconds + 0.08f * Rand();
                float s = Rand() < 0.75f ? side : -side;
                var v = Vector3.down * (0.3f + Rand()) + across * s * ((down ? 1f : 0.5f) + 1.4f * Rand()) + away * 0.4f + Jitter(0.4f);
                Spawn(set, i, basis, v, Spin(1f, 4f), start);
            }
            var foot = basis.MultiplyPoint3x4(new Vector3(set.Bounds.center.x, bottom, set.Bounds.center.z));
            dust.Burst(foot, Mathf.Max(size.x, size.z) * 0.4f, DustPuffs.StoneDust, down ? 10 : 6);
        }

        // The roof falls in, the walls fall outward, and a cloud rolls out.
        void CaveIn(ChunkSet set, in Matrix4x4 basis, FeatureDef d, Vector3 away, float seconds, int hold)
        {
            float share = NextShare(d, d.DeadDef);
            float bottom = Bottom(set), top = Top(set), height = top - bottom;
            float cut = share < 0f ? bottom : bottom + Mathf.Min(share, 0.45f) * height, roof = bottom + RoofLine * height;
            var middle = basis.MultiplyPoint3x4(new Vector3(set.Bounds.center.x, bottom, set.Bounds.center.z));
            for (int i = 0; i < set.Count; i++)
            {
                float y = set.Centre[i].y;
                if (y < cut) { Spawn(set, i, basis, Vector3.zero, Vector3.zero, 0f, hold); continue; }
                var c = basis.MultiplyPoint3x4(set.Centre[i]);
                var outward = c - middle;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : away;
                if (y >= roof)
                {
                    var v = Vector3.down * (0.5f + Rand()) - outward * (0.3f + 0.6f * Rand()) + Jitter(0.3f);
                    Spawn(set, i, basis, v, Spin(1f, 3f), 0.1f * Rand() * seconds);
                }
                else
                {
                    float start = (0.2f + 0.35f * Mathf.Clamp01((roof - y) / Mathf.Max(0.01f, roof - cut))) * seconds;
                    var v = outward * (1f + 1.8f * Rand()) + Vector3.up * (0.5f * Rand()) + away * 0.4f;
                    var w = Vector3.Cross(Vector3.up, outward) * (2f + 2f * Rand()) + Jitter(0.5f);
                    Spawn(set, i, basis, v, w, start);
                }
            }
            var colour = set.Interior == Interior.Stone ? DustPuffs.StoneDust : DustPuffs.EarthDust;
            dust.Burst(middle, Mathf.Max(set.Bounds.extents.x, set.Bounds.extents.z), colour, 12);
        }

        // Every chunk bursts away from the blow.
        void Shatter(ChunkSet set, in Matrix4x4 basis, Vector3 from)
        {
            for (int i = 0; i < set.Count; i++)
            {
                var c = basis.MultiplyPoint3x4(set.Centre[i]);
                var outward = c - from;
                outward.y = 0f;
                var v = outward.normalized * (2.5f + 3f * Rand()) + Vector3.up * (2f + 3f * Rand()) + Jitter(1f);
                Spawn(set, i, basis, v, Spin(4f, 10f));
            }
            var colour = set.Interior == Interior.Ice ? new Color(0.85f, 0.92f, 1f, 0.5f) : DustPuffs.StoneDust;
            dust.Burst(basis.MultiplyPoint3x4(set.Bounds.center), set.Bounds.extents.magnitude * 0.6f, colour, 6);
        }

        // Smaller things break apart and scatter away from the blow.
        void Scatter(ChunkSet set, in Matrix4x4 basis, Vector3 away)
        {
            for (int i = 0; i < set.Count; i++)
            {
                var v = away * (1.2f + 2f * Rand()) + Vector3.up * (1.2f + 1.5f * Rand()) + Jitter(1f);
                Spawn(set, i, basis, v, Spin(2f, 6f), 0.1f * Rand());
            }
            dust.Burst(basis.MultiplyPoint3x4(set.Bounds.center), set.Bounds.extents.magnitude * 0.5f, DustPuffs.EarthDust, 4);
        }

        // A few chips knocked off by a blast that does not kill, the only mark a rock takes.
        public void Chip(Vector3 at, Vector3 from, float reach, Interior interior)
        {
            if (!debris.Room(3)) return;
            var draws = ChipDraws(interior);
            seed = (uint)Mathf.RoundToInt(at.x * 977f + at.z * 131f + debris.Now * 61f) | 1u;
            var out_ = at - from;
            out_.y = 0f;
            out_ = out_.sqrMagnitude > 1e-4f ? out_.normalized : Vector3.forward;
            for (int k = 0; k < 3; k++)
            {
                var p = at - out_ * reach * 0.5f + Vector3.up * (0.3f + Rand() * 0.6f);
                var v = (-out_ + Jitter(0.6f)) * (1.5f + 2f * Rand()) + Vector3.up * (1.5f + Rand() * 2f);
                float s = 0.08f + 0.08f * Rand();
                debris.Spawn(draws, Matrix4x4.TRS(p, Quaternion.Euler(Rand() * 360f, Rand() * 360f, 0f), Vector3.one * s), Vector3.zero, ChipCorners, v, Spin(5f, 12f),
                    0f, 0, -1, PieceExplode.None, false, true);
            }
        }

        readonly ChunkDraw[][] chipDraws = new ChunkDraw[4][];
        readonly List<Material> chipMats = new List<Material>();

        ChunkDraw[] ChipDraws(Interior interior)
        {
            if (chip == null) chip = ChipMesh();
            var draws = chipDraws[(int)interior];
            if (draws != null) return draws;
            var m = Looks.Model(null);
            m.color = Color.Lerp(Fracture.InteriorColour(interior), Color.white, 0.2f);
            chipMats.Add(m);
            return chipDraws[(int)interior] = new[] { new ChunkDraw { Mesh = chip, Submesh = 0, Material = m } };
        }

        // Splinters and grit thrown from a break, finer than its chunks.
        void Fines(ChunkSet set, in Matrix4x4 basis, Vector3 away, int n)
        {
            if (n <= 0 || !debris.Room(n)) return;
            var draws = ChipDraws(set.Interior);
            var b = set.Bounds;
            for (int k = 0; k < n; k++)
            {
                var local = new Vector3(Mathf.Lerp(b.min.x, b.max.x, Rand()), Mathf.Lerp(b.min.y, b.max.y, Rand() * 0.7f), Mathf.Lerp(b.min.z, b.max.z, Rand()));
                var p = basis.MultiplyPoint3x4(local);
                var outward = p - basis.MultiplyPoint3x4(new Vector3(b.center.x, local.y, b.center.z));
                outward.y = 0f;
                var v = (outward.normalized + away * 0.6f + Jitter(0.5f)) * (1.5f + 2.5f * Rand()) + Vector3.up * (1.5f + 2.5f * Rand());
                float s = 0.05f + 0.08f * Rand();
                debris.Spawn(draws, Matrix4x4.TRS(p, Quaternion.Euler(Rand() * 360f, Rand() * 360f, 0f), new Vector3(s, s * (0.5f + Rand()), s)),
                    Vector3.zero, ChipCorners, v, Spin(6f, 14f), 0.15f * Rand(), 0, -1, PieceExplode.None, false, true);
            }
        }

        static readonly Vector3[] ChipCorners =
        {
            new Vector3(0f, 1f, 0f), new Vector3(-0.9f, -0.5f, -0.5f), new Vector3(0.9f, -0.5f, -0.5f), new Vector3(0f, -0.5f, 1f),
        };

        // A small four-sided stone.
        static Mesh ChipMesh()
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            int[][] faces = { new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 0, 3, 1 }, new[] { 1, 3, 2 } };
            foreach (var f in faces)
            {
                var a = ChipCorners[f[0]]; var b = ChipCorners[f[1]]; var c = ChipCorners[f[2]];
                var normal = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(normal, a + b + c) < 0f) { (b, c) = (c, b); normal = -normal; }
                foreach (var p in new[] { a, b, c }) { t.Add(v.Count); v.Add(p); n.Add(normal); }
            }
            var m = new Mesh { name = "chip", hideFlags = HideFlags.DontSave };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, new List<Vector2>(new Vector2[v.Count]));
            var white = new Color32[v.Count];
            for (int i = 0; i < white.Length; i++) white[i] = new Color32(200, 200, 200, 255);
            m.colors32 = white;
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        // A swept feature sinks into the ground and leaves nothing.
        public void Sink(List<(Mesh mesh, int sub, Material mat, Matrix4x4 m, bool flat)> draws)
        {
            foreach (var d in draws)
            {
                if (d.flat || d.mesh == null || d.mat == null || !debris.Room(1)) continue;
                var b = d.mesh.bounds;
                int i = debris.Spawn(new[] { new ChunkDraw { Mesh = d.mesh, Submesh = d.sub, Material = d.mat } }, d.m, b.center, null, Vector3.zero, Vector3.zero);
                if (i >= 0) debris.Sink(i, b.extents.magnitude);
            }
        }

        public void Dispose()
        {
            if (chip != null) Looks.Release(chip);
            chip = null;
            foreach (var m in chipMats) Looks.Release(m);
            chipMats.Clear();
            System.Array.Clear(chipDraws, 0, chipDraws.Length);
        }
    }
}
