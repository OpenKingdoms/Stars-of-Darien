// MapEditTool.cs - the map editor's hand on the ground: raise, lower,
// flatten and smooth the heights under a round brush, paint blocks with a
// picture from the game's library, and place or take away features. Each
// stroke goes to the backend, and the terrain rebuilds just where it
// changed.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace OpenKingdomsUnity.Game.World
{
    public enum EditTool { Raise, Lower, Flatten, Smooth, Paint, Feature, Erase }

    public sealed class MapEditTool
    {
        public const float StrokeInterval = 0.06f;

        readonly IGameBackend backend;
        readonly WorldView world;
        byte[] cells;
        int cellsW, cellsH;
        float nextStroke;
        byte flattenTo;

        public EditTool Tool = EditTool.Raise;
        public int Radius = 3;        // cells
        public int Strength = 3;      // bytes a stroke at the centre
        public uint PaintChunk;       // a library id, 0 for none
        public int FeatureDef = -1;
        public int Strokes { get; private set; }
        public bool Dirty { get; private set; }

        public MapEditTool(IGameBackend backend, WorldView world)
        {
            this.backend = backend;
            this.world = world;
            ReloadCells();
        }

        public void ReloadCells()
        {
            int n = backend.ReadCells(null, out cellsW, out cellsH);
            cells = new byte[Math.Max(0, n)];
            if (n > 0) backend.ReadCells(cells, out cellsW, out cellsH);
        }

        public void MarkSaved() => Dirty = false;

        public void Update()
        {
            var cam = world.Camera != null ? world.Camera.GetComponent<Camera>() : null;
            if (cam == null) return;
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            bool onGround = OrderInput.GroundPoint(cam.ScreenPointToRay(Input.mousePosition), backend, out var at);
            float cell = backend.Terrain.CellSize;
            world.Entities.Brush = onGround && !overUi
                ? new EntityRenderer.BrushState { At = at, Radius = (Tool == EditTool.Feature || Tool == EditTool.Erase ? 0.6f : Radius) * cell, Tool = Tool }
                : (EntityRenderer.BrushState?)null;
            if (!onGround || overUi) return;

            int cx = Mathf.RoundToInt(at.x / cell), cz = Mathf.RoundToInt(-at.z / cell);
            if (Input.GetMouseButtonDown(0) && cells != null && cx >= 0 && cz >= 0 && cx < cellsW && cz < cellsH)
                flattenTo = cells[cz * cellsW + cx];

            if (Tool == EditTool.Feature || Tool == EditTool.Erase)
            {
                if (Input.GetMouseButtonDown(0)) { if (Tool == EditTool.Feature) Place(cx, cz); else Erase(at); }
                return;
            }
            if (!Input.GetMouseButton(0) || Time.unscaledTime < nextStroke) return;
            nextStroke = Time.unscaledTime + StrokeInterval;
            if (Tool == EditTool.Paint) Paint(at); else Sculpt(cx, cz);
        }

        // One stroke of a height tool, strongest at the centre.
        public void Sculpt(int cx, int cz)
        {
            if (cells == null || cellsW == 0) return;
            int x0 = Mathf.Max(0, cx - Radius), x1 = Mathf.Min(cellsW - 1, cx + Radius);
            int z0 = Mathf.Max(0, cz - Radius), z1 = Mathf.Min(cellsH - 1, cz + Radius);
            if (x1 < x0 || z1 < z0) return;
            int w = x1 - x0 + 1, h = z1 - z0 + 1;
            var values = new byte[w * h];
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    int gx = x0 + x, gz = z0 + z;
                    float d = Mathf.Sqrt((gx - cx) * (gx - cx) + (gz - cz) * (gz - cz)) / Mathf.Max(1, Radius);
                    float fall = Mathf.Clamp01(1f - d);
                    fall = fall * fall * (3 - 2 * fall);
                    int v = cells[gz * cellsW + gx];
                    switch (Tool)
                    {
                        case EditTool.Raise: v += Mathf.RoundToInt(Strength * fall); break;
                        case EditTool.Lower: v -= Mathf.RoundToInt(Strength * fall); break;
                        case EditTool.Flatten: v = Mathf.RoundToInt(Mathf.Lerp(v, flattenTo, Mathf.Clamp01(fall * Strength / 4f))); break;
                        case EditTool.Smooth: v = Mathf.RoundToInt(Mathf.Lerp(v, Average(gx, gz), Mathf.Clamp01(fall * Strength / 4f))); break;
                    }
                    values[z * w + x] = (byte)Mathf.Clamp(v, 0, 255);
                }
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    cells[(z0 + z) * cellsW + x0 + x] = values[z * w + x];
            if (!backend.EditCells(x0, z0, w, h, values)) return;
            Changed(new RectInt(x0, z0, w, h));
        }

        float Average(int x, int z)
        {
            float sum = 0;
            int n = 0;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int gx = x + dx, gz = z + dz;
                    if (gx < 0 || gz < 0 || gx >= cellsW || gz >= cellsH) continue;
                    sum += cells[gz * cellsW + gx];
                    n++;
                }
            return n > 0 ? sum / n : cells[z * cellsW + x];
        }

        // Every block under the square brush takes the chosen picture, tiled
        // so neighbouring blocks show neighbouring squares of it.
        public void Paint(Vector3 at)
        {
            if (PaintChunk == 0) return;
            var t = backend.Terrain;
            var pic = PictureSize(PaintChunk);
            int squaresX = Mathf.Max(1, pic.x / Mathf.Max(1, t.BlockTexels)), squaresY = Mathf.Max(1, pic.y / Mathf.Max(1, t.BlockTexels));
            float r = Radius * t.CellSize;
            int bx0 = Mathf.Max(0, Mathf.FloorToInt((at.x - r) / t.BlockSize)), bx1 = Mathf.Min(t.BlocksW - 1, Mathf.FloorToInt((at.x + r) / t.BlockSize));
            int by0 = Mathf.Max(0, Mathf.FloorToInt((-at.z - r) / t.BlockSize)), by1 = Mathf.Min(t.BlocksH - 1, Mathf.FloorToInt((-at.z + r) / t.BlockSize));
            if (bx1 < bx0 || by1 < by0) return;
            int w = bx1 - bx0 + 1, h = by1 - by0 + 1;
            var ids = new uint[w * h];
            var tx = new byte[w * h];
            var ty = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    ids[y * w + x] = PaintChunk;
                    tx[y * w + x] = (byte)((bx0 + x) % squaresX);
                    ty[y * w + x] = (byte)((by0 + y) % squaresY);
                }
            if (!backend.PaintBlocks(bx0, by0, w, h, ids, tx, ty)) return;
            int per = TerrainBuilder.SamplesPerBlock(t);
            Changed(new RectInt(bx0 * per, by0 * per, w * per, h * per));
        }

        readonly Dictionary<uint, Vector2Int> pictureSizes = new Dictionary<uint, Vector2Int>();

        Vector2Int PictureSize(uint id)
        {
            if (pictureSizes.TryGetValue(id, out var s)) return s;
            var img = backend.ChunkPicture(id);
            s = img != null ? new Vector2Int(img.Width, img.Height) : new Vector2Int(256, 256);
            pictureSizes[id] = s;
            return s;
        }

        void Place(int cx, int cz)
        {
            if (FeatureDef < 0) return;
            if (backend.PlaceFeature(FeatureDef, cx, cz) >= 0) { Strokes++; Dirty = true; }
        }

        void Erase(Vector3 at)
        {
            var list = new FeatureState[EntityRenderer.MaxFeatures];
            int n = backend.ReadFeatures(list);
            int best = -1;
            float bestD = 2f * 2f;
            for (int i = 0; i < n; i++)
            {
                var d = list[i].Position - at;
                d.y = 0;
                if (d.sqrMagnitude < bestD) { bestD = d.sqrMagnitude; best = list[i].Index; }
            }
            if (best >= 0 && backend.RemoveFeature(best)) { Strokes++; Dirty = true; }
        }

        // Cells changed: rebuild the terrain regions over them.
        void Changed(RectInt cellRect)
        {
            Strokes++;
            Dirty = true;
            var t = backend.Terrain;
            int per = TerrainBuilder.SamplesPerBlock(t);
            world.Terrain.Rebuild(new RectInt(cellRect.xMin / per, cellRect.yMin / per,
                Mathf.CeilToInt((float)cellRect.width / per) + 1, Mathf.CeilToInt((float)cellRect.height / per) + 1));
        }
    }
}
