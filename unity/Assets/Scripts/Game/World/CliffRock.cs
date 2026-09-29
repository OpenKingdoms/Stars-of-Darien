// CliffRock.cs - rock for steep ground, until art supplies it: one tiling
// picture made once from noise, with lightness in red around a middle grey
// (ridges, blotches and faint level layers) and its bumps' slope in green
// and blue.
// The terrain shader lays it on three planes over faces steeper than about
// 40 degrees, where the map's own picture smears, and gives those faces
// more of the sky's light.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public static class CliffRock
    {
        public const int Size = 256;
        public const float TileUnits = 5f;
        // How much more sky light the steepest face takes, and how hard its bumps are.
        public const float AmbientLift = 0.7f, Bump = 0.5f;

        static Texture2D tex;

        public static void Apply()
        {
            if (tex == null) tex = Make();
            Shader.SetGlobalTexture("_OkuRock", tex);
            Shader.SetGlobalVector("_OkuRockParams", new Vector4(TileUnits, AmbientLift, Bump, 0f));
        }

        public static Texture2D Make()
        {
            int n = Size * Size;
            var height = new float[n];
            var light = new float[n];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = (float)x / Size, v = (float)y / Size;
                    int i = y * Size + x;
                    // Rows of v run up a wall, so the layers lie level.
                    float ridge = 1f - Mathf.Abs(GroundDetail.Fbm(u, v, 4, 5, 51) * 2f - 1f);
                    float layers = 0.5f + 0.5f * Mathf.Sin((v * 6f + GroundDetail.Fbm(u, v, 2, 3, 61) * 3f) * 2f * Mathf.PI);
                    float grit = GroundDetail.Fbm(u, v, 32, 2, 71);
                    float blotch = GroundDetail.Fbm(u, v, 2, 3, 91);
                    height[i] = ridge * 0.6f + grit * 0.25f + layers * 0.15f;
                    light[i] = ridge * 0.45f + blotch * 0.25f + layers * 0.15f + grit * 0.15f;
                }
            GroundDetail.Centre(light, 0.15f);
            var px = new Color32[n];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    float gx = height[y * Size + (x + 1) % Size] - height[y * Size + (x + Size - 1) % Size];
                    float gz = height[((y + 1) % Size) * Size + x] - height[((y + Size - 1) % Size) * Size + x];
                    px[i] = new Color32(GroundDetail.Byte(light[i]), GroundDetail.Byte(0.5f + gx * 6f), GroundDetail.Byte(0.5f + gz * 6f), 255);
                }
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true)
            {
                name = "cliff rock", hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4,
            };
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }
    }
}
