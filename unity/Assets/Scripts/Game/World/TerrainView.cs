// TerrainView.cs - builds the ground and the sea for a loaded game: a
// GameObject per region with a LODGroup over its detailed and coarse
// meshes, and a wave mesh at sea level covering the map and a margin.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class TerrainView
    {
        public const int CoarseStep = 4;

        public GameObject Root { get; private set; }
        public GameObject Water { get; private set; }
        public GameObject Apron { get; private set; }
        public int Regions { get; private set; }
        readonly List<Object> owned = new List<Object>();
        readonly Dictionary<int, RgbaImage> chunkImages = new Dictionary<int, RgbaImage>();

        IGameBackend backend;
        GameObject[,] regions;
        readonly Dictionary<GameObject, List<Object>> regionOwned = new Dictionary<GameObject, List<Object>>();

        RgbaImage Chunk(int c)
        {
            if (!chunkImages.TryGetValue(c, out var img)) chunkImages[c] = img = backend.TerrainChunk(c);
            return img;
        }

        public void Build(IGameBackend backend, Transform parent)
        {
            this.backend = backend;
            var t = backend.Terrain;
            Root = new GameObject("Terrain");
            Root.transform.SetParent(parent, false);
            int rw = TerrainBuilder.RegionsW(t), rh = TerrainBuilder.RegionsH(t);
            regions = new GameObject[rw, rh];
            for (int ry = 0; ry < rh; ry++)
                for (int rx = 0; rx < rw; rx++)
                    regions[rx, ry] = BuildRegion(t, rx, ry);
            Apron = BuildRing(t, Chunk, Root.transform);
            Shader.SetGlobalVector("_OkuMapSize", new Vector4(t.Size.x, t.Size.y, 0, 0));
            GroundDetail.Apply(true);
            chunkImages.Clear();

            if (t.SeaLevel > 0) Water = BuildWater(t, parent);
        }

        GameObject BuildRegion(MapTerrain t, int rx, int ry)
        {
            var region = new GameObject($"Region {rx},{ry}");
            region.transform.SetParent(Root.transform, false);
            var mine = new List<Object>();
            regionOwned[region] = mine;

            // One picture of the region's ground, near and far alike.
            var picture = RegionTexture(t, rx, ry);
            mine.Add(picture);
            var mat = Looks.Terrain(picture, t.SeaLevel);
            mine.Add(mat);
            var only = new List<int> { -1 };
            var near = Child(region, "LOD0", TerrainBuilder.ToMesh(TerrainBuilder.Detail(t, rx, ry), $"terrain {rx},{ry}", only), mine);
            near.sharedMaterial = mat;

            var coarse = TerrainBuilder.Coarse(t, rx, ry, CoarseStep);
            var far = Child(region, "LOD1", TerrainBuilder.ToMesh(coarse, $"terrain far {rx},{ry}", only), mine);
            far.sharedMaterial = mat;
            far.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var lod = region.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.12f, new Renderer[] { near }), new LOD(0.0f, new Renderer[] { far }) });
            lod.RecalculateBounds();
            Regions++;
            return region;
        }

        // Rebuilds the regions that cover a rectangle of blocks, after the
        // ground there was edited: heights, or which picture a block shows.
        public void Rebuild(RectInt blocks)
        {
            if (regions == null) return;
            var t = backend.Terrain;
            int rw = regions.GetLength(0), rh = regions.GetLength(1);
            int x0 = Mathf.Clamp((blocks.xMin - 1) / TerrainBuilder.RegionBlocks, 0, rw - 1);
            int x1 = Mathf.Clamp(blocks.xMax / TerrainBuilder.RegionBlocks, 0, rw - 1);
            int y0 = Mathf.Clamp((blocks.yMin - 1) / TerrainBuilder.RegionBlocks, 0, rh - 1);
            int y1 = Mathf.Clamp(blocks.yMax / TerrainBuilder.RegionBlocks, 0, rh - 1);
            for (int ry = y0; ry <= y1; ry++)
                for (int rx = x0; rx <= x1; rx++)
                {
                    var old = regions[rx, ry];
                    if (regionOwned.TryGetValue(old, out var list)) { foreach (var o in list) Looks.Release(o); regionOwned.Remove(old); }
                    Looks.Release(old);
                    Regions--;
                    regions[rx, ry] = BuildRegion(t, rx, ry);
                }
            chunkImages.Clear();
            if (Water != null) RefreshWater(t);
        }

        Texture2D waterDepth;

        void RefreshWater(MapTerrain t)
        {
            var mat = Water.GetComponent<MeshRenderer>().sharedMaterial;
            if (waterDepth != null) Looks.Release(waterDepth);
            waterDepth = SeaDepth(t);
            mat.SetTexture("_DepthTex", waterDepth);
        }

        // The land past the playable edge: the edge ring, textured with a
        // picture of the whole map mirrored at the edge.
        GameObject BuildRing(MapTerrain t, System.Func<int, RgbaImage> chunk, Transform parent)
        {
            var mesh = EdgeRing.Build(t);
            owned.Add(mesh);
            var picture = UI.UiKit.ToTexture(WholeMap(t, chunk, 1024), true);
            picture.wrapMode = TextureWrapMode.Clamp;
            owned.Add(picture);
            var mat = new Material(Looks.Find("OkuRing", "Unlit/Texture")) { hideFlags = HideFlags.DontSave, mainTexture = picture };
            mat.SetFloat("_Width", EdgeRing.Width);
            owned.Add(mat);
            var go = new GameObject("Edge ring");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            // Past the ring, a plain of haze at the ring's far height out to
            // the far clip, so no gap can ever show between land and sky.
            float shelf = EdgeRing.Shelf(t) - 0.05f;
            var c = new Vector3(t.Size.x / 2, shelf, -t.Size.y / 2);
            const float far = 4000f;
            var plain = new Mesh { name = "haze plain", hideFlags = HideFlags.DontSave };
            plain.vertices = new[] { c + new Vector3(-far, 0, far), c + new Vector3(far, 0, far), c + new Vector3(far, 0, -far), c + new Vector3(-far, 0, -far) };
            plain.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            plain.uv = new Vector2[4];
            plain.uv2 = new[] { Vector2.one * 999, Vector2.one * 999, Vector2.one * 999, Vector2.one * 999 };
            plain.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            plain.bounds = new Bounds(c, new Vector3(2 * far, 1, 2 * far));
            owned.Add(plain);
            var pg = new GameObject("Haze plain");
            pg.transform.SetParent(go.transform, false);
            pg.AddComponent<MeshFilter>().sharedMesh = plain;
            var pr = pg.AddComponent<MeshRenderer>();
            pr.sharedMaterial = mat;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            return go;
        }

        // A picture of the whole map, at most size pixels across.
        public static RgbaImage WholeMap(MapTerrain t, System.Func<int, RgbaImage> chunk, int size)
        {
            var mapSize = t.Size;
            int w = size, h = Mathf.Max(1, Mathf.RoundToInt(size * mapSize.y / mapSize.x));
            if (h > size) { h = size; w = Mathf.Max(1, Mathf.RoundToInt(size * mapSize.x / mapSize.y)); }
            var img = new RgbaImage(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float wx = (x + 0.5f) / w * mapSize.x, wz = (y + 0.5f) / h * mapSize.y;
                    int bx = Mathf.Min(t.BlocksW - 1, (int)(wx / t.BlockSize)), by = Mathf.Min(t.BlocksH - 1, (int)(wz / t.BlockSize));
                    int b = by * t.BlocksW + bx;
                    var c = chunk(t.Blocks[3 * b]);
                    if (c == null) continue;
                    int cx = t.Blocks[3 * b + 1] + (int)((wx / t.BlockSize - bx) * t.BlockTexels);
                    int cy = t.Blocks[3 * b + 2] + (int)((wz / t.BlockSize - by) * t.BlockTexels);
                    cx = Mathf.Clamp(cx, 0, c.Width - 1);
                    cy = Mathf.Clamp(cy, 0, c.Height - 1);
                    System.Buffer.BlockCopy(c.Pixels, (cy * c.Width + cx) * 4, img.Pixels, (y * w + x) * 4, 4);
                }
            return img;
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

        MeshRenderer Child(GameObject region, string name, Mesh mesh, List<Object> keep)
        {
            keep.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(region.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            return go.AddComponent<MeshRenderer>();
        }

        // The region's picture, mipmapped and compressed where the
        // hardware can, with no copy kept in memory.
        Texture2D RegionTexture(MapTerrain t, int rx, int ry)
        {
            var tex = UI.UiKit.ToTexture(TerrainBuilder.RegionPicture(t, rx, ry, Chunk), true);
            tex.name = $"ground {rx},{ry}";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 8;
            if (SystemInfo.SupportsTextureFormat(TextureFormat.DXT1)) tex.Compress(false);
            tex.Apply(false, true);
            return tex;
        }

        GameObject BuildWater(MapTerrain t, Transform parent)
        {
            float margin = EdgeRing.Width * t.CellSize + 40f, cell = 2f;
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
            foreach (var list in regionOwned.Values) foreach (var o in list) Looks.Release(o);
            regionOwned.Clear();
            regions = null;
            if (waterDepth != null) Looks.Release(waterDepth);
            Regions = 0;
        }
    }
}
