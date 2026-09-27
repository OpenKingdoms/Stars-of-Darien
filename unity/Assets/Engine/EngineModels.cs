// EngineModels.cs - the engine's models as Unity meshes: one mesh per
// piece, a submesh per texture batch, cached by engine model id, with
// textures and materials shared by engine texture id.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Engine
{
    public sealed class EngineModel
    {
        public int NodeCount;
        public string[] NodeNames;
        public Mesh[] Pieces;             // null for a piece with no triangles
        public Material[][] Materials;    // per piece, per submesh
        public Bounds RestBounds;         // model units
        // Vertices are stored in world units, model units times this, so
        // piece matrices stay near unit scale and lighting keeps its
        // precision. A pose matrix from the engine takes Unscale first.
        public float VertexScale = 1f;
        public Matrix4x4 Unscale = Matrix4x4.identity;
    }

    public sealed class EngineModels
    {
        readonly Dictionary<int, EngineModel> models = new Dictionary<int, EngineModel>();
        readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();
        readonly List<Object> owned = new List<Object>();
        readonly Shader shader;
        Material vertexColour;

        public EngineModels()
        {
            shader = Resources.Load<Shader>("Shaders/OkModel");
            if (shader == null) shader = Shader.Find("Standard");
        }

        public int Count => models.Count;

        public EngineModel Get(int id)
        {
            if (id < 0) return null;
            if (models.TryGetValue(id, out var m)) return m;
            m = Build(id);
            models[id] = m;
            return m;
        }

        public Material MaterialFor(int texture)
        {
            if (texture < 0)
            {
                if (vertexColour == null) vertexColour = NewMaterial(Texture2D.whiteTexture, "vertex colour");
                return vertexColour;
            }
            if (materials.TryGetValue(texture, out var mat)) return mat;
            int need = OkEngine.okx_texture(texture, null, 0, out int w, out int h);
            Texture2D tex = Texture2D.whiteTexture;
            if (need > 0 && w > 0 && h > 0)
            {
                var px = new byte[need];
                OkEngine.okx_texture(texture, px, need, out w, out h);
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = $"okx texture {texture}" };
                tex.LoadRawTextureData(px);
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.Apply(false, true);
                owned.Add(tex);
            }
            mat = NewMaterial(tex, $"okx material {texture}");
            materials[texture] = mat;
            return mat;
        }

        Material NewMaterial(Texture tex, string name)
        {
            var mat = new Material(shader) { name = name, mainTexture = tex, enableInstancing = true };
            owned.Add(mat);
            return mat;
        }

        EngineModel Build(int id)
        {
            if (OkEngine.okx_model_info(id, out var info) != 0 || info.vertCount <= 0) return null;
            int V = info.vertCount;
            var pos = new float[V * 3];
            var nrm = new float[V * 3];
            var uv = new float[V * 2];
            var col = new uint[V];
            var node = new int[V];
            var idx = new int[info.indexCount];
            OkEngine.okx_model_geometry(id, pos, nrm, uv, col, node, idx);
            var nodes = new OkxNode[info.nodeCount];
            OkEngine.okx_model_nodes(id, nodes, nodes.Length);
            var batches = new OkxBatch[info.batchCount];
            OkEngine.okx_model_batches(id, batches, batches.Length);

            float vs = info.scale * EngineSettings.PxToUnits;
            if (vs <= 0) vs = 1;
            var model = new EngineModel
            {
                VertexScale = vs,
                Unscale = Matrix4x4.Scale(Vector3.one / vs),
                NodeCount = info.nodeCount,
                NodeNames = new string[info.nodeCount],
                Pieces = new Mesh[info.nodeCount],
                Materials = new Material[info.nodeCount][],
                RestBounds = new Bounds()
            };
            model.RestBounds.SetMinMax(new Vector3(info.minX, info.minY, info.minZ),
                                       new Vector3(info.maxX, info.maxY, info.maxZ));
            for (int n = 0; n < info.nodeCount; n++) model.NodeNames[n] = nodes[n].name;

            // A vertex belongs to one piece, and so does each triangle.
            var remap = new int[V];
            for (int n = 0; n < info.nodeCount; n++)
            {
                var verts = new List<Vector3>();
                var norms = new List<Vector3>();
                var uvs = new List<Vector2>();
                var cols = new List<Color32>();
                var subs = new List<List<int>>();
                var mats = new List<Material>();
                for (int v = 0; v < V; v++) remap[v] = -1;
                for (int b = 0; b < batches.Length; b++)
                {
                    List<int> tris = null;
                    bool textured = batches[b].texture >= 0;
                    int end = batches[b].firstIndex + batches[b].indexCount;
                    for (int i = batches[b].firstIndex; i + 2 < end && i + 2 < idx.Length; i += 3)
                    {
                        if (node[idx[i]] != n) continue;
                        if (tris == null) tris = new List<int>();
                        for (int k = 0; k < 3; k++)
                        {
                            int src = idx[i + k];
                            if (remap[src] < 0)
                            {
                                remap[src] = verts.Count;
                                verts.Add(new Vector3(pos[3 * src], pos[3 * src + 1], pos[3 * src + 2]) * vs);
                                norms.Add(new Vector3(nrm[3 * src], nrm[3 * src + 1], nrm[3 * src + 2]));
                                uvs.Add(new Vector2(uv[2 * src], uv[2 * src + 1]));
                                uint c = col[src];
                                byte a = (byte)(c >> 24);
                                cols.Add(textured ? new Color32(255, 255, 255, a)
                                                  : new Color32((byte)c, (byte)(c >> 8), (byte)(c >> 16), a));
                            }
                            tris.Add(remap[src]);
                        }
                    }
                    if (tris == null) continue;
                    subs.Add(tris);
                    mats.Add(MaterialFor(batches[b].texture));
                }
                if (subs.Count == 0) continue;
                var mesh = new Mesh { name = $"okx {id} piece {n} {nodes[n].name}" };
                mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                mesh.SetUVs(0, uvs);
                mesh.SetColors(cols);
                mesh.subMeshCount = subs.Count;
                for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s, false);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                owned.Add(mesh);
                model.Pieces[n] = mesh;
                model.Materials[n] = mats.ToArray();
            }
            return model;
        }

        public void Dispose()
        {
            foreach (var o in owned) EngineSettings.Release(o);
            owned.Clear();
            models.Clear();
            materials.Clear();
            vertexColour = null;
        }
    }
}
