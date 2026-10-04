// FxLights.cs - short-lived point lights round a lightmap weapon's shots
// and blasts, and the flashes of the blasts themselves: asks close together
// merge, the strongest take a pool sized by the Battle effects setting, a
// glow fades in and out rather than blinking, and a flash is up at once.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FxLights
    {
        // As many as the Battle effects setting allows. Forward+ lights every
        // pixel by all of them, so a patch of ground under a fight never
        // drops lights at its seams.
        public static int Budget => FxQuality.Current.Lights;
        // How far and how bright a blast's flash may get.
        public const float FlashRange = 48f, FlashIntensity = 80f;
        // A light lit last frame ranks this much higher, so near ties hold.
        public const float Stay = 1.35f;
        // Seconds a light takes to come up or go out.
        public const float Fade = 0.12f;
        // Asks nearer than this share of their reach become one light.
        public const float MergeReach = 0.75f;
        const int MaxClusters = 48;

        struct Request
        {
            public Vector3 At;
            public Color Colour;
            public float Range, Intensity, Flicker, Weight, MaxRange, MaxIntensity;
            public int Key;
            public bool Snap;
        }

        sealed class Slot
        {
            public Light Light;
            public int Key = int.MinValue;
            public float Level, Target;
            public Vector3 At;
            public Color Colour;
            public float Range, Intensity;
            public bool Snap;
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
        // The strongest and widest light on now, and the least saturated.
        public float Brightest { get; private set; }
        // The share of each light's strength the scene's own light allows, last frame.
        public float Calm { get; private set; } = 1f;
        public static float CalmFor(float sceneLight) => Mathf.Lerp(0.4f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.8f, sceneLight)));
        public float Widest { get; private set; }
        public float Palest { get; private set; }

        public void Begin() => asks.Clear();

        // A light of a size at a point, scaled 0 to 1 by the effect's own
        // rise and fall. Key names the effect, flicker only shades it.
        public void Ask(Vector3 at, Color colour, FxLight size, float scale, int key, float flicker = 1f)
        {
            if (scale <= 0.01f) return;
            FxLook.LightSize(size, out float range, out float intensity);
            if (range <= 0f) return;
            asks.Add(new Request
            {
                At = at, Colour = colour, Range = range * Mathf.Lerp(0.6f, 1f, scale), Intensity = intensity * scale, Flicker = flicker, Key = key,
                MaxRange = FxLook.MaxLightRange, MaxIntensity = FxLook.MaxLightIntensity,
            });
        }

        // A blast's flash: up at once, lighting the ground out to reach.
        public void Flash(Vector3 at, Color colour, float reach, float intensity, int key)
        {
            if (intensity <= 0.01f || reach <= 0f) return;
            asks.Add(new Request
            {
                At = at, Colour = colour, Range = reach * 1.5f, Intensity = intensity, Flicker = 1f, Key = key,
                MaxRange = FlashRange, MaxIntensity = FlashIntensity, Snap = true,
            });
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
            Brightest = Widest = 0f;
            Palest = 1f;
            // On a dim or night map the same flash would blow out the dark
            // ground round it, so lights scale with how the scene is lit.
            Calm = CalmFor(LodestonePulse.SceneLight());
            foreach (var s in slots)
            {
                s.Level = s.Snap && s.Target > s.Level ? s.Target : Mathf.MoveTowards(s.Level, s.Target, step);
                bool on = s.Level > 0.005f;
                if (!on) s.Key = int.MinValue;
                if (s.Light.enabled != on) { s.Light.enabled = on; Toggles++; }
                if (!on) continue;
                Lit++;
                s.Light.transform.position = s.At;
                s.Light.color = s.Colour;
                s.Light.range = s.Range;
                s.Light.intensity = s.Intensity * s.Level * Calm;
                // As asked for, before the scene's calm.
                Brightest = Mathf.Max(Brightest, s.Intensity * s.Level);
                Widest = Mathf.Max(Widest, s.Range);
                Color.RGBToHSV(s.Colour, out _, out float sat, out _);
                Palest = Mathf.Min(Palest, sat);
            }
            litBefore.Clear();
            foreach (var s in slots) if (s.Key != int.MinValue) litBefore.Add(s.Key);
        }

        static void Follow(Slot s, in Request c)
        {
            s.Target = 1f;
            s.At = c.At;
            s.Colour = c.Colour;
            s.Range = Mathf.Min(c.Range, c.MaxRange);
            s.Intensity = Mathf.Min(c.Intensity * c.Flicker, c.MaxIntensity);
            s.Snap = c.Snap;
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
                m.MaxRange = Mathf.Max(m.MaxRange, a.MaxRange);
                m.MaxIntensity = Mathf.Max(m.MaxIntensity, a.MaxIntensity);
                m.Snap |= a.Snap;
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
