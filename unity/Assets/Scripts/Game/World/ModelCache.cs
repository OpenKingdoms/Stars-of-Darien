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
        public Bounds RestBounds;         // the whole model at rest, world units, from its root
    }

    public sealed class ModelCache
    {
        readonly IGameBackend backend;
        readonly Dictionary<int, PresentedModel> models = new Dictionary<int, PresentedModel>();
        readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();
        readonly Dictionary<Material, Material> flatMaterials = new Dictionary<Material, Material>();

        // A piece thinner than this, in world units, lies flat: a build pad
        // or a floor decal. It draws pulled toward the camera, so it never
        // fights the ground it lies on.
        public const float FlatHeight = 0.02f;
        public const float FlatOffsetFactor = -1f, FlatOffsetUnits = -4f;

        public static bool IsFlat(Bounds b) => b.size.y < FlatHeight && Mathf.Max(b.size.x, b.size.z) > FlatHeight * 10f;

        // The material for a flat piece: the same look with a depth offset.
        public Material FlatMaterial(Material m)
        {
            if (m == null) return null;
            if (flatMaterials.TryGetValue(m, out var f)) return f;
            f = new Material(m) { name = m.name + " (flat)", hideFlags = HideFlags.DontSave };
            f.SetFloat("_OffsetFactor", FlatOffsetFactor);
            f.SetFloat("_OffsetUnits", FlatOffsetUnits);
            owned.Add(f);
            flatMaterials[m] = f;
            return f;
        }
        readonly List<Object> owned = new List<Object>();

        // Faceted pieces shade smooth across edges gentler than this, in degrees. 0 keeps the given normals.
        public float SmoothingAngle = NormalSmoother.DefaultAngle;

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

        // Makes one material the model still lacks, for a loading screen
        // that builds a model's materials a frame at a time. False once the
        // model is built or has every material.
        public bool WarmMaterial(int id)
        {
            if (id < 0 || models.ContainsKey(id)) return false;
            var data = backend.GetModel(id);
            if (data == null) return false;
            foreach (var batch in data.Batches)
                if (!materials.ContainsKey(batch.Texture))
                {
                    MaterialFor(batch.Texture);
                    return true;
                }
            return false;
        }

        // The original models are one-sided polygons over open shells, such
        // as a tower's cannon port or a flag's cloth seen from behind, so
        // they draw from both sides. Texels are cut below the 3D view's
        // own alpha of 0.2.
        public const float ModelCutoff = 0.2f;

        public Material MaterialFor(int texture)
        {
            if (materials.TryGetValue(texture, out var m)) return m;
            Texture tex = null;
            if (texture >= 0)
            {
                var t = CoverageMipped(backend.Texture(texture));
                if (t != null) { t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Trilinear; t.anisoLevel = 4; owned.Add(t); }
                tex = t;
            }
            m = Looks.Model(tex);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetFloat("_Cutoff", ModelCutoff);
            owned.Add(m);
            materials[texture] = m;
            return m;
        }

        // A picture with mips that keep its cut-out coverage. Plain mips
        // average the see-through texels of the atlas in, so thin parts
        // such as flag cloth, a few texels tall in the atlas, fall under the
        // cutoff and open up as the view zooms out. Here each smaller level
        // takes the most opaque of the four texels under it, and its colour
        // weighted by alpha, so what shows up close still shows from afar.
        public static Texture2D CoverageMipped(RgbaImage img)
        {
            if (img == null) return null;
            int w = img.Width, h = img.Height;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { hideFlags = HideFlags.DontSave };
            tex.SetPixelData(img.Pixels, 0);
            var rgb = new float[w * h * 3];
            var a = new float[w * h];
            for (int i = 0; i < w * h; i++)
            {
                a[i] = img.Pixels[i * 4 + 3] / 255f;
                for (int k = 0; k < 3; k++) rgb[i * 3 + k] = img.Pixels[i * 4 + k] / 255f;
            }
            for (int level = 1; level < tex.mipmapCount; level++)
            {
                int nw = Mathf.Max(1, w >> 1), nh = Mathf.Max(1, h >> 1);
                var nrgb = new float[nw * nh * 3];
                var na = new float[nw * nh];
                for (int y = 0; y < nh; y++)
                    for (int x = 0; x < nw; x++)
                    {
                        float sa = 0, r = 0, g = 0, b = 0, pr = 0, pg = 0, pb = 0;
                        int n = 0;
                        for (int dy = 0; dy < 2; dy++)
                            for (int dx = 0; dx < 2; dx++)
                            {
                                int sx = Mathf.Min(w - 1, x * 2 + dx), sy = Mathf.Min(h - 1, y * 2 + dy);
                                int s = sy * w + sx;
                                float al = a[s];
                                sa += al;
                                r += rgb[s * 3] * al; g += rgb[s * 3 + 1] * al; b += rgb[s * 3 + 2] * al;
                                pr += rgb[s * 3]; pg += rgb[s * 3 + 1]; pb += rgb[s * 3 + 2];
                                n++;
                            }
                        int o = y * nw + x;
                        float most = 0;
                        for (int dy = 0; dy < 2; dy++)
                            for (int dx = 0; dx < 2; dx++)
                                most = Mathf.Max(most, a[Mathf.Min(h - 1, y * 2 + dy) * w + Mathf.Min(w - 1, x * 2 + dx)]);
                        na[o] = most;
                        if (sa > 1e-4f) { nrgb[o * 3] = r / sa; nrgb[o * 3 + 1] = g / sa; nrgb[o * 3 + 2] = b / sa; }
                        else { nrgb[o * 3] = pr / n; nrgb[o * 3 + 1] = pg / n; nrgb[o * 3 + 2] = pb / n; }
                    }
                var bytes = new byte[nw * nh * 4];
                for (int i = 0; i < nw * nh; i++)
                {
                    bytes[i * 4] = (byte)Mathf.Clamp(Mathf.RoundToInt(nrgb[i * 3] * 255f), 0, 255);
                    bytes[i * 4 + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(nrgb[i * 3 + 1] * 255f), 0, 255);
                    bytes[i * 4 + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(nrgb[i * 3 + 2] * 255f), 0, 255);
                    bytes[i * 4 + 3] = (byte)Mathf.Clamp(Mathf.RoundToInt(na[i] * 255f), 0, 255);
                }
                tex.SetPixelData(bytes, level);
                w = nw; h = nh; rgb = nrgb; a = na;
            }
            tex.Apply(false);
            return tex;
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
                if (SmoothingAngle > 0) NormalSmoother.Apply(verts, norms, uvs, cols, subs, SmoothingAngle);
                var mesh = new Mesh { name = d.Name + "/" + d.Pieces[p].Name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                mesh.SetUVs(0, uvs);
                mesh.SetColors(cols);
                mesh.subMeshCount = subs.Count;
                for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
                mesh.RecalculateBounds();
                if (IsFlat(mesh.bounds))
                    for (int s = 0; s < mats.Count; s++) mats[s] = FlatMaterial(mats[s]);
                owned.Add(mesh);
                result.Pieces[p] = mesh;
                result.Materials[p] = mats.ToArray();
            }
            // Rest bounds from the pieces themselves, placed by their offsets.
            var rest = new Matrix4x4[pieces];
            bool any = false;
            for (int p = 0; p < pieces; p++)
            {
                var m = Matrix4x4.Translate(d.Pieces[p].Offset * d.Scale);
                int parent = d.Pieces[p].Parent;
                rest[p] = parent >= 0 && parent < p ? rest[parent] * m : m;
                if (result.Pieces[p] == null) continue;
                var b = result.Pieces[p].bounds;
                foreach (var corner in new[] { b.min, b.max, new Vector3(b.min.x, b.max.y, b.min.z), new Vector3(b.max.x, b.min.y, b.max.z) })
                {
                    var w = rest[p].MultiplyPoint3x4(corner);
                    if (!any) { result.RestBounds = new Bounds(w, Vector3.zero); any = true; }
                    else result.RestBounds.Encapsulate(w);
                }
            }
            return result;
        }

        public void Dispose()
        {
            InstancedDraws.ForgetMirrors();
            foreach (var o in owned) Looks.Release(o);
            owned.Clear();
            models.Clear();
            materials.Clear();
        }
    }
}
