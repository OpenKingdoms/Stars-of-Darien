// GlbLoader.cs - reads binary glTF (.glb) at runtime with no package: the
// node tree with its transforms, triangle meshes with normals and one set
// of UVs, and base colour (factor and embedded PNG or JPEG texture). glTF
// is right handed with +Z toward the viewer, and the drop-in convention
// exports Blender's south (-Y) as glTF +Z. The map's south is Unity's -Z,
// so z flips: positions and normals negate z, rotations negate x and y,
// and triangles turn round.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Game.World
{
    // Values a glb's nodes carry in their glTF extras.
    public sealed class GlbExtras : MonoBehaviour
    {
        public float StandTop;
        public string ReplacesPiece, ReplacesTexture;
    }

    public static class GlbLoader
    {
        // A template GameObject, inactive and hidden, or null with the reason.
        public static GameObject Load(string path, out string error)
        {
            try { return Load(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path), out error); }
            catch (Exception e) { error = e.Message; return null; }
        }

        public static GameObject Load(byte[] glb, string name, out string error)
        {
            error = null;
            if (glb.Length < 20 || BitConverter.ToUInt32(glb, 0) != 0x46546C67) { error = "not a glb file"; return null; }
            int jsonLen = BitConverter.ToInt32(glb, 12);
            if (BitConverter.ToUInt32(glb, 16) != 0x4E4F534A) { error = "first chunk is not JSON"; return null; }
            var json = MiniJson.Parse(Encoding.UTF8.GetString(glb, 20, jsonLen));
            int binStart = 20 + jsonLen + 8, binLen = 0;
            if (20 + jsonLen + 8 <= glb.Length) binLen = BitConverter.ToInt32(glb, 20 + jsonLen);
            var ctx = new Ctx { Json = json, Bin = glb, BinStart = binStart, BinLen = binLen };

            var root = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var scene = MiniJson.Arr(json, "scenes");
            int sceneIndex = Math.Max(0, MiniJson.Int(json, "scene", 0));
            var nodes = scene != null && sceneIndex < scene.Count ? MiniJson.Arr(scene[sceneIndex], "nodes") : null;
            if (nodes == null)
            {
                // No scene: every node without a parent.
                nodes = new List<object>();
                var all = MiniJson.Arr(json, "nodes");
                var child = new HashSet<int>();
                if (all != null)
                {
                    foreach (var n in all) foreach (var c in MiniJson.Arr(n, "children") ?? new List<object>()) child.Add((int)(double)c);
                    for (int i = 0; i < all.Count; i++) if (!child.Contains(i)) nodes.Add((double)i);
                }
            }
            foreach (var n in nodes) Node(ctx, (int)(double)n, root.transform);
            return root;
        }

        sealed class Ctx
        {
            public object Json;
            public byte[] Bin;
            public int BinStart, BinLen;
            public readonly Dictionary<int, Material> Materials = new Dictionary<int, Material>();
            public readonly Dictionary<int, Texture2D> Textures = new Dictionary<int, Texture2D>();
        }

        static void Node(Ctx ctx, int index, Transform parent)
        {
            var n = MiniJson.Arr(ctx.Json, "nodes")[index];
            var extras = MiniJson.Obj(n, "extras");
            var go = new GameObject(MiniJson.Text(n, "name", "node" + index)) { hideFlags = HideFlags.HideAndDontSave };
            var t = go.transform;
            t.SetParent(parent, false);
            var m = MiniJson.Arr(n, "matrix");
            if (m != null && m.Count == 16)
            {
                // Column major, converted by flipping z on both sides.
                var mat = new Matrix4x4();
                for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) mat[r, c] = (float)(double)m[c * 4 + r];
                var flip = Matrix4x4.Scale(new Vector3(1, 1, -1));
                mat = flip * mat * flip;
                t.localPosition = mat.GetColumn(3);
                t.localRotation = mat.rotation;
                t.localScale = mat.lossyScale;
            }
            else
            {
                var tr = MiniJson.Arr(n, "translation");
                if (tr != null) t.localPosition = new Vector3(F(tr[0]), F(tr[1]), -F(tr[2]));
                var ro = MiniJson.Arr(n, "rotation");
                if (ro != null) t.localRotation = new Quaternion(-F(ro[0]), -F(ro[1]), F(ro[2]), F(ro[3]));
                var sc = MiniJson.Arr(n, "scale");
                if (sc != null) t.localScale = new Vector3(F(sc[0]), F(sc[1]), F(sc[2]));
            }
            int mesh = MiniJson.Int(n, "mesh");
            // A plinth says how high a building on it stands.
            if (extras != null)
            {
                var x = go.AddComponent<GlbExtras>();
                if (extras.TryGetValue("standTop", out var top) && top is double d) x.StandTop = (float)d;
                x.ReplacesPiece = MiniJson.Text(extras, "replacesPiece", null);
                x.ReplacesTexture = MiniJson.Text(extras, "replacesTexture", null);
            }
            if (mesh >= 0) AddMesh(ctx, mesh, go);
            foreach (var c in MiniJson.Arr(n, "children") ?? new List<object>()) Node(ctx, (int)(double)c, t);
        }

        static float F(object o) => (float)(double)o;

        static void AddMesh(Ctx ctx, int index, GameObject go)
        {
            var meshJson = MiniJson.Arr(ctx.Json, "meshes")[index];
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var subs = new List<int[]>();
            var mats = new List<Material>();
            foreach (var p in MiniJson.Arr(meshJson, "primitives"))
            {
                if (MiniJson.Int(p, "mode", 4) != 4) continue;
                var attrs = MiniJson.Obj(p, "attributes");
                int pos = MiniJson.Int(attrs, "POSITION");
                if (pos < 0) continue;
                int basev = verts.Count;
                var P = ReadFloats(ctx, pos, 3);
                int count = P.Length / 3;
                for (int i = 0; i < count; i++) verts.Add(new Vector3(P[3 * i], P[3 * i + 1], -P[3 * i + 2]));
                int nrm = MiniJson.Int(attrs, "NORMAL");
                if (nrm >= 0)
                {
                    var N = ReadFloats(ctx, nrm, 3);
                    for (int i = 0; i < count; i++) norms.Add(new Vector3(N[3 * i], N[3 * i + 1], -N[3 * i + 2]));
                }
                else for (int i = 0; i < count; i++) norms.Add(Vector3.zero);
                int uv = MiniJson.Int(attrs, "TEXCOORD_0");
                if (uv >= 0)
                {
                    var U = ReadFloats(ctx, uv, 2);
                    // glTF's v runs down the picture, Unity's up.
                    for (int i = 0; i < count; i++) uvs.Add(new Vector2(U[2 * i], 1f - U[2 * i + 1]));
                }
                else for (int i = 0; i < count; i++) uvs.Add(Vector2.zero);

                int[] idx;
                int ind = MiniJson.Int(p, "indices");
                if (ind >= 0) idx = ReadInts(ctx, ind);
                else { idx = new int[count]; for (int i = 0; i < count; i++) idx[i] = i; }
                // z flipped, so triangles turn round.
                for (int i = 0; i + 2 < idx.Length; i += 3)
                {
                    int a = idx[i + 1];
                    idx[i + 1] = idx[i + 2] + basev;
                    idx[i + 2] = a + basev;
                    idx[i] += basev;
                }
                int matIndex = MiniJson.Int(p, "material");
                if (DoubleSided(ctx, matIndex))
                {
                    // The back of a double-sided surface as its own faces, with
                    // normals turned round, so both sides light and shade right.
                    int backBase = verts.Count;
                    for (int i = 0; i < count; i++)
                    {
                        verts.Add(verts[basev + i]);
                        norms.Add(-norms[basev + i]);
                        uvs.Add(uvs[basev + i]);
                    }
                    var both = new int[idx.Length * 2];
                    Array.Copy(idx, both, idx.Length);
                    for (int i = 0; i + 2 < idx.Length; i += 3)
                    {
                        both[idx.Length + i] = idx[i] - basev + backBase;
                        both[idx.Length + i + 1] = idx[i + 2] - basev + backBase;
                        both[idx.Length + i + 2] = idx[i + 1] - basev + backBase;
                    }
                    idx = both;
                }
                subs.Add(idx);
                mats.Add(MaterialFor(ctx, matIndex));
            }
            if (subs.Count == 0) return;
            var mesh = new Mesh { name = MiniJson.Text(meshJson, "name", go.name), hideFlags = HideFlags.DontSave };
            mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            var white = new Color32[verts.Count];
            for (int i = 0; i < white.Length; i++) white[i] = new Color32(255, 255, 255, 255);
            mesh.colors32 = white;
            mesh.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
            bool haveNormals = false;
            foreach (var nv in norms) if (nv != Vector3.zero) { haveNormals = true; break; }
            if (haveNormals) mesh.SetNormals(norms); else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats.ToArray();
        }

        static Material MaterialFor(Ctx ctx, int index)
        {
            if (ctx.Materials.TryGetValue(index, out var m)) return m;
            var json = index >= 0 ? MiniJson.Arr(ctx.Json, "materials")?[index] : null;
            var pbr = MiniJson.Obj(json, "pbrMetallicRoughness");
            Texture2D tex = null;
            var bct = MiniJson.Obj(pbr, "baseColorTexture");
            if (bct != null) tex = TextureFor(ctx, MiniJson.Int(bct, "index"));
            m = Looks.Model(tex);
            m.name = MiniJson.Text(json, "name", "glb material");
            var factor = MiniJson.Arr(pbr, "baseColorFactor");
            if (factor != null && factor.Count == 4) m.color = new Color(F(factor[0]), F(factor[1]), F(factor[2]), F(factor[3]));
            m.SetFloat("_Glossiness", (1f - (float)MiniJson.Num(pbr, "roughnessFactor", 1)) * 0.5f);
            // Cut out by the texture's alpha whatever the mode says, since
            // sprite-painted models leave the sprite's clear pixels clear.
            m.SetFloat("_Cutoff", MiniJson.Text(json, "alphaMode") == "MASK" ? (float)MiniJson.Num(json, "alphaCutoff", 0.5) : 0.5f);
            ctx.Materials[index] = m;
            return m;
        }

        static bool DoubleSided(Ctx ctx, int material)
        {
            var json = material >= 0 ? MiniJson.Arr(ctx.Json, "materials")?[material] : null;
            return json is Dictionary<string, object> d && d.TryGetValue("doubleSided", out var ds) && ds is bool b && b;
        }

        static Texture2D TextureFor(Ctx ctx, int index)
        {
            if (index < 0) return null;
            if (ctx.Textures.TryGetValue(index, out var t)) return t;
            var texJson = MiniJson.Arr(ctx.Json, "textures")[index];
            int src = MiniJson.Int(texJson, "source");
            var img = src >= 0 ? MiniJson.Arr(ctx.Json, "images")[src] : null;
            int view = MiniJson.Int(img, "bufferView");
            if (view >= 0)
            {
                View(ctx, view, out int off, out int len, out _);
                var bytes = new byte[len];
                Buffer.BlockCopy(ctx.Bin, off, bytes, 0, len);
                t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { hideFlags = HideFlags.DontSave, name = MiniJson.Text(img, "name", "glb texture") };
                if (!t.LoadImage(bytes)) t = null;
                else
                {
                    t.wrapMode = TextureWrapMode.Clamp;
                    t.filterMode = FilterMode.Trilinear;
                    t.anisoLevel = 4;
                }
            }
            ctx.Textures[index] = t;
            return t;
        }

        static void View(Ctx ctx, int view, out int offset, out int length, out int stride)
        {
            var v = MiniJson.Arr(ctx.Json, "bufferViews")[view];
            if (MiniJson.Int(v, "buffer", 0) != 0) throw new NotSupportedException("only the glb's own buffer is read");
            offset = ctx.BinStart + MiniJson.Int(v, "byteOffset", 0);
            length = MiniJson.Int(v, "byteLength", 0);
            stride = MiniJson.Int(v, "byteStride", 0);
        }

        static int Components(string type) =>
            type == "SCALAR" ? 1 : type == "VEC2" ? 2 : type == "VEC3" ? 3 : type == "VEC4" ? 4 : type == "MAT4" ? 16 : 1;

        static float[] ReadFloats(Ctx ctx, int accessor, int want)
        {
            var a = MiniJson.Arr(ctx.Json, "accessors")[accessor];
            int count = MiniJson.Int(a, "count", 0), comps = Components(MiniJson.Text(a, "type"));
            int type = MiniJson.Int(a, "componentType", 5126);
            bool norm = a is Dictionary<string, object> d && d.TryGetValue("normalized", out var nv) && nv is bool nb && nb;
            var result = new float[count * want];
            if (MiniJson.Int(a, "bufferView") < 0) return result;
            View(ctx, MiniJson.Int(a, "bufferView"), out int off, out _, out int stride);
            off += MiniJson.Int(a, "byteOffset", 0);
            int size = type == 5126 ? 4 : type == 5123 || type == 5122 ? 2 : 1;
            if (stride == 0) stride = size * comps;
            for (int i = 0; i < count; i++)
                for (int c = 0; c < Mathf.Min(comps, want); c++)
                {
                    int at = off + i * stride + c * size;
                    float v;
                    switch (type)
                    {
                        case 5126: v = BitConverter.ToSingle(ctx.Bin, at); break;
                        case 5123: v = BitConverter.ToUInt16(ctx.Bin, at); if (norm) v /= 65535f; break;
                        case 5122: v = BitConverter.ToInt16(ctx.Bin, at); if (norm) v = Mathf.Max(v / 32767f, -1f); break;
                        case 5121: v = ctx.Bin[at]; if (norm) v /= 255f; break;
                        default: v = (sbyte)ctx.Bin[at]; if (norm) v = Mathf.Max(v / 127f, -1f); break;
                    }
                    result[i * want + c] = v;
                }
            return result;
        }

        static int[] ReadInts(Ctx ctx, int accessor)
        {
            var a = MiniJson.Arr(ctx.Json, "accessors")[accessor];
            int count = MiniJson.Int(a, "count", 0), type = MiniJson.Int(a, "componentType", 5125);
            View(ctx, MiniJson.Int(a, "bufferView"), out int off, out _, out _);
            off += MiniJson.Int(a, "byteOffset", 0);
            var r = new int[count];
            for (int i = 0; i < count; i++)
                r[i] = type == 5125 ? (int)BitConverter.ToUInt32(ctx.Bin, off + 4 * i)
                     : type == 5123 ? BitConverter.ToUInt16(ctx.Bin, off + 2 * i)
                     : ctx.Bin[off + i];
            return r;
        }
    }
}
