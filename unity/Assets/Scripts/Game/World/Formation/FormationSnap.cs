// FormationSnap.cs - moves slots off ground their units cannot stand on.
// Slots go in exposure order, so the front keeps the best ground. A slot
// that fits stays, else it takes the nearest free spot that fits, ring by
// ring out to 16 cells, preferring spots behind it. A spot counts only in
// the region its unit can reach, so a line at a river stays on its bank.
// Each placed slot claims its footprint's cells, and a slot with nowhere
// to go is sent as drawn. Pure: the ground is behind an interface.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    // Cells are one world unit square. Cell (x, y) covers world x from x to
    // x + 1 and z from -y to -(y + 1), rows running south as the map's do.
    public interface IFormationGround
    {
        int Width { get; }
        int Height { get; }
        bool CanStand(FormationMover mover, int x, int y);
        // The connected stretch of ground (or water) a cell is in for a
        // mover, -1 where it cannot stand. A unit reaches only its own.
        int Region(FormationMover mover, int x, int y);
    }

    public static class FormationRegions
    {
        // Labels each cell whose mask has the bit with its 4-connected
        // region, from 0, and the rest -1. Returns the number of regions.
        public static int Label(int w, int h, byte[] mask, byte bit, int[] ids)
        {
            int n = w * h;
            for (int i = 0; i < n; i++) ids[i] = -1;
            var stack = new int[n];
            int next = 0;
            for (int start = 0; start < n; start++)
            {
                if (ids[start] >= 0 || (mask[start] & bit) == 0) continue;
                int top = 0;
                stack[top++] = start;
                ids[start] = next;
                while (top > 0)
                {
                    int c = stack[--top], x = c % w;
                    if (x > 0 && Open(c - 1)) stack[top++] = c - 1;
                    if (x < w - 1 && Open(c + 1)) stack[top++] = c + 1;
                    if (c >= w && Open(c - w)) stack[top++] = c - w;
                    if (c + w < n && Open(c + w)) stack[top++] = c + w;
                }
                next++;
            }
            return next;

            bool Open(int c)
            {
                if (ids[c] >= 0 || (mask[c] & bit) == 0) return false;
                ids[c] = next;
                return true;
            }
        }
    }

    public sealed class FormationSnap
    {
        int[] claimed = new int[0];
        int stamp, budget;

        // Spots tried by the last Snap's searches. A formation spilling far
        // off the map would try thousands per slot, so the searches share a
        // budget and the slots left over are sent as drawn.
        public int Tried { get; private set; }

        // keepClaims: the cells the last Snap placed stay taken, so the water
        // layer's boats and the ground layer's hovers never share a cell.
        public void Snap(FormationLayout l, IFormationGround ground, bool keepClaims = false)
        {
            int n = l.Count;
            if (ground == null || ground.Width <= 0 || ground.Height <= 0)
            {
                for (int i = 0; i < n; i++) { l.Slots[i].World = l.Slots[i].Wanted; l.Slots[i].State = SlotState.Good; }
                return;
            }
            int w = ground.Width, h = ground.Height;
            if (claimed.Length < w * h) { claimed = new int[w * h]; stamp = 0; keepClaims = false; }
            if (!keepClaims || stamp == 0) { if (++stamp == int.MaxValue) { System.Array.Clear(claimed, 0, claimed.Length); stamp = 1; } }
            budget = FormationTuning.SnapBudget;
            Tried = 0;
            var middle = l.A + l.Dir * (l.Length * 0.5f);

            for (int o = 0; o < n; o++)
            {
                ref var s = ref l.Slots[l.SnapOrder[o]];
                int mi = s.Member >= 0 ? s.Member : l.SnapOrder[o];
                var m = l.Members[mi];
                int fp = FormationPlanner.Footprint(m);
                // Inside the map by half a footprint.
                float half = fp * 0.5f;
                var want = new Vector2(Mathf.Clamp(s.Wanted.x, half, Mathf.Max(half, w - half)), Mathf.Clamp(s.Wanted.y, -Mathf.Max(half, h - half), -half));
                s.Wanted = want;
                float gx = want.x, gy = -want.y;
                // Where its unit stands, else where the line was drawn.
                region = RegionNear(ground, m.Kind.Mover, m.Position);
                if (region < 0) region = RegionNear(ground, m.Kind.Mover, middle);
                if (Fits(ground, m.Kind.Mover, fp, gx, gy))
                {
                    Claim(w, fp, gx, gy);
                    s.World = want;
                    s.State = SlotState.Good;
                    continue;
                }
                if (Search(ground, m.Kind.Mover, fp, gx, gy, l.Face, out float bx, out float by))
                {
                    Claim(w, fp, bx, by);
                    // Centred on the cells it claimed.
                    s.World = new Vector2(First(bx, fp) + half, -(First(by, fp) + half));
                    s.State = SlotState.Snapped;
                }
                else
                {
                    s.World = want;
                    s.State = SlotState.Nowhere;
                }
            }
        }

        // The nearest fitting spot ring by ring, nearer first, then behind
        // before beside before in front.
        bool Search(IFormationGround g, FormationMover mover, int fp, float gx, float gy, Vector2 face, out float bx, out float by)
        {
            bx = by = 0;
            for (int r = 1; r <= FormationTuning.SnapFar; r++)
            {
                float best = float.MaxValue;
                for (int k = 0; k < 8 * r; k++)
                {
                    Ring(r, k, out int dx, out int dy);
                    // World offset (dx, -dy). Behind is against the facing.
                    float ahead = dx * face.x - dy * face.y;
                    float score = (dx * dx + dy * dy) * 4f + (ahead < -1e-4f ? 0f : ahead > 1e-4f ? 2f : 1f);
                    if (score >= best) continue;
                    if (budget <= 0) return best < float.MaxValue;
                    budget--;
                    Tried++;
                    if (!Fits(g, mover, fp, gx + dx, gy + dy)) continue;
                    best = score;
                    bx = gx + dx;
                    by = gy + dy;
                }
                if (best < float.MaxValue) return true;
            }
            return false;
        }

        // The k-th of the 8r cells on the square ring r out, going round it.
        static void Ring(int r, int k, out int dx, out int dy)
        {
            int side = 2 * r, i = k % side;
            switch (k / side)
            {
                case 0: dx = -r + i; dy = -r; break;
                case 1: dx = r; dy = -r + i; break;
                case 2: dx = r - i; dy = r; break;
                default: dx = -r; dy = r - i; break;
            }
        }

        static int First(float centre, int fp) => Mathf.FloorToInt(centre - fp * 0.5f + 0.5f);

        // The region of the cell at a world point, or of one close by when
        // that cell is not standable (a unit at the water's edge).
        static int RegionNear(IFormationGround g, FormationMover mover, Vector2 at)
        {
            int cx = Mathf.FloorToInt(at.x), cy = Mathf.FloorToInt(-at.y);
            for (int r = 0; r <= 2; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= g.Width || y >= g.Height) continue;
                        int id = g.Region(mover, x, y);
                        if (id >= 0) return id;
                    }
            return -1;
        }

        // The region the slot being placed must stay in, -1 for any.
        int region;

        bool Fits(IFormationGround g, FormationMover mover, int fp, float gx, float gy)
        {
            int x0 = First(gx, fp), y0 = First(gy, fp), w = g.Width, h = g.Height;
            if (x0 < 0 || y0 < 0 || x0 + fp > w || y0 + fp > h) return false;
            for (int y = y0; y < y0 + fp; y++)
                for (int x = x0; x < x0 + fp; x++)
                    if (claimed[y * w + x] == stamp || !g.CanStand(mover, x, y)) return false;
            return region < 0 || g.Region(mover, x0, y0) == region;
        }

        void Claim(int w, int fp, float gx, float gy)
        {
            int x0 = First(gx, fp), y0 = First(gy, fp);
            for (int y = y0; y < y0 + fp; y++)
                for (int x = x0; x < x0 + fp; x++)
                    claimed[y * w + x] = stamp;
        }
    }
}
