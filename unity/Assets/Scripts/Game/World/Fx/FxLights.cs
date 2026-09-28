// FxLights.cs - short-lived point lights round bright magic and blasts: asks
// close together merge, the strongest take a small pool, and a light fades in
// and out rather than blinking as the ranking shifts.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FxLights
    {
        // No more than the renderer lights each object with, so a patch of
        // ground under a fight never drops lights at its seams.
        public const int Budget = 4;
        // A light lit last frame ranks this much higher, so near ties hold.
        public const float Stay = 1.35f;
        // Seconds a light takes to come up or go out.
        public const float Fade = 0.12f;
        // Asks nearer than this share of their reach become one light.
        public const float MergeReach = 0.5f;
        const int MaxClusters = 48;

        struct Request
        {
            public Vector3 At;
            public Color Colour;
            public float Range, Intensity, Flicker, Weight;
            public int Key;
        }

        sealed class Slot
        {
            public Light Light;
            public int Key = int.MinValue;
            public float Level, Target;
            public Vector3 At;
            public Color Colour;
            public float Range, Intensity;
        }

        sealed class ByWeight : IComparer<Request>
        {
            public int Compare(Request x, Request y) => y.Weight.CompareTo(x.Weight);
        }

        static readonly ByWeight byWeight = new ByWeight();
        readonly List<Request> asks = new List<Request>();
        readonly List<Request> clusters = new List<Request>();
        readonly List<Slot> slots = new List<Slot>();
        readonly HashSet<int> litBefore = new HashSet<int>(), litNow = new HashSet<int>();
        GameObject root;

        // Lights on now, and how many times one went on or off since the start.
        public int Lit { get; private set; }
        public int Toggles { get; private set; }
        public int Asked => asks.Count;

        public void Begin() => asks.Clear();

        // A light of a size at a point, scaled 0 to 1 by the effect's own
        // rise and fall. Key names the effect, flicker only shades it.
        public void Ask(Vector3 at, Color colour, FxLight size, float scale, int key, float flicker = 1f)
        {
            if (scale <= 0.01f) return;
            FxLook.LightSize(size, out float range, out float intensity);
            if (range <= 0f) return;
            asks.Add(new Request { At = at, Colour = colour, Range = range * Mathf.Lerp(0.6f, 1f, scale), Intensity = intensity * scale, Flicker = flicker, Key = key });
        }

        // The pool takes the asks that matter most to the view at focus.
        public void Commit(Vector3 focus, float viewSize, float dt)
        {
            float near = Mathf.Max(8f, viewSize * 0.5f);
            for (int i = 0; i < asks.Count; i++)
            {
                var a = asks[i];
                a.Weight = a.Intensity * a.Range / (1f + Vector3.Distance(a.At, focus) / near);
                asks[i] = a;
            }
            asks.Sort(byWeight);
            Merge();
            for (int i = 0; i < clusters.Count; i++)
            {
                var c = clusters[i];
                if (litBefore.Contains(c.Key)) c.Weight *= Stay;
                clusters[i] = c;
            }
            clusters.Sort(byWeight);
            int take = Mathf.Min(Budget, clusters.Count);

            litNow.Clear();
            for (int i = 0; i < take; i++) litNow.Add(clusters[i].Key);
            // Slots whose light lost its place fade out, the rest follow theirs.
            foreach (var s in slots)
            {
                s.Target = 0f;
                if (s.Key == int.MinValue || !litNow.Contains(s.Key)) continue;
                for (int i = 0; i < take; i++)
                    if (clusters[i].Key == s.Key) { Follow(s, clusters[i]); break; }
            }
            for (int i = 0; i < take; i++)
            {
                var c = clusters[i];
                if (HasSlot(c.Key)) continue;
                var s = FreeSlot();
                if (s == null) break;
                s.Key = c.Key;
                s.Level = 0f;
                Follow(s, c);
            }
            float step = dt <= 0f ? 1f : Mathf.Clamp01(dt / Fade);
            Lit = 0;
            foreach (var s in slots)
            {
                s.Level = Mathf.MoveTowards(s.Level, s.Target, step);
                bool on = s.Level > 0.005f;
                if (!on) s.Key = int.MinValue;
                if (s.Light.enabled != on) { s.Light.enabled = on; Toggles++; }
                if (!on) continue;
                Lit++;
                s.Light.transform.position = s.At;
                s.Light.color = s.Colour;
                s.Light.range = s.Range;
                s.Light.intensity = s.Intensity * s.Level;
            }
            litBefore.Clear();
            foreach (var s in slots) if (s.Key != int.MinValue) litBefore.Add(s.Key);
        }

        static void Follow(Slot s, in Request c)
        {
            s.Target = 1f;
            s.At = c.At;
            s.Colour = c.Colour;
            s.Range = c.Range;
            s.Intensity = c.Intensity * c.Flicker;
        }

        bool HasSlot(int key)
        {
            foreach (var s in slots) if (s.Key == key) return true;
            return false;
        }

        // An unused slot, a new one while the pool is short, else nothing
        // until a fading light has gone out.
        Slot FreeSlot()
        {
            foreach (var s in slots) if (s.Key == int.MinValue) return s;
            if (slots.Count >= Budget) return null;
            if (root == null) root = new GameObject("Effect lights") { hideFlags = HideFlags.DontSave };
            var l = new GameObject("Effect light").AddComponent<Light>();
            l.transform.SetParent(root.transform, false);
            l.type = LightType.Point;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            l.enabled = false;
            var slot = new Slot { Light = l };
            slots.Add(slot);
            return slot;
        }

        // Strongest first, each ask joins the first light it stands close to,
        // which keeps its place and adds the ask's strength.
        void Merge()
        {
            clusters.Clear();
            foreach (var a in asks)
            {
                int into = -1;
                for (int i = 0; i < clusters.Count; i++)
                {
                    var c = clusters[i];
                    float reach = MergeReach * Mathf.Max(c.Range, a.Range);
                    if ((c.At - a.At).sqrMagnitude < reach * reach) { into = i; break; }
                }
                if (into < 0)
                {
                    if (clusters.Count < MaxClusters) clusters.Add(a);
                    continue;
                }
                var m = clusters[into];
                // The light keeps the name it was lit under.
                if (!litBefore.Contains(m.Key) && litBefore.Contains(a.Key)) m.Key = a.Key;
                float total = m.Intensity + a.Intensity;
                if (total > 0f) m.Colour = (m.Colour * m.Intensity + a.Colour * a.Intensity) / total;
                m.Intensity = Mathf.Min(total, m.Intensity * 1.6f + 0.01f);
                m.Range = Mathf.Max(m.Range, a.Range);
                m.Weight += a.Weight;
                clusters[into] = m;
            }
        }

        public void Dispose()
        {
            if (root != null) Looks.Release(root);
            root = null;
            slots.Clear();
            asks.Clear();
            litBefore.Clear();
            Lit = 0;
        }
    }
}
