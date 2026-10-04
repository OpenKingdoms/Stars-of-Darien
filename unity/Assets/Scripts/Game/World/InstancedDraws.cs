// InstancedDraws.cs - collects matrices per mesh, submesh and material
// over a frame, then draws each group with GPU instancing, 1023 at a time.
// An instance may carry a lift to its self light, a lodestone's breath, a
// dithered fade, for scenery handing over to its next stage, and the marks
// fire and magic left on it (SceneryLook).
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
            public readonly List<float> Fades = new List<float>();
            public readonly List<SceneryInstance> Looks = new List<SceneryInstance>();
            public bool Lifted, Faded, Marked;
        }

        readonly Dictionary<Key, Group> groups = new Dictionary<Key, Group>();
        readonly Matrix4x4[] chunk = new Matrix4x4[1023];
        readonly float[] liftChunk = new float[1023];
        readonly float[] fadeChunk = new float[1023];
        readonly Vector4[] markChunk = new Vector4[1023], heatChunk = new Vector4[1023], bendChunk = new Vector4[1023], footChunk = new Vector4[1023];
        // One block per lifted draw in a frame, since a draw keeps the block it was given.
        readonly List<MaterialPropertyBlock> blocks = new List<MaterialPropertyBlock>();
        int blocksUsed;
        static readonly int LiftId = Shader.PropertyToID("_PulseLift");
        static readonly int FadeId = Shader.PropertyToID("_OkuFade");
        static readonly int MarkId = Shader.PropertyToID("_OkuMark"), HeatId = Shader.PropertyToID("_OkuHeat");
        static readonly int BendId = Shader.PropertyToID("_OkuBend"), FootId = Shader.PropertyToID("_OkuFoot");
        public bool CastShadows = true;
        public int Count { get; private set; }
        public int DrawCalls { get; private set; }

        public void Clear()
        {
            foreach (var g in groups.Values) { g.Matrices.Clear(); g.Lifts.Clear(); g.Fades.Clear(); g.Looks.Clear(); g.Lifted = g.Faded = g.Marked = false; }
            Count = 0;
        }

        // lift: the instance's self light is 1 + lift times its rest. fade:
        // 0 drawn whole, from 0 to 1 dithering away, from 0 to -1 dithering in.
        public void Add(Mesh mesh, int submesh, Material material, in Matrix4x4 m, float lift = 0f, float fade = 0f) =>
            Add(mesh, submesh, material, m, lift, fade, default, false);

        // look: the marks fire and magic left on it.
        public void Add(Mesh mesh, int submesh, Material material, in Matrix4x4 m, float lift, float fade, in SceneryInstance look) =>
            Add(mesh, submesh, material, m, lift, fade, look, true);

        void Add(Mesh mesh, int submesh, Material material, in Matrix4x4 m, float lift, float fade, in SceneryInstance look, bool marked)
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
            g.Fades.Add(fade);
            // Looks are kept only once a group has a marked instance.
            if (marked)
            {
                while (g.Looks.Count < g.Matrices.Count - 1) g.Looks.Add(default);
                g.Looks.Add(look);
                g.Marked = true;
            }
            else if (g.Marked) g.Looks.Add(default);
            if (lift != 0f) g.Lifted = true;
            if (fade != 0f) g.Faded = true;
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

        void SetLooks(MaterialPropertyBlock block, List<SceneryInstance> looks, int start, int n)
        {
            for (int i = 0; i < n; i++)
            {
                var l = looks[start + i];
                markChunk[i] = l.Mark;
                heatChunk[i] = l.Heat;
                bendChunk[i] = l.Bend;
                footChunk[i] = l.Foot;
            }
            block.SetVectorArray(MarkId, markChunk);
            block.SetVectorArray(HeatId, heatChunk);
            block.SetVectorArray(BendId, bendChunk);
            block.SetVectorArray(FootId, footChunk);
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
                        if (g.Lifted || g.Faded || g.Marked)
                        {
                            rp.matProps = Block();
                            rp.matProps.SetFloat(LiftId, g.Lifts[i]);
                            rp.matProps.SetFloat(FadeId, g.Fades[i]);
                            if (g.Marked)
                            {
                                var look = g.Looks[i];
                                rp.matProps.SetVector(MarkId, look.Mark);
                                rp.matProps.SetVector(HeatId, look.Heat);
                                rp.matProps.SetVector(BendId, look.Bend);
                                rp.matProps.SetVector(FootId, look.Foot);
                            }
                        }
                        Graphics.RenderMesh(rp, kv.Key.Mesh, kv.Key.Submesh, list[i]);
                    }
                    DrawCalls += list.Count;
                    continue;
                }
                for (int start = 0; start < list.Count; start += chunk.Length)
                {
                    int n = Mathf.Min(chunk.Length, list.Count - start);
                    list.CopyTo(start, chunk, 0, n);
                    if (g.Lifted || g.Faded || g.Marked)
                    {
                        rp.matProps = Block();
                        if (g.Lifted) { g.Lifts.CopyTo(start, liftChunk, 0, n); rp.matProps.SetFloatArray(LiftId, liftChunk); }
                        if (g.Faded) { g.Fades.CopyTo(start, fadeChunk, 0, n); rp.matProps.SetFloatArray(FadeId, fadeChunk); }
                        if (g.Marked) SetLooks(rp.matProps, g.Looks, start, n);
                    }
                    Graphics.RenderMeshInstanced(rp, kv.Key.Mesh, kv.Key.Submesh, chunk, n);
                    DrawCalls++;
                }
            }
        }
    }
}
