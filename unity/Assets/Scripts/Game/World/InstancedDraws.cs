// InstancedDraws.cs - collects matrices per mesh, submesh and material
// over a frame, then draws each group with GPU instancing, 1023 at a time.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class InstancedDraws
    {
        struct Key
        {
            public Mesh Mesh;
            public int Submesh;
            public Material Material;

            public override int GetHashCode() => (Mesh.GetHashCode() * 397 ^ Submesh) * 397 ^ Material.GetHashCode();
            public override bool Equals(object o) => o is Key k && k.Mesh == Mesh && k.Submesh == Submesh && k.Material == Material;
        }

        readonly Dictionary<Key, List<Matrix4x4>> groups = new Dictionary<Key, List<Matrix4x4>>();
        readonly Matrix4x4[] chunk = new Matrix4x4[1023];
        public bool CastShadows = true;
        public int Count { get; private set; }
        public int DrawCalls { get; private set; }

        public void Clear()
        {
            foreach (var g in groups.Values) g.Clear();
            Count = 0;
        }

        public void Add(Mesh mesh, int submesh, Material material, in Matrix4x4 m)
        {
            var k = new Key { Mesh = mesh, Submesh = submesh, Material = material };
            if (!groups.TryGetValue(k, out var list)) groups[k] = list = new List<Matrix4x4>();
            list.Add(m);
            Count++;
        }

        public void Draw(int layer = 0)
        {
            DrawCalls = 0;
            foreach (var kv in groups)
            {
                var list = kv.Value;
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
                    foreach (var m in list) Graphics.RenderMesh(rp, kv.Key.Mesh, kv.Key.Submesh, m);
                    DrawCalls += list.Count;
                    continue;
                }
                for (int start = 0; start < list.Count; start += chunk.Length)
                {
                    int n = Mathf.Min(chunk.Length, list.Count - start);
                    list.CopyTo(start, chunk, 0, n);
                    Graphics.RenderMeshInstanced(rp, kv.Key.Mesh, kv.Key.Submesh, chunk, n);
                    DrawCalls++;
                }
            }
        }
    }
}
