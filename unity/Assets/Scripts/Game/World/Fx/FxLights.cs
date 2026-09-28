// FxLights.cs - short-lived point lights round bright magic and blasts, from
// a small pool given each frame to the brightest asks nearest the view.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FxLights
    {
        public const int Budget = 8;

        struct Request
        {
            public Vector3 At;
            public Color Colour;
            public float Range, Intensity, Weight;
        }

        readonly List<Request> asks = new List<Request>();
        readonly List<Light> pool = new List<Light>();
        GameObject root;
        public int Lit { get; private set; }
        public int Asked => asks.Count;

        public void Begin() => asks.Clear();

        // A light of a size at a point, scaled 0 to 1 by the effect's own
        // rise and fall.
        public void Ask(Vector3 at, Color colour, FxLight size, float scale)
        {
            if (scale <= 0.01f) return;
            FxLook.LightSize(size, out float range, out float intensity);
            if (range <= 0f) return;
            asks.Add(new Request { At = at, Colour = colour, Range = range * Mathf.Lerp(0.6f, 1f, scale), Intensity = intensity * scale });
        }

        // The pool takes the asks that matter most to the view at focus.
        public void Commit(Vector3 focus, float viewSize)
        {
            for (int i = 0; i < asks.Count; i++)
            {
                var a = asks[i];
                float d = Vector3.Distance(a.At, focus);
                a.Weight = a.Intensity * a.Range / (1f + d / Mathf.Max(8f, viewSize * 0.5f));
                asks[i] = a;
            }
            asks.Sort((x, y) => y.Weight.CompareTo(x.Weight));
            Lit = Mathf.Min(Budget, asks.Count);
            if (Lit > 0 && root == null) root = new GameObject("Effect lights") { hideFlags = HideFlags.DontSave };
            while (pool.Count < Lit)
            {
                var l = new GameObject("Effect light").AddComponent<Light>();
                l.transform.SetParent(root.transform, false);
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.ForcePixel;
                pool.Add(l);
            }
            for (int i = 0; i < pool.Count; i++)
            {
                var l = pool[i];
                bool on = i < Lit;
                if (l.enabled != on) l.enabled = on;
                if (!on) continue;
                var a = asks[i];
                l.transform.position = a.At;
                l.color = a.Colour;
                l.range = a.Range;
                l.intensity = a.Intensity;
            }
        }

        public void Dispose()
        {
            if (root != null) Looks.Release(root);
            root = null;
            pool.Clear();
            asks.Clear();
        }
    }
}
