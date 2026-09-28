// FormationSnap.cs - moves slots off ground their units cannot stand on.
// Slots go in exposure order, so the front keeps the best ground. A slot
// that fits stays, else it takes the nearest free spot that fits, ring by
// ring out to 16 cells, preferring spots behind it. Each placed slot claims
// its footprint's cells, and a slot with nowhere to go is sent as drawn.
// Pure: the ground is behind an interface.
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
    }

    public sealed class FormationSnap
    {
        int[] claimed = new int[0];
        int stamp, budget;

        // Spots tried by the last Snap's searches. A formation spilling far
        // off the map would try thousands per slot, so the searches share a
        // budget and the slots left over are sent as drawn.
        public int Tried { get; private set; }

        public void Snap(FormationLayout l, IFormationGround ground)
        {
            int n = l.Count;
            if (ground == null || ground.Width <= 0 || ground.Height <= 0)
            {
                for (int i = 0; i < n; i++) { l.Slots[i].World = l.Slots[i].Wanted; l.Slots[i].State = SlotState.Good; }
                return;
            }
            int w = ground.Width, h = ground.Height;
            if (claimed.Length < w * h) { claimed = new int[w * h]; stamp = 0; }
            if (++stamp == int.MaxValue) { System.Array.Clear(claimed, 0, claimed.Length); stamp = 1; }
            budget = FormationTuning.SnapBudget;
            Tried = 0;

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
                    if (budget <= 0) return false;
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

        bool Fits(IFormationGround g, FormationMover mover, int fp, float gx, float gy)
        {
            int x0 = First(gx, fp), y0 = First(gy, fp), w = g.Width, h = g.Height;
            if (x0 < 0 || y0 < 0 || x0 + fp > w || y0 + fp > h) return false;
            for (int y = y0; y < y0 + fp; y++)
                for (int x = x0; x < x0 + fp; x++)
                    if (claimed[y * w + x] == stamp || !g.CanStand(mover, x, y)) return false;
            return true;
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
