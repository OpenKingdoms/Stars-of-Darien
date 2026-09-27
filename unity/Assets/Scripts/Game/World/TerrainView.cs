// TerrainView.cs - builds the ground and the sea for a loaded game: a
// GameObject per region with a LODGroup over its detailed and coarse
// meshes, and a wave mesh at sea level covering the map and a margin.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class TerrainView
    {
        public const int BakeSize = 256;
        public const int CoarseStep = 4;

        public GameObject Root { get; private set; }
        public GameObject Water { get; private set; }
        public GameObject Apron { get; private set; }
        public int Regions { get; private set; }
        readonly List<Object> owned = new List<Object>();
        readonly Dictionary<int, Material> chunkMats = new Dictionary<int, Material>();
        readonly Dictionary<int, RgbaImage> chunkImages = new Dictionary<int, RgbaImage>();

        public void Build(IGameBackend backend, Transform parent)
        {
            var t = backend.Terrain;
            Root = new GameObject("Terrain");
            Root.transform.SetParent(parent, false);

            RgbaImage Chunk(int c)
            {
                if (!chunkImages.TryGetValue(c, out var img)) chunkImages[c] = img = backend.TerrainChunk(c);
                return img;
            }
            Vector2Int Size(int c)
            {
                var img = Chunk(c);
                return img != null ? new Vector2Int(img.Width, img.Height) : Vector2Int.zero;
            }

            int rw = TerrainBuilder.RegionsW(t), rh = TerrainBuilder.RegionsH(t);
            for (int ry = 0; ry < rh; ry++)
                for (int rx = 0; rx < rw; rx++)
                {
                    var region = new GameObject($"Region {rx},{ry}");
                    region.transform.SetParent(Root.transform, false);

                    var detail = TerrainBuilder.Detail(t, rx, ry, Size);
                    var order = new List<int>(detail.Triangles.Keys);
                    var near = Child(region, "LOD0", TerrainBuilder.ToMesh(detail, $"terrain {rx},{ry}", order));
                    var mats = new Material[order.Count];
                    for (int i = 0; i < order.Count; i++) mats[i] = ChunkMaterial(order[i], Chunk, t.SeaLevel);
                    near.sharedMaterials = mats;

                    var coarse = TerrainBuilder.Coarse(t, rx, ry, CoarseStep);
                    var far = Child(region, "LOD1", TerrainBuilder.ToMesh(coarse, $"terrain far {rx},{ry}", new List<int> { -1 }));
                    var baked = UI.UiKit.ToTexture(TerrainBuilder.BakeRegion(t, rx, ry, BakeSize, Chunk), true);
                    baked.wrapMode = TextureWrapMode.Clamp;
                    owned.Add(baked);
                    var farMat = Looks.Terrain(baked, t.SeaLevel);
                    owned.Add(farMat);
                    far.sharedMaterial = farMat;
                    far.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                    var lod = region.AddComponent<LODGroup>();
                    lod.SetLODs(new[] { new LOD(0.12f, new Renderer[] { near }), new LOD(0.0f, new Renderer[] { far }) });
                    lod.RecalculateBounds();
                    Regions++;
                }
            Apron = BuildApron(t, Chunk, Root.transform);
            chunkImages.Clear();

            if (t.SeaLevel > 0) Water = BuildWater(t, parent);
        }

        // Land past the map edge, so the world does not stop at a void: a
        // skirt of ground sloping away and down, in the colour of the edge.
        GameObject BuildApron(MapTerrain t, System.Func<int, RgbaImage> chunk, Transform parent)
        {
            const float reach = 140f, step = 4f;
            var size = t.Size;
            var ring = new List<Vector2>();
            for (float x = 0; x < size.x; x += step) ring.Add(new Vector2(x, 0));
            for (float z = 0; z < size.y; z += step) ring.Add(new Vector2(size.x, -z));
            for (float x = size.x; x > 0; x -= step) ring.Add(new Vector2(x, -size.y));
            for (float z = size.y; z > 0; z -= step) ring.Add(new Vector2(0, -z));
            float low = float.MaxValue;
            foreach (var p in ring) low = Mathf.Min(low, t.Sample(p.x, p.y));
            float farY = (t.SeaLevel > 0 ? Mathf.Min(low, t.SeaLevel) : low) - 4f;
            var centre = new Vector2(size.x / 2, -size.y / 2);
            var verts = new List<Vector3>();
            var cols = new List<Color32>();
            var tris = new List<int>();
            foreach (var p in ring)
            {
                var out2 = p - centre;
                out2 = new Vector2(Mathf.Abs(out2.x) >= size.x / 2 - 0.01f ? Mathf.Sign(out2.x) : 0, Mathf.Abs(out2.y) >= size.y / 2 - 0.01f ? Mathf.Sign(out2.y) : 0).normalized;
                var c = EdgeColour(t, chunk, p);
                verts.Add(new Vector3(p.x, t.Sample(p.x, p.y), p.y));
                verts.Add(new Vector3(p.x + out2.x * reach, farY, p.y + out2.y * reach));
                cols.Add(c);
                cols.Add(Color32.Lerp(c, new Color32(90, 95, 85, 255), 0.5f));
            }
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                int a = 2 * i, b = 2 * ((i + 1) % n);
                // The ring runs clockwise seen from above, and the outer edge
                // lies outside it, so this order faces up.
                tris.Add(a); tris.Add(b + 1); tris.Add(b);
                tris.Add(a); tris.Add(a + 1); tris.Add(b + 1);
            }
            var mesh = new Mesh { name = "apron" };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            var norms = new Vector3[verts.Count];
            for (int i = 0; i < norms.Length; i++) norms[i] = Vector3.up;
            mesh.normals = norms;
            mesh.uv = new Vector2[verts.Count];
            mesh.RecalculateBounds();
            owned.Add(mesh);
            var go = new GameObject("Apron");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            var mat = Looks.Model(null);
            owned.Add(mat);
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static Color32 EdgeColour(MapTerrain t, System.Func<int, RgbaImage> chunk, Vector2 p)
        {
            int bx = Mathf.Clamp((int)(p.x / t.BlockSize), 0, t.BlocksW - 1);
            int by = Mathf.Clamp((int)(-p.y / t.BlockSize), 0, t.BlocksH - 1);
            int b = by * t.BlocksW + bx;
            var img = chunk(t.Blocks[3 * b]);
            if (img == null) return new Color32(90, 100, 80, 255);
            // The average of the block's square, which hides single pixels.
            int r = 0, g = 0, bl = 0, count = 0;
            for (int y = 0; y < t.BlockTexels; y += 4)
                for (int x = 0; x < t.BlockTexels; x += 4)
                {
                    int px = Mathf.Clamp(t.Blocks[3 * b + 1] + x, 0, img.Width - 1), py = Mathf.Clamp(t.Blocks[3 * b + 2] + y, 0, img.Height - 1);
                    int o = (py * img.Width + px) * 4;
                    r += img.Pixels[o]; g += img.Pixels[o + 1]; bl += img.Pixels[o + 2]; count++;
                }
            return count > 0 ? new Color32((byte)(r / count), (byte)(g / count), (byte)(bl / count), 255) : new Color32(90, 100, 80, 255);
        }

        MeshRenderer Child(GameObject region, string name, Mesh mesh)
        {
            owned.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(region.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            return go.AddComponent<MeshRenderer>();
        }

        Material ChunkMaterial(int chunk, System.Func<int, RgbaImage> images, float sea)
        {
            if (chunkMats.TryGetValue(chunk, out var m)) return m;
            var tex = UI.UiKit.ToTexture(images(chunk), true);
            if (tex != null) { tex.anisoLevel = 4; owned.Add(tex); }
            m = Looks.Terrain(tex, sea);
            owned.Add(m);
            chunkMats[chunk] = m;
            return m;
        }

        GameObject BuildWater(MapTerrain t, Transform parent)
        {
            const float margin = 60f, cell = 2f;
            var size = t.Size;
            int nx = Mathf.CeilToInt((size.x + 2 * margin) / cell), nz = Mathf.CeilToInt((size.y + 2 * margin) / cell);
            var verts = new Vector3[(nx + 1) * (nz + 1)];
            for (int z = 0; z <= nz; z++)
                for (int x = 0; x <= nx; x++)
                    verts[z * (nx + 1) + x] = new Vector3(-margin + x * cell, 0, margin - z * cell);
            var tris = new int[nx * nz * 6];
            int k = 0;
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int nw = z * (nx + 1) + x, ne = nw + 1, sw = nw + nx + 1, se = sw + 1;
                    tris[k++] = nw; tris[k++] = ne; tris[k++] = se;
                    tris[k++] = nw; tris[k++] = se; tris[k++] = sw;
                }
            var mesh = new Mesh { name = "sea", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.up * 4);
            owned.Add(mesh);
            var go = new GameObject("Sea");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0, t.SeaLevel, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            var mat = Looks.Water();
            owned.Add(mat);
            var depth = SeaDepth(t);
            owned.Add(depth);
            mat.SetTexture("_DepthTex", depth);
            mat.SetVector("_MapRect", new Vector4(0, -size.y, size.x, size.y));
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        // Water depth over the ground at every height sample, over 4 units,
        // row 0 of the grid (north) at the top of the picture.
        public static Texture2D SeaDepth(MapTerrain t)
        {
            var tex = new Texture2D(t.HeightsW, t.HeightsH, TextureFormat.R8, false, true)
            {
                hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            var px = new byte[t.HeightsW * t.HeightsH];
            for (int z = 0; z < t.HeightsH; z++)
                for (int x = 0; x < t.HeightsW; x++)
                {
                    float d = Mathf.Clamp01((t.SeaLevel - t.HeightAt(x, z)) / 4f);
                    px[(t.HeightsH - 1 - z) * t.HeightsW + x] = (byte)(d * 255);
                }
            tex.SetPixelData(px, 0);
            tex.Apply(false);
            return tex;
        }

        public void Dispose()
        {
            if (Root != null) Looks.Release(Root);
            if (Water != null) Looks.Release(Water);
            foreach (var o in owned) Looks.Release(o);
            owned.Clear();
            chunkMats.Clear();
            Regions = 0;
        }
    }
}
