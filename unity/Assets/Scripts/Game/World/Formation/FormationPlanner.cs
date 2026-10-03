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
        public const float ClassicRightHoldSeconds = 0.2f;  // the classic right button stays a cancel until held this long
        public const float Gap = 1f;                // between neighbours in Line, Block and Wedge
        public const float BlockGap = 1f;           // extra depth between role blocks in a Line
        public const float WingGap = 1f;            // between the front block and a wing
        public const float WedgeDepth = 0.87f;      // a wedge rank's depth, in pitches
        public const int MaxRanks = 4;              // the deepest a Line or Loose role block gets
        public const int HungarianMax = 64;         // per role group, above it the projection sort
        public const int SnapFar = 16;
        public const int SnapBudget = 16000;        // spots tried per layer and layout
        public const float RecomputeCells = 0.25f, RecomputeSeconds = 0.25f;
        public const int SlotLinesMax = 24;
        public const float QueuedAlpha = 0.4f, QueuedArrive = 1f, QueuedSeconds = 120f;
        public const int UnitsPerCall = 256;
        public const float StandingNear = 3f, StandingAgree = 0.6f;
        public const float StandingRank = 1f;       // depth of a rough rank, as they stand
        public const float ShortestLine = 2f;       // cells, below it the drag gives no direction
        public const int ReadoutKeyDrags = 3;       // drags that show the key hints
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
        public int Class;               // only members of this class take it (0 for most)
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
        public bool Drawn;              // the drag was long enough to set the direction
        internal float[] Keys = Array.Empty<float>();
        internal int[] Tie = Array.Empty<int>(), Tmp = Array.Empty<int>(), Work = Array.Empty<int>();
        internal int[] Classes = Array.Empty<int>();    // per member, against the slots' Class

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
            Classes = new int[size];
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
        // come after, in FormationAssign and FormationSnap. A drag shorter
        // than ShortestLine faces the camera's way (or the facing rule's)
        // and centres the formation on the press, as a click would.
        public static void Plan(FormationLayout l, in FormationRequest req)
        {
            int n = l.Count;
            l.Reserve(n);
            l.A = req.A;
            l.B = req.B;
            var d = req.B - req.A;
            float len = d.magnitude;
            l.Drawn = len >= FormationTuning.ShortestLine;
            if (!l.Drawn)
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
            SetFace(l, f);
            l.Fixed = req.AsTheyStand;
            for (int i = 0; i < n; i++) l.Classes[i] = 0;
            if (n == 0) { l.Frontage = 0; l.Origin = req.A; l.Wide = l.Deep = 0; return; }

            int slots;
            bool wedge = false;
            if (req.AsTheyStand) slots = AsTheyStand(l, req.FaceAbout);
            else if (n == 1) slots = Single(l);
            else
                switch (req.Shape)
                {
                    case FormationShape.Block: slots = Block(l); break;
                    case FormationShape.Wedge: slots = Wedge(l); wedge = true; break;
                    case FormationShape.Loose: slots = Loose(l); break;
                    default: slots = Line(l); break;
                }
            if (!req.AsTheyStand)
            {
                l.Origin = OriginOf(l, wedge, slots);
                for (int i = 0; i < slots; i++)
                {
                    ref var s = ref l.Slots[i];
                    s.Wanted = l.Origin + l.Right * s.Local.x + l.Face * s.Local.y;
                    s.World = s.Wanted;
                    s.Member = -1;
                    s.State = SlotState.Good;
                }
            }
            bool exposure = !req.AsTheyStand && n > 1 && (req.Shape == FormationShape.Block || wedge);
            if (!exposure) FrontFirst(l);
            Measure(l, wedge, req.AsTheyStand ? FormationTuning.StandingRank : 0.01f);
        }

        static void SetFace(FormationLayout l, Vector2 f)
        {
            l.Face = f;
            l.Right = new Vector2(f.y, -f.x);
            l.Heading = HeadingOf(f);
        }

        // A drawn formation grows out from the press along the line, a
        // wedge with its point on the line. An undrawn one centres on it.
        static Vector2 OriginOf(FormationLayout l, bool wedge, int k)
        {
            if (!l.Drawn)
            {
                float top = float.MinValue, bottom = float.MaxValue;
                for (int i = 0; i < k; i++) { top = Mathf.Max(top, l.Slots[i].Local.y); bottom = Mathf.Min(bottom, l.Slots[i].Local.y); }
                return l.A - l.Face * ((top + bottom) * 0.5f);
            }
            float along = wedge ? Mathf.Min(l.Frontage, l.Length) : l.Frontage;
            return l.A + l.Dir * (along * 0.5f);
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

        static int CountOf(FormationLayout l, FormationRole r, int cls)
        {
            int c = 0;
            for (int i = 0; i < l.Count; i++) if (l.Members[i].Kind.Role == r && l.Classes[i] == cls) c++;
            return c;
        }

        static int WidestOf(FormationLayout l, FormationRole r)
        {
            int w = 0;
            for (int i = 0; i < l.Count; i++) if (l.Members[i].Kind.Role == r) w = Mathf.Max(w, Pitch(l.Members[i]));
            return w;
        }

        public static int Footprint(in FormationMember m) => m.Kind.Footprint > 0 ? m.Kind.Footprint : FormationRoles.DefaultFootprint;

        // The room a member takes in a formation: its footprint, or a ship's
        // hull, which is longer than the cells it sails on.
        public static int Pitch(in FormationMember m) => Mathf.Max(Footprint(m), m.Kind.Spacing);

        static int PerRank(float frontage, float pitch, int n) =>
            Mathf.Clamp(Mathf.FloorToInt(frontage / pitch + 1e-4f), 1, Mathf.Max(1, n));

        static int CeilDiv(int a, int b) => (a + b - 1) / b;

        static void Put(FormationLayout l, ref int k, FormationRole role, int cls, float x, float y)
        {
            l.Slots[k].Local = new Vector2(x, y);
            l.Slots[k].Role = role;
            l.Slots[k].Class = cls;
            k++;
        }

        // Ranks of n at pitch p from y0 back, the last one centred. A last
        // rank of under a third widens the others by one or two instead,
        // so nobody stands alone behind. Returns the last rank's y.
        static float Ranks(FormationLayout l, ref int k, FormationRole role, int cls, int n, float p, float frontage, float y0, out int perRank, out int ranks)
        {
            perRank = PerRank(frontage, p, n);
            ranks = CeilDiv(n, perRank);
            if (ranks > 1 && 3 * (n - perRank * (ranks - 1)) < perRank)
            {
                int wider = CeilDiv(n, ranks - 1);
                if (3 * (wider - perRank) <= Mathf.Max(3, perRank)) { perRank = wider; ranks--; }
            }
            for (int j = 0; j < ranks; j++)
            {
                int c = j < ranks - 1 ? perRank : n - perRank * (ranks - 1);
                for (int i = 0; i < c; i++) Put(l, ref k, role, cls, (i - (c - 1) * 0.5f) * p, y0 - j * p);
            }
            return y0 - (ranks - 1) * p;
        }

        // ---- Common footprints ----

        // The regular members of a group stand at the pitch of the footprint
        // most of them share. Wider ones (class 1) stand in ranks of their
        // own behind, at theirs, so one catapult does not spread a block.
        struct Group { public int Count, Big; public float Pitch, BigPitch; }

        static readonly int[] footprints = new int[33];

        static Group Split(FormationLayout l, FormationRole role, bool all)
        {
            Array.Clear(footprints, 0, footprints.Length);
            for (int i = 0; i < l.Count; i++)
                if (all || l.Members[i].Kind.Role == role) footprints[Mathf.Clamp(Pitch(l.Members[i]), 1, 32)]++;
            int common = FormationRoles.DefaultFootprint, most = 0;
            for (int fp = 1; fp < footprints.Length; fp++) if (footprints[fp] > most) { common = fp; most = footprints[fp]; }
            var g = new Group { Pitch = common + FormationTuning.Gap };
            int widest = 0;
            for (int i = 0; i < l.Count; i++)
            {
                if (!all && l.Members[i].Kind.Role != role) continue;
                int fp = Pitch(l.Members[i]);
                if (fp > common) { l.Classes[i] = 1; g.Big++; widest = Mathf.Max(widest, fp); }
                else { l.Classes[i] = 0; g.Count++; }
            }
            g.BigPitch = widest + FormationTuning.Gap;
            return g;
        }

        // ---- One unit ----

        // Half a pitch along the line in every shape, or on the press.
        static int Single(FormationLayout l)
        {
            var m = l.Members[0];
            l.Slots[0].Local = Vector2.zero;
            l.Slots[0].Role = m.Kind.Role;
            l.Slots[0].Class = 0;
            l.Frontage = Pitch(m) + FormationTuning.Gap;
            return 1;
        }

        // ---- Line ----

        static readonly Group[] lineGroups = new Group[6];

        static int Line(FormationLayout l)
        {
            int cav = CountOf(l, FormationRole.Cavalry);
            bool wings = cav > 0 && (CountOf(l, FormationRole.Melee) > 0 || CountOf(l, FormationRole.Ranged) > 0);
            // A block never deeper than MaxRanks, and no wider than it needs.
            float minW = 0, maxW = 0;
            for (int r = 0; r < lineGroups.Length; r++)
            {
                var role = (FormationRole)r;
                lineGroups[r] = default;
                if (wings && role == FormationRole.Cavalry) continue;
                if (CountOf(l, role) == 0) continue;
                var g = lineGroups[r] = Split(l, role, false);
                minW = Mathf.Max(minW, g.Pitch * CeilDiv(g.Count, FormationTuning.MaxRanks));
                maxW = Mathf.Max(maxW, g.Count * g.Pitch);
                if (g.Big == 0) continue;
                minW = Mathf.Max(minW, g.BigPitch * CeilDiv(g.Big, FormationTuning.MaxRanks));
                maxW = Mathf.Max(maxW, g.Big * g.BigPitch);
            }
            float w = Mathf.Clamp(l.Length, minW, Mathf.Max(minW, maxW));
            float widest = w;

            int k = 0;
            float lastY = 0, prevP = -1;
            bool first = true;
            int frontRanks = 1, frontCount = 0;
            float frontP = 0;
            for (int b = -1; b < LineOrder.Length; b++)
            {
                var role = b < 0 ? FormationRole.Cavalry : LineOrder[b];
                if (b < 0 && wings) continue;
                var g = lineGroups[(int)role];
                if (g.Count == 0) continue;
                float y = prevP > 0 ? lastY - (prevP + g.Pitch) * 0.5f - FormationTuning.BlockGap : 0f;
                lastY = Ranks(l, ref k, role, 0, g.Count, g.Pitch, w, y, out int perRank, out int ranks);
                widest = Mathf.Max(widest, perRank * g.Pitch);
                prevP = g.Pitch;
                if (g.Big > 0)
                {
                    lastY = Ranks(l, ref k, role, 1, g.Big, g.BigPitch, w, lastY - (g.Pitch + g.BigPitch) * 0.5f, out int bigPer, out int bigRanks);
                    widest = Mathf.Max(widest, bigPer * g.BigPitch);
                    prevP = g.BigPitch;
                    ranks += bigRanks;
                }
                if (first) { frontRanks = ranks; frontCount = Mathf.Min(perRank, g.Count); frontP = g.Pitch; first = false; }
            }
            l.Frontage = widest;
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
                        Put(l, ref k, FormationRole.Cavalry, 0, side == 0 ? -x : x, -j * step);
                    }
                }
            }
            return k;
        }

        // ---- Block ----

        static int Block(FormationLayout l)
        {
            var g = Split(l, FormationRole.Melee, true);
            int n = g.Count;
            float p = g.Pitch;
            // At least as wide as deep.
            int minCols = Mathf.CeilToInt(Mathf.Sqrt(n) - 1e-4f);
            float w = Mathf.Clamp(l.Length, minCols * p, Mathf.Max(minCols, n) * p);
            int cols = PerRank(w, p, n);
            int rows = (n + cols - 1) / cols;
            l.Frontage = cols * p;
            float span = cols * p + 1f;
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
                    l.Slots[k].Class = 0;
                    // Ring, then face, sides front to back, then out from the
                    // centre line. Equal keys fall to the slot index, left first.
                    l.Keys[k] = ((q * 3 + face) * (rows + 1) + (face == 1 ? r : 0)) * span + Mathf.Abs(x);
                    l.SnapOrder[k] = k;
                    k++;
                }
            }
            FillByExposure(l, k, FillOrder);
            if (g.Big == 0) return k;

            // The wide ones behind, centre first, the command and siege there.
            int start = k;
            float y = -(rows - 1) * p - (p + g.BigPitch) * 0.5f - FormationTuning.BlockGap;
            Ranks(l, ref k, FormationRole.Melee, 1, g.Big, g.BigPitch, l.Frontage, y, out _, out _);
            for (int s = start; s < k; s++)
            {
                l.Keys[s] = -l.Slots[s].Local.y * 1e3f + Mathf.Abs(l.Slots[s].Local.x);
                l.Tie[s] = s;
                l.SnapOrder[s] = s;
            }
            FormationSort.ByKey(l.SnapOrder, start, k - start, l.Keys, l.Tie, l.Tmp);
            int at = start;
            for (int o = FillOrder.Length - 1; o >= 0; o--)
            {
                int c = CountOf(l, FillOrder[o], 1);
                for (int j = 0; j < c && at < k; j++) l.Slots[l.SnapOrder[at++]].Role = FillOrder[o];
            }
            return k;
        }

        // ---- Wedge ----

        static float[] rankPitch = new float[64];

        // Each rank at the pitch of the widest unit in it. Members go to the
        // slots in exposure order, by role and then the narrow first, so a
        // wide one lands further in and a slot's class is its footprint.
        static int Wedge(FormationLayout l)
        {
            int n = l.Count;
            int ranks = 1;
            while (ranks * (ranks + 1) / 2 < n) ranks++;
            if (rankPitch.Length < ranks) rankPitch = new float[Mathf.NextPowerOfTwo(ranks)];

            // Exposure from the shape alone: the point, then the outer edges
            // and the open rear rank front to back, then the inside from the
            // outside in, so the last to fill (the monarch) stands in the middle.
            float cy = 0;
            for (int r = 0, left = n; r < ranks; r++) { int c = Mathf.Min(r + 1, left); cy += r * c; left -= c; }
            cy /= n;
            int k = 0, placed = 0;
            for (int r = 0; r < ranks; r++)
            {
                int c = Mathf.Min(r + 1, n - placed);
                for (int i = 0; i < c; i++)
                {
                    float xi = i - (c - 1) * 0.5f;
                    l.Slots[k].Local = new Vector2(xi, 0);
                    bool edge = i == 0 || i == c - 1 || r == ranks - 1;
                    float dist = new Vector2(xi, (r - cy) * FormationTuning.WedgeDepth).magnitude;
                    l.Keys[k] = r == 0 ? 0 : edge ? 1e5f + r * 1e3f + (i == 0 ? 0 : i == c - 1 ? 1 : 2 + i) : 2e5f + (1e3f - dist) * 10f;
                    l.Tie[k] = k;
                    l.SnapOrder[k] = k;
                    k++;
                }
                placed += c;
            }
            FormationSort.ByKey(l.SnapOrder, 0, k, l.Keys, l.Tie, l.Tmp);

            // Members by role order, then footprint, then handle order.
            for (int i = 0; i < n; i++)
            {
                var m = l.Members[i];
                int fp = Pitch(m);
                l.Classes[i] = fp;
                l.Work[i] = i;
                l.Keys[i] = WedgeRank(m.Kind.Role) * 64 + Mathf.Min(fp, 63);
                l.Tie[i] = i;
            }
            FormationSort.ByKey(l.Work, 0, n, l.Keys, l.Tie, l.Tmp);
            for (int r = 0; r < ranks; r++) rankPitch[r] = 0;
            for (int o = 0; o < n; o++)
            {
                int mi = l.Work[o], si = l.SnapOrder[o];
                ref var s = ref l.Slots[si];
                s.Role = l.Members[mi].Kind.Role;
                s.Class = l.Classes[mi];
                int r = RankOf(si);
                rankPitch[r] = Mathf.Max(rankPitch[r], s.Class + FormationTuning.Gap);
            }

            // The drag stretches the ranks' spacing up to twice their pitch.
            float rear = rankPitch[ranks - 1];
            float stretch = ranks > 1 ? Mathf.Clamp(l.Length / (rear * (ranks - 1)), 1f, 2f) : 1f;
            float y = 0, frontage = 0;
            int at = 0;
            for (int r = 0; r < ranks; r++)
            {
                if (r > 0) y -= FormationTuning.WedgeDepth * (rankPitch[r - 1] + rankPitch[r]) * 0.5f;
                int c = Mathf.Min(r + 1, n - at);
                float q = rankPitch[r] * stretch;
                for (int i = 0; i < c; i++)
                {
                    ref var s = ref l.Slots[at + i];
                    s.Local = new Vector2(s.Local.x * q, y);
                }
                frontage = Mathf.Max(frontage, (c - 1) * q + rankPitch[r]);
                at += c;
            }
            l.Frontage = frontage;
            return k;
        }

        static int WedgeRank(FormationRole role)
        {
            for (int i = 0; i < WedgeOrder.Length; i++) if (WedgeOrder[i] == role) return i;
            return WedgeOrder.Length;
        }

        // A wedge's slots are laid rank by rank, r + 1 to rank r.
        static int RankOf(int slot)
        {
            int r = Mathf.FloorToInt((Mathf.Sqrt(8f * slot + 1f) - 1f) * 0.5f + 1e-4f);
            while (r > 0 && r * (r + 1) / 2 > slot) r--;
            while ((r + 1) * (r + 2) / 2 <= slot) r++;
            return r;
        }

        // Sorts slot indices by their exposure key into SnapOrder, then
        // hands them out to the roles in order, class 0 members only.
        static void FillByExposure(FormationLayout l, int k, FormationRole[] order)
        {
            for (int i = 0; i < k; i++) l.Tie[i] = i;
            FormationSort.ByKey(l.SnapOrder, 0, k, l.Keys, l.Tie, l.Tmp);
            int at = 0;
            foreach (var role in order)
            {
                int c = CountOf(l, role, 0);
                for (int j = 0; j < c && at < k; j++) l.Slots[l.SnapOrder[at++]].Role = role;
            }
        }

        // ---- Loose ----

        // The front role groups spread over the line. Smaller groups, and
        // the command and siege always, keep their own loose pitch in the
        // middle behind them rather than stretching out to the flanks.
        static int Loose(FormationLayout l)
        {
            float minLen = 0;
            foreach (var role in LooseOrder)
            {
                int c = CountOf(l, role);
                if (c == 0 || Stays(role)) continue;
                minLen = Mathf.Max(minLen, (CeilDiv(c, FormationTuning.MaxRanks) - 1) * (2 * WidestOf(l, role) + FormationTuning.Gap));
            }
            float len = Mathf.Max(l.Length, minLen);
            l.Frontage = len;
            int k = 0, frontPer = 0;
            float y = 0, lastY = 0, prevP = -1;
            foreach (var role in LooseOrder)
            {
                int n = CountOf(l, role);
                if (n == 0) continue;
                float p = 2 * WidestOf(l, role) + FormationTuning.Gap;
                if (prevP > 0) y = lastY - (prevP + p) * 0.5f - FormationTuning.BlockGap;
                int per = Mathf.Clamp(Mathf.FloorToInt(len / p + 1e-4f) + 1, 1, n);
                bool spread = !Stays(role) && n >= frontPer;
                float s = spread ? (per > 1 ? len / (per - 1) : 0f) : p;
                if (spread && frontPer == 0) frontPer = per;
                int rows = (n + per - 1) / per;
                for (int j = 0; j < rows; j++)
                {
                    int c = j < rows - 1 ? per : n - per * (rows - 1);
                    float shift = (j & 1) == 1 ? s * 0.5f : 0f;
                    for (int i = 0; i < c; i++) Put(l, ref k, role, 0, (i - (c - 1) * 0.5f) * s + shift, y - j * p);
                }
                lastY = y - (rows - 1) * p;
                prevP = p;
            }
            return k;
        }

        static bool Stays(FormationRole role) => role == FormationRole.Command || role == FormationRole.Siege;

        // ---- As they stand ----

        // The group's layout turned to the new facing, its front on the
        // line. With no line drawn it keeps its facing (F turns it about)
        // and its middle goes to the press.
        static int AsTheyStand(FormationLayout l, bool faceAbout)
        {
            int n = l.Count;
            var mid = l.A + l.Dir * (l.Length * 0.5f);
            var c = Vector2.zero;
            for (int i = 0; i < n; i++) c += Standing(l.Members[i]);
            c /= n;
            var g = StandingFacing(l.Members, n, mid);
            if (!l.Drawn) SetFace(l, faceAbout ? -g : g);
            float cos = Vector2.Dot(g, l.Face), sin = g.x * l.Face.y - g.y * l.Face.x;
            float front = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var v = Standing(l.Members[i]) - c;
                var o = new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
                l.Slots[i].Wanted = o;
                front = Mathf.Max(front, Vector2.Dot(o, l.Face));
            }
            l.Origin = l.Drawn ? mid : l.A;
            float shift = l.Drawn ? front : 0f;
            l.Frontage = 0;
            for (int i = 0; i < n; i++)
            {
                ref var s = ref l.Slots[i];
                var o = s.Wanted - l.Face * shift;
                s.Local = new Vector2(Vector2.Dot(o, l.Right), Vector2.Dot(o, l.Face));
                s.Wanted = l.Origin + o;
                s.World = s.Wanted;
                s.Role = l.Members[i].Kind.Role;
                s.Class = 0;
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
