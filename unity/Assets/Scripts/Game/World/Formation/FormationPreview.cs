// FormationPreview.cs - what a live formation drag shows on the ground: the
// drawn line (faded past the widest the formation grows), a marker per
// unit with its role icon and a tick for the facing, a large arrow for the
// facing, faint lines from units to their slots, and tethers from snapped
// slots to where the shape wanted them. Queued formations stay faintly
// until their units get there. Drawn instanced with overlay materials.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FormationPreview
    {
        readonly IGameBackend backend;
        readonly InstancedDraws draws = new InstancedDraws { CastShadows = false };

        struct Marker
        {
            public Matrix4x4 Base, Icon, Tick, Mark, Stem, Tether;
            public int Paint, IconMesh, MarkMesh;
            public bool Air, Snapped;
            public int Unit;
            public Vector3 At;
            public float Since;
        }

        readonly List<Marker> markers = new List<Marker>();
        readonly List<Marker> queued = new List<Marker>();
        Matrix4x4 arrow;
        bool hasArrow;

        public int Markers => markers.Count;
        public int QueuedCount => queued.Count;
        public int DrawCalls => draws.DrawCalls;

        public FormationPreview(IGameBackend backend) => this.backend = backend;

        public void Clear()
        {
            markers.Clear();
            hasArrow = false;
        }

        // ---- Building the markers, when the layout changes ----

        public void Build(FormationInput input)
        {
            Res.Ensure();
            markers.Clear();
            foreach (var l in input.Layers)
                for (int s = 0; s < l.Count; s++) markers.Add(MarkerFor(l, s, false));
            var main = input.Main();
            hasArrow = main.Count > 0;
            if (!hasArrow) return;
            var c = Vector2.zero;
            for (int s = 0; s < main.Count; s++) c += main.Slots[s].World;
            c /= main.Count;
            // In front of the formation, pointing the way it faces.
            float front = float.MinValue;
            for (int s = 0; s < main.Count; s++) front = Mathf.Max(front, Vector2.Dot(main.Slots[s].World - c, main.Face));
            var at = c + main.Face * (Mathf.Max(0, front) + 2.5f);
            // Tilted to the slope so its head does not sink into rising ground.
            var normal = Normal(at.x, at.y, 1.3f);
            var turn = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, main.Heading, 0);
            arrow = Matrix4x4.TRS(new Vector3(at.x, Height(at.x, at.y), at.y) + normal * 0.15f, turn, new Vector3(1.6f, 1f, 2.6f));
        }

        // A formation sent without Shift replaces what its units had queued.
        public void ForgetQueued(FormationLayout l)
        {
            if (queued.Count == 0) return;
            var gone = new HashSet<int>();
            for (int i = 0; i < l.Count; i++) gone.Add(l.Members[i].Handle);
            queued.RemoveAll(q => gone.Contains(q.Unit));
        }

        public void AddQueued(FormationLayout l, float now)
        {
            Res.Ensure();
            for (int s = 0; s < l.Count; s++)
            {
                if (!FormationInput.IsSent(l, s)) continue;
                var m = MarkerFor(l, s, true);
                m.Since = now;
                queued.Add(m);
            }
        }

        // A queued marker goes once its unit stands within a cell of it,
        // has died, or two minutes have passed.
        public void Prune(FormationInput input, float now)
        {
            int kept = 0;
            for (int i = 0; i < queued.Count; i++)
            {
                var q = queued[i];
                if (now - q.Since > FormationTuning.QueuedSeconds || !input.UnitAt(q.Unit, out var p)
                    || new Vector2(p.x - q.At.x, p.z - q.At.z).magnitude <= FormationTuning.QueuedArrive) continue;
                queued[kept++] = q;
            }
            queued.RemoveRange(kept, queued.Count - kept);
        }

        Marker MarkerFor(FormationLayout l, int s, bool isQueued)
        {
            var slot = l.Slots[s];
            var member = l.Members[slot.Member >= 0 ? slot.Member : s];
            var mover = member.Kind.Mover;
            float fp = FormationPlanner.Footprint(member), size = fp * 0.9f;
            float x = slot.World.x, z = slot.World.y;
            float h = Height(x, z);
            bool air = mover == FormationMover.Flyer;
            var normal = air ? Vector3.up : Normal(x, z, Mathf.Max(0.5f, fp * 0.5f));
            var turn = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, l.Heading, 0);
            var ground = new Vector3(x, h, z);
            var at = ground + (air ? Vector3.up * 0.5f : normal * 0.06f);
            int paint = slot.State == SlotState.Snapped ? Res.Snapped : slot.State == SlotState.Nowhere ? Res.Nowhere
                : l.Layer == FormationLayer.Water ? Res.Water : Res.Good;
            var m = new Marker
            {
                Base = Matrix4x4.TRS(at, turn, new Vector3(size, 1, size)),
                Icon = Matrix4x4.TRS(at + normal * 0.01f, turn, new Vector3(size * 0.8f, 1, size * 0.8f)),
                Tick = Matrix4x4.TRS(at + normal * 0.01f, turn, new Vector3(size, 1, size)),
                Mark = Matrix4x4.TRS(at + normal * 0.01f + turn * new Vector3(size * 0.32f, 0, -size * 0.32f), turn, new Vector3(size * 0.3f, 1, size * 0.3f)),
                Stem = Matrix4x4.TRS(ground, Quaternion.Euler(0, l.Heading, 0), new Vector3(1, 0.5f, 1)),
                Paint = paint + (isQueued ? Res.QueuedOffset : 0),
                IconMesh = (int)slot.Role,
                MarkMesh = air ? Res.WingMark : l.Layer == FormationLayer.Water ? Res.AnchorMark : -1,
                Air = air,
                Snapped = slot.State == SlotState.Snapped && !isQueued,
                Unit = member.Handle,
                At = new Vector3(x, h, z),
            };
            if (m.Snapped)
            {
                var from = new Vector3(slot.Wanted.x, Height(slot.Wanted.x, slot.Wanted.y) + 0.08f, slot.Wanted.y);
                m.Tether = Segment(from, at + Vector3.up * 0.02f, 0.07f);
            }
            return m;
        }

        // The ground, or the water over it, so a marker on the water shows.
        float Height(float x, float z)
        {
            float h = backend.GroundHeight(x, z);
            var t = backend.Terrain;
            return t != null && t.SeaLevel > 0 ? Mathf.Max(h, t.SeaLevel) : h;
        }

        // The ground's slope under a marker, from four samples.
        Vector3 Normal(float x, float z, float r)
        {
            float dx = (Height(x + r, z) - Height(x - r, z)) / (2 * r);
            float dz = (Height(x, z + r) - Height(x, z - r)) / (2 * r);
            return new Vector3(-dx, 1f, -dz).normalized;
        }

        static Matrix4x4 Segment(Vector3 from, Vector3 to, float width)
        {
            var d = to - from;
            float len = d.magnitude;
            if (len < 1e-4f) return Matrix4x4.TRS(from, Quaternion.identity, Vector3.zero);
            return Matrix4x4.TRS((from + to) * 0.5f, Quaternion.LookRotation(d / len), new Vector3(width, 1, len));
        }

        // ---- Drawing, every frame ----

        public void Draw(FormationInput input, bool live)
        {
            if (!live && queued.Count == 0) return;
            Res.Ensure();
            draws.Clear();
            foreach (var q in queued) AddMarker(q);
            if (live)
            {
                AddLine(input);
                foreach (var m in markers) AddMarker(m);
                if (hasArrow) draws.Add(Res.Arrow, 0, Res.Paints[Res.ArrowPaint], arrow);
                if (markers.Count <= FormationTuning.SlotLinesMax)
                    foreach (var m in markers)
                        if (input.UnitAt(m.Unit, out var p))
                            draws.Add(Res.Flat, 0, Res.Paints[Res.UnitLine], Segment(p + Vector3.up * 0.12f, m.At + Vector3.up * 0.1f, 0.06f));
            }
            draws.Draw();
        }

        void AddMarker(in Marker m)
        {
            var paint = Res.Paints[m.Paint];
            bool faint = m.Paint >= Res.QueuedOffset;
            var ink = Res.Paints[faint ? Res.Icon + Res.QueuedOffset : Res.Icon];
            if (m.Air)
            {
                draws.Add(Res.Ring, 0, paint, m.Base);
                draws.Add(Res.Stem, 0, ink, m.Stem);
            }
            else draws.Add(Res.Disc, 0, paint, m.Base);
            draws.Add(Res.Icons[m.IconMesh], 0, ink, m.Icon);
            draws.Add(Res.Tick, 0, ink, m.Tick);
            if (m.MarkMesh >= 0) draws.Add(Res.Icons[m.MarkMesh], 0, ink, m.Mark);
            if (m.Snapped) draws.Add(Res.Flat, 0, Res.Paints[Res.Tether], m.Tether);
        }

        // The line as drawn, lying on the ground, solid where the formation
        // stands and faded past its widest. Too short to set a direction,
        // it is not drawn at all.
        void AddLine(FormationInput input)
        {
            var a = input.LineFrom;
            var b = input.LineTo;
            float len = (b - a).magnitude;
            float from = float.MaxValue, to = float.MinValue;
            foreach (var l in input.Layers)
            {
                if (l.Count == 0 || !l.Drawn) continue;
                float along = Vector2.Dot(l.Origin - l.A, l.Dir);
                float half = l.Fixed ? len : l.Frontage * 0.5f;
                from = Mathf.Min(from, along - half);
                to = Mathf.Max(to, along + half);
            }
            if (from > to) return;
            int n = Mathf.Clamp(Mathf.CeilToInt(len), 1, 64);
            var prev = new Vector3(a.x, Height(a.x, a.y) + 0.09f, a.y);
            for (int i = 1; i <= n; i++)
            {
                float t = (float)i / n;
                var q = Vector2.Lerp(a, b, t);
                var p = new Vector3(q.x, Height(q.x, q.y) + 0.09f, q.y);
                float mid = (t - 0.5f / n) * len;
                bool solid = mid >= from - 1e-3f && mid <= to + 1e-3f;
                draws.Add(Res.Flat, 0, Res.Paints[solid ? Res.LineUsed : Res.LineFaded], Segment(prev, p, 0.18f));
                prev = p;
            }
        }

        // ---- Meshes and materials, made once ----

        static class Res
        {
            public const int Good = 0, Snapped = 1, Nowhere = 2, Water = 3, Icon = 4, QueuedOffset = 5;
            public const int LineUsed = 10, LineFaded = 11, ArrowPaint = 12, UnitLine = 13, Tether = 14;
            public const int WingMark = 6, AnchorMark = 7;
            public static Material[] Paints;
            public static Mesh Disc, Ring, Tick, Flat, Stem, Arrow;
            public static Mesh[] Icons;

            public static void Ensure()
            {
                if (Paints != null && Paints[0] != null && Disc != null) return;
                var colours = new[]
                {
                    new Color(0.4f, 1f, 0.4f, 0.38f), new Color(1f, 0.74f, 0.22f, 0.5f), new Color(1f, 0.28f, 0.22f, 0.55f),
                    new Color(0.3f, 0.9f, 0.85f, 0.42f), new Color(1f, 1f, 1f, 0.88f),
                };
                Paints = new Material[15];
                for (int i = 0; i < 5; i++)
                {
                    int queue = i == Icon ? 3011 : 3010;
                    Paints[i] = Make(colours[i], queue);
                    var faint = colours[i];
                    faint.a *= FormationTuning.QueuedAlpha;
                    Paints[i + QueuedOffset] = Make(faint, queue);
                }
                Paints[LineUsed] = Make(new Color(0.45f, 1f, 0.45f, 0.85f), 3009);
                Paints[LineFaded] = Make(new Color(0.45f, 1f, 0.45f, 0.25f), 3009);
                Paints[ArrowPaint] = Make(new Color(0.92f, 1f, 0.92f, 0.6f), 3012);
                Paints[UnitLine] = Make(new Color(1f, 1f, 1f, 0.2f), 3009);
                Paints[Tether] = Make(new Color(1f, 0.74f, 0.22f, 0.75f), 3009);

                Disc = Shapes.Disc(0.5f, 28);
                Ring = Shapes.Ring(0.38f, 0.5f, 28);
                Tick = Shapes.Build("tick", s => s.Tri(-0.13f, 0.34f, 0.13f, 0.34f, 0f, 0.5f));
                Flat = Shapes.Build("flat", s => s.Rect(-0.5f, -0.5f, 0.5f, 0.5f));
                Stem = Shapes.Stem(0.04f);
                Arrow = Shapes.Build("arrow", s => { s.Rect(-0.12f, -0.5f, 0.12f, 0.1f); s.Tri(-0.35f, 0.1f, 0.35f, 0.1f, 0f, 0.5f); });
                Icons = new[]
                {
                    // Melee: a sword.
                    Shapes.Build("melee", s => { s.Rect(-0.06f, -0.15f, 0.06f, 0.36f); s.Tri(-0.06f, 0.36f, 0.06f, 0.36f, 0f, 0.48f); s.Rect(-0.24f, -0.22f, 0.24f, -0.13f); s.Rect(-0.045f, -0.45f, 0.045f, -0.22f); }),
                    // Cavalry: two chevrons.
                    Shapes.Build("cavalry", s => { s.Bar(-0.36f, 0.02f, 0f, 0.38f, 0.1f); s.Bar(0f, 0.38f, 0.36f, 0.02f, 0.1f); s.Bar(-0.36f, -0.3f, 0f, 0.06f, 0.1f); s.Bar(0f, 0.06f, 0.36f, -0.3f, 0.1f); }),
                    // Ranged: an arrow.
                    Shapes.Build("ranged", s => { s.Rect(-0.04f, -0.42f, 0.04f, 0.22f); s.Tri(-0.17f, 0.2f, 0.17f, 0.2f, 0f, 0.46f); s.Tri(-0.16f, -0.46f, -0.04f, -0.3f, -0.04f, -0.46f); s.Tri(0.04f, -0.46f, 0.04f, -0.3f, 0.16f, -0.46f); }),
                    // Caster: a four-pointed star.
                    Shapes.Build("caster", s => { s.Quad(0f, 0.46f, 0.11f, 0f, 0f, -0.46f, -0.11f, 0f); s.Quad(-0.46f, 0f, 0f, 0.11f, 0.46f, 0f, 0f, -0.11f); }),
                    // Siege: a block with a barrel.
                    Shapes.Build("siege", s => { s.Rect(-0.28f, -0.34f, 0.28f, 0.16f); s.Rect(-0.08f, 0.16f, 0.08f, 0.46f); }),
                    // Command: a crown.
                    Shapes.Build("command", s => { s.Rect(-0.32f, -0.32f, 0.32f, -0.12f); s.Tri(-0.32f, -0.12f, -0.16f, -0.12f, -0.32f, 0.3f); s.Tri(-0.11f, -0.12f, 0.11f, -0.12f, 0f, 0.38f); s.Tri(0.16f, -0.12f, 0.32f, -0.12f, 0.32f, 0.3f); }),
                    // The air layer's mark: wings.
                    Shapes.Build("wings", s => { s.Tri(-0.48f, 0.2f, -0.04f, -0.12f, -0.04f, 0.12f); s.Tri(0.04f, 0.12f, 0.04f, -0.12f, 0.48f, 0.2f); }),
                    // The water layer's mark: an anchor.
                    Shapes.Build("anchor", s => { s.Rect(-0.05f, -0.34f, 0.05f, 0.36f); s.Rect(-0.22f, 0.18f, 0.22f, 0.27f); s.Bar(-0.32f, -0.12f, 0f, -0.4f, 0.09f); s.Bar(0f, -0.4f, 0.32f, -0.12f, 0.09f); }),
                };
            }

            static Material Make(Color c, int queue)
            {
                var m = Looks.Overlay(c);
                m.renderQueue = queue;
                return m;
            }
        }

        // Flat shapes in the ground plane, x right and z forward, white so
        // the material colours them.
        sealed class Shapes
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<int> t = new List<int>();

            public static Mesh Build(string name, System.Action<Shapes> draw)
            {
                var s = new Shapes();
                draw(s);
                return s.Mesh(name);
            }

            Mesh Mesh(string name)
            {
                var m = new Mesh { name = "formation " + name, hideFlags = HideFlags.DontSave };
                m.SetVertices(v);
                var c = new Color32[v.Count];
                var n = new Vector3[v.Count];
                for (int i = 0; i < c.Length; i++) { c[i] = new Color32(255, 255, 255, 255); n[i] = Vector3.up; }
                m.colors32 = c;
                m.normals = n;
                m.SetTriangles(t, 0);
                return m;
            }

            public void Tri(float x0, float z0, float x1, float z1, float x2, float z2)
            {
                int b = v.Count;
                v.Add(new Vector3(x0, 0, z0)); v.Add(new Vector3(x1, 0, z1)); v.Add(new Vector3(x2, 0, z2));
                t.Add(b); t.Add(b + 1); t.Add(b + 2);
            }

            public void Quad(float x0, float z0, float x1, float z1, float x2, float z2, float x3, float z3)
            {
                Tri(x0, z0, x1, z1, x2, z2);
                Tri(x0, z0, x2, z2, x3, z3);
            }

            public void Rect(float x0, float z0, float x1, float z1) => Quad(x0, z0, x0, z1, x1, z1, x1, z0);

            // A bar of a width from one point to another.
            public void Bar(float x0, float z0, float x1, float z1, float width)
            {
                var d = new Vector2(x1 - x0, z1 - z0).normalized * (width * 0.5f);
                var n = new Vector2(-d.y, d.x);
                Quad(x0 + n.x, z0 + n.y, x1 + n.x, z1 + n.y, x1 - n.x, z1 - n.y, x0 - n.x, z0 - n.y);
            }

            public static Mesh Disc(float r, int segments) => Build("disc", s =>
            {
                for (int i = 0; i < segments; i++)
                {
                    float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                    s.Tri(0, 0, Mathf.Sin(a0) * r, Mathf.Cos(a0) * r, Mathf.Sin(a1) * r, Mathf.Cos(a1) * r);
                }
            });

            public static Mesh Ring(float inner, float outer, int segments) => Build("ring", s =>
            {
                for (int i = 0; i < segments; i++)
                {
                    float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                    s.Quad(Mathf.Sin(a0) * inner, Mathf.Cos(a0) * inner, Mathf.Sin(a0) * outer, Mathf.Cos(a0) * outer,
                        Mathf.Sin(a1) * outer, Mathf.Cos(a1) * outer, Mathf.Sin(a1) * inner, Mathf.Cos(a1) * inner);
                }
            });

            // Two thin upright quads crossed, from the ground up one unit.
            public static Mesh Stem(float half)
            {
                var s = new Shapes();
                int b = 0;
                foreach (var side in new[] { Vector3.right, Vector3.forward })
                {
                    var o = side * half;
                    s.v.Add(-o); s.v.Add(-o + Vector3.up); s.v.Add(o + Vector3.up); s.v.Add(o);
                    s.t.Add(b); s.t.Add(b + 1); s.t.Add(b + 2); s.t.Add(b); s.t.Add(b + 2); s.t.Add(b + 3);
                    b += 4;
                }
                return s.Mesh("stem");
            }
        }
    }
}
