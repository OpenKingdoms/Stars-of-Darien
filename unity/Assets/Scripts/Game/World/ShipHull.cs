// ShipHull.cs - a ship model's hull at the waterline, from its pieces at
// rest: how deep the keel sits, and how long and wide the hull is where it
// meets the water. The hull is the piece with the most to it, wide and low
// in the model. Its sides are the part of it out toward the beam (masts
// stand in the middle), and it sits a third of its side under water, but
// never so deep that its gunports go under.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public readonly struct ShipHull
    {
        // Keel: the hull's lowest point above the model's origin. Draft: how
        // far under the surface the keel sits. Middle: the waterline's middle
        // along the model's z. All in world units.
        public readonly float Keel, Draft, HalfBeam, HalfLength, Middle;

        public ShipHull(float keel, float draft, float halfBeam, float halfLength, float middle)
        {
            Keel = keel; Draft = draft; HalfBeam = halfBeam; HalfLength = halfLength; Middle = middle;
        }

        public const float SideUnder = 0.3f, MinDraft = 0.15f, MaxDraft = 1f;

        // How much deeper than the backends' lift the model is drawn, so its
        // keel sits Draft under the surface.
        public float Sink => Mathf.Max(0f, Draft - (Afloat.Draft - Keel));

        public static ShipHull Of(PresentedModel m)
        {
            var b = m.RestBounds;
            var fallback = new ShipHull(b.min.y, 0.3f, Mathf.Max(b.extents.x * 0.5f, 0.25f), Mathf.Max(b.extents.z * 0.8f, 0.5f), b.center.z);
            var pieces = m.Data?.Pieces;
            if (pieces == null || m.Pieces == null) return fallback;
            int n = Mathf.Min(pieces.Length, m.Pieces.Length);
            var rest = new Matrix4x4[n];
            var box = new Bounds[n];
            int hull = -1;
            float best = 0f;
            for (int p = 0; p < n; p++)
            {
                var at = Matrix4x4.Translate(pieces[p].Offset * m.Data.Scale);
                int parent = pieces[p].Parent;
                rest[p] = parent >= 0 && parent < p ? rest[parent] * at : at;
                if (m.Pieces[p] == null) continue;
                var pb = m.Pieces[p].bounds;
                box[p] = new Bounds(rest[p].MultiplyPoint3x4(pb.center), pb.size);
                float score = m.Pieces[p].vertexCount * box[p].size.x * box[p].size.z / (1f + Mathf.Max(0f, box[p].min.y - b.min.y));
                if (score > best) { best = score; hull = p; }
            }
            if (hull < 0) return fallback;
            var verts = m.Pieces[hull].vertices;
            float keel = float.MaxValue, beam = 0f, cx = box[hull].center.x;
            for (int i = 0; i < verts.Length; i++)
            {
                var v = rest[hull].MultiplyPoint3x4(verts[i]);
                keel = Mathf.Min(keel, v.y);
                beam = Mathf.Max(beam, Mathf.Abs(v.x - cx));
            }
            // The rail: the highest point out toward the sides.
            float rail = keel;
            for (int i = 0; i < verts.Length; i++)
            {
                var v = rest[hull].MultiplyPoint3x4(verts[i]);
                if (Mathf.Abs(v.x - cx) > beam * 0.5f) rail = Mathf.Max(rail, v.y);
            }
            float side = Mathf.Max(rail - keel, 0.1f);
            float draft = side * SideUnder;
            // Anything else hanging low over the hull, cannons at their ports
            // or sails, stays dry. Oars and rudders reaching the keel dip.
            var hb = box[hull];
            for (int p = 0; p < n; p++)
            {
                if (p == hull || m.Pieces[p] == null) continue;
                var pb = box[p];
                if (pb.max.x < hb.min.x || pb.min.x > hb.max.x || pb.max.z < hb.min.z || pb.min.z > hb.max.z) continue;
                float low = pb.min.y - keel;
                if (low > side * 0.08f && low < draft + 0.08f) draft = low - 0.08f;
            }
            draft = Mathf.Clamp(draft, MinDraft, MaxDraft);
            // The waterline's length and width, from the hull below it.
            float z0 = float.MaxValue, z1 = float.MinValue, wide = 0f;
            for (int i = 0; i < verts.Length; i++)
            {
                var v = rest[hull].MultiplyPoint3x4(verts[i]);
                if (v.y > keel + draft + 0.05f) continue;
                z0 = Mathf.Min(z0, v.z);
                z1 = Mathf.Max(z1, v.z);
                wide = Mathf.Max(wide, Mathf.Abs(v.x - cx));
            }
            if (z1 < z0) { z0 = hb.min.z; z1 = hb.max.z; wide = beam; }
            return new ShipHull(keel, draft, Mathf.Max(wide, 0.2f), Mathf.Max((z1 - z0) * 0.5f, 0.4f), (z0 + z1) * 0.5f);
        }
    }
}
