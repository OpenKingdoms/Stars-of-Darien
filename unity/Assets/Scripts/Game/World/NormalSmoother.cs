// NormalSmoother.cs - smooth shading for faceted models. Each triangle
// corner takes the area weighted average of the face normals around its
// position that lie within an angle of its own face, so curved surfaces
// shade round while hard edges stay hard. Corners that end up the same are
// welded back into one vertex.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public static class NormalSmoother
    {
        public const float DefaultAngle = 45f;

        // Rewrites the lists in place. The given normals only settle which
        // way each face points, as the winding may be mirrored.
        public static void Apply(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Color32> cols,
            List<List<int>> subs, float angle = DefaultAngle)
        {
            float cosLimit = Mathf.Cos(angle * Mathf.Deg2Rad);
            var faces = new List<(int sub, int a, int b, int c, Vector3 n)>();
            for (int s = 0; s < subs.Count; s++)
            {
                var t = subs[s];
                for (int i = 0; i + 2 < t.Count; i += 3)
                {
                    int a = t[i], b = t[i + 1], c = t[i + 2];
                    var n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                    var given = norms[a] + norms[b] + norms[c];
                    if (given.sqrMagnitude > 1e-12f && Vector3.Dot(n, given) < 0) n = -n;
                    faces.Add((s, a, b, c, n));
                }
            }

            // Faces touching each position, positions snapped to a fine grid.
            float snap = 1e-4f;
            foreach (var v in verts) snap = Mathf.Max(snap, Mathf.Abs(v.x) * 1e-5f, Mathf.Abs(v.y) * 1e-5f, Mathf.Abs(v.z) * 1e-5f);
            Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x / snap), Mathf.RoundToInt(p.y / snap), Mathf.RoundToInt(p.z / snap));
            var around = new Dictionary<Vector3Int, List<int>>();
            for (int f = 0; f < faces.Count; f++)
                foreach (int v in new[] { faces[f].a, faces[f].b, faces[f].c })
                {
                    var k = Key(verts[v]);
                    if (!around.TryGetValue(k, out var list)) around[k] = list = new List<int>();
                    if (list.Count == 0 || list[list.Count - 1] != f) list.Add(f);
                }

            var outV = new List<Vector3>();
            var outN = new List<Vector3>();
            var outU = new List<Vector2>();
            var outC = new List<Color32>();
            var weld = new Dictionary<(int, Vector3Int), int>();
            var outSubs = new List<List<int>>();
            foreach (var _ in subs) outSubs.Add(new List<int>());
            foreach (var face in faces)
            {
                var own = face.n.sqrMagnitude > 1e-20f ? face.n.normalized : Vector3.up;
                foreach (int v in new[] { face.a, face.b, face.c })
                {
                    var sum = Vector3.zero;
                    foreach (int g in around[Key(verts[v])])
                    {
                        var other = faces[g].n;
                        if (other.sqrMagnitude < 1e-20f) continue;
                        if (Vector3.Dot(other.normalized, own) >= cosLimit) sum += other;
                    }
                    var n = sum.sqrMagnitude > 1e-20f ? sum.normalized : own;
                    var nk = new Vector3Int(Mathf.RoundToInt(n.x * 1000), Mathf.RoundToInt(n.y * 1000), Mathf.RoundToInt(n.z * 1000));
                    if (!weld.TryGetValue((v, nk), out int index))
                    {
                        index = outV.Count;
                        weld[(v, nk)] = index;
                        outV.Add(verts[v]);
                        outN.Add(n);
                        outU.Add(uvs[v]);
                        outC.Add(cols[v]);
                    }
                    outSubs[face.sub].Add(index);
                }
            }
            verts.Clear(); verts.AddRange(outV);
            norms.Clear(); norms.AddRange(outN);
            uvs.Clear(); uvs.AddRange(outU);
            cols.Clear(); cols.AddRange(outC);
            for (int s = 0; s < subs.Count; s++) { subs[s].Clear(); subs[s].AddRange(outSubs[s]); }
        }
    }
}
