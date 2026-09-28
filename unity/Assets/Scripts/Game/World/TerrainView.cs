// TerrainView.cs - builds the ground and the sea for a loaded game: a
// GameObject per region with a LODGroup over its detailed and coarse
// meshes, the edge ring past the map, and the sea (WaterView).
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class TerrainView
    {
        public const int CoarseStep = 4;

        public GameObject Root { get; private set; }
        public WaterView Sea { get; private set; }
        public GameObject Water => Sea?.Root;
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
            var whole = WholeMap(t, Chunk, 1024);
            Apron = BuildRing(t, whole, Root.transform);
            Shader.SetGlobalVector("_OkuMapSize", new Vector4(t.Size.x, t.Size.y, 0, 0));
            GroundDetail.Apply(true);
            chunkImages.Clear();

            seaParent = parent;
            if (t.SeaLevel > 0)
            {
                seaShelf = EdgeRing.Shelf(t);
                bedLuma = BedLuma(t, whole);
                BuildSea();
                // A sea level with no water under it anywhere draws no sea.
                if (!Sea.AnyWater) { Sea.Dispose(); Sea = null; }
            }
            if (Sea == null) WaterView.ClearGlobals();
        }

        Transform seaParent;
        float seaShelf, bedLuma;
        string seaClimate = "";

        // The climate whose water the sea shows, now and if an edit makes one.
        public void SetSeaClimate(string climate)
        {
            seaClimate = climate ?? "";
            Sea?.SetClimate(seaClimate);
        }

        void BuildSea()
        {
            var t = backend.Terrain;
            Sea = new WaterView();
            Sea.Build(seaParent, t.Size, t.CellSize, t.SeaLevel, SeaGround, EdgeRing.Width * t.CellSize + 40f);
            Sea.SetBedLuma(bedLuma);
            Sea.SetClimate(seaClimate);
        }

        // The ground under the sea, read from the backend each time, since
        // an edit in the map editor may replace its terrain. Past the map
        // the sea lies over the ring's own ground.
        float SeaGround(float x, float z)
        {
            var t = backend.Terrain;
            var size = t.Size;
            return x >= 0 && x <= size.x && z <= 0 && z >= -size.y ? t.Sample(x, z) : EdgeRing.Height(t, new Vector2(x, z), seaShelf);
        }

        // The usual lightness of the painted ground under the sea, linear,
        // so the sea bed keeps the painting's light and dark around it.
        static float BedLuma(MapTerrain t, RgbaImage whole)
        {
            double sum = 0;
            int n = 0;
            var size = t.Size;
            for (int y = 0; y < whole.Height; y += 2)
                for (int x = 0; x < whole.Width; x += 2)
                {
                    float wx = (x + 0.5f) / whole.Width * size.x, wz = -(y + 0.5f) / whole.Height * size.y;
                    if (t.SeaLevel - t.Sample(wx, wz) < 0.3f) continue;
                    int o = (y * whole.Width + x) * 4;
                    float r = Mathf.GammaToLinearSpace(whole.Pixels[o] / 255f), g = Mathf.GammaToLinearSpace(whole.Pixels[o + 1] / 255f), b = Mathf.GammaToLinearSpace(whole.Pixels[o + 2] / 255f);
                    sum += 0.3f * r + 0.59f * g + 0.11f * b;
                    n++;
                }
            return n > 0 ? (float)(sum / n) : 0.08f;
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
            if (Sea != null) Sea.Bake(blocks.xMin * t.BlockSize, blocks.xMax * t.BlockSize, -blocks.yMin * t.BlockSize, -blocks.yMax * t.BlockSize);
            else if (t.SeaLevel > 0 && WetIn(t, blocks)) BuildSea();
        }

        // Whether any ground in a rectangle of blocks lies under the sea.
        static bool WetIn(MapTerrain t, RectInt blocks)
        {
            int per = TerrainBuilder.SamplesPerBlock(t);
            for (int z = Mathf.Max(0, blocks.yMin * per); z <= Mathf.Min(t.HeightsH - 1, blocks.yMax * per); z++)
                for (int x = Mathf.Max(0, blocks.xMin * per); x <= Mathf.Min(t.HeightsW - 1, blocks.xMax * per); x++)
                    if (t.HeightAt(x, z) < t.SeaLevel) return true;
            return false;
        }

        // The land past the playable edge: the edge ring, textured with a
        // picture of the whole map mirrored at the edge.
        GameObject BuildRing(MapTerrain t, RgbaImage whole, Transform parent)
        {
            var mesh = EdgeRing.Build(t);
            owned.Add(mesh);
            var picture = UI.UiKit.ToTexture(whole, true);
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

            // Past the ring, haze at the ring's far height out to the far
            // clip. It stays outside the ring, so it never hides the map.
            var plain = EdgeRing.HazeFrame(t);
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

        public void Dispose()
        {
            if (Root != null) Looks.Release(Root);
            Sea?.Dispose();
            Sea = null;
            foreach (var o in owned) Looks.Release(o);
            owned.Clear();
            foreach (var list in regionOwned.Values) foreach (var o in list) Looks.Release(o);
            regionOwned.Clear();
            regions = null;
            Regions = 0;
        }
    }
}
