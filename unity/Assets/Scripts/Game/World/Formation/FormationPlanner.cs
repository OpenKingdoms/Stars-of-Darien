// FormationPlanner.cs - the shape a dragged formation takes: the line the
// player drew, the facing, the frontage, and a slot for every unit in
// Line, Block, Wedge or Loose, or "as they stand" with Alt. Pure: plain
// structs in and out, no scene, so the tests drive it directly.
//
// Space is the backend's: x east, z north (a Vector2's y), one unit a
// cell, headings in degrees clockwise from north. A slot's local frame
// has x to the formation's right and y forward, the front rank on the
// drawn line at y = 0 and the ranks behind it at negative y.
using System;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public enum FormationShape { Line, Block, Wedge, Loose }

    public enum FormationFacing { ByDrag, AwayFromGroup }

    public enum SlotState { Good, Snapped, Nowhere }

    // Every number the formation code tunes, in one place.
    public static class FormationTuning
    {
        public const float DragPixels = 12f, ClassicRightDragPixels = 16f;
        public const float Gap = 1f;                // between neighbours in Line, Block and Wedge
        public const float BlockGap = 1f;           // extra depth between role blocks in a Line
        public const float WingGap = 1f;            // between the front block and a wing
        public const float WedgeDepth = 0.87f;      // a wedge rank's depth, in pitches
        public const int HungarianMax = 64;         // per role group, above it the projection sort
        public const int SnapFar = 16;
        public const int SnapBudget = 16000;        // spots tried per layer and layout
        public const float RecomputeCells = 0.25f, RecomputeSeconds = 0.25f;
        public const int SlotLinesMax = 60;
        public const float QueuedAlpha = 0.4f, QueuedArrive = 1f, QueuedSeconds = 120f;
        public const int UnitsPerCall = 256;
        public const float StandingNear = 3f, StandingAgree = 0.6f;
        public const float StandingRank = 1f;       // depth of a rough rank, as they stand
        public const float ShortestLine = 0.1f;
    }

    public struct FormationMember
    {
        public int Handle;
        public Vector2 Position;        // world x, z now
        public float Heading;
        public bool Moving;
        public FormationKind Kind;
        // The slot this client last gave it, for "as they stand".
        public bool HasSlot;
        public Vector2 Slot;
        public float SlotHeading;
        public int SlotFormation;
    }

    public struct FormationSlot
    {
        public Vector2 Local;           // x to the right, y forward, from the origin
        public Vector2 Wanted;          // world x, z where the shape puts it
        public Vector2 World;           // where it is sent, after snapping
        public FormationRole Role;      // the role group that fills it
        public int Member;              // index into the layout's members, -1 until assigned
        public SlotState State;
    }

    public struct FormationRequest
    {
        public Vector2 A, B;            // the ground under the press and under the pointer
        public FormationShape Shape;
        public bool FaceAbout, AsTheyStand;
        public FormationFacing Facing;
        public Vector2 CameraRight;     // the line's direction when the drag is too short to give one
    }

    // One layer's formation. Buffers grow to the largest group seen and
    // are reused, so a live drag allocates nothing.
    public sealed class FormationLayout
    {
        public FormationLayer Layer;
        public FormationMember[] Members = Array.Empty<FormationMember>();
        public FormationSlot[] Slots = Array.Empty<FormationSlot>();
        public int[] SnapOrder = Array.Empty<int>();    // slot indices, most exposed first
        public int Count;
        public Vector2 A, B, Origin, Dir, Right, Face;
        public float Length, Frontage, Heading;
        public int Wide, Deep;
        public bool Fixed;              // "as they stand": each slot keeps its member
        internal float[] Keys = Array.Empty<float>();
        internal int[] Tie = Array.Empty<int>(), Tmp = Array.Empty<int>(), Work = Array.Empty<int>();

        public void Reserve(int n)
        {
            if (Members.Length >= n) return;
            int size = Mathf.Max(8, Mathf.NextPowerOfTwo(n));
            Array.Resize(ref Members, size);
            Slots = new FormationSlot[size];
            SnapOrder = new int[size];
            Keys = new float[size];
            Tie = new int[size];
            Tmp = new int[size];
            Work = new int[size];
        }
    }

    public static class FormationPlanner
    {
        static readonly FormationRole[] LineOrder = { FormationRole.Melee, FormationRole.Ranged, FormationRole.Caster, FormationRole.Siege, FormationRole.Command };
        static readonly FormationRole[] FillOrder = { FormationRole.Melee, FormationRole.Cavalry, FormationRole.Ranged, FormationRole.Caster, FormationRole.Siege, FormationRole.Command };
        static readonly FormationRole[] WedgeOrder = { FormationRole.Cavalry, FormationRole.Melee, FormationRole.Ranged, FormationRole.Caster, FormationRole.Siege, FormationRole.Command };
        static readonly FormationRole[] LooseOrder = FillOrder;

        static readonly string[] Names = { "Line", "Block", "Wedge", "Loose" };

        public static string Name(FormationShape s) => Names[(int)s];

        public static FormationShape Next(FormationShape s) => (FormationShape)(((int)s + 1) % 4);

        public static float HeadingOf(Vector2 dir)
        {
            float h = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            return h < 0 ? h + 360f : h;
        }

        public static Vector2 DirOf(float heading) =>
            new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));

        // Lays out the layout's members (sorted by handle) as the request
        // asks: the frame, then a slot per member. Assignment and snapping
        // come after, in FormationAssign and FormationSnap.
        public static void Plan(FormationLayout l, in FormationRequest req)
        {
            int n = l.Count;
            l.Reserve(n);
            l.A = req.A;
            l.B = req.B;
            var d = req.B - req.A;
            float len = d.magnitude;
            if (len < FormationTuning.ShortestLine)
            {
                d = req.CameraRight.sqrMagnitude > 1e-6f ? req.CameraRight.normalized : Vector2.right;
                len = 0f;
            }
            else d /= len;
            l.Dir = d;
            l.Length = len;
            var f = new Vector2(-d.y, d.x);
            if (req.Facing == FormationFacing.AwayFromGroup && n > 0)
            {
                var mid = req.A + d * (len * 0.5f);
                if (Vector2.Dot(f, mid - Centre(l)) < 0) f = -f;
            }
            if (req.FaceAbout) f = -f;
            l.Face = f;
            l.Right = new Vector2(f.y, -f.x);
            l.Heading = HeadingOf(f);
            l.Fixed = req.AsTheyStand;
            if (n == 0) { l.Frontage = 0; l.Origin = req.A; l.Wide = l.Deep = 0; return; }

            int slots = 0;
            if (req.AsTheyStand) slots = AsTheyStand(l);
            else
                switch (req.Shape)
                {
                    case FormationShape.Block: slots = Block(l); break;
                    case FormationShape.Wedge: slots = Wedge(l); break;
                    case FormationShape.Loose: slots = Loose(l); break;
                    default: slots = Line(l); break;
                }
            if (!req.AsTheyStand)
            {
                l.Origin = l.A + l.Dir * (l.Frontage * 0.5f);
                for (int i = 0; i < slots; i++)
                {
                    ref var s = ref l.Slots[i];
                    s.Wanted = l.Origin + l.Right * s.Local.x + l.Face * s.Local.y;
                    s.World = s.Wanted;
                    s.Member = -1;
                    s.State = SlotState.Good;
                }
            }
            if (req.Shape != FormationShape.Block && req.Shape != FormationShape.Wedge || req.AsTheyStand) FrontFirst(l);
            Measure(l, req.Shape == FormationShape.Wedge && !req.AsTheyStand, req.AsTheyStand ? FormationTuning.StandingRank : 0.01f);
        }

        static Vector2 Centre(FormationLayout l)
        {
            var c = Vector2.zero;
            for (int i = 0; i < l.Count; i++) c += l.Members[i].Position;
            return c / Mathf.Max(1, l.Count);
        }

        static int CountOf(FormationLayout l, FormationRole r)
        {
            int c = 0;
            for (int i = 0; i < l.Count; i++) if (l.Members[i].Kind.Role == r) c++;
            return c;
        }

        static int WidestOf(FormationLayout l, FormationRole r)
        {
            int w = 0;
            for (int i = 0; i < l.Count; i++) if (l.Members[i].Kind.Role == r) w = Mathf.Max(w, Footprint(l.Members[i]));
            return w;
        }

        static int WidestAll(FormationLayout l)
        {
            int w = 0;
            for (int i = 0; i < l.Count; i++) w = Mathf.Max(w, Footprint(l.Members[i]));
            return w;
        }

        public static int Footprint(in FormationMember m) => m.Kind.Footprint > 0 ? m.Kind.Footprint : FormationRoles.DefaultFootprint;

        static int PerRank(float frontage, float pitch, int n) =>
            Mathf.Clamp(Mathf.FloorToInt(frontage / pitch + 1e-4f), 1, Mathf.Max(1, n));

        static void Put(FormationLayout l, ref int k, FormationRole role, float x, float y)
        {
            l.Slots[k].Local = new Vector2(x, y);
            l.Slots[k].Role = role;
            k++;
        }

        // Ranks of n at pitch p from y0 back, the last one centred. Returns
        // the last rank's y.
        static float Ranks(FormationLayout l, ref int k, FormationRole role, int n, float p, float frontage, float y0, out int perRank, out int ranks)
        {
            perRank = PerRank(frontage, p, n);
            ranks = (n + perRank - 1) / perRank;
            for (int j = 0; j < ranks; j++)
            {
                int c = j < ranks - 1 ? perRank : n - perRank * (ranks - 1);
                for (int i = 0; i < c; i++) Put(l, ref k, role, (i - (c - 1) * 0.5f) * p, y0 - j * p);
            }
            return y0 - (ranks - 1) * p;
        }

        // ---- Line ----

        static int Line(FormationLayout l)
        {
            int cav = CountOf(l, FormationRole.Cavalry);
            bool wings = cav > 0 && (CountOf(l, FormationRole.Melee) > 0 || CountOf(l, FormationRole.Ranged) > 0);
            float minW = 0, maxW = 0;
            foreach (FormationRole r in FillOrder)
            {
                int c = CountOf(l, r);
                if (c == 0) continue;
                float p = WidestOf(l, r) + FormationTuning.Gap;
                minW = Mathf.Max(minW, p);
                if (!(wings && r == FormationRole.Cavalry)) maxW = Mathf.Max(maxW, c * p);
            }
            float w = Mathf.Clamp(l.Length, minW, Mathf.Max(minW, maxW));
            l.Frontage = w;

            int k = 0;
            float y = 0, lastY = 0, prevP = -1;
            bool first = true;
            int frontRanks = 1, frontCount = 0;
            float frontP = 0;
            for (int b = -1; b < LineOrder.Length; b++)
            {
                var role = b < 0 ? FormationRole.Cavalry : LineOrder[b];
                if (b < 0 && wings) continue;
                int c = CountOf(l, role);
                if (c == 0) continue;
                float p = WidestOf(l, role) + FormationTuning.Gap;
                if (prevP > 0) y = lastY - (prevP + p) * 0.5f - FormationTuning.BlockGap;
                lastY = Ranks(l, ref k, role, c, p, w, y, out int perRank, out int ranks);
                if (first) { frontRanks = ranks; frontCount = Mathf.Min(perRank, c); frontP = p; first = false; }
                prevP = p;
            }
            if (wings)
            {
                float pc = WidestOf(l, FormationRole.Cavalry) + FormationTuning.Gap;
                float h = frontCount * frontP * 0.5f;
                float step = Mathf.Max(frontP, pc);
                int left = (cav + 1) / 2;
                for (int side = 0; side < 2; side++)
                {
                    int ns = side == 0 ? left : cav - left;
                    if (ns == 0) continue;
                    int cols = (ns + frontRanks - 1) / frontRanks;
                    for (int s = 0; s < ns; s++)
                    {
                        int j = s / cols, i = s % cols;
                        float x = h + FormationTuning.WingGap + pc * (i + 0.5f);
                        Put(l, ref k, FormationRole.Cavalry, side == 0 ? -x : x, -j * step);
                    }
                }
            }
            return k;
        }

        // ---- Block ----

        static int Block(FormationLayout l)
        {
            int n = l.Count;
            float p = WidestAll(l) + FormationTuning.Gap;
            float w = Mathf.Clamp(l.Length, p, n * p);
            int cols = PerRank(w, p, n);
            int rows = (n + cols - 1) / cols;
            l.Frontage = cols * p;
            int k = 0;
            for (int r = 0; r < rows; r++)
            {
                int c = r < rows - 1 ? cols : n - cols * (rows - 1);
                for (int i = 0; i < c; i++)
                {
                    float grid = i + (cols - c) * 0.5f;
                    float ring = Mathf.Min(Mathf.Min(grid, cols - 1 - grid), Mathf.Min(r, rows - 1 - r));
                    int q = Mathf.FloorToInt(ring + 1e-4f);
                    int face = r == q ? 0 : r == rows - 1 - q ? 2 : 1;
                    float x = (i - (c - 1) * 0.5f) * p;
                    l.Slots[k].Local = new Vector2(x, -r * p);
                    // Ring, then face, sides front to back, then out from the
                    // centre line. Equal keys fall to the slot index, left first.
                    l.Keys[k] = q * 1e5f + face * 3e4f + (face == 1 ? r * 200f : 0f) + Mathf.Abs(x);
                    l.SnapOrder[k] = k;
                    k++;
                }
            }
            FillByExposure(l, k, FillOrder);
            return k;
        }

        // ---- Wedge ----

        static int Wedge(FormationLayout l)
        {
            int n = l.Count;
            float p = WidestAll(l) + FormationTuning.Gap;
            int ranks = 1;
            while (ranks * (ranks + 1) / 2 < n) ranks++;
            float q = p;
            if (ranks > 1)
            {
                float w = Mathf.Clamp(l.Length, p * (ranks - 1), 2 * p * (ranks - 1));
                q = Mathf.Clamp(w / (ranks - 1), p, 2 * p);
                l.Frontage = q * (ranks - 1);
            }
            else l.Frontage = 0;
            int k = 0, placed = 0;
            for (int r = 0; r < ranks; r++)
            {
                int c = Mathf.Min(r + 1, n - placed);
                for (int i = 0; i < c; i++)
                {
                    float x = (i - (c - 1) * 0.5f) * q;
                    l.Slots[k].Local = new Vector2(x, -FormationTuning.WedgeDepth * r * p);
                    // The point, then the outer edges front to back (left, right),
                    // then the inside front to back.
                    bool edge = i == 0 || i == c - 1;
                    l.Keys[k] = r == 0 ? 0 : edge ? 1e5f + r * 10 + (i == 0 ? 0 : 1) : 2e5f + r * 1e2f + Mathf.Abs(x) * 0.5f;
                    l.SnapOrder[k] = k;
                    k++;
                }
                placed += c;
            }
            FillByExposure(l, k, WedgeOrder);
            return k;
        }

        // Sorts slot indices by their exposure key into SnapOrder, then
        // hands them out to the roles in order.
        static void FillByExposure(FormationLayout l, int k, FormationRole[] order)
        {
            for (int i = 0; i < k; i++) l.Tie[i] = i;
            FormationSort.ByKey(l.SnapOrder, 0, k, l.Keys, l.Tie, l.Tmp);
            int at = 0;
            foreach (var role in order)
            {
                int c = CountOf(l, role);
                for (int j = 0; j < c && at < k; j++) l.Slots[l.SnapOrder[at++]].Role = role;
            }
        }

        // ---- Loose ----

        static int Loose(FormationLayout l)
        {
            float len = l.Length;
            l.Frontage = len;
            int k = 0;
            float y = 0, lastY = 0, prevP = -1;
            foreach (var role in LooseOrder)
            {
                int n = CountOf(l, role);
                if (n == 0) continue;
                float p = 2 * WidestOf(l, role) + FormationTuning.Gap;
                if (prevP > 0) y = lastY - (prevP + p) * 0.5f - FormationTuning.BlockGap;
                int per = Mathf.Clamp(Mathf.FloorToInt(len / p + 1e-4f) + 1, 1, n);
                float s = per > 1 ? len / (per - 1) : 0f;
                int rows = (n + per - 1) / per;
                for (int j = 0; j < rows; j++)
                {
                    int c = j < rows - 1 ? per : n - per * (rows - 1);
                    float shift = (j & 1) == 1 ? s * 0.5f : 0f;
                    for (int i = 0; i < c; i++) Put(l, ref k, role, (i - (c - 1) * 0.5f) * s + shift, y - j * p);
                }
                lastY = y - (rows - 1) * p;
                prevP = p;
            }
            return k;
        }

        // ---- As they stand ----

        static int AsTheyStand(FormationLayout l)
        {
            int n = l.Count;
            var mid = l.A + l.Dir * (l.Length * 0.5f);
            var c = Vector2.zero;
            for (int i = 0; i < n; i++) c += Standing(l.Members[i]);
            c /= n;
            var g = StandingFacing(l.Members, n, mid);
            float cos = Vector2.Dot(g, l.Face), sin = g.x * l.Face.y - g.y * l.Face.x;
            float front = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var v = Standing(l.Members[i]) - c;
                var o = new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
                l.Slots[i].Wanted = o;
                front = Mathf.Max(front, Vector2.Dot(o, l.Face));
            }
            l.Origin = mid;
            l.Frontage = 0;
            for (int i = 0; i < n; i++)
            {
                ref var s = ref l.Slots[i];
                var o = s.Wanted - l.Face * front;
                s.Local = new Vector2(Vector2.Dot(o, l.Right), Vector2.Dot(o, l.Face));
                s.Wanted = mid + o;
                s.World = s.Wanted;
                s.Role = l.Members[i].Kind.Role;
                s.Member = i;
                s.State = SlotState.Good;
            }
            return n;
        }

        // Where a unit counts as standing: its slot while it still marches there.
        public static Vector2 Standing(in FormationMember m) => m.HasSlot && m.Moving ? m.Slot : m.Position;

        // The way a group faces now: its last formation's facing while at
        // least half of it stands in that formation, else the units' mean
        // heading when they agree, else from their centre toward the line.
        public static Vector2 StandingFacing(FormationMember[] m, int n, Vector2 lineMiddle)
        {
            if (n == 0) return Vector2.up;
            int best = -1, bestCount = 0;
            for (int i = 0; i < n; i++)
            {
                if (!m[i].HasSlot) continue;
                int id = m[i].SlotFormation, c = 0;
                for (int j = 0; j < n; j++) if (m[j].HasSlot && m[j].SlotFormation == id) c++;
                if (c > bestCount || c == bestCount && id > best) { best = id; bestCount = c; }
            }
            if (best >= 0)
            {
                int near = 0;
                float heading = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!m[i].HasSlot || m[i].SlotFormation != best) continue;
                    heading = m[i].SlotHeading;
                    if ((m[i].Position - m[i].Slot).magnitude <= FormationTuning.StandingNear) near++;
                }
                if (near * 2 >= n) return DirOf(heading);
            }
            var sum = Vector2.zero;
            for (int i = 0; i < n; i++) sum += DirOf(m[i].Heading);
            if (sum.magnitude / n >= FormationTuning.StandingAgree) return sum.normalized;
            var c0 = Vector2.zero;
            for (int i = 0; i < n; i++) c0 += Standing(m[i]);
            var toward = lineMiddle - c0 / n;
            return toward.sqrMagnitude > 1e-6f ? toward.normalized : Vector2.up;
        }

        // ---- Order and measures ----

        // Front first, then out from the centre: the order slots are
        // snapped in for Line, Loose and as they stand.
        static void FrontFirst(FormationLayout l)
        {
            int n = l.Count;
            for (int i = 0; i < n; i++)
            {
                var p = l.Slots[i].Local;
                l.Keys[i] = -p.y * 1e3f + Mathf.Abs(p.x);
                l.Tie[i] = i;
                l.SnapOrder[i] = i;
            }
            FormationSort.ByKey(l.SnapOrder, 0, n, l.Keys, l.Tie, l.Tmp);
        }

        // Wide is the front rank's count with any wings, deep the ranks
        // from front to back. A wedge counts its widest rank. A rank spans
        // band cells of depth, so a group as it stands counts rough rows.
        static void Measure(FormationLayout l, bool widest, float band)
        {
            int n = l.Count;
            for (int i = 0; i < n; i++) { l.Work[i] = i; l.Keys[i] = -l.Slots[i].Local.y; l.Tie[i] = i; }
            FormationSort.ByKey(l.Work, 0, n, l.Keys, l.Tie, l.Tmp);
            int deep = 0, wide = 0, run = 0;
            float last = float.MaxValue;
            for (int o = 0; o < n; o++)
            {
                float y = l.Slots[l.Work[o]].Local.y;
                if (last - y > band) { deep++; last = y; run = 0; }
                run++;
                if (widest ? run > wide : deep == 1) wide = widest ? run : wide + 1;
            }
            l.Wide = wide;
            l.Deep = deep;
        }
    }

    // A stable sort of indices by a float key, ties by a second int key,
    // with a caller's buffer, so it allocates nothing.
    public static class FormationSort
    {
        public static void ByKey(int[] idx, int start, int n, float[] key, int[] tie, int[] tmp)
        {
            if (n < 2) return;
            if (n <= 24)
            {
                for (int i = start + 1; i < start + n; i++)
                {
                    int v = idx[i], j = i - 1;
                    while (j >= start && Less(v, idx[j], key, tie)) { idx[j + 1] = idx[j]; j--; }
                    idx[j + 1] = v;
                }
                return;
            }
            for (int width = 1; width < n; width *= 2)
            {
                for (int lo = 0; lo < n; lo += 2 * width)
                {
                    int mid = Math.Min(lo + width, n), hi = Math.Min(lo + 2 * width, n);
                    int a = lo, b = mid, o = lo;
                    while (a < mid && b < hi) tmp[o++] = Less(idx[start + b], idx[start + a], key, tie) ? idx[start + b++] : idx[start + a++];
                    while (a < mid) tmp[o++] = idx[start + a++];
                    while (b < hi) tmp[o++] = idx[start + b++];
                }
                Array.Copy(tmp, 0, idx, start, n);
            }
        }

        static bool Less(int x, int y, float[] key, int[] tie) => key[x] < key[y] || key[x] == key[y] && tie[x] < tie[y];
    }
}
