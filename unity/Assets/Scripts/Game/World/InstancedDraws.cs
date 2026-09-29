// InstancedDraws.cs - collects matrices per mesh, submesh and material
// over a frame, then draws each group with GPU instancing, 1023 at a time.
// An instance may carry a lift to its self light, a lodestone's breath.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class InstancedDraws
    {
        // Equatable, so a dictionary lookup does not box it.
        struct Key : System.IEquatable<Key>
        {
            public Mesh Mesh;
            public int Submesh;
            public Material Material;

            public override int GetHashCode() => (Mesh.GetHashCode() * 397 ^ Submesh) * 397 ^ Material.GetHashCode();
            public bool Equals(Key k) => k.Mesh == Mesh && k.Submesh == Submesh && k.Material == Material;
            public override bool Equals(object o) => o is Key k && Equals(k);
        }

        sealed class Group
        {
            public readonly List<Matrix4x4> Matrices = new List<Matrix4x4>();
            public readonly List<float> Lifts = new List<float>();
            public bool Lifted;
        }

        readonly Dictionary<Key, Group> groups = new Dictionary<Key, Group>();
        readonly Matrix4x4[] chunk = new Matrix4x4[1023];
        readonly float[] liftChunk = new float[1023];
        // One block per lifted draw in a frame, since a draw keeps the block it was given.
        readonly List<MaterialPropertyBlock> blocks = new List<MaterialPropertyBlock>();
        int blocksUsed;
        static readonly int LiftId = Shader.PropertyToID("_PulseLift");
        public bool CastShadows = true;
        public int Count { get; private set; }
        public int DrawCalls { get; private set; }

        public void Clear()
        {
            foreach (var g in groups.Values) { g.Matrices.Clear(); g.Lifts.Clear(); g.Lifted = false; }
            Count = 0;
        }

        // lift: the instance's self light is 1 + lift times its rest.
        public void Add(Mesh mesh, int submesh, Material material, in Matrix4x4 m, float lift = 0f)
        {
            var matrix = m;
            // Instancing cannot flip culling per instance, so a mirrored
            // matrix (the original models are mirrored in x) draws a mirrored
            // copy of the mesh with an unmirrored matrix instead.
            if (m.determinant < 0)
            {
                mesh = Mirrored(mesh);
                matrix = m * FlipX;
            }
            var k = new Key { Mesh = mesh, Submesh = submesh, Material = material };
            if (!groups.TryGetValue(k, out var g)) groups[k] = g = new Group();
            g.Matrices.Add(matrix);
            g.Lifts.Add(lift);
            if (lift != 0f) g.Lifted = true;
            Count++;
        }

        static readonly Matrix4x4 FlipX = Matrix4x4.Scale(new Vector3(-1, 1, 1));
        static readonly Dictionary<Mesh, Mesh> mirrors = new Dictionary<Mesh, Mesh>();

        public static Mesh Mirrored(Mesh mesh)
        {
            if (mirrors.TryGetValue(mesh, out var m) && m != null) return m;
            m = Object.Instantiate(mesh);
            m.name = mesh.name + " (mirrored)";
            m.hideFlags = HideFlags.DontSave;
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++) v[i].x = -v[i].x;
            m.vertices = v;
            var n = m.normals;
            for (int i = 0; i < n.Length; i++) n[i].x = -n[i].x;
            if (n.Length > 0) m.normals = n;
            for (int s = 0; s < m.subMeshCount; s++)
            {
                var t = m.GetTriangles(s);
                for (int i = 0; i + 2 < t.Length; i += 3) { int a = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = a; }
                m.SetTriangles(t, s);
            }
            m.RecalculateBounds();
            mirrors[mesh] = m;
            return m;
        }

        // Mirrored copies outlive a game only as long as their meshes.
        public static void ForgetMirrors()
        {
            foreach (var m in mirrors.Values) if (m != null) Looks.Release(m);
            mirrors.Clear();
        }

        MaterialPropertyBlock Block()
        {
            if (blocksUsed == blocks.Count) blocks.Add(new MaterialPropertyBlock());
            var b = blocks[blocksUsed++];
            b.Clear();
            return b;
        }

        public void Draw(int layer = 0)
        {
            DrawCalls = 0;
            blocksUsed = 0;
            foreach (var kv in groups)
            {
                var g = kv.Value;
                var list = g.Matrices;
                if (list.Count == 0) continue;
                var rp = new RenderParams(kv.Key.Material)
                {
                    layer = layer,
                    shadowCastingMode = CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                    worldBounds = new Bounds(Vector3.zero, Vector3.one * 100000f),
                };
                if (!kv.Key.Material.enableInstancing)
                {
                    // A drop-in model's own material may not instance.
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (g.Lifted) { rp.matProps = Block(); rp.matProps.SetFloat(LiftId, g.Lifts[i]); }
                        Graphics.RenderMesh(rp, kv.Key.Mesh, kv.Key.Submesh, list[i]);
                    }
                    DrawCalls += list.Count;
                    continue;
                }
                for (int start = 0; start < list.Count; start += chunk.Length)
                {
                    int n = Mathf.Min(chunk.Length, list.Count - start);
                    list.CopyTo(start, chunk, 0, n);
                    if (g.Lifted)
                    {
                        g.Lifts.CopyTo(start, liftChunk, 0, n);
                        rp.matProps = Block();
                        rp.matProps.SetFloatArray(LiftId, liftChunk);
                    }
                    Graphics.RenderMeshInstanced(rp, kv.Key.Mesh, kv.Key.Submesh, chunk, n);
                    DrawCalls++;
                }
            }
        }
    }
}
