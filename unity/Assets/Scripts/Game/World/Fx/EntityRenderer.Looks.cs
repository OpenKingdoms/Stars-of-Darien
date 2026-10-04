// EntityRenderer.Looks.cs - the renderer's side of the marks fire and magic
// leave on scenery: each feature's SceneryLook, kept with it through its
// stages and drawn per instance, a grid to find the scenery a blast reaches,
// and a burning feature's charred model dithering away as its burnt stage
// dithers in.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed partial class EntityRenderer
    {
        // A burning feature's charred model fades into its burnt stage over this.
        public const float BurntFadeSeconds = 1.2f;
        const float GridCell = 8f;

        int[] gridHead = new int[0], gridNext = new int[0];
        int gridW, gridH, gridCount = -1;
        bool gridStale = true;

        struct Leaving
        {
            public List<(Mesh mesh, int sub, Material mat, Matrix4x4 m)> Draws;
            public SceneryInstance Look;
            public bool Marked;
            public Vector3 At;
            public float From, Length;
        }

        readonly List<Leaving> leaving = new List<Leaving>();
        readonly Stack<List<(Mesh, int, Material, Matrix4x4)>> spareDraws = new Stack<List<(Mesh, int, Material, Matrix4x4)>>();

        // Features drawn with a mark in the last frame, and models fading out.
        public int MarkedDrawn { get; private set; }
        public int LeavingCount => leaving.Count;

        // The look of the feature an event names, made if it has none. Null
        // when the renderer draws nothing for it.
        public SceneryLook LookAt(int index, int def, Vector3 at)
        {
            var e = EntryFor(index, def, at);
            return e != null ? LookOf(e) : null;
        }

        // The look of whatever feature stands nearest a point within reach, or null.
        public SceneryLook LookNear(Vector3 at, float within)
        {
            EnsureGrid();
            FeatureEntry best = null;
            float bestD = within * within;
            Visit(at, within, (e, d2) => { if (d2 <= bestD) { bestD = d2; best = e; } });
            return best != null ? LookOf(best) : null;
        }

        // The looks of every feature whose footprint lies within radius of a
        // point, made where missing. Returns how many it added.
        public int LooksNear(Vector3 at, float radius, List<SceneryLook> into)
        {
            EnsureGrid();
            int before = into.Count;
            visitInto = into;
            Visit(at, radius, addLook);
            visitInto = null;
            return into.Count - before;
        }

        List<SceneryLook> visitInto;
        System.Action<FeatureEntry, float> addLook;

        void Visit(Vector3 at, float radius, System.Action<FeatureEntry, float> each)
        {
            if (gridW == 0) return;
            float reach = radius + GridCell * 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((at.x - reach) / GridCell), 0, gridW - 1), x1 = Mathf.Clamp(Mathf.FloorToInt((at.x + reach) / GridCell), 0, gridW - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((-at.z - reach) / GridCell), 0, gridH - 1), z1 = Mathf.Clamp(Mathf.FloorToInt((-at.z + reach) / GridCell), 0, gridH - 1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = gridHead[z * gridW + x]; i >= 0; i = gridNext[i])
                    {
                        var e = featureEntries[i];
                        if (e.Def == int.MinValue || e.Draws.Count == 0 && !e.Card) continue;
                        // Measured to the edge of what is drawn, so a wide hut is reached at its walls.
                        float edge = e.Look != null ? e.Look.Width * 0.4f : 0.4f;
                        float dx = e.Position.x - at.x, dz = e.Position.z - at.z;
                        float d = Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) - edge);
                        if (d <= radius) each(e, d * d);
                    }
        }

        // The features bucketed by a coarse grid, built again when the list changed.
        void EnsureGrid()
        {
            if (addLook == null) addLook = (e, d2) => visitInto.Add(LookOf(e));
            int n = featureEntries.Count;
            if (!gridStale && n == gridCount) return;
            var t = backend.Terrain;
            var size = t != null ? t.Size : new Vector2(256f, 256f);
            gridW = Mathf.Max(1, Mathf.CeilToInt(size.x / GridCell) + 1);
            gridH = Mathf.Max(1, Mathf.CeilToInt(size.y / GridCell) + 1);
            if (gridHead.Length != gridW * gridH) gridHead = new int[gridW * gridH];
            for (int i = 0; i < gridHead.Length; i++) gridHead[i] = -1;
            if (gridNext.Length < n) gridNext = new int[Mathf.NextPowerOfTwo(Mathf.Max(64, n))];
            for (int i = 0; i < n; i++)
            {
                var p = featureEntries[i].Position;
                int x = Mathf.Clamp(Mathf.FloorToInt(p.x / GridCell), 0, gridW - 1), z = Mathf.Clamp(Mathf.FloorToInt(-p.z / GridCell), 0, gridH - 1);
                int c = z * gridW + x;
                gridNext[i] = gridHead[c];
                gridHead[c] = i;
            }
            gridCount = n;
            gridStale = false;
        }

        SceneryLook LookOf(FeatureEntry e)
        {
            if (e.Look == null)
            {
                e.Look = new SceneryLook();
                PlaceLook(e);
            }
            return e.Look;
        }

        // A look follows its feature into each stage at the same place.
        void PlaceLook(FeatureEntry e)
        {
            var look = e.Look;
            if (look == null) return;
            var d = FeatureDefOf(e.Def);
            look.Def = e.Def;
            look.Foot = e.Position;
            look.Bounds = DrawnBounds(e, d);
            look.Classify(d);
        }

        // Lets a look go with its feature.
        static void DropLook(FeatureEntry e)
        {
            if (e.Look == null) return;
            e.Look.Gone = true;
            e.Look = null;
        }

        // What a feature's draws cover in the world, or its card, or its def's size.
        Bounds DrawnBounds(FeatureEntry e, FeatureDef d)
        {
            bool any = false;
            var b = new Bounds(e.Position, Vector3.zero);
            foreach (var draw in e.Draws)
            {
                if (draw.mesh == null) continue;
                var mb = draw.mesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) != 0 ? 1 : -1, (k & 2) != 0 ? 1 : -1, (k & 4) != 0 ? 1 : -1));
                    var w = draw.m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(w, Vector3.zero); any = true; }
                    else b.Encapsulate(w);
                }
            }
            if (any) return b;
            if (e.Card) return new Bounds(e.Position + Vector3.up * ((e.Bottom + e.Top) * 0.5f), new Vector3(e.W, Mathf.Max(0.2f, e.Top - e.Bottom), e.W));
            float cell = backend.Terrain != null ? backend.Terrain.CellSize : 1f;
            var foot = d != null ? new Vector2(Mathf.Max(1, d.Footprint.x), Mathf.Max(1, d.Footprint.y)) * cell : Vector2.one;
            float h = d != null && d.Height > 0f ? d.Height : 1f;
            return new Bounds(e.Position + Vector3.up * h * 0.5f, new Vector3(foot.x, h, foot.y));
        }

        // The swap from burning to burnt: the charred model stays and dithers
        // away while its burnt stage dithers in, each filling the other's holes.
        void LeaveBurning(in FeatureEvent e)
        {
            var entry = EntryFor(e.Feature, e.Def, e.Position);
            if (entry == null || entry.Hold != 0 || entry.Draws.Count == 0 || !Shows(e.Position, 6f)) return;
            var draws = spareDraws.Count > 0 ? spareDraws.Pop() : new List<(Mesh, int, Material, Matrix4x4)>();
            draws.Clear();
            foreach (var d in entry.Draws) if (!d.flat) draws.Add((d.mesh, d.sub, d.mat, d.m));
            var leave = new Leaving { Draws = draws, At = e.Position, From = simNow, Length = BurntFadeSeconds };
            if (entry.Look != null && entry.Look.Shows) { entry.Look.Pack(out leave.Look); leave.Marked = true; }
            leaving.Add(leave);
            entry.FadeLength = BurntFadeSeconds;
        }

        void DrawLeaving()
        {
            for (int i = leaving.Count - 1; i >= 0; i--)
            {
                var l = leaving[i];
                float t = (simNow - l.From) / l.Length;
                if (t >= 1f || t < 0f)
                {
                    spareDraws.Push(l.Draws);
                    leaving.RemoveAt(i);
                    continue;
                }
                if (Unseen != null && Unseen(l.At)) continue;
                var sink = ScarMap.Sink(l.At);
                float fade = Mathf.Max(0.001f, t);
                foreach (var d in l.Draws)
                {
                    if (l.Marked) solid.Add(d.mesh, d.sub, d.mat, sink * d.m, 0f, fade, l.Look);
                    else solid.Add(d.mesh, d.sub, d.mat, sink * d.m, 0f, fade);
                }
            }
        }

        void ForgetLooks()
        {
            foreach (var e in featureEntries) DropLook(e);
            leaving.Clear();
            gridStale = true;
        }
    }
}
