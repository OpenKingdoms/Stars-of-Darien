// FractureCache.cs - each kind's chunks, split once while the battle loads
// a slice at a time, and in battle at most a couple of milliseconds a frame
// for a kind first needed there. Until a kind is ready its parts break away whole.
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Game.World
{
    // One drawn part of a kind at rest, placed in the kind's space.
    public struct KindPart
    {
        public Mesh Mesh;
        public int Submesh;
        public Material Material;
        public Matrix4x4 Local;
        public bool Flat;
    }

    public struct ChunkDraw
    {
        public Mesh Mesh;
        public int Submesh;
        public Material Material;
    }

    // A kind's chunks: what each draws, where it sits in the kind's space,
    // the point it turns about and the points it rests on, in its mesh's space.
    public sealed class ChunkSet
    {
        public BreakKind Kind;
        public Interior Interior;
        public ChunkDraw[][] Draws;
        public Matrix4x4[] Place;
        public Vector3[] Pivot;
        public Vector3[][] Support;
        public Vector3[] Centre;        // kind space
        public float[] Size;            // about how far each reaches from its pivot
        public Bounds Bounds;           // the whole kind, kind space
        public bool Whole;              // its parts unsplit, while the split is not ready
        public int Count => Draws.Length;
    }

    public sealed class FractureCache
    {
        // A unit model's piece, keyed apart from feature defs.
        public static long PieceKey(int model, int piece) => 1L << 40 | (long)model << 12 | (uint)piece;

        struct Want
        {
            public long Key;
            public string Name;
            public BreakKind Kind;
            public Interior Interior;
        }

        readonly System.Func<long, List<KindPart>, bool> partsOf;
        readonly Dictionary<long, ChunkSet> sets = new Dictionary<long, ChunkSet>();
        readonly Dictionary<long, ChunkSet> wholes = new Dictionary<long, ChunkSet>();
        readonly HashSet<long> known = new HashSet<long>();
        readonly Queue<Want> queue = new Queue<Want>();
        readonly Dictionary<(Material, Interior), Material> chunkMats = new Dictionary<(Material, Interior), Material>();
        readonly List<Object> owned = new List<Object>();
        readonly List<KindPart> parts = new List<KindPart>();

        // The job under way: its kind, its parts read so far, the materials
        // of its slots, and once split, its chunks gathered into meshes of a
        // bounded size, each chunk's materials their own submeshes.
        Want current;
        bool working;
        FractureSource source;
        int partsRead, partId, partSlot, readFrom;
        FractureJob job;
        readonly List<Material> slotMats = new List<Material>();
        readonly List<Mesh> made = new List<Mesh>();
        int meshed;
        int[] meshOf, firstSub;
        public const int MeshVertices = 40000;

        // Chunk counts are scaled by the effects quality.
        public float CountScale = 1f;
        public int Ready => sets.Count;
        public int Waiting => queue.Count + (working ? 1 : 0);
        // The longest single step taken, in milliseconds, for the budget's tests.
        public double LongestStepMs { get; private set; }
        public double SpentMs { get; private set; }
        public string LongestStep { get; private set; } = "";
        // Kinds that broke into groups of their parts rather than by cuts.
        public int Grouped { get; private set; }

        public FractureCache(System.Func<long, List<KindPart>, bool> partsOf) => this.partsOf = partsOf;

        public void ResetTimes() => SpentMs = LongestStepMs = 0;

        // The split chunks, or null while it is not ready.
        public ChunkSet Get(long key) => sets.TryGetValue(key, out var s) ? s : null;

        public bool Known(long key) => known.Contains(key);

        // Asks for a kind to be split. Once asked, it is not asked again.
        public void Ask(long key, string name, BreakKind kind, Interior interior)
        {
            if (!known.Add(key)) return;
            queue.Enqueue(new Want { Key = key, Name = name ?? key.ToString(), Kind = kind, Interior = interior });
        }

        // Splits for about budgetMs. True once nothing is waiting.
        public bool Work(double budgetMs)
        {
            long from = Stopwatch.GetTimestamp();
            while (true)
            {
                long stepFrom = Stopwatch.GetTimestamp();
                string what = !working ? "begin" : job == null ? "read" : !job.Done ? "split" : meshed < job.Chunks.Count ? "mesh" : "finish";
                if (!working)
                {
                    if (queue.Count == 0) return true;
                    Begin(queue.Dequeue());
                }
                else if (job == null) Read();
                else if (!job.Done) job.Step();
                else if (meshed < job.Chunks.Count) MeshSome(job.Chunks, current.Name);
                else Finish();
                double step = Ms(stepFrom);
                SpentMs += step;
                if (step > LongestStepMs) { LongestStepMs = step; LongestStep = what + " " + current.Name; }
                if (Ms(from) >= budgetMs) return queue.Count == 0 && !working;
            }
        }

        static double Ms(long from) => (Stopwatch.GetTimestamp() - from) * 1000.0 / Stopwatch.Frequency;

        void Begin(Want w)
        {
            current = w;
            parts.Clear();
            slotMats.Clear();
            made.Clear();
            meshed = 0;
            job = null;
            source = null;
            if (!partsOf(w.Key, parts)) return;
            FractureSource.ForgetRead();
            source = new FractureSource();
            partsRead = 0;
            readFrom = 0;
            partId = -1;
            working = true;
        }

        // One part of the model into the source a step, and the job once all are read.
        void Read()
        {
            if (partsRead < parts.Count)
            {
                var p = parts[partsRead];
                if (p.Flat || p.Mesh == null) { partsRead++; return; }
                if (readFrom == 0)
                {
                    // Submeshes of one mesh placed alike are one part of the model.
                    bool same = partsRead > 0 && parts[partsRead - 1].Mesh == p.Mesh && parts[partsRead - 1].Local == p.Local;
                    if (!same || partId < 0) partId = source.PartCount;
                    partSlot = slotMats.IndexOf(p.Material);
                    if (partSlot < 0) { partSlot = slotMats.Count; slotMats.Add(p.Material); }
                }
                int total = source.Add(p.Mesh, p.Submesh, partSlot, p.Local, partId, readFrom, FractureJob.Batch);
                readFrom += FractureJob.Batch;
                if (total < 0 || readFrom >= total) { partsRead++; readFrom = 0; }
                return;
            }
            if (source.TriangleCount == 0) { working = false; source = null; return; }
            var size = source.Bounds().size;
            job = new FractureJob(source, current.Name, Fracture.ChunkCount(current.Kind, size, current.Name, CountScale));
            source = null;
            meshOf = null;
        }

        // The next chunks, up to a mesh's worth of vertices, into one mesh.
        void MeshSome(List<Chunk> chunks, string name)
        {
            if (meshOf == null) { meshOf = new int[chunks.Count]; firstSub = new int[chunks.Count]; }
            int from = meshed, to = meshed, verts = 0, subs = 0;
            while (to < chunks.Count && (to == from || verts + chunks[to].Positions.Length <= MeshVertices))
            {
                verts += chunks[to].Positions.Length;
                subs += chunks[to].Triangles.Length;
                to++;
            }
            var pos = new Vector3[verts];
            var nor = new Vector3[verts];
            var uv = new Vector2[verts];
            var col = new Color32[verts];
            var baseOf = new int[to - from];
            int at = 0, sub = 0;
            for (int i = from; i < to; i++)
            {
                var c = chunks[i];
                System.Array.Copy(c.Positions, 0, pos, at, c.Positions.Length);
                System.Array.Copy(c.Normals, 0, nor, at, c.Normals.Length);
                System.Array.Copy(c.Uvs, 0, uv, at, c.Uvs.Length);
                System.Array.Copy(c.Colors, 0, col, at, c.Colors.Length);
                baseOf[i - from] = at;
                meshOf[i] = made.Count;
                firstSub[i] = sub;
                at += c.Positions.Length;
                sub += c.Triangles.Length;
            }
            var m = new Mesh { name = name + " chunks " + made.Count, hideFlags = HideFlags.DontSave, indexFormat = verts > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(pos);
            m.SetNormals(nor);
            m.SetUVs(0, uv);
            m.SetColors(col);
            m.subMeshCount = subs;
            for (int i = from; i < to; i++)
                for (int s = 0; s < chunks[i].Triangles.Length; s++)
                    m.SetTriangles(chunks[i].Triangles[s], firstSub[i] + s, false, baseOf[i - from]);
            m.RecalculateBounds();
            owned.Add(m);
            made.Add(m);
            meshed = to;
        }

        void Finish()
        {
            var chunks = job.Chunks;
            int n = chunks.Count;
            if (n == 0)
            {
                // Nothing to split it into, so it breaks whole.
                working = false;
                job = null;
                made.Clear();
                return;
            }
            var set = new ChunkSet
            {
                Kind = current.Kind, Interior = current.Interior, Draws = new ChunkDraw[n][], Place = new Matrix4x4[n], Pivot = new Vector3[n],
                Support = new Vector3[n][], Centre = new Vector3[n], Size = new float[n],
            };
            bool first = true;
            for (int i = 0; i < n; i++)
            {
                var c = chunks[i];
                set.Draws[i] = new ChunkDraw[c.Slots.Length];
                for (int s = 0; s < c.Slots.Length; s++)
                    set.Draws[i][s] = new ChunkDraw { Mesh = made[meshOf[i]], Submesh = firstSub[i] + s, Material = ChunkMaterial(slotMats[c.Slots[s]], current.Interior) };
                set.Place[i] = Matrix4x4.Translate(c.Centre);
                set.Pivot[i] = Vector3.zero;
                set.Support[i] = c.Support;
                set.Centre[i] = c.Centre;
                set.Size[i] = c.Bounds.extents.magnitude;
                var b = new Bounds(c.Centre + c.Bounds.center, c.Bounds.size);
                if (first) { set.Bounds = b; first = false; } else set.Bounds.Encapsulate(b);
            }
            sets[current.Key] = set;
            Grouped += job.Grouped ? 1 : 0;
            working = false;
            job = null;
            made.Clear();
        }

        // A kind's parts as they are, one piece each, for one not split yet.
        public ChunkSet Whole(long key, BreakKind kind, Interior interior)
        {
            if (wholes.TryGetValue(key, out var w)) return w;
            parts.Clear();
            if (!partsOf(key, parts)) { wholes[key] = null; return null; }
            var list = new List<KindPart>();
            foreach (var p in parts) if (!p.Flat && p.Mesh != null) list.Add(p);
            int n = list.Count;
            if (n == 0) { wholes[key] = null; return null; }
            w = new ChunkSet
            {
                Kind = kind, Interior = interior, Whole = true, Draws = new ChunkDraw[n][], Place = new Matrix4x4[n], Pivot = new Vector3[n],
                Support = new Vector3[n][], Centre = new Vector3[n], Size = new float[n],
            };
            for (int i = 0; i < n; i++)
            {
                var p = list[i];
                var b = p.Submesh < p.Mesh.subMeshCount ? p.Mesh.GetSubMesh(p.Submesh).bounds : p.Mesh.bounds;
                if (b.size == Vector3.zero) b = p.Mesh.bounds;
                w.Draws[i] = new[] { new ChunkDraw { Mesh = p.Mesh, Submesh = p.Submesh, Material = ChunkMaterial(p.Material, interior) } };
                w.Place[i] = p.Local;
                w.Pivot[i] = b.center;
                w.Support[i] = Corners(b);
                w.Centre[i] = p.Local.MultiplyPoint3x4(b.center);
                w.Size[i] = b.extents.magnitude;
                var kb = new Bounds(w.Centre[i], Vector3.zero);
                foreach (var corner in w.Support[i]) kb.Encapsulate(p.Local.MultiplyPoint3x4(corner));
                if (i == 0) w.Bounds = kb; else w.Bounds.Encapsulate(kb);
            }
            wholes[key] = w;
            return w;
        }

        static Vector3[] Corners(Bounds b)
        {
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++)
                c[i] = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
            return c;
        }

        static readonly int CullId = Shader.PropertyToID("_Cull"), InteriorId = Shader.PropertyToID("_Interior"),
            InteriorColourId = Shader.PropertyToID("_InteriorColor");

        // A material's copy for chunks: both faces drawn, the inside shaded as the interior.
        public Material ChunkMaterial(Material from, Interior interior)
        {
            if (from == null) return null;
            if (chunkMats.TryGetValue((from, interior), out var m)) return m;
            m = new Material(from) { name = from.name + " (chunk)", hideFlags = HideFlags.DontSave };
            m.SetFloat(CullId, (float)CullMode.Off);
            m.SetFloat(InteriorId, 1f);
            m.SetColor(InteriorColourId, Fracture.InteriorColour(interior));
            owned.Add(m);
            return chunkMats[(from, interior)] = m;
        }

        readonly Dictionary<Material, Material> charMats = new Dictionary<Material, Material>();
        static readonly int ColourId = Shader.PropertyToID("_Color");

        // A material burnt black, for a stage that fire made.
        public Material CharMaterial(Material from)
        {
            if (from == null) return null;
            if (charMats.TryGetValue(from, out var m)) return m;
            m = new Material(from) { name = from.name + " (charred)", hideFlags = HideFlags.DontSave };
            if (m.HasProperty(ColourId)) m.SetColor(ColourId, m.GetColor(ColourId) * new Color(0.28f, 0.25f, 0.23f, 1f));
            owned.Add(m);
            return charMats[from] = m;
        }

        public void Dispose()
        {
            charMats.Clear();
            foreach (var o in owned) Looks.Release(o);
            owned.Clear();
            sets.Clear();
            wholes.Clear();
            known.Clear();
            queue.Clear();
            made.Clear();
            source = null;
            chunkMats.Clear();
            working = false;
            job = null;
        }
    }
}
