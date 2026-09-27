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
            chunkImages.Clear();

            if (t.SeaLevel > 0) Water = BuildWater(t, parent);
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
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
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
