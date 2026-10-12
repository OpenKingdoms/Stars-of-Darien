// ScarMapSave.cs - the scar map as a saved game keeps it: its textures and
// the CPU copy of the dips byte for byte, the stamps still waiting, the
// regions of ground dented and the clock its fading steps by.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Game.World
{
    // What a scar map held when a game was saved. Each texture is RGBA, a
    // byte a channel, row 0 on the map's south edge.
    public sealed class ScarState
    {
        public EffectsQuality Level;
        public int TexW, TexH;
        public float SizeX, SizeZ;
        // The battle's clock then, when the fading last stepped, and the seed of the next mark.
        public float Now, SteppedAt;
        public int Marked;
        // Null when there was no graphics card to read them from.
        public byte[] Marks, Shape, Fade;
        // The dips and rims as DentField keeps them, TexW by TexH.
        public byte[] Depth, Rim;
        public ScarStamp[] Pending = new ScarStamp[0];
        // The regions of ground asked to dip, and how finely.
        public (int x, int y, int factor)[] Regions = new (int, int, int)[0];
    }

    public sealed partial class ScarMap
    {
        readonly Queue<(int x, int y)> regionsToRestore = new Queue<(int, int)>();

        // Dented regions a restore still has to build.
        public int RegionsToRestore => regionsToRestore.Count;

        // What the scar map holds now, its textures read back from the card.
        public ScarState Capture()
        {
            bool card = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            var s = new ScarState
            {
                Level = Level, TexW = TexW, TexH = TexH, SizeX = size.x, SizeZ = size.y,
                Now = now, SteppedAt = steppedAt, Marked = marked,
                Marks = card ? ReadBytes(marks) : null, Shape = card ? ReadBytes(shape) : null, Fade = card ? ReadBytes(fade) : null,
                Depth = Dents.Depths(), Rim = Dents.Rims(), Pending = pending.ToArray(),
            };
            if (ground != null) s.Regions = ground.RefinedList().ToArray();
            return s;
        }

        // Takes what a saved game kept, its clock moved onto this battle's, so
        // frost melts, wet dries and holy light fades on from where they were.
        // Kept at another setting, the textures are resampled and the dips
        // read back from the shape, as a change of setting does.
        public void Restore(ScarState s, float clock)
        {
            if (s == null) return;
            float shift = clock - s.Now;
            now = clock;
            steppedAt = s.SteppedAt + shift;
            marked = s.Marked;
            pending.Clear();
            int cap = QueueCap(Level);
            foreach (var p in s.Pending)
            {
                if (pending.Count >= cap) break;
                pending.Add(p);
            }
            bool same = s.TexW == TexW && s.TexH == TexH;
            Upload(marks, s.Marks, s.TexW, s.TexH);
            Upload(shape, s.Shape, s.TexW, s.TexH);
            Upload(fade, s.Fade, s.TexW, s.TexH);
            if (same && s.Depth != null && s.Rim != null && s.Depth.Length == TexW * TexH && s.Rim.Length == TexW * TexH) Dents.Load(s.Depth, s.Rim);
            else if (s.Shape != null)
            {
                var px = Read(shape);
                if (px != null) Dents.Load(px);
            }
            SetGlobals();
            regionsToRestore.Clear();
            var t = backend.Terrain;
            if (quality.Ground != ScarGround.Dips || ground == null || t == null) return;
            if (same && FxQuality.For(s.Level).Ground == ScarGround.Dips)
            {
                foreach (var r in s.Regions) regionsToRestore.Enqueue((r.x, r.y));
                return;
            }
            // Saved with no dips in the mesh: every region a dip reaches.
            float span = TerrainBuilder.RegionBlocks * t.BlockSize;
            for (int ry = 0; ry * span < size.y; ry++)
                for (int rx = 0; rx * span < size.x; rx++)
                    if (Dents.AnyIn(rx * span, -(ry + 1) * span, (rx + 1) * span, -ry * span)) regionsToRestore.Enqueue((rx, ry));
        }

        // Builds the restored dented regions for about budgetMs. True once
        // every one is in place.
        public bool RestoreGround(double budgetMs)
        {
            if (ground == null) { regionsToRestore.Clear(); return true; }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int factor = Refinement;
            while (regionsToRestore.Count > 0)
            {
                var (x, y) = regionsToRestore.Dequeue();
                ground.RefineNow(x, y, factor);
                if (clock.Elapsed.TotalMilliseconds >= budgetMs) break;
            }
            return regionsToRestore.Count == 0;
        }

        // A texture's texels as RGBA bytes, row 0 on the map's south edge.
        public static byte[] ReadBytes(RenderTexture rt)
        {
            if (rt == null) return null;
            if (SystemInfo.supportsAsyncGPUReadback)
            {
                var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
                req.WaitForCompletion();
                if (!req.hasError) return req.GetData<byte>().ToArray();
            }
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            var bytes = tex.GetRawTextureData<byte>().ToArray();
            Looks.Release(tex);
            return bytes;
        }

        // Puts saved texels into a texture, copied as they are at its size
        // and filtered to it at another.
        static void Upload(RenderTexture rt, byte[] rgba, int w, int h)
        {
            if (rt == null || rgba == null || w <= 0 || h <= 0 || rgba.Length != w * h * 4) return;
            bool same = w == rt.width && h == rt.height;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true)
            {
                name = "scar restore", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp,
                filterMode = same ? FilterMode.Point : FilterMode.Bilinear,
            };
            tex.SetPixelData(rgba, 0);
            tex.Apply(false);
            if (same && tex.graphicsFormat == rt.graphicsFormat && (SystemInfo.copyTextureSupport & CopyTextureSupport.TextureToRT) != 0)
                Graphics.CopyTexture(tex, rt);
            else
                Graphics.Blit(tex, rt);
            Looks.Release(tex);
        }
    }
}
