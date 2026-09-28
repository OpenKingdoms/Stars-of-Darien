// FormationAssign.cs - which unit takes which slot. Roles decide the slots
// a unit may take, and within a role group units are matched to slots to
// keep paths short: the Hungarian algorithm on straight-line distance up to
// 64 units, whose paths never cross, and above that a sort by projection,
// whose paths stay uncrossed within a rank but may cross between ranks.
// A slot with a class takes only members of that class. Pure.
using System;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FormationAssign
    {
        // Squared distance, weighted far below a cell, breaks ties toward
        // the matching that moves every unit alike (a group moved without
        // turning keeps its places).
        const double TieWeight = 1e-9;

        int[] slots = new int[64], members = new int[64], work = new int[64], tmp = new int[64], tie = new int[64];
        readonly int[] classes = new int[40];
        float[] key = new float[64];
        double[] cost = new double[65 * 65], u = new double[65], v = new double[65], minv = new double[65];
        int[] p = new int[65], way = new int[65];
        bool[] used = new bool[65];

        public void Assign(FormationLayout l)
        {
            if (l.Fixed) return;
            int n = l.Count;
            Grow(n);
            for (int r = 0; r < 6; r++)
            {
                var role = (FormationRole)r;
                int nc = 0;
                for (int i = 0; i < n; i++)
                {
                    if (l.Slots[i].Role != role) continue;
                    int cls = l.Slots[i].Class, j = 0;
                    while (j < nc && classes[j] != cls) j++;
                    if (j == nc && nc < classes.Length) classes[nc++] = cls;
                }
                for (int ci = 0; ci < nc; ci++)
                {
                    int cls = classes[ci], ns = 0, nm = 0;
                    for (int i = 0; i < n; i++) if (l.Slots[i].Role == role && l.Slots[i].Class == cls) slots[ns++] = i;
                    for (int i = 0; i < n; i++) if (l.Members[i].Kind.Role == role && l.Classes[i] == cls) members[nm++] = i;
                    int c = Mathf.Min(ns, nm);
                    if (c == 0) continue;
                    if (c <= FormationTuning.HungarianMax) Hungarian(l, c);
                    else Projection(l, c);
                }
            }
        }

        void Grow(int n)
        {
            if (slots.Length >= n) return;
            int size = Mathf.NextPowerOfTwo(n);
            slots = new int[size]; members = new int[size]; work = new int[size]; tmp = new int[size]; tie = new int[size];
            key = new float[size];
        }

        // Kuhn-Munkres with potentials, O(n^3), members as rows.
        void Hungarian(FormationLayout l, int n)
        {
            int w = n + 1;
            for (int i = 1; i <= n; i++)
            {
                var from = l.Members[members[i - 1]].Position;
                for (int j = 1; j <= n; j++)
                {
                    double d = (l.Slots[slots[j - 1]].Wanted - from).magnitude;
                    cost[i * w + j] = d + TieWeight * d * d;
                }
            }
            for (int j = 0; j <= n; j++) { u[j] = 0; v[j] = 0; p[j] = 0; way[j] = 0; }
            for (int i = 1; i <= n; i++)
            {
                p[0] = i;
                int j0 = 0;
                for (int j = 0; j <= n; j++) { minv[j] = double.MaxValue; used[j] = false; }
                do
                {
                    used[j0] = true;
                    int i0 = p[j0], j1 = 0;
                    double delta = double.MaxValue;
                    for (int j = 1; j <= n; j++)
                    {
                        if (used[j]) continue;
                        double cur = cost[i0 * w + j] - u[i0] - v[j];
                        if (cur < minv[j]) { minv[j] = cur; way[j] = j0; }
                        if (minv[j] < delta) { delta = minv[j]; j1 = j; }
                    }
                    for (int j = 0; j <= n; j++)
                    {
                        if (used[j]) { u[p[j]] += delta; v[j] -= delta; }
                        else minv[j] -= delta;
                    }
                    j0 = j1;
                } while (p[j0] != 0);
                do
                {
                    int j1 = way[j0];
                    p[j0] = p[j1];
                    j0 = j1;
                } while (j0 != 0);
            }
            for (int j = 1; j <= n; j++) l.Slots[slots[j - 1]].Member = members[p[j] - 1];
        }

        // Units sorted from front to back in the formation's frame and cut
        // into its ranks, then paired left to right within each rank.
        void Projection(FormationLayout l, int n)
        {
            // Slots front rank first, left to right.
            for (int i = 0; i < n; i++)
            {
                var s = l.Slots[slots[i]].Local;
                key[i] = -s.y * 1e3f + s.x;
                tie[i] = slots[i];
                work[i] = i;
            }
            FormationSort.ByKey(work, 0, n, key, tie, tmp);
            for (int i = 0; i < n; i++) tmp[i] = slots[work[i]];
            Array.Copy(tmp, slots, n);

            // Units front to back.
            for (int i = 0; i < n; i++)
            {
                var m = l.Members[members[i]];
                var rel = m.Position - l.Origin;
                key[i] = -Vector2.Dot(rel, l.Face);
                tie[i] = m.Handle;
                work[i] = i;
            }
            FormationSort.ByKey(work, 0, n, key, tie, tmp);
            for (int i = 0; i < n; i++) tmp[i] = members[work[i]];
            Array.Copy(tmp, members, n);

            // Each rank: its units by x against its slots by x.
            int at = 0;
            while (at < n)
            {
                float y = l.Slots[slots[at]].Local.y;
                int end = at + 1;
                while (end < n && Mathf.Abs(l.Slots[slots[end]].Local.y - y) <= 0.01f) end++;
                int c = end - at;
                for (int i = 0; i < c; i++)
                {
                    var m = l.Members[members[at + i]];
                    key[i] = Vector2.Dot(m.Position - l.Origin, l.Right);
                    tie[i] = m.Handle;
                    work[i] = at + i;
                }
                // key and tie are indexed from 0 here, so sort positions 0..c.
                for (int i = 0; i < c; i++) work[i] = i;
                FormationSort.ByKey(work, 0, c, key, tie, tmp);
                for (int i = 0; i < c; i++) l.Slots[slots[at + i]].Member = members[at + work[i]];
                at = end;
            }
        }
    }
}
