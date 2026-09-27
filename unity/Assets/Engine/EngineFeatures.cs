// EngineFeatures.cs - the map's features. A feature with a 3D model is
// drawn with its pose from the engine. A feature that is only a picture
// in the original stands as a camera facing quad, or lies on the ground
// for a mana pad, as OpenKingdoms' 3D view draws it. Sprites of one kind
// share one mesh.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Engine
{
    public sealed class EngineFeatures
    {
        public struct Placed
        {
            public int Model;
            public Matrix4x4[] Pieces;
        }

        public readonly List<Placed> Models = new List<Placed>();
        public int SpriteCount;       // features still drawn as pictures
        public int SpriteKinds;
        public GameObject Root;
        readonly List<Object> owned = new List<Object>();
        readonly float[] pose = new float[128 * 12];

        public void Build(Transform parent)
        {
            Root = new GameObject("Features");
            Root.transform.SetParent(parent, false);
            int n = OkEngine.okx_features(null, 0);
            var all = new OkxFeature[n];
            if (n > 0) OkEngine.okx_features(all, n);

            var bySprite = new Dictionary<int, List<OkxFeature>>();
            foreach (var f in all)
            {
                if (f.model >= 0)
                {
                    int nodes = OkEngine.okx_feature_pose(f.index, pose, 128);
                    if (nodes <= 0) continue;
                    var p = new Placed { Model = f.model, Pieces = new Matrix4x4[nodes] };
                    for (int i = 0; i < nodes; i++) p.Pieces[i] = EngineSettings.PoseToUnity(pose, i * 12);
                    Models.Add(p);
                }
                else if (f.sprite >= 0)
                {
                    if (!bySprite.TryGetValue(f.sprite, out var list)) bySprite[f.sprite] = list = new List<OkxFeature>();
                    list.Add(f);
                    SpriteCount++;
                }
            }
            var shader = Resources.Load<Shader>("Shaders/OkSprite");
            foreach (var kv in bySprite) BuildSprites(kv.Key, kv.Value, shader);
            SpriteKinds = bySprite.Count;
        }

        void BuildSprites(int def, List<OkxFeature> list, Shader shader)
        {
            int need = OkEngine.okx_sprite(def, null, 0, out int w, out int h);
            if (need <= 0) return;
            var px = new byte[need];
            OkEngine.okx_sprite(def, px, need, out w, out h);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = $"sprite {def}" };
            tex.LoadRawTextureData(px);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply(false, true);
            owned.Add(tex);
            var mat = new Material(shader) { name = $"sprite {def}", mainTexture = tex };
            owned.Add(mat);

            float s = EngineSettings.PxToUnits;
            var verts = new List<Vector3>();
            var uv = new List<Vector2>();
            var corner = new List<Vector2>();
            var tris = new List<int>();
            foreach (var f in list)
            {
                int b = verts.Count;
                if (f.flat != 0)
                {
                    // Lying on the ground, north up.
                    float x0 = f.x - f.offX, x1 = x0 + f.w;
                    float half = (f.top - f.bottom) * 0.5f;
                    verts.Add(EngineSettings.ToUnity(x0, f.bottom, f.z - half));
                    verts.Add(EngineSettings.ToUnity(x1, f.bottom, f.z - half));
                    verts.Add(EngineSettings.ToUnity(x1, f.bottom, f.z + half));
                    verts.Add(EngineSettings.ToUnity(x0, f.bottom, f.z + half));
                    for (int k = 0; k < 4; k++) corner.Add(Vector2.zero);
                }
                else
                {
                    var anchor = EngineSettings.ToUnity(f.x, 0, f.z);
                    for (int k = 0; k < 4; k++) verts.Add(anchor);
                    float left = -f.offX * s, right = (f.w - f.offX) * s;
                    corner.Add(new Vector2(left, f.top * s));
                    corner.Add(new Vector2(right, f.top * s));
                    corner.Add(new Vector2(right, f.bottom * s));
                    corner.Add(new Vector2(left, f.bottom * s));
                }
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            var mesh = new Mesh { name = $"sprites {def}", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, corner);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var bounds = mesh.bounds;
            bounds.Expand(new Vector3(w * s * 2, 64f, w * s * 2));
            mesh.bounds = bounds;
            owned.Add(mesh);
            var go = new GameObject(mesh.name);
            go.transform.SetParent(Root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        public void Dispose()
        {
            foreach (var o in owned) EngineSettings.Release(o);
            owned.Clear();
            Models.Clear();
            if (Root != null) EngineSettings.Release(Root);
        }
    }
}
