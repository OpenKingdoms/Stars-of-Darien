// EngineTerrain.cs - the map's ground from the engine: the height grid at
// tile corners, and each 32 pixel block textured from its square of a
// chunk picture, as OpenKingdoms' 3D view builds it. One mesh per region
// of 16 by 16 blocks, with a submesh per chunk picture it uses.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Engine
{
    public sealed class EngineTerrain
    {
        const int RegionBlocks = 16;

        public OkxTerrainInfo Info;
        public float[] Heights;
        public GameObject Root;
        public int ChunkTextures => chunkMats.Count;
        readonly Dictionary<int, Material> chunkMats = new Dictionary<int, Material>();
        readonly List<Object> owned = new List<Object>();

        public bool Build(Transform parent)
        {
            if (OkEngine.okx_terrain_info(out Info) != 0) return false;
            Heights = new float[Info.heightsW * Info.heightsH];
            OkEngine.okx_terrain_heights(Heights, Heights.Length);
            int nb = Info.blocksW * Info.blocksH;
            var blocks = new int[nb * 3];
            OkEngine.okx_terrain_blocks(blocks, blocks.Length);

            Root = new GameObject("Terrain");
            Root.transform.SetParent(parent, false);
            var shader = Resources.Load<Shader>("Shaders/OkModel");

            int regionsW = (Info.blocksW + RegionBlocks - 1) / RegionBlocks;
            int regionsH = (Info.blocksH + RegionBlocks - 1) / RegionBlocks;
            for (int ry = 0; ry < regionsH; ry++)
                for (int rx = 0; rx < regionsW; rx++)
                    BuildRegion(rx, ry, blocks, shader);
            return true;
        }

        // Height in engine pixels at a tile corner, clamped to the grid.
        float H(int tx, int tz)
        {
            tx = Mathf.Clamp(tx, 0, Info.heightsW - 1);
            tz = Mathf.Clamp(tz, 0, Info.heightsH - 1);
            return Heights[tz * Info.heightsW + tx];
        }

        void BuildRegion(int rx, int ry, int[] blocks, Shader shader)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var byChunk = new Dictionary<int, List<int>>();
            int tilesPerBlock = Info.blockPx / Info.tilePx;
            float s = EngineSettings.PxToUnits;

            for (int by = ry * RegionBlocks; by < Mathf.Min(Info.blocksH, (ry + 1) * RegionBlocks); by++)
            {
                for (int bx = rx * RegionBlocks; bx < Mathf.Min(Info.blocksW, (rx + 1) * RegionBlocks); bx++)
                {
                    int b = by * Info.blocksW + bx;
                    int chunk = blocks[3 * b], texX = blocks[3 * b + 1], texY = blocks[3 * b + 2];
                    Material mat = ChunkMaterial(chunk, shader, out int texW, out int texH);
                    if (mat == null) continue;
                    // Half a texel in from the square's edge, so filtering
                    // never reads the neighbouring square.
                    float u0 = (texX * Info.subPx + 0.5f) / texW, u1 = (texX * Info.subPx + Info.subPx - 0.5f) / texW;
                    float t0 = (texY * Info.subPx + 0.5f) / texH, t1 = (texY * Info.subPx + Info.subPx - 0.5f) / texH;
                    int n = tilesPerBlock;
                    int basev = verts.Count;
                    for (int gz = 0; gz <= n; gz++)
                    {
                        for (int gx = 0; gx <= n; gx++)
                        {
                            int tx = bx * n + gx, tz = by * n + gz;
                            float h = H(tx, tz);
                            verts.Add(new Vector3(tx * Info.tilePx * s, h * s, -tz * Info.tilePx * s));
                            // The slope in engine pixels, z flipped for Unity.
                            float dx = (H(tx + 1, tz) - H(tx - 1, tz)) / (2f * Info.tilePx);
                            float dz = (H(tx, tz + 1) - H(tx, tz - 1)) / (2f * Info.tilePx);
                            norms.Add(new Vector3(-dx, 1f, dz).normalized);
                            uvs.Add(new Vector2(u0 + (u1 - u0) * gx / n, t0 + (t1 - t0) * gz / n));
                        }
                    }
                    if (!byChunk.TryGetValue(chunk, out var tris)) byChunk[chunk] = tris = new List<int>();
                    int row = n + 1;
                    for (int gz = 0; gz < n; gz++)
                    {
                        for (int gx = 0; gx < n; gx++)
                        {
                            // North west, north east, south west, south east,
                            // clockwise seen from above.
                            int nw = basev + gz * row + gx, ne = nw + 1, sw = nw + row, se = sw + 1;
                            tris.Add(nw); tris.Add(ne); tris.Add(se);
                            tris.Add(nw); tris.Add(se); tris.Add(sw);
                        }
                    }
                }
            }
            if (verts.Count == 0) return;
            var mesh = new Mesh { name = $"terrain {rx},{ry}", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            var colours = new Color32[verts.Count];
            for (int i = 0; i < colours.Length; i++) colours[i] = new Color32(255, 255, 255, 255);
            mesh.colors32 = colours;
            mesh.subMeshCount = byChunk.Count;
            var mats = new Material[byChunk.Count];
            int k = 0;
            foreach (var kv in byChunk)
            {
                mesh.SetTriangles(kv.Value, k, false);
                mats[k++] = chunkMats[kv.Key];
            }
            mesh.RecalculateBounds();
            owned.Add(mesh);
            var go = new GameObject(mesh.name);
            go.transform.SetParent(Root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
        }

        readonly Dictionary<int, Vector2Int> chunkSize = new Dictionary<int, Vector2Int>();

        Material ChunkMaterial(int chunk, Shader shader, out int w, out int h)
        {
            if (chunkMats.TryGetValue(chunk, out var m))
            {
                var sz = chunkSize[chunk];
                w = sz.x; h = sz.y;
                return m;
            }
            int need = OkEngine.okx_terrain_chunk(chunk, null, 0, out w, out h);
            if (need <= 0) { w = h = 1; return null; }
            var px = new byte[need];
            OkEngine.okx_terrain_chunk(chunk, px, need, out w, out h);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = $"chunk {chunk}" };
            tex.SetPixelData(px, 0);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            tex.Apply(true, false);
            tex.Compress(true);
            tex.Apply(false, true);
            owned.Add(tex);
            m = new Material(shader) { name = $"chunk {chunk}", mainTexture = tex };
            m.SetFloat("_Cutoff", 0f);
            owned.Add(m);
            chunkMats[chunk] = m;
            chunkSize[chunk] = new Vector2Int(w, h);
            return m;
        }

        // The engine's own ground height at a Unity point, in Unity units.
        public float HeightAt(Vector3 p)
        {
            var e = EngineSettings.ToEngine(p);
            return OkEngine.okx_ground_height(e.x, e.y) * EngineSettings.PxToUnits;
        }

        public void Dispose()
        {
            foreach (var o in owned) EngineSettings.Release(o);
            owned.Clear();
            chunkMats.Clear();
            chunkSize.Clear();
            if (Root != null) EngineSettings.Release(Root);
        }
    }
}
