// FxTrails.cs - a fading ribbon along each shot's path, kept in simulation
// time: a glow behind magic, thin smoke behind a cannon ball, a streak behind an arrow.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FxTrails
    {
        public struct Style
        {
            public Color32 Colour;
            public float Width;     // world units at the head
            public float Seconds;   // how long a point lasts
            public float Glow;      // brightness, past 1 to bloom
            public bool Additive;
        }

        sealed class Trail
        {
            public readonly List<Vector3> Points = new List<Vector3>();
            public readonly List<float> Times = new List<float>();
            public Style Style;
            public float Seen;
        }

        readonly Dictionary<int, Trail> trails = new Dictionary<int, Trail>();
        readonly List<int> gone = new List<int>();
        public int Count => trails.Count;

        // A shot at its latest point. A shot far from where its id last was
        // is a new one in an old slot, and starts a new trail.
        public void Track(int id, Vector3 at, Style style, float now)
        {
            if (!trails.TryGetValue(id, out var t)) trails[id] = t = new Trail();
            int n = t.Points.Count;
            if (n > 0 && ((t.Points[n - 1] - at).sqrMagnitude > 36f || now < t.Times[n - 1])) { t.Points.Clear(); t.Times.Clear(); n = 0; }
            t.Style = style;
            t.Seen = now;
            if (n > 0 && (t.Points[n - 1] - at).sqrMagnitude < 0.0025f) { t.Points[n - 1] = at; return; }
            t.Points.Add(at);
            t.Times.Add(now);
        }

        // Ribbons turned to the camera, into the glow and smoke meshes.
        public void Draw(FxMesh glow, FxMesh smoke, Vector3 eye, float now)
        {
            gone.Clear();
            foreach (var kv in trails)
            {
                var t = kv.Value;
                float life = Mathf.Max(0.02f, t.Style.Seconds);
                int drop = 0;
                while (drop < t.Times.Count && now - t.Times[drop] > life) drop++;
                if (drop > 0) { t.Points.RemoveRange(0, drop); t.Times.RemoveRange(0, drop); }
                if (t.Points.Count == 0 || now - t.Seen > life) { gone.Add(kv.Key); continue; }
                if (t.Points.Count < 2) continue;
                Ribbon(t, t.Style.Additive ? glow : smoke, eye, now, life);
            }
            foreach (int id in gone) trails.Remove(id);
        }

        static void Ribbon(Trail t, FxMesh into, Vector3 eye, float now, float life)
        {
            var look = new Vector4(t.Style.Additive ? 1f : 0f, t.Style.Glow, 1f, 0f);
            int n = t.Points.Count;
            int prevA = -1, prevB = -1;
            for (int i = 0; i < n; i++)
            {
                var p = t.Points[i];
                var dir = (i < n - 1 ? t.Points[i + 1] : p) - (i > 0 ? t.Points[i - 1] : p);
                var side = Vector3.Cross(dir, eye - p);
                if (side.sqrMagnitude < 1e-8f) side = Vector3.right;
                side.Normalize();
                // The head is full width and bright, the tail thins away.
                float age = Mathf.Clamp01((now - t.Times[i]) / life);
                float k = (1f - age) * (i + 1f) / n;
                float hw = t.Style.Width * 0.5f * Mathf.Sqrt(k);
                var c = t.Style.Colour;
                c.a = (byte)Mathf.RoundToInt(c.a * k);
                int a = into.Vertex(p - side * hw, c, new Vector2(0.5f, 0f), look);
                int b = into.Vertex(p + side * hw, c, new Vector2(0.5f, 1f), look);
                if (prevA >= 0) { into.Triangle(prevA, b, prevB); into.Triangle(prevA, a, b); }
                prevA = a; prevB = b;
            }
        }

        public void Clear() => trails.Clear();
    }
}
