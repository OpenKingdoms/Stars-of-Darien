// GlbWriter.cs - a Unity model as a glb the game's loader reads back the
// same: nodes, triangle meshes with normals and UVs, and materials with
// their colour, smoothness, alpha mode, sides and main picture as PNG.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    public static class GlbWriter
    {
        sealed class Ctx
        {
            public readonly List<object> Nodes = new List<object>(), Meshes = new List<object>(), Materials = new List<object>();
            public readonly List<object> Textures = new List<object>(), Images = new List<object>(), Accessors = new List<object>(), Views = new List<object>();
            public readonly MemoryStream Bin = new MemoryStream();
            public readonly Dictionary<Material, int> MaterialIndex = new Dictionary<Material, int>();
            public readonly Dictionary<Texture, int> TextureIndex = new Dictionary<Texture, int>();
            public readonly List<string> Notes;
            public Ctx(List<string> notes) => Notes = notes;
        }

        public static byte[] Write(GameObject root, List<string> notes = null)
        {
            var ctx = new Ctx(notes ?? new List<string>());
            int top = Node(ctx, root.transform, true);
            var json = new Dictionary<string, object>
            {
                ["asset"] = new Dictionary<string, object> { ["version"] = "2.0", ["generator"] = "OpenKingdoms Studio" },
                ["scene"] = 0.0,
                ["scenes"] = new List<object> { new Dictionary<string, object> { ["nodes"] = new List<object> { (double)top } } },
                ["nodes"] = ctx.Nodes,
            };
            if (ctx.Meshes.Count > 0) json["meshes"] = ctx.Meshes;
            if (ctx.Materials.Count > 0) json["materials"] = ctx.Materials;
            if (ctx.Textures.Count > 0)
            {
                json["textures"] = ctx.Textures;
                json["images"] = ctx.Images;
                json["samplers"] = new List<object> { new Dictionary<string, object> { ["magFilter"] = 9729.0, ["minFilter"] = 9987.0 } };
            }
            if (ctx.Accessors.Count > 0) { json["accessors"] = ctx.Accessors; json["bufferViews"] = ctx.Views; }
            var f = new GlbFile { Json = json, Bin = ctx.Bin.ToArray() };
            if (f.Bin.Length > 0) json["buffers"] = new List<object> { new Dictionary<string, object> { ["byteLength"] = (double)f.Bin.Length } };
            return f.Write();
        }

        static int Node(Ctx ctx, Transform t, bool root)
        {
            var node = new Dictionary<string, object> { ["name"] = t.name };
            int index = ctx.Nodes.Count;
            ctx.Nodes.Add(node);
            // The root keeps its own turn and size but not its place, since the
            // model's origin is its anchor.
            var p = root ? Vector3.zero : t.localPosition;
            var q = t.localRotation;
            var s = t.localScale;
            if (p != Vector3.zero) node["translation"] = new List<object> { (double)p.x, (double)p.y, (double)-p.z };
            if (q != Quaternion.identity) node["rotation"] = new List<object> { (double)-q.x, (double)-q.y, (double)q.z, (double)q.w };
            if (s != Vector3.one) node["scale"] = new List<object> { (double)s.x, (double)s.y, (double)s.z };
            Mesh mesh = null;
            Material[] mats = null;
            var mr = t.GetComponent<MeshRenderer>();
            var mf = t.GetComponent<MeshFilter>();
            if (mr != null && mf != null) { mesh = mf.sharedMesh; mats = mr.sharedMaterials; }
            var sk = t.GetComponent<SkinnedMeshRenderer>();
            if (mesh == null && sk != null) { mesh = sk.sharedMesh; mats = sk.sharedMaterials; }
            if (mesh != null)
            {
                int m = Mesh(ctx, mesh, mats);
                if (m >= 0) node["mesh"] = (double)m;
            }
            var children = new List<object>();
            for (int i = 0; i < t.childCount; i++) children.Add((double)Node(ctx, t.GetChild(i), false));
            if (children.Count > 0) node["children"] = children;
            return index;
        }

        static int Mesh(Ctx ctx, Mesh mesh, Material[] mats)
        {
            Vector3[] v;
            try { v = mesh.vertices; }
            catch (Exception e) { ctx.Notes.Add($"The mesh {mesh.name} could not be read: {e.Message}"); return -1; }
            if (v.Length == 0) return -1;
            var n = mesh.normals;
            var uv = mesh.uv;
            var pos = new float[v.Length * 3];
            var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var hi = -lo;
            for (int i = 0; i < v.Length; i++)
            {
                var p = new Vector3(v[i].x, v[i].y, -v[i].z);
                pos[3 * i] = p.x; pos[3 * i + 1] = p.y; pos[3 * i + 2] = p.z;
                lo = Vector3.Min(lo, p);
                hi = Vector3.Max(hi, p);
            }
            var attrs = new Dictionary<string, object> { ["POSITION"] = (double)Floats(ctx, pos, 3, "VEC3", lo, hi) };
            if (n != null && n.Length == v.Length)
            {
                var nf = new float[n.Length * 3];
                for (int i = 0; i < n.Length; i++) { nf[3 * i] = n[i].x; nf[3 * i + 1] = n[i].y; nf[3 * i + 2] = -n[i].z; }
                attrs["NORMAL"] = (double)Floats(ctx, nf, 3, "VEC3", null, null);
            }
            if (uv != null && uv.Length == v.Length)
            {
                var uf = new float[uv.Length * 2];
                for (int i = 0; i < uv.Length; i++) { uf[2 * i] = uv[i].x; uf[2 * i + 1] = 1f - uv[i].y; }
                attrs["TEXCOORD_0"] = (double)Floats(ctx, uf, 2, "VEC2", null, null);
            }
            var prims = new List<object>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                var tri = mesh.GetTriangles(s);
                if (tri.Length < 3) continue;
                // z flipped, so triangles turn round.
                for (int i = 0; i + 2 < tri.Length; i += 3) { int a = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = a; }
                var prim = new Dictionary<string, object> { ["attributes"] = attrs, ["indices"] = (double)Indices(ctx, tri, v.Length), ["mode"] = 4.0 };
                var mat = mats != null && mats.Length > 0 ? mats[Mathf.Min(s, mats.Length - 1)] : null;
                if (mat != null) prim["material"] = (double)Material(ctx, mat);
                prims.Add(prim);
            }
            if (prims.Count == 0) return -1;
            ctx.Meshes.Add(new Dictionary<string, object> { ["name"] = mesh.name, ["primitives"] = prims });
            return ctx.Meshes.Count - 1;
        }

        static int Material(Ctx ctx, Material m)
        {
            if (ctx.MaterialIndex.TryGetValue(m, out int i)) return i;
            var pbr = new Dictionary<string, object> { ["metallicFactor"] = 0.0 };
            Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            pbr["baseColorFactor"] = new List<object> { (double)c.r, (double)c.g, (double)c.b, (double)c.a };
            float smooth = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0.2f;
            pbr["roughnessFactor"] = (double)Mathf.Clamp01(1f - smooth);
            Texture tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
            if (tex == null && m.HasProperty("_MainTex")) tex = m.GetTexture("_MainTex");
            if (tex != null)
            {
                int t = Texture(ctx, tex);
                if (t >= 0) pbr["baseColorTexture"] = new Dictionary<string, object> { ["index"] = (double)t };
            }
            var mat = new Dictionary<string, object> { ["name"] = m.name, ["pbrMetallicRoughness"] = pbr };
            string mode = AlphaMode(m);
            if (mode != "OPAQUE") mat["alphaMode"] = mode;
            if (mode == "MASK" && m.HasProperty("_Cutoff")) mat["alphaCutoff"] = (double)m.GetFloat("_Cutoff");
            if (m.HasProperty("_Cull") && m.GetFloat("_Cull") < 0.5f) mat["doubleSided"] = true;
            ctx.Materials.Add(mat);
            ctx.MaterialIndex[m] = ctx.Materials.Count - 1;
            return ctx.Materials.Count - 1;
        }

        // Cut out for URP's alpha clipping or the Standard shader's Cutout,
        // blended for their transparent modes, otherwise opaque.
        public static string AlphaMode(Material m)
        {
            if ((m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > 0.5f) || m.IsKeywordEnabled("_ALPHATEST_ON") ||
                (m.HasProperty("_Mode") && Mathf.RoundToInt(m.GetFloat("_Mode")) == 1)) return "MASK";
            if ((m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f) || (m.HasProperty("_Mode") && m.GetFloat("_Mode") > 1.5f)) return "BLEND";
            return "OPAQUE";
        }

        static int Texture(Ctx ctx, Texture tex)
        {
            if (ctx.TextureIndex.TryGetValue(tex, out int i)) return i;
            byte[] png = Png(tex, ctx.Notes);
            int result = -1;
            if (png != null)
            {
                int view = View(ctx, png);
                ctx.Images.Add(new Dictionary<string, object> { ["name"] = tex.name, ["bufferView"] = (double)view, ["mimeType"] = "image/png" });
                ctx.Textures.Add(new Dictionary<string, object> { ["source"] = (double)(ctx.Images.Count - 1), ["sampler"] = 0.0 });
                result = ctx.Textures.Count - 1;
            }
            ctx.TextureIndex[tex] = result;
            return result;
        }

        // Any texture as PNG, drawn through the GPU so unreadable and
        // compressed ones work too.
        public static byte[] Png(Texture tex, List<string> notes)
        {
            RenderTexture rt = null;
            Texture2D read = null;
            var was = RenderTexture.active;
            try
            {
                rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                read = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, false);
                read.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                read.Apply();
                return read.EncodeToPNG();
            }
            catch (Exception e)
            {
                notes?.Add($"The picture {tex.name} could not be read: {e.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = was;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (read != null) UnityEngine.Object.DestroyImmediate(read);
            }
        }

        static int View(Ctx ctx, byte[] bytes)
        {
            while (ctx.Bin.Length % 4 != 0) ctx.Bin.WriteByte(0);
            long at = ctx.Bin.Length;
            ctx.Bin.Write(bytes, 0, bytes.Length);
            ctx.Views.Add(new Dictionary<string, object> { ["buffer"] = 0.0, ["byteOffset"] = (double)at, ["byteLength"] = (double)bytes.Length });
            return ctx.Views.Count - 1;
        }

        static int Floats(Ctx ctx, float[] data, int comps, string type, Vector3? lo, Vector3? hi)
        {
            var bytes = new byte[data.Length * 4];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            int view = View(ctx, bytes);
            var acc = new Dictionary<string, object>
            {
                ["bufferView"] = (double)view, ["componentType"] = 5126.0, ["count"] = (double)(data.Length / comps), ["type"] = type,
            };
            if (lo.HasValue && hi.HasValue)
            {
                acc["min"] = new List<object> { (double)lo.Value.x, (double)lo.Value.y, (double)lo.Value.z };
                acc["max"] = new List<object> { (double)hi.Value.x, (double)hi.Value.y, (double)hi.Value.z };
            }
            ctx.Accessors.Add(acc);
            return ctx.Accessors.Count - 1;
        }

        static int Indices(Ctx ctx, int[] tri, int vertices)
        {
            byte[] bytes;
            double type;
            if (vertices <= 65535)
            {
                bytes = new byte[tri.Length * 2];
                for (int i = 0; i < tri.Length; i++) { bytes[2 * i] = (byte)tri[i]; bytes[2 * i + 1] = (byte)(tri[i] >> 8); }
                type = 5123;
            }
            else
            {
                bytes = new byte[tri.Length * 4];
                Buffer.BlockCopy(tri, 0, bytes, 0, bytes.Length);
                type = 5125;
            }
            int view = View(ctx, bytes);
            ctx.Accessors.Add(new Dictionary<string, object> { ["bufferView"] = (double)view, ["componentType"] = type, ["count"] = (double)tri.Length, ["type"] = "SCALAR" });
            return ctx.Accessors.Count - 1;
        }
    }
}
