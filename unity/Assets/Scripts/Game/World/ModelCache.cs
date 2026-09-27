// ModelCache.cs - backend models as Unity meshes: one mesh per piece with
// a submesh per texture batch, and one material per texture, shared by
// every model that uses it. A drop-in override replaces a model whole.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class PresentedModel
    {
        public ModelData Data;
        public Mesh[] Pieces;             // null for a piece with no triangles
        public Material[][] Materials;    // per piece, per submesh
        public OverrideModel Override;    // a drop-in replacement, drawn instead
        public Matrix4x4[] RestInverse;   // per piece, world scale, for overrides that follow pieces
        public Matrix4x4 Unscale;         // right-multiplies a backend pose to suit the scaled meshes
    }

    public sealed class ModelCache
    {
        readonly IGameBackend backend;
        readonly Dictionary<int, PresentedModel> models = new Dictionary<int, PresentedModel>();
        readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();
        readonly List<Object> owned = new List<Object>();

        public ModelCache(IGameBackend backend) => this.backend = backend;

        public PresentedModel Get(int id) => Get(id, OverrideKind.Unit, null);

        // A model, with the drop-in override found under any of the names.
        public PresentedModel Get(int id, OverrideKind kind, string[] names)
        {
            if (id < 0) return null;
            if (models.TryGetValue(id, out var m)) return m;
            var data = backend.GetModel(id);
            m = data != null ? Build(data) : null;
            if (m != null)
            {
                m.Unscale = Matrix4x4.Scale(Vector3.one / Mathf.Max(1e-12f, data.Scale));
                var all = new List<string>();
                if (names != null) all.AddRange(names);
                all.Add(data.Name);
                m.Override = OverrideLoader.Find(kind, data.Pieces, all.ToArray());
                if (m.Override != null) m.RestInverse = OverrideModel.RestInverse(data.Pieces, data.Scale);
            }
            models[id] = m;
            return m;
        }

        public Material MaterialFor(int texture)
        {
            if (materials.TryGetValue(texture, out var m)) return m;
            Texture tex = null;
            if (texture >= 0)
            {
                var t = UI.UiKit.ToTexture(backend.Texture(texture), true);
                if (t != null) { t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Bilinear; owned.Add(t); }
                tex = t;
            }
            m = Looks.Model(tex);
            owned.Add(m);
            materials[texture] = m;
            return m;
        }

        PresentedModel Build(ModelData d)
        {
            int pieces = d.Pieces.Length;
            var result = new PresentedModel { Data = d, Pieces = new Mesh[pieces], Materials = new Material[pieces][] };
            for (int p = 0; p < pieces; p++)
            {
                var remap = new Dictionary<int, int>();
                var verts = new List<Vector3>();
                var norms = new List<Vector3>();
                var uvs = new List<Vector2>();
                var cols = new List<Color32>();
                var subs = new List<List<int>>();
                var mats = new List<Material>();
                foreach (var batch in d.Batches)
                {
                    List<int> tris = null;
                    int end = Mathf.Min(batch.FirstIndex + batch.IndexCount, d.Indices.Length);
                    for (int i = batch.FirstIndex; i + 2 < end; i += 3)
                    {
                        int a = d.Indices[i];
                        if (d.VertexPiece[a] != p) continue;
                        if (tris == null) tris = new List<int>();
                        for (int k = 0; k < 3; k++)
                        {
                            int v = d.Indices[i + k];
                            if (!remap.TryGetValue(v, out int nv))
                            {
                                nv = verts.Count;
                                remap[v] = nv;
                                // Scale goes into the vertices, so poses carry no tiny
                                // scale, which the built-in lit shaders draw black.
                                verts.Add(d.Positions[v] * d.Scale);
                                norms.Add(d.Normals != null && v < d.Normals.Length ? d.Normals[v] : Vector3.up);
                                uvs.Add(d.Uvs != null && v < d.Uvs.Length ? d.Uvs[v] : Vector2.zero);
                                cols.Add(d.Colors != null && v < d.Colors.Length ? d.Colors[v] : new Color32(255, 255, 255, 255));
                            }
                            tris.Add(nv);
                        }
                    }
                    if (tris == null) continue;
                    subs.Add(tris);
                    mats.Add(MaterialFor(batch.Texture));
                }
                if (subs.Count == 0) continue;
                var mesh = new Mesh { name = d.Name + "/" + d.Pieces[p].Name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                mesh.SetUVs(0, uvs);
                mesh.SetColors(cols);
                mesh.subMeshCount = subs.Count;
                for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
                mesh.RecalculateBounds();
                owned.Add(mesh);
                result.Pieces[p] = mesh;
                result.Materials[p] = mats.ToArray();
            }
            return result;
        }

        public void Dispose()
        {
            foreach (var o in owned) Looks.Release(o);
            owned.Clear();
            models.Clear();
            materials.Clear();
        }
    }
}
