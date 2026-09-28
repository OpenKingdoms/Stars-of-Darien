// GalleryLayout.cs - the gallery's grid in name order, left to right and
// then south. Each column is as wide as its widest plinth and each row as
// deep as its deepest, a gap apart, so nothing overlaps. x east, y north.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    public static class GalleryLayout
    {
        public const float Gap = 1f;

        public struct Slot
        {
            public int Item;        // index into the list given
            public Vector2 Centre;  // cells, x east and y north
            public Vector2 Size;    // the plinth, cells
            public Rect Rect => new Rect(Centre - Size * 0.5f, Size);
        }

        public sealed class Result
        {
            public readonly List<Slot> Slots = new List<Slot>();
            public int Cols, Rows;
            public Rect Bounds;     // round every slot, cells
        }

        // The filter, ignoring case, anywhere in the name.
        public static bool Matches(string name, string filter) =>
            string.IsNullOrWhiteSpace(filter) || (name ?? "").IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;

        // Items whose names match the filter, in the order given, in a grid
        // near square in count.
        public static Result Arrange(IReadOnlyList<string> names, IReadOnlyList<Vector2> sizes, string filter, float gap = Gap)
        {
            var r = new Result();
            var pick = new List<int>();
            for (int i = 0; i < names.Count; i++) if (Matches(names[i], filter)) pick.Add(i);
            if (pick.Count == 0) return r;
            int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(pick.Count)));
            int rows = (pick.Count + cols - 1) / cols;
            var colW = new float[cols];
            var rowD = new float[rows];
            for (int k = 0; k < pick.Count; k++)
            {
                var s = Size(sizes[pick[k]]);
                colW[k % cols] = Mathf.Max(colW[k % cols], s.x);
                rowD[k / cols] = Mathf.Max(rowD[k / cols], s.y);
            }
            var colX = new float[cols];
            var rowY = new float[rows];
            float x = 0f;
            for (int c = 0; c < cols; c++) { colX[c] = x + colW[c] * 0.5f; x += colW[c] + gap; }
            float y = 0f;
            for (int w = 0; w < rows; w++) { rowY[w] = y - rowD[w] * 0.5f; y -= rowD[w] + gap; }
            for (int k = 0; k < pick.Count; k++)
                r.Slots.Add(new Slot { Item = pick[k], Centre = new Vector2(colX[k % cols], rowY[k / cols]), Size = Size(sizes[pick[k]]) });
            r.Cols = cols;
            r.Rows = rows;
            r.Bounds = Rect.MinMaxRect(0f, y + gap, x - gap, 0f);
            return r;
        }

        static Vector2 Size(Vector2 s) => new Vector2(Mathf.Max(0.1f, s.x), Mathf.Max(0.1f, s.y));

        // Names in the order a person counts: Tree2 before Tree10.
        public static int NaturalCompare(string a, string b)
        {
            a ??= "";
            b ??= "";
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string na = a.Substring(si, i - si).TrimStart('0'), nb = b.Substring(sj, j - sj).TrimStart('0');
                    if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                    int c = string.CompareOrdinal(na, nb);
                    if (c != 0) return c;
                    continue;
                }
                int d = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                if (d != 0) return d;
                i++;
                j++;
            }
            int rest = (a.Length - i).CompareTo(b.Length - j);
            return rest != 0 ? rest : string.CompareOrdinal(a, b);
        }
    }
}
