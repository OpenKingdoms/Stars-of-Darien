// LodestonePulse.cs - a lodestone breathes light: its crystal's own glow
// eases between 80% and 115% of rest over six seconds with no step
// anywhere, and at each swell a soft ring of the crystal's colour spreads
// from it and fades, one a breath. Each lodestone keeps its own time, from
// where it stands. Gentler from the classic camera, which sees many at
// once, and in a dim scene, where a glow tells more.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public static class LodestonePulse
    {
        public const float Period = 6f;
        public const float Low = 0.8f, High = 1.15f;
        // The ring's life as a share of a breath, its strongest, and how far
        // past the crystal it spreads, in world units.
        public const float RingLife = 0.6f, RingAlpha = 0.24f, RingReach = 2.2f;

        // The eight lodestones and divine lodestones.
        public static bool IsLodestone(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return false;
            string n = objectName.ToUpperInvariant();
            return n.Length == 7 && (n.EndsWith("LODE") || n.EndsWith("MANA"));
        }

        // 0 to 1 through a breath, 0 at its dimmest.
        public static float Cycle(float seconds, float phase) => Mathf.Repeat(seconds / Period + phase, 1f);

        // A lodestone's own offset, so neighbours do not breathe as one.
        public static float Phase(Vector3 at) => Mathf.Repeat(at.x * 0.618034f + at.z * 0.414214f, 1f);

        // The crystal's light as a share of its rest. calm, 0 to 1, narrows
        // the swing about rest.
        public static float Gain(float seconds, float phase, float calm = 1f)
        {
            float swell = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * Cycle(seconds, phase));
            return 1f + (Mathf.Lerp(Low, High, swell) - 1f) * calm;
        }

        // How far through its life the ring born at the last swell is, 0 to
        // 1, or -1 while none shows.
        public static float RingAge(float seconds, float phase)
        {
            float since = Mathf.Repeat(Cycle(seconds, phase) - 0.5f, 1f);
            return since < RingLife ? since / RingLife : -1f;
        }

        // Its strength, 0 to 1: up from nothing, then fading to nothing.
        public static float RingStrength(float age) =>
            age < 0f ? 0f : Mathf.SmoothStep(0f, 1f, age / 0.15f) * (1f - age) * (1f - age);

        // Its radius, from the crystal's edge out, quick and then slowing.
        public static float RingRadius(float age, float crystal, float reach)
        {
            float a = Mathf.Clamp01(age);
            return crystal + reach * (1f - (1f - a) * (1f - a));
        }

        // 1 in a low view, less toward the classic camera's tilt.
        public static float ViewCalm(float pitch) => Mathf.Lerp(1f, 0.7f, Mathf.InverseLerp(35f, GameCamera.ClassicPitch, pitch));

        // 1 in daylight, less in a dim scene.
        public static float LightCalm(float light) => Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(0.25f, 0.9f, light));

        // How brightly the scene is lit, 0 to 1, by the sun and the sky.
        public static float SceneLight()
        {
            var sun = RenderSettings.sun;
            float s = sun != null && sun.isActiveAndEnabled ? sun.intensity * Mathf.Max(sun.color.r, Mathf.Max(sun.color.g, sun.color.b)) : 0f;
            return Mathf.Clamp01(s * 0.75f + RenderSettings.ambientIntensity * 0.3f);
        }
    }

    // Where a card model's crystal is and what colour it glows: the light
    // its glowing parts give off, weighted by how bright and how big each is.
    public sealed class LodestoneGlow
    {
        public Vector3 Centre;      // in the model's frame
        public float Radius;        // the glowing parts' reach across, world units
        public Color Colour;        // brightest channel 1

        public static LodestoneGlow Of(OverrideModel model)
        {
            if (model == null) return null;
            var centre = Vector3.zero;
            var colour = Color.black;
            float weight = 0f;
            var reach = new Bounds();
            bool any = false;
            foreach (var part in model.Parts)
            {
                var m = part.Material;
                if (m == null || part.Mesh == null || part.Submesh >= part.Mesh.subMeshCount) continue;
                var light = Color.black;
                float self = m.HasProperty("_Emission") ? m.GetFloat("_Emission") : 0f;
                if (self > 0f) light += m.color * Mean(m.mainTexture) * self;
                if (m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor"))
                    light += (Color)m.GetVector("_EmissionColor") * Mean(m.GetTexture("_EmissionMap"));
                float strength = Mathf.Max(light.r, Mathf.Max(light.g, light.b));
                if (strength < 0.01f) continue;
                var b = part.Mesh.GetSubMesh(part.Submesh).bounds;
                var at = part.NodeToRoot.MultiplyPoint3x4(b.center);
                var size = part.NodeToRoot.MultiplyVector(b.size);
                size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                float area = size.x * size.y + size.y * size.z + size.z * size.x + 1e-4f;
                centre += at * (strength * area);
                colour += light * area;
                weight += strength * area;
                if (!any) { reach = new Bounds(at, size); any = true; }
                else reach.Encapsulate(new Bounds(at, size));
            }
            if (weight <= 0f) return null;
            float top = Mathf.Max(colour.r, Mathf.Max(colour.g, colour.b));
            return new LodestoneGlow
            {
                Centre = centre / weight,
                Radius = Mathf.Max(0.3f, Mathf.Max(reach.extents.x, reach.extents.z)),
                Colour = top > 0f ? new Color(colour.r / top, colour.g / top, colour.b / top, 1f) : Color.white,
            };
        }

        // A texture's mean colour, from a small mip, or white.
        static Color Mean(Texture t)
        {
            if (!(t is Texture2D t2) || !t2.isReadable) return Color.white;
            try
            {
                int mip = Mathf.Max(0, t2.mipmapCount - 4);
                var px = t2.GetPixels32(mip);
                if (px.Length == 0) return Color.white;
                long r = 0, g = 0, b = 0;
                foreach (var p in px) { r += p.r; g += p.g; b += p.b; }
                float n = px.Length * 255f;
                return new Color(r / n, g / n, b / n, 1f);
            }
            catch (UnityException) { return Color.white; }
        }
    }
}
