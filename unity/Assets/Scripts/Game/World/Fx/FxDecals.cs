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

        sealed class Mark
        {
            public Vector3 At;
            public float Size, Born, Turn;
            // The ground under the scorch's and the embers' grids, read once.
            public readonly Vector3[] Scorch = new Vector3[Grid * Grid], Ember = new Vector3[Grid * Grid];
        }

        readonly List<Mark> marks = new List<Mark>();
        readonly Stack<Mark> spare = new Stack<Mark>();
        readonly Vector3[] shadow = new Vector3[9];
        public int Count => marks.Count;

        public void Scorch(Vector3 at, float size, float now, int seed, System.Func<float, float, float> height)
        {
            if (marks.Count >= Max) { spare.Push(marks[0]); marks.RemoveAt(0); }
            var m = spare.Count > 0 ? spare.Pop() : new Mark();
            m.At = at; m.Size = size; m.Born = now; m.Turn = (seed * 137.5f) % 360f;
            Lay(m.Scorch, at, size, m.Turn, Grid, height);
            Lay(m.Ember, at, size * 0.8f, m.Turn, Grid, height);
            marks.Add(m);
        }

        public void Draw(FxMesh ground, FxMesh glow, float now)
        {
            for (int i = marks.Count - 1; i >= 0; i--)
            {
                var m = marks[i];
                float age = now - m.Born;
                if (age > FxLook.ScorchSeconds || age < -1f) { spare.Push(m); marks.RemoveAt(i); continue; }
                float fadeIn = Mathf.Clamp01(age / 0.25f);
                float fadeOut = Mathf.Clamp01((FxLook.ScorchSeconds - age) / 5f);
                var dark = new Color32(34, 27, 22, (byte)Mathf.RoundToInt(200f * fadeIn * fadeOut));
                Drape(ground, m.Scorch, Grid, dark, FxMesh.ScorchRect, default);
                float ember = 1f - age / EmberSeconds;
                if (ember > 0f)
                {
                    var hot = new Color32(255, 110, 36, (byte)Mathf.RoundToInt(255f * ember * ember));
                    Drape(glow, m.Ember, Grid, hot, FxMesh.WholeRect, new Vector4(1f, 1f, 0.02f, 0f));
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
            Lay(shadow, new Vector3(shot.x, g, shot.z), size * (1f + above * 0.03f), 0f, 3, height);
            Drape(ground, shadow, 3, c, FxMesh.ShadowRect, default);
        }

        // A square of size across at `at`, turned, as a grid of points on the ground.
        static void Lay(Vector3[] into, Vector3 at, float size, float turn, int grid, System.Func<float, float, float> height)
        {
            var q = Quaternion.Euler(0f, turn, 0f);
            for (int y = 0; y < grid; y++)
                for (int x = 0; x < grid; x++)
                {
                    var p = at + q * new Vector3((x / (grid - 1f) - 0.5f) * size, 0f, (y / (grid - 1f) - 0.5f) * size);
                    p.y = height(p.x, p.z) + 0.05f;
                    into[y * grid + x] = p;
                }
        }

        static void Drape(FxMesh into, Vector3[] points, int grid, Color32 c, Vector4 rect, Vector4 look)
        {
            int first = -1;
            for (int y = 0; y < grid; y++)
                for (int x = 0; x < grid; x++)
                {
                    float fx = x / (grid - 1f), fy = y / (grid - 1f);
                    int v = into.Vertex(points[y * grid + x], c, new Vector2(Mathf.Lerp(rect.x, rect.z, fx), Mathf.Lerp(rect.y, rect.w, fy)), look);
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

        public void Clear()
        {
            foreach (var m in marks) spare.Push(m);
            marks.Clear();
        }
    }
}
