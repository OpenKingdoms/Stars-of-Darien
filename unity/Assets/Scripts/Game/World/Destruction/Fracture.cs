// Fracture.cs - splits a model into chunks, the same way every time for a
// kind, seeded from its name. A model built of many small parts, such as a
// wall of logs or stones, breaks into groups of its parts. Any other is cut
// by planes, and a chunk is left open where it was cut and draws both
// faces, its inside shaded as the material's interior. Pure, and done in
// small steps, so a test or a loading slice can run it.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    // What a broken thing is, which decides how it breaks.
    public enum BreakKind : byte { None, Tree, Wall, Hut, Building, Body, Scatter, Rock, Piece }

    // What shows inside a chunk where it was cut.
    public enum Interior : byte { Wood, Stone, Ice, Scrap }

    // A model's triangles in its kind's space, each with the material slot it
    // draws in and the part of the model it belongs to.
    public sealed class FractureSource
    {
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<Color32> Colors = new List<Color32>();
        public readonly List<int> Triangles = new List<int>();
        public readonly List<int> Slots = new List<int>();
        public readonly List<int> Parts = new List<int>();
        public int SlotCount, PartCount;

        public int TriangleCount => Triangles.Count / 3;

        // The mesh read last, kept while its instances are added one by one.
        static readonly List<Vector3> vbuf = new List<Vector3>(), nbuf = new List<Vector3>();
        static readonly List<Vector2> ubuf = new List<Vector2>();
        static readonly List<Color32> cbuf = new List<Color32>();
        static readonly List<int> tbuf = new List<int>();
        static Mesh lastMesh;
        static int lastSub = -1, mark;
        static int[] map = new int[0], stamp = new int[0];

        public static void ForgetRead()
        {
            lastMesh = null;
            lastSub = -1;
        }

        // Adds the vertices and triangles of one submesh placed by toKind, as
        // the given part, or a new one. False when the CPU cannot read it.
        public bool Add(Mesh mesh, int submesh, int slot, Matrix4x4 toKind, int part = -1) =>
            Add(mesh, submesh, slot, toKind, part, 0, int.MaxValue) >= 0;

        // The same for count triangles from the first, so a big mesh can be
        // read a batch at a time. Returns how many triangles the submesh has,
        // or -1 when the CPU cannot read it.
        public int Add(Mesh mesh, int submesh, int slot, Matrix4x4 toKind, int part, int first, int count)
        {
            if (mesh == null || !mesh.isReadable || submesh < 0 || submesh >= mesh.subMeshCount) return -1;
            if (mesh != lastMesh)
            {
                mesh.GetVertices(vbuf);
                mesh.GetNormals(nbuf);
                mesh.GetUVs(0, ubuf);
                mesh.GetColors(cbuf);
                lastMesh = mesh;
                lastSub = -1;
            }
            if (submesh != lastSub)
            {
                mesh.GetTriangles(tbuf, submesh);
                lastSub = submesh;
            }
            if (part < 0) part = PartCount;
            PartCount = Mathf.Max(PartCount, part + 1);
            if (map.Length < vbuf.Count)
            {
                map = new int[vbuf.Count];
                stamp = new int[vbuf.Count];
                mark = 0;
            }
            // Each placement's vertices are its own, kept across its batches.
            if (first == 0) mark++;
            var normalTo = toKind.inverse.transpose;
            bool flip = toKind.determinant < 0;
            int total = tbuf.Count / 3, end = (int)System.Math.Min(total, (long)first + count);
            for (int t = first * 3; t < end * 3; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    // A mirrored placement keeps its faces facing out.
                    int v = tbuf[t + (flip && k > 0 ? 3 - k : k)];
                    if (stamp[v] != mark)
                    {
                        stamp[v] = mark;
                        map[v] = Positions.Count;
                        Positions.Add(toKind.MultiplyPoint3x4(vbuf[v]));
                        var n = v < nbuf.Count ? normalTo.MultiplyVector(nbuf[v]) : Vector3.up;
                        Normals.Add(n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up);
                        Uvs.Add(v < ubuf.Count ? ubuf[v] : Vector2.zero);
                        Colors.Add(v < cbuf.Count ? cbuf[v] : new Color32(255, 255, 255, 255));
                    }
                    Triangles.Add(map[v]);
                }
                Slots.Add(slot);
                Parts.Add(part);
            }
            SlotCount = Mathf.Max(SlotCount, slot + 1);
            return total;
        }

        public Bounds Bounds()
        {
            if (Positions.Count == 0) return new Bounds();
            var b = new Bounds(Positions[0], Vector3.zero);
            for (int i = 1; i < Positions.Count; i++) b.Encapsulate(Positions[i]);
            return b;
        }
    }

    // One chunk, its vertices about its own centre in kind space.
    public sealed class Chunk
    {
        public Vector3 Centre;
        public Vector3[] Positions, Normals;
        public Vector2[] Uvs;
        public Color32[] Colors;
        public int[] Slots;             // the material slot each submesh draws
        public int[][] Triangles;       // per submesh
        public Vector3[] Support;       // its outermost points, to rest on the ground by
        public Bounds Bounds;           // about Centre
        public float Area;
    }

    // A split in steps, each a bounded amount of work: measuring the model,
    // grouping its parts, cutting a batch of a piece's triangles at a time,
    // and finishing one chunk.
    public sealed class FractureJob
    {
        enum Phase { Measure, Group, Collect, Cut, Finish, Done }

        // Triangles one step goes through.
        public const int Batch = 6000;
        // Chunks are drawn this much larger about their middles, so the seams
        // of a model standing in its chunks do not open.
        public const float Inflate = 1.004f;

        Vector3[] P, N;
        Vector2[] U;
        Color32[] C;
        int verts;
        int[] T, S, Pt;
        float[] A;
        int tris;
        readonly int slots, parts;
        readonly List<int[]> pieces = new List<int[]>();
        readonly List<Bounds> pieceBounds = new List<Bounds>();
        readonly List<bool> solid = new List<bool>();
        readonly Dictionary<long, int> cutEdges = new Dictionary<long, int>();
        int[] front = new int[64], back = new int[64];
        int nf, nb;
        float areaFront, areaBack;
        Vector3 frontMin, frontMax, backMin, backMax;
        readonly int target;
        uint rng;
        Phase phase;
        int cursor, finished;
        int[] map, stamp;

        // Measuring: the whole and each part's area and middle.
        float total;
        Vector3 lo, hi;
        readonly float[] partArea;
        readonly Vector3[] partMiddle;

        // Grouping: each part's group, the groups' middles, and their triangles as they are gathered.
        int[] groupOf, groupCount;
        Vector3[] groupMiddle, groupMin, groupMax;
        int[][] groupLists;

        // The cut under way: its piece, plane, and what to undo it to.
        int cutPiece = -1, attempt, vertsBefore, trisBefore;
        float nx, ny, nz, d0, eps;

        public readonly List<Chunk> Chunks = new List<Chunk>();
        public bool Done => phase == Phase.Done;
        public int Target => target;
        // Whether it broke into groups of its parts rather than by cuts.
        public bool Grouped { get; private set; }

        public FractureJob(FractureSource src, string name, int target)
        {
            verts = src.Positions.Count;
            P = src.Positions.ToArray();
            N = src.Normals.ToArray();
            U = src.Uvs.ToArray();
            C = src.Colors.ToArray();
            tris = src.TriangleCount;
            T = src.Triangles.ToArray();
            S = src.Slots.ToArray();
            Pt = src.Parts.Count == tris ? src.Parts.ToArray() : new int[tris];
            A = new float[Mathf.Max(1, tris)];
            slots = Mathf.Max(1, src.SlotCount);
            parts = Mathf.Max(1, src.PartCount);
            partArea = new float[parts];
            partMiddle = new Vector3[parts];
            this.target = Mathf.Max(1, target);
            rng = Fracture.Seed(name);
            phase = tris == 0 ? Phase.Done : Phase.Measure;
            lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        }

        float Next() => Fracture.Rand01(ref rng);

        float AreaOf(int a, int b, int c) => Vector3.Cross(P[b] - P[a], P[c] - P[a]).magnitude * 0.5f;

        // One bounded step. True once the chunks are all made.
        public bool Step()
        {
            switch (phase)
            {
                case Phase.Measure: Measure(); break;
                case Phase.Group: Group(); break;
                case Phase.Collect: Collect(); break;
                case Phase.Cut: Cut(); break;
                case Phase.Finish:
                    if (finished < pieces.Count)
                    {
                        var c = Finish(pieces[finished++]);
                        if (c != null) Chunks.Add(c);
                    }
                    if (finished >= pieces.Count) phase = Phase.Done;
                    break;
            }
            return phase == Phase.Done;
        }

        void Measure()
        {
            int end = Mathf.Min(tris, cursor + Batch);
            for (int t = cursor; t < end; t++)
            {
                var a = P[T[t * 3]];
                var b = P[T[t * 3 + 1]];
                var c = P[T[t * 3 + 2]];
                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                A[t] = area;
                total += area;
                lo = Vector3.Min(lo, Vector3.Min(a, Vector3.Min(b, c)));
                hi = Vector3.Max(hi, Vector3.Max(a, Vector3.Max(b, c)));
                partArea[Pt[t]] += area;
                partMiddle[Pt[t]] += (a + b + c) * (area / 3f);
            }
            cursor = end;
            if (cursor < tris) return;
            cursor = 0;
            // Many parts, none of them most of the model: it breaks along them.
            float biggest = 0f;
            int real = 0;
            for (int p = 0; p < parts; p++)
            {
                if (partArea[p] <= 0f) continue;
                real++;
                partMiddle[p] /= partArea[p];
                biggest = Mathf.Max(biggest, partArea[p]);
            }
            if (real >= target && target > 1 && biggest < total * 0.25f)
            {
                Grouped = true;
                phase = Phase.Group;
                return;
            }
            var all = new int[tris];
            for (int t = 0; t < tris; t++) all[t] = t;
            var bounds = new Bounds();
            bounds.SetMinMax(lo, hi);
            pieces.Add(all);
            pieceBounds.Add(bounds);
            solid.Add(false);
            phase = Phase.Cut;
        }

        // Gathers the parts into as many groups as chunks, by where they lie:
        // seeded far apart, then a few rounds of moving each part to the
        // nearest group's middle.
        void Group()
        {
            if (groupOf == null)
            {
                groupOf = new int[parts];
                groupMiddle = new Vector3[target];
                var near = new float[parts];
                int first = -1;
                for (int guard = 0; guard < parts && first < 0; guard++)
                {
                    int p = (int)(Next() * parts) % parts;
                    if (partArea[p] > 0f) first = p;
                }
                if (first < 0) first = 0;
                for (int p = 0; p < parts; p++) near[p] = float.MaxValue;
                int pick = first;
                for (int g = 0; g < target; g++)
                {
                    groupMiddle[g] = partMiddle[pick];
                    int far = pick;
                    float farthest = -1f;
                    for (int p = 0; p < parts; p++)
                    {
                        if (partArea[p] <= 0f) continue;
                        near[p] = Mathf.Min(near[p], (partMiddle[p] - groupMiddle[g]).sqrMagnitude);
                        if (near[p] > farthest) { farthest = near[p]; far = p; }
                    }
                    pick = far;
                }
                return;
            }
            if (cursor < 6)
            {
                cursor++;
                var sum = new Vector3[target];
                var weight = new float[target];
                for (int p = 0; p < parts; p++)
                {
                    if (partArea[p] <= 0f) continue;
                    int best = 0;
                    float d = float.MaxValue;
                    for (int g = 0; g < target; g++)
                    {
                        float e = (partMiddle[p] - groupMiddle[g]).sqrMagnitude;
                        if (e < d) { d = e; best = g; }
                    }
                    groupOf[p] = best;
                    sum[best] += partMiddle[p] * partArea[p];
                    weight[best] += partArea[p];
                }
                for (int g = 0; g < target; g++) if (weight[g] > 0f) groupMiddle[g] = sum[g] / weight[g];
                return;
            }
            // How many triangles each group has, from each part's area count.
            groupCount = new int[target];
            var partTris = new int[parts];
            for (int t = 0; t < tris; t++) partTris[Pt[t]]++;
            for (int p = 0; p < parts; p++) groupCount[groupOf[p]] += partTris[p];
            groupLists = new int[target][];
            groupMin = new Vector3[target];
            groupMax = new Vector3[target];
            for (int g = 0; g < target; g++)
            {
                groupLists[g] = new int[groupCount[g]];
                groupMin[g] = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                groupMax[g] = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                groupCount[g] = 0;
            }
            cursor = 0;
            phase = Phase.Collect;
        }

        // Each group's triangles and bounds, a batch at a time.
        void Collect()
        {
            int end = Mathf.Min(tris, cursor + Batch);
            for (int t = cursor; t < end; t++)
            {
                int g = groupOf[Pt[t]];
                groupLists[g][groupCount[g]++] = t;
                for (int k = 0; k < 3; k++) { var p = P[T[t * 3 + k]]; groupMin[g] = Vector3.Min(groupMin[g], p); groupMax[g] = Vector3.Max(groupMax[g], p); }
            }
            cursor = end;
            if (cursor < tris) return;
            for (int g = 0; g < target; g++)
            {
                if (groupLists[g].Length == 0) continue;
                var b = new Bounds();
                b.SetMinMax(groupMin[g], groupMax[g]);
                pieces.Add(groupLists[g]);
                pieceBounds.Add(b);
                solid.Add(true);
            }
            groupLists = null;
            cursor = 0;
            phase = Phase.Finish;
        }

        // A batch of the cut under way, or the start of the next.
        void Cut()
        {
            if (cutPiece < 0)
            {
                if (pieces.Count >= target || !Largest(out cutPiece)) { cutPiece = -1; phase = Phase.Finish; return; }
                attempt = 0;
                Plane();
            }
            var piece = pieces[cutPiece];
            int end = Mathf.Min(piece.Length, cursor + Batch);
            for (int i = cursor; i < end; i++)
            {
                int t = piece[i];
                int a = T[t * 3], bb = T[t * 3 + 1], c = T[t * 3 + 2];
                var pa = P[a];
                var pb = P[bb];
                var pc = P[c];
                float da = nx * pa.x + ny * pa.y + nz * pa.z - d0, db = nx * pb.x + ny * pb.y + nz * pb.z - d0, dc = nx * pc.x + ny * pc.y + nz * pc.z - d0;
                int sa = Side(da), sb = Side(db), sc = Side(dc);
                if (sa <= 0 && sb <= 0 && sc <= 0) { ToBack(t, pa, pb, pc); continue; }
                if (sa >= 0 && sb >= 0 && sc >= 0) { ToFront(t, pa, pb, pc); continue; }
                Split(t, a, bb, c, da, db, dc, sa, sb, sc);
            }
            cursor = end;
            if (cursor < piece.Length) return;
            cursor = 0;
            float all = areaFront + areaBack;
            if (nf > 0 && nb > 0 && Mathf.Min(areaFront, areaBack) >= all * 0.04f)
            {
                var fb = new Bounds();
                fb.SetMinMax(frontMin, frontMax);
                var bk = new Bounds();
                bk.SetMinMax(backMin, backMax);
                pieces[cutPiece] = Copy(front, nf);
                pieceBounds[cutPiece] = fb;
                pieces.Add(Copy(back, nb));
                pieceBounds.Add(bk);
                solid.Add(false);
                cutPiece = -1;
                return;
            }
            // A cut that misses, or shaves a sliver, is undone and tried another way.
            verts = vertsBefore;
            tris = trisBefore;
            if (++attempt < 4) Plane();
            else { solid[cutPiece] = true; cutPiece = -1; }
        }

        // The biggest piece that may still split.
        bool Largest(out int best)
        {
            best = -1;
            float most = 0f;
            for (int i = 0; i < pieces.Count; i++)
            {
                if (solid[i]) continue;
                var s = pieceBounds[i].size;
                float m = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                if (m > most + 1e-6f) { most = m; best = i; }
            }
            return best >= 0 && most >= 1e-4f;
        }

        // Across the piece's long way, tilted, a little off its middle.
        void Plane()
        {
            var b = pieceBounds[cutPiece];
            var size = b.size;
            float wx = size.x * size.x, wy = size.y * size.y, wz = size.z * size.z, pick = Next() * (wx + wy + wz);
            var axis = pick < wx ? Vector3.right : pick < wx + wy ? Vector3.up : Vector3.forward;
            var n = (axis + new Vector3(Next() - 0.5f, Next() - 0.5f, Next() - 0.5f) * 0.7f).normalized;
            float reach = Mathf.Abs(n.x) * size.x + Mathf.Abs(n.y) * size.y + Mathf.Abs(n.z) * size.z;
            var point = b.center + n * ((Next() - 0.5f) * 0.4f * reach);
            eps = Mathf.Max(1e-6f, reach * 1e-5f);
            nx = n.x; ny = n.y; nz = n.z;
            d0 = nx * point.x + ny * point.y + nz * point.z;
            vertsBefore = verts;
            trisBefore = tris;
            nf = nb = 0;
            areaFront = areaBack = 0f;
            frontMin = backMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            frontMax = backMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            cutEdges.Clear();
            cursor = 0;
        }

        static int[] Copy(int[] from, int n)
        {
            var a = new int[n];
            System.Array.Copy(from, a, n);
            return a;
        }

        void ToFront(int t, Vector3 pa, Vector3 pb, Vector3 pc)
        {
            if (nf == front.Length) System.Array.Resize(ref front, nf * 2);
            front[nf++] = t;
            areaFront += A[t];
            frontMin = Vector3.Min(frontMin, Vector3.Min(pa, Vector3.Min(pb, pc)));
            frontMax = Vector3.Max(frontMax, Vector3.Max(pa, Vector3.Max(pb, pc)));
        }

        void ToBack(int t, Vector3 pa, Vector3 pb, Vector3 pc)
        {
            if (nb == back.Length) System.Array.Resize(ref back, nb * 2);
            back[nb++] = t;
            areaBack += A[t];
            backMin = Vector3.Min(backMin, Vector3.Min(pa, Vector3.Min(pb, pc)));
            backMax = Vector3.Max(backMax, Vector3.Max(pa, Vector3.Max(pb, pc)));
        }

        int Side(float d) => d > eps ? 1 : d < -eps ? -1 : 0;

        readonly int[] poly = new int[3];
        readonly float[] dist = new float[3];
        readonly int[] sides = new int[3];
        readonly int[] frontPoly = new int[4], backPoly = new int[4];

        void Split(int t, int a, int b, int c, float da, float db, float dc, int sa, int sb, int sc)
        {
            poly[0] = a; poly[1] = b; poly[2] = c;
            dist[0] = da; dist[1] = db; dist[2] = dc;
            sides[0] = sa; sides[1] = sb; sides[2] = sc;
            int fn = 0, bn = 0;
            for (int i = 0; i < 3; i++)
            {
                int j = (i + 1) % 3;
                if (sides[i] >= 0) frontPoly[fn++] = poly[i];
                if (sides[i] <= 0) backPoly[bn++] = poly[i];
                if (sides[i] * sides[j] < 0)
                {
                    int x = Edge(poly[i], poly[j], dist[i], dist[j]);
                    frontPoly[fn++] = x;
                    backPoly[bn++] = x;
                }
            }
            int slot = S[t], part = Pt[t];
            for (int k = 1; k + 1 < fn; k++) { int nt = Add(frontPoly[0], frontPoly[k], frontPoly[k + 1], slot, part); ToFront(nt, P[frontPoly[0]], P[frontPoly[k]], P[frontPoly[k + 1]]); }
            for (int k = 1; k + 1 < bn; k++) { int nt = Add(backPoly[0], backPoly[k], backPoly[k + 1], slot, part); ToBack(nt, P[backPoly[0]], P[backPoly[k]], P[backPoly[k + 1]]); }
        }

        int Add(int a, int b, int c, int slot, int part)
        {
            if (tris == S.Length)
            {
                int grow = Mathf.Max(16, tris * 2);
                System.Array.Resize(ref S, grow);
                System.Array.Resize(ref Pt, grow);
                System.Array.Resize(ref A, grow);
                System.Array.Resize(ref T, grow * 3);
            }
            T[tris * 3] = a; T[tris * 3 + 1] = b; T[tris * 3 + 2] = c;
            S[tris] = slot;
            Pt[tris] = part;
            A[tris] = AreaOf(a, b, c);
            return tris++;
        }

        // Where an edge crosses the plane, shared by the triangles on both sides of it.
        int Edge(int i, int j, float di, float dj)
        {
            if (i > j) { (i, j) = (j, i); (di, dj) = (dj, di); }
            long key = (long)i << 32 | (uint)j;
            if (cutEdges.TryGetValue(key, out int x)) return x;
            if (verts == P.Length)
            {
                int grow = Mathf.Max(16, verts * 2);
                System.Array.Resize(ref P, grow);
                System.Array.Resize(ref N, grow);
                System.Array.Resize(ref U, grow);
                System.Array.Resize(ref C, grow);
            }
            float f = di / (di - dj);
            x = verts++;
            P[x] = Vector3.LerpUnclamped(P[i], P[j], f);
            var n = Vector3.LerpUnclamped(N[i], N[j], f);
            N[x] = n.sqrMagnitude > 1e-12f ? n.normalized : N[i];
            U[x] = Vector2.LerpUnclamped(U[i], U[j], f);
            C[x] = Color32.Lerp(C[i], C[j], f);
            cutEdges[key] = x;
            return x;
        }

        static readonly Vector3[] Directions = Fracture.SupportDirections();

        Chunk Finish(int[] list)
        {
            float area = 0f;
            var centre = Vector3.zero;
            foreach (int t in list)
            {
                area += A[t];
                centre += (P[T[t * 3]] + P[T[t * 3 + 1]] + P[T[t * 3 + 2]]) * (A[t] / 3f);
            }
            if (area <= 1e-10f) return null;
            centre /= area;
            if (map == null || map.Length < verts) { map = new int[verts]; stamp = new int[verts]; }
            int mark = finished + 1, used = 0;
            var perSlot = new int[slots];
            foreach (int t in list)
            {
                perSlot[S[t]] += 3;
                for (int k = 0; k < 3; k++)
                {
                    int v = T[t * 3 + k];
                    if (stamp[v] != mark) { stamp[v] = mark; map[v] = used++; }
                }
            }
            var chunk = new Chunk { Centre = centre, Area = area, Positions = new Vector3[used], Normals = new Vector3[used], Uvs = new Vector2[used], Colors = new Color32[used] };
            foreach (int t in list)
                for (int k = 0; k < 3; k++)
                {
                    int v = T[t * 3 + k], nv = map[v];
                    chunk.Positions[nv] = (P[v] - centre) * Inflate;
                    chunk.Normals[nv] = N[v];
                    chunk.Uvs[nv] = U[v];
                    chunk.Colors[nv] = C[v];
                }
            int subs = 0;
            for (int s = 0; s < slots; s++) if (perSlot[s] > 0) subs++;
            chunk.Slots = new int[subs];
            chunk.Triangles = new int[subs][];
            var fill = new int[slots];
            var subOf = new int[slots];
            for (int s = 0, k = 0; s < slots; s++)
            {
                if (perSlot[s] == 0) continue;
                chunk.Slots[k] = s;
                chunk.Triangles[k] = new int[perSlot[s]];
                subOf[s] = k++;
            }
            foreach (int t in list)
            {
                int s = S[t];
                var into = chunk.Triangles[subOf[s]];
                for (int c = 0; c < 3; c++) into[fill[s]++] = map[T[t * 3 + c]];
            }
            var min = chunk.Positions[0];
            var max = min;
            foreach (var p in chunk.Positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            chunk.Bounds = new Bounds((min + max) * 0.5f, max - min);
            chunk.Support = Fracture.Support(chunk.Positions, Directions);
            return chunk;
        }
    }

    public static class Fracture
    {
        // FNV-1a over the name's characters, the same on every machine.
        public static uint Seed(string name)
        {
            uint h = 2166136261u;
            foreach (char ch in name ?? "") { h ^= ch; h *= 16777619u; }
            return h == 0 ? 1u : h;
        }

        // xorshift, 0 to 1.
        public static float Rand01(ref uint s)
        {
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            return (s & 0xFFFFFF) / 16777216f;
        }

        // How many chunks a kind breaks into: a tree 6 to 10, a wall or hut
        // 12 to 24, a building 16 to 40, bigger ones more, times the quality's scale.
        public static int ChunkCount(BreakKind kind, Vector3 size, string name, float scale = 1f)
        {
            int lo, hi;
            switch (kind)
            {
                case BreakKind.Tree: lo = 6; hi = 10; break;
                case BreakKind.Wall: case BreakKind.Hut: lo = 12; hi = 24; break;
                case BreakKind.Building: lo = 16; hi = 40; break;
                case BreakKind.Body: lo = 6; hi = 12; break;
                case BreakKind.Piece: lo = 3; hi = 6; break;
                default: lo = 5; hi = 10; break;
            }
            uint s = Seed(name);
            float big = Mathf.Clamp01((size.magnitude - 2f) / 10f);
            float n = lo + (hi - lo) * (0.5f * big + 0.5f * Rand01(ref s));
            return Mathf.Max(2, Mathf.RoundToInt(n * scale));
        }

        // Runs a whole split at once.
        public static List<Chunk> Split(FractureSource src, string name, int target)
        {
            var job = new FractureJob(src, name, target);
            while (!job.Step()) { }
            return job.Chunks;
        }

        // Fourteen ways out: the axes and the cube's corners.
        public static Vector3[] SupportDirections()
        {
            var d = new List<Vector3> { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            for (int i = 0; i < 8; i++) d.Add(new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1).normalized);
            return d.ToArray();
        }

        // The farthest point each way, without repeats.
        public static Vector3[] Support(Vector3[] points, Vector3[] directions)
        {
            var picked = new List<Vector3>();
            foreach (var dir in directions)
            {
                int best = 0;
                float most = float.MinValue;
                for (int i = 0; i < points.Length; i++)
                {
                    float d = Vector3.Dot(points[i], dir);
                    if (d > most) { most = d; best = i; }
                }
                if (!picked.Contains(points[best])) picked.Add(points[best]);
            }
            return picked.ToArray();
        }

        // What a feature is, from its category and name as the data gives
        // them. The data leaves unit wrecks with no category.
        public static BreakKind KindOf(FeatureDef d)
        {
            if (d == null) return BreakKind.None;
            string c = (d.Category ?? "").ToLowerInvariant(), n = (d.Name ?? "").ToLowerInvariant();
            if (c.Length == 0 && !string.IsNullOrEmpty(d.ObjectName) || c.Contains("corpse") || c.Contains("bod") || c.Contains("wreck")) return BreakKind.Body;
            if (c.Contains("wave") || c.Contains("noise") || c.Contains("sound")) return BreakKind.None;
            if (c.Contains("rock") || c.Contains("mana") || c.Contains("lodestone") || c.Contains("spire") || c.Contains("mound")) return BreakKind.Rock;
            if (c.Contains("tree")) return BreakKind.Tree;
            if (c.Contains("wall")) return BreakKind.Wall;
            if (c.Contains("dwell") || c.Contains("hut") || n.Contains("hut") || n.Contains("shed") || n.Contains("well")) return BreakKind.Hut;
            if (c.Contains("build") || c.Contains("tower") || c.Contains("ruin") || c.Contains("device") || n.Contains("house") ||
                n.Contains("invent") || n.Contains("fountain") || n.Contains("temple")) return BreakKind.Building;
            return BreakKind.Scatter;
        }

        // What its inside shows: stone for walls and buildings, ice for the
        // frozen, scrap for wrecks, and wood for the rest.
        public static Interior InteriorOf(FeatureDef d, BreakKind kind)
        {
            string c = (d?.Category ?? "").ToLowerInvariant(), n = (d?.Name ?? "").ToLowerInvariant();
            if (kind == BreakKind.Body)
            {
                if (n.Contains("frozen") || n.Contains("ice") || c.Contains("frozen")) return Interior.Ice;
                if (n.Contains("stone") || c.Contains("stone")) return Interior.Stone;
                return Interior.Scrap;
            }
            if (kind == BreakKind.Wall || kind == BreakKind.Building || kind == BreakKind.Rock) return Interior.Stone;
            return Interior.Wood;
        }

        // Fresh wood, raw stone, ice and dark scrap. The shader mixes it with
        // the face's own colour, so a leafy crown stays leafy inside.
        public static Color InteriorColour(Interior i)
        {
            switch (i)
            {
                case Interior.Stone: return new Color(0.58f, 0.55f, 0.5f);
                case Interior.Ice: return new Color(0.72f, 0.84f, 0.95f);
                case Interior.Scrap: return new Color(0.28f, 0.27f, 0.26f);
                default: return new Color(0.42f, 0.31f, 0.2f);
            }
        }
    }
}
