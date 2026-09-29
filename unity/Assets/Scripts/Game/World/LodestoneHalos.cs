// LodestoneHalos.cs - the rings lodestones give off as they breathe: soft
// rings turned to the camera, in the effects' own additive blend, easing
// into the ground where they meet it.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class LodestoneHalos
    {
        // Where the ring lies across its picture, as a share of half its width.
        public const float RingAt = 0.72f;

        readonly FxMesh mesh = new FxMesh();
        Material mat;
        Texture2D ring;
        Vector3 right = Vector3.right, up = Vector3.up;
        public int Count { get; private set; }

        public void Clear(Camera cam)
        {
            mesh.Clear();
            Count = 0;
            if (cam != null) { right = cam.transform.right; up = cam.transform.up; }
        }

        // strength 0 to 1 scales the light the ring adds.
        public void Add(Vector3 centre, float radius, Color colour, float strength)
        {
            if (strength <= 0.004f || radius <= 0f) return;
            float half = radius / RingAt;
            var r = right * half;
            var u = up * half;
            var c = new Color(colour.r, colour.g, colour.b, Mathf.Clamp01(strength));
            mesh.Quad(centre - r - u, centre + r - u, centre + r + u, centre - r + u, FxMesh.WholeRect, c, new Vector4(1f, 1f, 3f, 0f));
            Count++;
        }

        public void Draw()
        {
            if (Count == 0) return;
            if (mat == null)
            {
                ring = RingTexture();
                mat = new Material(Looks.Find("OkuEffect", "Sprites/Default")) { hideFlags = HideFlags.DontSave, mainTexture = ring, name = "lodestone halo" };
            }
            mesh.Draw(mat);
        }

        public void Dispose()
        {
            mesh.Dispose();
            if (mat != null) Looks.Release(mat);
            if (ring != null) Looks.Release(ring);
            mat = null;
            ring = null;
        }

        // A white ring fading softly both ways from RingAt, over a faint haze inside it.
        public static Texture2D RingTexture()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "lodestone ring" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float band = (r - RingAt) / 0.15f;
                    float a = Mathf.Exp(-band * band) + 0.12f * Mathf.Clamp01(1f - r / RingAt);
                    a *= 1f - Mathf.SmoothStep(0f, 1f, (r - 0.9f) / 0.1f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }
    }
}
