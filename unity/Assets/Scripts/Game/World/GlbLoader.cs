// GlbLoader.cs - reads binary glTF (.glb) at runtime with no package: the
// node tree with its transforms, triangle meshes with normals and one set
// of UVs, and base colour (factor and embedded PNG or JPEG texture, or a
// picture painted at load from the player's own game files). glTF
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
            var ctx = new Ctx { Json = json, Bin = glb, BinStart = binStart, BinLen = binLen, Name = $"{name}:{jsonLen}" };

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
            public string Name;
            public Faces Faces;
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
            var cols = new List<Color>();
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
                // Vertex colour multiplies the base colour, where a model has it.
                int col = MiniJson.Int(attrs, "COLOR_0");
                if (col >= 0)
                {
                    var acc = MiniJson.Arr(ctx.Json, "accessors")[col];
                    int comps = MiniJson.Text(acc, "type") == "VEC4" ? 4 : 3;
                    var C = ReadFloats(ctx, col, comps);
                    for (int i = 0; i < count; i++)
                        cols.Add(new Color(C[comps * i], C[comps * i + 1], C[comps * i + 2], comps == 4 ? C[comps * i + 3] : 1f));
                }
                else for (int i = 0; i < count; i++) cols.Add(Color.white);

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
                        cols.Add(cols[basev + i]);
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
            mesh.SetColors(cols);
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
            var extras = MiniJson.Obj(json, "extras");
            var paint = MiniJson.Obj(extras, "okPaint");
            var bct = MiniJson.Obj(pbr, "baseColorTexture");
            if (paint != null) tex = PaintedTexture(paint, ctx, index);
            else if (bct != null) tex = TextureFor(ctx, MiniJson.Int(bct, "index"));
            m = Looks.Model(tex);
            m.name = MiniJson.Text(json, "name", "glb material");
            var factor = MiniJson.Arr(pbr, "baseColorFactor");
            if (factor != null && factor.Count == 4) m.color = new Color(F(factor[0]), F(factor[1]), F(factor[2]), F(factor[3]));
            if (paint != null) m.color = PaintColour(paint, extras, m.color, tex != null);
            m.SetFloat("_Glossiness", (1f - (float)MiniJson.Num(pbr, "roughnessFactor", 1)) * 0.5f);
            Physical(ctx, json, pbr, m);
            // A plain emissive colour becomes the shader's self light.
            var glow = MiniJson.Arr(json, "emissiveFactor");
            if (glow != null && glow.Count == 3 && MiniJson.Obj(json, "emissiveTexture") == null)
            {
                float e = Mathf.Max(F(glow[0]), Mathf.Max(F(glow[1]), F(glow[2])));
                var strength = MiniJson.Obj(MiniJson.Obj(json, "extensions"), "KHR_materials_emissive_strength");
                if (strength != null) e *= (float)MiniJson.Num(strength, "emissiveStrength", 1);
                if (e > 0) m.SetFloat("_Emission", e);
            }
            // The glTF alpha mode: OPAQUE ignores alpha, so nothing is cut out
            // and no clear texel opens a hole, MASK cuts at alphaCutoff (0.5
            // by default), and BLEND blends over what is behind it.
            string mode = MiniJson.Text(json, "alphaMode", "OPAQUE");
            // A painted picture's clear texels cut out only when okPaint says so.
            if (paint != null) mode = MiniJson.Text(paint, "alpha", "opaque") == "mask" ? "MASK" : "OPAQUE";
            m.SetFloat("_Cutoff", mode == "MASK" ? (float)MiniJson.Num(json, "alphaCutoff", 0.5) : mode == "BLEND" ? 0.004f : 0f);
            if (mode == "OPAQUE") m.color = new Color(m.color.r, m.color.g, m.color.b, 1f);
            if (mode == "BLEND") Blended(m);
            Emission(ctx, json, m);
            ctx.Materials[index] = m;
            return m;
        }

        // The glTF metal and roughness, their texture, and a clear coat, lit
        // physically. glTF's defaults are fully metal and fully rough.
        static void Physical(Ctx ctx, object json, object pbr, Material m)
        {
            m.EnableKeyword("_OKU_PBR");
            m.SetFloat("_Metallic", (float)MiniJson.Num(pbr, "metallicFactor", 1));
            m.SetFloat("_Smoothness", 1f - (float)MiniJson.Num(pbr, "roughnessFactor", 1));
            var mrt = MiniJson.Obj(pbr, "metallicRoughnessTexture");
            if (mrt != null)
            {
                var tex = TextureFor(ctx, MiniJson.Int(mrt, "index"));
                if (tex != null) m.SetTexture("_MetalRoughMap", tex);
            }
            var coat = MiniJson.Obj(MiniJson.Obj(json, "extensions"), "KHR_materials_clearcoat");
            if (coat != null)
            {
                float f = (float)MiniJson.Num(coat, "clearcoatFactor", 0);
                if (f > 0)
                {
                    m.EnableKeyword("_CLEARCOAT");
                    m.SetFloat("_ClearCoat", f);
                    m.SetFloat("_ClearCoatSmoothness", 1f - (float)MiniJson.Num(coat, "clearcoatRoughnessFactor", 0));
                }
            }
        }

        // A soft glow or halo: drawn after the solid world, blended over it,
        // writing no depth and casting no shadow.
        public static void Blended(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
        }

        // A textured glow: emissiveFactor times KHR_materials_emissive_strength,
        // in linear light, over emissiveTexture.
        static void Emission(Ctx ctx, object json, Material m)
        {
            var factor = MiniJson.Arr(json, "emissiveFactor");
            var et = MiniJson.Obj(json, "emissiveTexture");
            // A plain colour is the shader's self light, read above.
            if (factor == null || factor.Count < 3 || et == null) return;
            float strength = (float)MiniJson.Num(MiniJson.Obj(MiniJson.Obj(json, "extensions"), "KHR_materials_emissive_strength"), "emissiveStrength", 1);
            var c = new Vector4(F(factor[0]), F(factor[1]), F(factor[2]), 1f) * strength;
            c.w = 1f;
            if (c.x <= 0 && c.y <= 0 && c.z <= 0) return;
            m.SetVector("_EmissionColor", c);
            var tex = TextureFor(ctx, MiniJson.Int(et, "index"));
            if (tex != null) m.SetTexture("_EmissionMap", tex);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        // ---- Painted at load ----
        //
        // A material with okPaint extras names the original picture it was
        // made against instead of holding it (tools/sprite-replace/okpaint.py):
        //   kind, name, world   the picture: "feature" is a sprite feature's
        //                       first frame, "texture" a 3DO texture
        //   gain, bleed         the colour lift, and clear texels filled
        //   alpha, border       "opaque" or "mask", and the outer ring cleared
        //   size                [w, h] the UVs were made against
        //   tint                a multiplier, 1 when left out
        //   mask                {"cover": "others", "hotspot": [hx, hy]}: with
        //                       alpha "mask", texels under the model's faces in
        //                       other materials, seen by the classic camera, clear
        //   delit               henge.py's stones: the light under each texel
        //                       divided out, each stone's shading flattened
        // okFallback beside it is the colour drawn without the picture, and
        // then the base colour factor is that colour rather than a tint.

        // Where okPaint takes its pictures: the backend with the player's
        // game files. Null, or a backend without them, draws plain colour.
        public static IGameBackend Painter { get; private set; }
        public static Func<string, string, string, RgbaImage> Pictures { get; private set; }

        public static readonly Color Neutral = new Color(0.5f, 0.5f, 0.5f, 1f);

        static readonly Dictionary<string, Texture2D> painted = new Dictionary<string, Texture2D>();

        public static void SetPainter(IGameBackend backend)
        {
            if (backend != null && ReferenceEquals(backend, Painter)) return;
            SetPictures(backend != null ? (Func<string, string, string, RgbaImage>)backend.PaintPicture : null);
            Painter = backend;
        }

        // Pictures from anywhere, by kind, name and world, as the tests give them.
        public static void SetPictures(Func<string, string, string, RgbaImage> source)
        {
            Painter = null;
            Pictures = source;
            painted.Clear();
        }

        // The pictures asked for so far, one per name and treatment.
        public static int PaintedCount => painted.Count;

        static Texture2D PaintedTexture(Dictionary<string, object> paint, Ctx ctx = null, int material = -1)
        {
            string kind = MiniJson.Text(paint, "kind", "feature") ?? "", name = MiniJson.Text(paint, "name", "") ?? "";
            string world = MiniJson.Text(paint, "world", "") ?? "";
            float gain = (float)MiniJson.Num(paint, "gain", 1);
            bool bleed = Flag(paint, "bleed"), border = Flag(paint, "border");
            bool opaque = MiniJson.Text(paint, "alpha", "opaque") != "mask";
            var mask = MiniJson.Obj(paint, "mask");
            var delit = MiniJson.Obj(paint, "delit");
            string key = $"{kind}|{world}|{name}|{gain:R}|{bleed}|{opaque}|{border}".ToLowerInvariant();
            // A mask or delit depends on the model's own faces as well.
            if ((mask != null || delit != null) && ctx != null) key += $"|{ctx.Name}|{material}";
            if (painted.TryGetValue(key, out var t)) return t;
            var img = Pictures?.Invoke(kind, name, world);
            var size = MiniJson.Arr(paint, "size");
            if (img != null && size != null && size.Count == 2 && ((int)(double)size[0] != img.Width || (int)(double)size[1] != img.Height))
            {
                Debug.LogWarning($"okPaint {kind} {name}: the game's picture is {img.Width}x{img.Height}, the model was made against {size[0]}x{size[1]}");
                img = null;
            }
            if (img != null)
            {
                var px = Paint(img, gain, bleed, opaque, border);
                if (mask != null && ctx != null && MiniJson.Text(mask, "cover", "") == "others")
                {
                    var hot = MiniJson.Arr(mask, "hotspot");
                    Cover(px, FacesOf(ctx), material, hot != null && hot.Count == 2 ? new Vector2(F(hot[0]), F(hot[1])) : Vector2.zero, img.Width, img.Height);
                }
                if (delit != null && ctx != null) px = Delit(px, img, FacesOf(ctx), delit);
                t = new Texture2D(img.Width, img.Height, TextureFormat.RGBA32, true) { hideFlags = HideFlags.DontSave, name = name };
                // Rows top-down in the picture, bottom-up in the texture.
                var flipped = new byte[px.Length];
                int row = img.Width * 4;
                for (int y = 0; y < img.Height; y++) Buffer.BlockCopy(px, y * row, flipped, (img.Height - 1 - y) * row, row);
                t.SetPixelData(flipped, 0);
                t.Apply(true);
                t.wrapMode = TextureWrapMode.Clamp;
                t.filterMode = FilterMode.Trilinear;
                t.anisoLevel = 4;
            }
            painted[key] = t;
            return t;
        }

        static Color PaintColour(Dictionary<string, object> paint, Dictionary<string, object> extras, Color factor, bool havePicture)
        {
            var fb = MiniJson.Arr(extras, "okFallback");
            bool haveFallback = fb != null && fb.Count >= 3;
            float tint = (float)MiniJson.Num(paint, "tint", 1);
            var multiply = haveFallback ? new Color(tint, tint, tint, 1f) : factor * tint;
            multiply.a = 1f;
            if (havePicture) return multiply;
            return haveFallback ? new Color(F(fb[0]), F(fb[1]), F(fb[2]), 1f) : Neutral * multiply;
        }

        static bool Flag(Dictionary<string, object> o, string key) => o != null && o.TryGetValue(key, out var v) && v is bool b && b;

        // The picture as carve.Sprite leaves it (tools/sprite-replace/carve.py),
        // in the same float32 steps so the bytes match: clear texels filled
        // from their opaque neighbours ring by ring, colour times gain, alpha
        // 1 everywhere when opaque, the outer ring cleared for border. RGBA
        // bytes, rows top-down, in and out.
        public static byte[] Paint(RgbaImage img, float gain, bool bleed, bool opaque, bool border)
        {
            int w = img.Width, h = img.Height, n = w * h;
            var src = img.Pixels;
            var rgb = new float[n * 3];
            var known = new bool[n];
            const float inv = 1f / 255f;
            // Indexed bottom-up as Blender keeps an image, so the fill adds
            // its neighbours in the same order.
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int s = ((h - 1 - y) * w + x) * 4, i = y * w + x;
                    for (int c = 0; c < 3; c++) rgb[i * 3 + c] = src[s + c] * inv;
                    known[i] = src[s + 3] * inv > 0.5f;
                }
            if (bleed) Bleed(rgb, known, w, h);
            var o = new byte[n * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int d = ((h - 1 - y) * w + x) * 4, i = y * w + x;
                    for (int c = 0; c < 3; c++) o[d + c] = ToByte(Mathf.Clamp((float)(rgb[i * 3 + c] * gain), 0f, 1f));
                    o[d + 3] = opaque ? (byte)255 : src[d + 3];
                    if (border && (x == 0 || y == 0 || x == w - 1 || y == h - 1)) o[d + 3] = 0;
                }
            return o;
        }

        // Blender's float to byte, rounding half up. Mono may keep a float
        // sum in double, so each step is cast back to float as C rounds it.
        static byte ToByte(float v) => v <= 0f ? (byte)0 : v > 1f - 0.5f / 255f ? (byte)255 : (byte)(float)((float)(255f * v) + 0.5f);

        // carve.Sprite.bleed: each pass gives every clear texel with an opaque
        // neighbour their mean, until none is left clear.
        static void Bleed(float[] rgb, bool[] known, int w, int h)
        {
            int n = w * h, have = 0;
            foreach (var k in known) if (k) have++;
            if (have == 0) return;
            // As numpy's shift(arr, dy, dx): the value at (y, x) moves to (y + dy, x + dx).
            var dirs = new[] { (0, 1), (0, -1), (1, 0), (-1, 0) };
            var acc = new float[n * 3];
            var cnt = new float[n];
            var grow = new bool[n];
            while (have < n)
            {
                Array.Clear(acc, 0, acc.Length);
                Array.Clear(cnt, 0, cnt.Length);
                foreach (var (dy, dx) in dirs)
                    for (int y = 0; y < h; y++)
                    {
                        int sy = y - dy;
                        if (sy < 0 || sy >= h) continue;
                        for (int x = 0; x < w; x++)
                        {
                            int sx = x - dx;
                            if (sx < 0 || sx >= w) continue;
                            int s = sy * w + sx, i = y * w + x;
                            if (!known[s]) continue;
                            acc[i * 3] += rgb[s * 3];
                            acc[i * 3 + 1] += rgb[s * 3 + 1];
                            acc[i * 3 + 2] += rgb[s * 3 + 2];
                            cnt[i] += 1f;
                        }
                    }
                int grew = 0;
                for (int i = 0; i < n; i++)
                {
                    grow[i] = !known[i] && cnt[i] > 0f;
                    if (!grow[i]) continue;
                    for (int c = 0; c < 3; c++) rgb[i * 3 + c] = acc[i * 3 + c] / cnt[i];
                    grew++;
                }
                if (grew == 0) break;
                for (int i = 0; i < n; i++) if (grow[i]) known[i] = true;
                have += grew;
            }
        }

        // ---- The model seen by the classic camera ----
        //
        // A mask or delit reads the model's own triangles in Blender's frame
        // (x east, y north, z up, one unit a cell): glTF's (x, y, z) is
        // Blender's (x, -z, y). A point lands on the picture at column
        // hx + 16 x and row hy - 16 y - 8 z, and the camera sees first the
        // point with the greatest 2 z - y.

        public sealed class Faces
        {
            public readonly List<Vector3> A = new List<Vector3>(), B = new List<Vector3>(), C = new List<Vector3>();
            public readonly List<int> Material = new List<int>(), Stone = new List<int>();
            public int Count => A.Count;

            public void Add(Vector3 a, Vector3 b, Vector3 c, int material, int stone)
            {
                A.Add(a); B.Add(b); C.Add(c);
                Material.Add(material);
                Stone.Add(stone);
            }
        }

        // Every triangle of every mesh, with its material and its stone
        // (TEXCOORD_1 u rounded down, 0 without it).
        static Faces FacesOf(Ctx ctx)
        {
            if (ctx.Faces != null) return ctx.Faces;
            var f = new Faces();
            foreach (var meshJson in MiniJson.Arr(ctx.Json, "meshes") ?? new List<object>())
                foreach (var p in MiniJson.Arr(meshJson, "primitives") ?? new List<object>())
                {
                    if (MiniJson.Int(p, "mode", 4) != 4) continue;
                    var attrs = MiniJson.Obj(p, "attributes");
                    int pos = MiniJson.Int(attrs, "POSITION");
                    if (pos < 0) continue;
                    var P = ReadFloats(ctx, pos, 3);
                    int count = P.Length / 3;
                    int st = MiniJson.Int(attrs, "TEXCOORD_1");
                    var S = st >= 0 ? ReadFloats(ctx, st, 2) : null;
                    int ind = MiniJson.Int(p, "indices");
                    int[] idx;
                    if (ind >= 0) idx = ReadInts(ctx, ind);
                    else { idx = new int[count]; for (int i = 0; i < count; i++) idx[i] = i; }
                    Vector3 V(int i) => new Vector3(P[3 * i], -P[3 * i + 2], P[3 * i + 1]);
                    int mat = MiniJson.Int(p, "material");
                    for (int i = 0; i + 2 < idx.Length; i += 3)
                        f.Add(V(idx[i]), V(idx[i + 1]), V(idx[i + 2]), mat, S != null ? Mathf.FloorToInt(S[2 * idx[i]]) : 0);
                }
            ctx.Faces = f;
            return f;
        }

        // For each texel centre, rows top-down, the face the classic camera
        // sees there first, or -1. use picks the faces that count.
        public static int[] ClassicHits(Faces f, Vector2 hot, int w, int h, Func<int, bool> use = null)
        {
            var hit = new int[w * h];
            var depth = new float[w * h];
            for (int i = 0; i < hit.Length; i++) { hit[i] = -1; depth[i] = float.NegativeInfinity; }
            Vector2 S(Vector3 v) => new Vector2(hot.x + 16f * v.x, hot.y - 16f * v.y - 8f * v.z);
            float E(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
            for (int t = 0; t < f.Count; t++)
            {
                if (use != null && !use(t)) continue;
                Vector2 a = S(f.A[t]), b = S(f.B[t]), c = S(f.C[t]);
                float area = E(a, b, c);
                if (Mathf.Abs(area) < 1e-9f) continue;
                float da = 2f * f.A[t].z - f.A[t].y, db = 2f * f.B[t].z - f.B[t].y, dc = 2f * f.C[t].z - f.C[t].y;
                int x0 = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - 0.5f));
                int x1 = Mathf.Min(w - 1, Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - 0.5f));
                int y0 = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - 0.5f));
                int y1 = Mathf.Min(h - 1, Mathf.FloorToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - 0.5f));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        float u = E(b, c, p) / area, v = E(c, a, p) / area, s = E(a, b, p) / area;
                        if (u < -1e-5f || v < -1e-5f || s < -1e-5f) continue;
                        float d = u * da + v * db + s * dc;
                        int i = y * w + x;
                        if (d > depth[i]) { depth[i] = d; hit[i] = t; }
                    }
            }
            return hit;
        }

        // Clears, in RGBA rows top-down, the texels a face in another
        // material covers.
        public static void Cover(byte[] px, Faces f, int material, Vector2 hot, int w, int h)
        {
            var hit = ClassicHits(f, hot, w, h, t => f.Material[t] != material);
            for (int i = 0; i < hit.Length; i++) if (hit[i] >= 0) px[i * 4 + 3] = 0;
        }

        static float Luma(float r, float g, float b) => r * 0.2126f + g * 0.7152f + b * 0.0722f;
        static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
        static float ToSrgb(float c)
        {
            c = Mathf.Clamp01(c);
            return c <= 0.0031308f ? c * 12.92f : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;
        }

        // henge.py's delit: px is the picture as Paint leaves it opaque and
        // src the picture as served, for its clear texels. Each texel the
        // model covers takes its face's light out, at most 2.5 times, then
        // each stone is scaled to its grey by the median brightness of its
        // opaque texels and its broad shading is flattened by a gaussian of
        // 1.5 texels, keeping detail between dark and 1.7 times the mean
        // and the hue within 0.3 of the grey. The rest fill from the covered
        // ones as Paint fills.
        public static byte[] Delit(byte[] px, RgbaImage src, Faces f, Dictionary<string, object> delit)
        {
            int w = src.Width, h = src.Height, n = w * h;
            var hot = MiniJson.Arr(delit, "hotspot");
            var li = MiniJson.Arr(delit, "light");
            var light = li != null && li.Count == 3 ? new Vector3(F(li[0]), F(li[1]), F(li[2])).normalized : Vector3.up;
            float ambient = (float)MiniJson.Num(delit, "ambient", 0.33), direct = (float)MiniJson.Num(delit, "direct", 0.64);
            var stones = MiniJson.Arr(delit, "stones") ?? new List<object>();
            var grey = new Vector3[stones.Count];
            var dark = new float[stones.Count];
            for (int k = 0; k < stones.Count; k++)
            {
                var gg = MiniJson.Arr(stones[k], "grey");
                grey[k] = gg != null && gg.Count == 3 ? new Vector3(F(gg[0]), F(gg[1]), F(gg[2])) : new Vector3(0.5f, 0.5f, 0.5f);
                dark[k] = (float)MiniJson.Num(stones[k], "dark", 0.15);
            }
            var hit = ClassicHits(f, hot != null && hot.Count == 2 ? new Vector2(F(hot[0]), F(hot[1])) : Vector2.zero, w, h);
            var rgb = new float[n * 3];
            var alpha = new bool[n];
            var known = new bool[n];
            var owner = new int[n];
            const float inv = 1f / 255f;
            for (int i = 0; i < n; i++)
            {
                for (int c = 0; c < 3; c++) rgb[i * 3 + c] = ToLinear(px[i * 4 + c] * inv);
                alpha[i] = src.Pixels[i * 4 + 3] * inv > 0.5f;
                owner[i] = -1;
                int t = hit[i];
                if (t < 0) continue;
                var nrm = Vector3.Cross(f.B[t] - f.A[t], f.C[t] - f.A[t]).normalized;
                float shade = ambient + direct * Mathf.Max(0f, Vector3.Dot(nrm, light));
                float lift = Mathf.Min(2.5f, 1f / Mathf.Max(1e-6f, shade));
                for (int c = 0; c < 3; c++) rgb[i * 3 + c] *= lift;
                known[i] = true;
                int k = f.Stone[t];
                owner[i] = k >= 0 && k < stones.Count ? k : -1;
            }
            var L = new float[n];
            var inStone = new float[n];
            for (int k = 0; k < stones.Count; k++)
            {
                var on = new List<float>();
                for (int i = 0; i < n; i++) if (owner[i] == k && alpha[i]) on.Add(Luma(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]));
                bool any = false;
                for (int i = 0; i < n; i++) { inStone[i] = owner[i] == k ? 1f : 0f; any |= owner[i] == k; }
                if (!any) continue;
                float g = Luma(grey[k].x, grey[k].y, grey[k].z);
                if (on.Count >= 12)
                {
                    on.Sort();
                    int m = on.Count / 2;
                    float median = on.Count % 2 == 1 ? on[m] : (on[m - 1] + on[m]) * 0.5f;
                    float scale = Mathf.Min(2.5f, Mathf.Max(0.4f, g / Mathf.Max(1e-4f, median)));
                    for (int i = 0; i < n; i++) if (owner[i] == k) for (int c = 0; c < 3; c++) rgb[i * 3 + c] *= scale;
                }
                for (int i = 0; i < n; i++) L[i] = Mathf.Max(Luma(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]), 1e-5f);
                var mean = MaskedBlur(L, inStone, w, h, 1.5f);
                var hue0 = grey[k] / g;
                for (int i = 0; i < n; i++)
                {
                    if (owner[i] != k) continue;
                    float ratio = L[i] / Mathf.Max(mean[i], 1e-5f);
                    float broad = Mathf.Clamp(Mathf.Sqrt(mean[i] / g), 0.85f, 1.2f);
                    float want = g * Mathf.Clamp(ratio, dark[k], 1.7f) * broad;
                    float keep = Mathf.Clamp01(L[i] / (0.4f * g));
                    for (int c = 0; c < 3; c++)
                    {
                        float hue = rgb[i * 3 + c] / L[i];
                        hue = hue0[c] + Mathf.Clamp(hue - hue0[c], -0.3f, 0.3f) * keep;
                        rgb[i * 3 + c] = hue * want;
                    }
                }
            }
            // henge.py fills its rows top-down, as they are here.
            if (Array.IndexOf(known, true) >= 0) Bleed(rgb, known, w, h);
            var o = new byte[n * 4];
            for (int i = 0; i < n; i++)
            {
                for (int c = 0; c < 3; c++) o[i * 4 + c] = ToByte(ToSrgb(rgb[i * 3 + c]));
                o[i * 4 + 3] = 255;
            }
            return o;
        }

        // henge.py's blur: the gaussian mean of val over the texels where
        // mask is 1, rows top-down, nothing wrapping at the edges.
        static float[] MaskedBlur(float[] val, float[] mask, int w, int h, float sigma)
        {
            int r = Math.Max(1, (int)Math.Round(3 * sigma, MidpointRounding.ToEven));
            var k = new float[2 * r + 1];
            for (int i = -r; i <= r; i++) k[i + r] = Mathf.Exp(-i * i / (2f * sigma * sigma));
            float[] Conv(float[] a)
            {
                var t = new float[w * h];
                var o = new float[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float sum = 0f;
                        for (int j = -r; j <= r; j++) { int sx = x - j; if (sx >= 0 && sx < w) sum += k[j + r] * a[y * w + sx]; }
                        t[y * w + x] = sum;
                    }
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float sum = 0f;
                        for (int j = -r; j <= r; j++) { int sy = y - j; if (sy >= 0 && sy < h) sum += k[j + r] * t[sy * w + x]; }
                        o[y * w + x] = sum;
                    }
                return o;
            }
            var vm = new float[w * h];
            for (int i = 0; i < vm.Length; i++) vm[i] = val[i] * mask[i];
            var top = Conv(vm);
            var bottom = Conv(mask);
            for (int i = 0; i < top.Length; i++) top[i] /= Mathf.Max(bottom[i], 1e-6f);
            return top;
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
