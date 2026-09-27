// FogView.cs - the local player's fog of war, read from the backend a few
// times a second into a texture the terrain and model shaders darken by,
// and asked whether a point is in sight so enemies out of it stay hidden.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FogView
    {
        public const float Interval = 0.25f;

        readonly IGameBackend backend;
        byte[] fog, pixels;
        int width, height;
        Texture2D tex;
        float next;
        public bool Active { get; private set; }

        // A debug switch: draw everything as if in sight.
        public static bool Disabled;

        public FogView(IGameBackend backend) => this.backend = backend;

        public void Update(bool force = false)
        {
            if (!force && Time.unscaledTime < next) return;
            next = Time.unscaledTime + Interval;
            int need = backend.ReadFog(null, out int w, out int h);
            if (Disabled || need <= 0 || w <= 0 || h <= 0) { SetActive(false); return; }
            if (fog == null || fog.Length < need) fog = new byte[need];
            backend.ReadFog(fog, out width, out height);
            if (tex == null || tex.width != width || tex.height != height)
            {
                if (tex != null) Looks.Release(tex);
                tex = new Texture2D(width, height, TextureFormat.R8, false, true)
                {
                    hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                };
                pixels = new byte[width * height];
            }
            // Row 0 is the north edge, the top of the texture. Two box blurs
            // soften the cell steps into a feathered edge.
            if (soft == null || soft.Length != width * height) { soft = new float[width * height]; spare = new float[width * height]; }
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                {
                    byte f = fog[z * width + x];
                    soft[z * width + x] = f >= 2 ? 255f : f == 1 ? 128f : 0f;
                }
            for (int pass = 0; pass < 2; pass++) { Blur(soft, spare, width, height, 1, 0); Blur(spare, soft, width, height, 0, 1); }
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                    pixels[(height - 1 - z) * width + x] = (byte)Mathf.Clamp(soft[z * width + x], 0, 255);
            tex.SetPixelData(pixels, 0);
            tex.Apply(false);
            var size = backend.Terrain.Size;
            Shader.SetGlobalTexture("_OkuFogTex", tex);
            Shader.SetGlobalVector("_OkuFogRect", new Vector4(0, 0, size.x, size.y));
            SetActive(true);
        }

        float[] soft, spare;

        static void Blur(float[] from, float[] to, int w, int h, int dx, int dz)
        {
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    int x0 = Mathf.Max(0, x - dx), x1 = Mathf.Min(w - 1, x + dx);
                    int z0 = Mathf.Max(0, z - dz), z1 = Mathf.Min(h - 1, z + dz);
                    to[z * w + x] = (from[z0 * w + x0] + from[z * w + x] + from[z1 * w + x1]) / 3f;
                }
        }

        void SetActive(bool on)
        {
            Active = on;
            Shader.SetGlobalFloat("_OkuFogOn", on ? 1f : 0f);
        }

        // In sight now, or true when there is no fog.
        public bool InSight(Vector3 p)
        {
            if (!Active || fog == null) return true;
            var t = backend.Terrain;
            int x = Mathf.Clamp(Mathf.RoundToInt(p.x / t.CellSize), 0, width - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt(-p.z / t.CellSize), 0, height - 1);
            return fog[z * width + x] >= 2;
        }

        public void Dispose()
        {
            SetActive(false);
            if (tex != null) Looks.Release(tex);
            tex = null;
        }
    }
}
