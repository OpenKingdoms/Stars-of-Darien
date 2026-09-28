// FxMesh.cs - a dynamic mesh of effect quads and ribbons under one material,
// and the soft pictures the effects make themselves: a round glow and the ground marks.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FxMesh
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Color32> c = new List<Color32>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<Vector4> fx = new List<Vector4>();
        readonly List<int> t = new List<int>();
        Mesh mesh;
        public int Quads { get; private set; }
        public int Vertices => v.Count;

        public void Clear()
        {
            v.Clear(); c.Clear(); uv.Clear(); fx.Clear(); t.Clear();
            Quads = 0;
        }

        public int Vertex(Vector3 at, Color32 colour, Vector2 texel, Vector4 look)
        {
            v.Add(at); c.Add(colour); uv.Add(texel); fx.Add(look);
            return v.Count - 1;
        }

        public void Triangle(int a, int b, int d) { t.Add(a); t.Add(b); t.Add(d); }

        // Corners bottom left, bottom right, top right, top left, sampling
        // rect (x, y bottom left and z, w top right).
        public void Quad(Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Vector4 rect, Color32 colour, Vector4 look)
        {
            int i = Vertex(bl, colour, new Vector2(rect.x, rect.y), look);
            Vertex(br, colour, new Vector2(rect.z, rect.y), look);
            Vertex(tr, colour, new Vector2(rect.z, rect.w), look);
            Vertex(tl, colour, new Vector2(rect.x, rect.w), look);
            t.Add(i); t.Add(i + 2); t.Add(i + 1);
            t.Add(i); t.Add(i + 3); t.Add(i + 2);
            Quads++;
        }

        public void Draw(Material material, int layer = 0)
        {
            if (v.Count == 0 || material == null) return;
            if (mesh == null) { mesh = new Mesh { name = "effects", hideFlags = HideFlags.DontSave }; mesh.MarkDynamic(); }
            mesh.Clear();
            mesh.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, fx);
            mesh.SetTriangles(t, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            Graphics.RenderMesh(new RenderParams(material) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, layer = layer }, mesh, 0, Matrix4x4.identity);
        }

        public void Dispose()
        {
            Looks.Release(mesh);
            mesh = null;
        }

        // A white disc fading from the middle out: the profile of every
        // beam, trail and glow. Stored as added art reads it.
        public static Texture2D GlowTexture()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "effect glow" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a * (3f - 2f * a);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        // The ground marks side by side: a ragged scorch on the left half,
        // a soft round shadow on the right. Only alpha matters.
        public static Texture2D MarkTexture()
        {
            const int n = 128;
            var tex = new Texture2D(n * 2, n, TextureFormat.RGBA32, true, true) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "effect marks" };
            var px = new Color32[n * 2 * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    // A burnt blotch with a ragged rim and streaks thrown out.
                    float rim = 0.62f + 0.1f * Mathf.Sin(ang * 5f + 1.3f) + 0.06f * Mathf.Sin(ang * 11f + 0.4f) + 0.05f * Mathf.Sin(ang * 23f);
                    float burn = Mathf.Clamp01((rim - r) / 0.28f);
                    float streak = Mathf.Pow(Mathf.Abs(Mathf.Sin(ang * 7f + 2f)), 12f) * Mathf.Clamp01((0.95f - r) / 0.3f) * 0.6f;
                    float grain = 0.85f + 0.15f * Mathf.Sin(x * 1.7f + y * 0.9f) * Mathf.Sin(x * 0.6f - y * 1.3f);
                    float a = Mathf.Clamp01(Mathf.Max(burn * burn, streak) * grain);
                    px[y * n * 2 + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    float s = Mathf.Clamp01(1f - r);
                    s = s * s * (3f - 2f * s);
                    px[y * n * 2 + n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(s * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        public static readonly Vector4 ScorchRect = new Vector4(0f, 0f, 0.5f, 1f);
        public static readonly Vector4 ShadowRect = new Vector4(0.5f, 0f, 1f, 1f);
        public static readonly Vector4 WholeRect = new Vector4(0f, 0f, 1f, 1f);
    }
}
