// FxDecals.cs - scorch and embers where fire struck, fading, and the soft
// shadow straight under a shot, all draped over the ground's heights.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FxDecals
    {
        public const int Max = 48;
        const int Grid = 5;
        public const float EmberSeconds = 1.6f;

        struct Mark
        {
            public Vector3 At;
            public float Size, Born, Turn;
        }

        readonly List<Mark> marks = new List<Mark>();
        public int Count => marks.Count;

        public void Scorch(Vector3 at, float size, float now, int seed)
        {
            if (marks.Count >= Max) marks.RemoveAt(0);
            marks.Add(new Mark { At = at, Size = size, Born = now, Turn = (seed * 137.5f) % 360f });
        }

        public void Draw(FxMesh ground, FxMesh glow, System.Func<float, float, float> height, float now)
        {
            for (int i = marks.Count - 1; i >= 0; i--)
            {
                var m = marks[i];
                float age = now - m.Born;
                if (age > FxLook.ScorchSeconds || age < -1f) { marks.RemoveAt(i); continue; }
                float fadeIn = Mathf.Clamp01(age / 0.25f);
                float fadeOut = Mathf.Clamp01((FxLook.ScorchSeconds - age) / 5f);
                var dark = new Color32(34, 27, 22, (byte)Mathf.RoundToInt(200f * fadeIn * fadeOut));
                Drape(ground, m.At, m.Size, m.Turn, dark, FxMesh.ScorchRect, default, height);
                float ember = 1f - age / EmberSeconds;
                if (ember > 0f)
                {
                    var hot = new Color32(255, 110, 36, (byte)Mathf.RoundToInt(255f * ember * ember));
                    Drape(glow, m.At, m.Size * 0.8f, m.Turn, hot, FxMesh.WholeRect, new Vector4(1f, 1f, 0.02f, 0f), height);
                }
            }
        }

        // A shot's shadow on the ground under it, darker the lower it flies.
        public void Shadow(FxMesh ground, Vector3 shot, float size, float strength, System.Func<float, float, float> height)
        {
            float g = height(shot.x, shot.z);
            float above = Mathf.Max(0f, shot.y - g);
            float k = strength * Mathf.Clamp01(1f - above / 14f);
            if (k <= 0.02f) return;
            var c = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(255f * k));
            Drape(ground, new Vector3(shot.x, g, shot.z), size * (1f + above * 0.03f), 0f, c, FxMesh.ShadowRect, default, height, 3);
        }

        // A square of size across at `at`, turned, as a grid laid on the ground.
        void Drape(FxMesh into, Vector3 at, float size, float turn, Color32 c, Vector4 rect, Vector4 look, System.Func<float, float, float> height, int grid = Grid)
        {
            var q = Quaternion.Euler(0f, turn, 0f);
            int first = -1;
            for (int y = 0; y < grid; y++)
                for (int x = 0; x < grid; x++)
                {
                    float fx = x / (grid - 1f), fy = y / (grid - 1f);
                    var local = q * new Vector3((fx - 0.5f) * size, 0f, (fy - 0.5f) * size);
                    var p = at + local;
                    p.y = height(p.x, p.z) + 0.05f;
                    int v = into.Vertex(p, c, new Vector2(Mathf.Lerp(rect.x, rect.z, fx), Mathf.Lerp(rect.y, rect.w, fy)), look);
                    if (first < 0) first = v;
                }
            for (int y = 0; y < grid - 1; y++)
                for (int x = 0; x < grid - 1; x++)
                {
                    int a = first + y * grid + x, b = a + 1, d = a + grid, e = d + 1;
                    into.Triangle(a, d, b);
                    into.Triangle(b, d, e);
                }
        }

        public void Clear() => marks.Clear();
    }
}
