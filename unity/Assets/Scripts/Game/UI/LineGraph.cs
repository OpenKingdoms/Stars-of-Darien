// LineGraph.cs - the end screen's graph, every line in one mesh.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Game.UI
{
    // A graph's lines in one mesh: each a run of points in 0 to 1 of the
    // rect, x right and y up, in its own colour and width.
    public sealed class LineGraph : MaskableGraphic
    {
        public struct Line
        {
            public Vector2[] Points;
            public Color Colour;
            public float Width;
        }

        public readonly List<Line> Lines = new List<Line>();
        // The vertices the last rebuild made, for tests.
        public int Drawn { get; private set; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            foreach (var l in Lines)
            {
                if (l.Points == null || l.Points.Length < 2) continue;
                for (int i = 1; i < l.Points.Length; i++)
                    Segment(vh, At(r, l.Points[i - 1]), At(r, l.Points[i]), l.Width, l.Colour);
            }
            Drawn = vh.currentVertCount;
        }

        static Vector2 At(Rect r, Vector2 f) => new Vector2(r.xMin + f.x * r.width, r.yMin + Mathf.Clamp01(f.y) * r.height);

        // A quad from a to b, run on by half its width at each end so the
        // joins close.
        static void Segment(VertexHelper vh, Vector2 a, Vector2 b, float w, Color c)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            d.Normalize();
            var n = new Vector2(-d.y, d.x) * (w / 2f);
            var e = d * (w / 2f);
            int i = vh.currentVertCount;
            vh.AddVert(a - e - n, c, Vector2.zero);
            vh.AddVert(a - e + n, c, Vector2.zero);
            vh.AddVert(b + e + n, c, Vector2.zero);
            vh.AddVert(b + e - n, c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
