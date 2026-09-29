// EdgeRing.cs - the land past the playable map, as modern RTS games show
// it: a ring of ground whose heights carry the map's edge outward, blurred
// along the edge and easing down to a low shelf (or the sea floor where the
// edge is water), textured with the map's own picture mirrored at the edge,
// and fading into the climate's haze with distance. It casts no shadow.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Game.World
{
    public static class EdgeRing
    {
        public const float Width = 32f;     // cells past the edge
        public const float Step = 2f;       // cells between ring vertices
        public const int Blur = 3;          // cells either way along the edge

        // The playable map in world units: x from 0 to size.x, z from
        // -size.y to 0. A point outside it clamps onto its edge.
        public static Vector2 Clamp(MapTerrain t, Vector2 p)
        {
            var size = t.Size;
            return new Vector2(Mathf.Clamp(p.x, 0, size.x), Mathf.Clamp(p.y, -size.y, 0));
        }

        // How far a point lies outside the map, 0 inside.
        public static float Outside(MapTerrain t, Vector2 p) => (p - Clamp(t, p)).magnitude;

        // The edge height near a point on the edge, averaged along the edge.
        static float EdgeHeight(MapTerrain t, Vector2 onEdge, Vector2 along)
        {
            float sum = 0;
            int n = 0;
            for (int k = -Blur; k <= Blur; k++)
            {
                var q = Clamp(t, onEdge + along * (k * t.CellSize));
                sum += t.Sample(q.x, q.y);
                n++;
            }
            return sum / n;
        }

        // The ground's height at a ring point: the edge's height eased down
        // to the shelf over the ring's width. Exactly the map's own height
        // at the edge, so no seam or drop shows.
        public static float Height(MapTerrain t, Vector2 p, float shelf)
        {
            var edge = Clamp(t, p);
            float d = (p - edge).magnitude;
            if (d <= 1e-4f) return t.Sample(p.x, p.y);
            var outward = (p - edge) / d;
            var along = new Vector2(-outward.y, outward.x);
            float blurred = EdgeHeight(t, edge, along);
            float atEdge = t.Sample(edge.x, edge.y);
            // Within a cell of the edge the map's own height wins, then the
            // blurred edge, easing to the shelf.
            float nearEdge = Mathf.Lerp(atEdge, blurred, Mathf.Clamp01(d / (2f * t.CellSize)));
            float fall = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / (Width * t.CellSize)));
            // Water at the edge: the ring is sea floor under the one sea.
            float floor = t.SeaLevel > 0 && atEdge < t.SeaLevel ? Mathf.Min(shelf, t.SeaLevel - 3f) : shelf;
            return Mathf.Lerp(nearEdge, floor, fall);
        }

        // A point's mirror image inside the map, for the ring's texture.
        public static Vector2 Mirror(MapTerrain t, Vector2 p)
        {
            var size = t.Size;
            float x = p.x < 0 ? -p.x : p.x > size.x ? 2 * size.x - p.x : p.x;
            float z = p.y > 0 ? -p.y : p.y < -size.y ? -2 * size.y - p.y : p.y;
            return Clamp(t, new Vector2(x, z));
        }

        // The low shelf the ring eases to: a little under the lowest edge.
        public static float Shelf(MapTerrain t)
        {
            var size = t.Size;
            float low = float.MaxValue;
            for (float x = 0; x <= size.x; x += t.CellSize) { low = Mathf.Min(low, t.Sample(x, 0), t.Sample(x, -size.y)); }
            for (float z = 0; z <= size.y; z += t.CellSize) { low = Mathf.Min(low, t.Sample(0, -z), t.Sample(size.x, -z)); }
            return low - 1.5f;
        }

        // How far the sea reaches past the map's edge, world units.
        public static float SeaReach(MapTerrain t) => Width * t.CellSize + 40f;

        // True where the map's edge meets the sea somewhere.
        public static bool SeaAtEdge(MapTerrain t) => t.SeaLevel > 0 && Shelf(t) + 1.5f < t.SeaLevel;

        // The plain past the ring out to the far clip, a frame of four quads
        // never over the map or the ring. Where the edge meets the sea it
        // starts under the sea's far end, just below its surface.
        public static Mesh HazeFrame(MapTerrain t, float far = 4000f)
        {
            var size = t.Size;
            float w = Width * t.CellSize, y = Shelf(t) - 0.05f;
            if (SeaAtEdge(t)) { w = SeaReach(t) - 2f * t.CellSize; y = t.SeaLevel - 0.3f; }
            float x0 = -w, x1 = size.x + w, zn = w, zs = -size.y - w;
            float fx0 = x0 - far, fx1 = x1 + far, fzn = zn + far, fzs = zs - far;
            var v = new List<Vector3>();
            void Quad(float ax, float az, float bx, float bz)
            {
                v.Add(new Vector3(ax, y, az)); v.Add(new Vector3(bx, y, az));
                v.Add(new Vector3(bx, y, bz)); v.Add(new Vector3(ax, y, bz));
            }
            Quad(fx0, fzn, fx1, zn);    // north
            Quad(fx0, zs, fx1, fzs);    // south
            Quad(fx0, zn, x0, zs);      // west
            Quad(x1, zn, fx1, zs);      // east
            var tris = new List<int>();
            for (int q = 0; q < 4; q++) { int b = q * 4; tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 }); }
            var mesh = new Mesh { name = "haze frame", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(v);
            var n = new Vector3[v.Count];
            var uv2 = new Vector2[v.Count];
            for (int i = 0; i < n.Length; i++) { n[i] = Vector3.up; uv2[i] = Vector2.one * 999; }
            mesh.normals = n;
            mesh.uv = new Vector2[v.Count];
            mesh.uv2 = uv2;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // The ring as one mesh: a grid over the map grown by the ring's
        // width, with the inside cut out. uv is the mirrored point on the
        // map picture, uv2.x the distance past the edge in cells.
        public static Mesh Build(MapTerrain t)
        {
            var size = t.Size;
            float w = Width * t.CellSize, step = Step * t.CellSize;
            float shelf = Shelf(t);
            int nx = Mathf.CeilToInt((size.x + 2 * w) / step), nz = Mathf.CeilToInt((size.y + 2 * w) / step);
            var index = new int[(nx + 1) * (nz + 1)];
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var dist = new List<Vector2>();
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    var p = new Vector2(-w + Mathf.Min(i * step, size.x + 2 * w), w - Mathf.Min(j * step, size.y + 2 * w));
                    // Snap the grid lines just outside the edge onto the edge,
                    // so the ring's inner border meets the map exactly.
                    if (p.x > 0 && p.x < step) p.x = 0;
                    if (p.x < size.x && p.x > size.x - step) p.x = size.x;
                    if (p.y < 0 && p.y > -step) p.y = 0;
                    if (p.y > -size.y && p.y < -size.y + step) p.y = -size.y;
                    index[j * (nx + 1) + i] = verts.Count;
                    verts.Add(new Vector3(p.x, Height(t, p, shelf), p.y));
                    var m = Mirror(t, p);
                    uvs.Add(new Vector2(m.x / size.x, 1f + m.y / size.y));
                    dist.Add(new Vector2(Outside(t, p) / t.CellSize, 0));
                }
            var tris = new List<int>();
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = index[j * (nx + 1) + i], b = index[j * (nx + 1) + i + 1];
                    int c = index[(j + 1) * (nx + 1) + i], d = index[(j + 1) * (nx + 1) + i + 1];
                    // Leave out the cells wholly inside the map.
                    var va = verts[a]; var vd = verts[d];
                    bool inside = va.x >= 0 && vd.x <= size.x && va.z <= 0 && vd.z >= -size.y;
                    if (inside) continue;
                    tris.Add(a); tris.Add(b); tris.Add(d);
                    tris.Add(a); tris.Add(d); tris.Add(c);
                }
            var mesh = new Mesh { name = "edge ring", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, dist);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
