// SceneryLook.cs - how fire and magic have marked one piece of scenery:
// char and the embers glowing in it, frost, withering, leaves lost, a golden
// sheen, wetness, a sway and a trunk split by lightning. FxFire and FxMagic
// write it, the renderer keeps it with the feature through its stages, and
// the model shader reads it per instance, so marked scenery stays instanced.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    // The four vectors the model shader reads for one instance.
    public struct SceneryInstance
    {
        public Vector4 Mark;    // char, frost, withering, holy sheen
        public Vector4 Heat;    // ember glow, leaves lost, wetness
        public Vector4 Bend;    // the top's swing in x and z, the split, the split's angle
        public Vector4 Foot;    // where it stands, and its height
    }

    public sealed class SceneryLook
    {
        // How long the fading marks last, in battle seconds.
        public const float FrostHold = 8f, FrostSeconds = 60f, WetSeconds = 60f, SheenSeconds = 6f, WitherSeconds = 2.5f;
        public const float ShakeSeconds = 1.4f, ShakeHz = 2.4f, GustSeconds = 2.5f;

        // Where it stands, what it is and how big it is drawn, kept by the renderer.
        public Vector3 Foot;
        public int Def = -1;
        public Bounds Bounds;
        public BreakKind Kind;
        public bool Plant;      // a tree, plant, grass or crop
        public bool Loose;      // rubble an earthquake shakes loose
        public bool Gone;       // the feature has left the field

        // Lasting marks, 0 to 1. The split is in world units at the top.
        public float Char, Bare, Split, SplitAngle;
        // Withering grows to its mark over a few seconds and stays.
        public float WitherTo, WitherAt = float.NegativeInfinity;
        // Fading marks: how strong when made and when, in battle seconds.
        public float FrostPeak, FrostAt, SheenPeak, SheenAt, WetPeak, WetAt;
        public float GlowPeak, GlowAt, GlowFor = 6f;
        // The glow of a fire burning in it now, set by the fire each frame.
        public float FireGlow;
        // A quake's shaking and a gust's push: the top's swing in world units, the way, and when.
        public float ShakeAmp, ShakeAt;
        public Vector2 ShakeWay;
        public float GustAmp, GustAt;
        public Vector2 GustWay;

        // What the shader draws now, from Step.
        public float Frost, Wither, Sheen, Wet, Glow;
        public Vector2 Bend;

        public float Height => Mathf.Max(0.2f, Bounds.max.y - Foot.y);
        public float Width => Mathf.Max(0.3f, Mathf.Max(Bounds.size.x, Bounds.size.z));
        // The middle of what is drawn, where a crown burns and leaves fall from.
        public Vector3 Middle => new Vector3(Bounds.center.x, Foot.y + Height * (Kind == BreakKind.Tree ? 0.65f : 0.5f), Bounds.center.z);

        public bool Shows =>
            Char > 0.002f || Glow > 0.002f || Frost > 0.002f || Wither > 0.002f || Bare > 0.002f || Sheen > 0.002f || Wet > 0.002f ||
            Split > 0.0005f || Bend.sqrMagnitude > 1e-6f;

        // The drawn marks at a battle time. True while any of them still changes.
        public bool Step(float now)
        {
            bool moving = false;
            float t = now - FrostAt;
            Frost = FrostPeak <= 0f ? 0f : FrostPeak * (1f - Smooth(FrostHold, FrostSeconds, t));
            if (FrostPeak > 0f && t < FrostSeconds) moving = true; else if (FrostPeak > 0f) FrostPeak = 0f;
            t = now - WitherAt;
            Wither = WitherTo * Smooth(0f, WitherSeconds, t);
            if (WitherTo > 0f && t < WitherSeconds) moving = true;
            t = now - SheenAt;
            Sheen = SheenPeak <= 0f ? 0f : SheenPeak * Mathf.Exp(-t / (SheenSeconds * 0.2f)) * Mathf.Clamp01(t / 0.15f + 0.2f);
            if (SheenPeak > 0f && t < SheenSeconds) moving = true; else if (SheenPeak > 0f) SheenPeak = 0f;
            t = now - WetAt;
            Wet = WetPeak <= 0f ? 0f : WetPeak * (1f - Smooth(WetSeconds * 0.3f, WetSeconds, t));
            if (WetPeak > 0f && t < WetSeconds) moving = true; else if (WetPeak > 0f) WetPeak = 0f;
            t = now - GlowAt;
            float fading = GlowPeak <= 0f ? 0f : GlowPeak * (1f - Smooth(0f, GlowFor, t));
            if (GlowPeak > 0f && t < GlowFor) moving = true; else if (GlowPeak > 0f) GlowPeak = 0f;
            Glow = Mathf.Max(FireGlow, fading);
            if (FireGlow > 0f) moving = true;

            var bend = Vector2.zero;
            t = now - ShakeAt;
            if (ShakeAmp > 0f)
            {
                if (t < ShakeSeconds * 4f)
                {
                    float a = ShakeAmp * Mathf.Exp(-Mathf.Max(0f, t) / ShakeSeconds);
                    float w = Mathf.Max(0f, t) * ShakeHz * Mathf.PI * 2f;
                    // A quake throws it one way and a little across, never still.
                    bend += ShakeWay * (a * Mathf.Sin(w)) + new Vector2(-ShakeWay.y, ShakeWay.x) * (a * 0.35f * Mathf.Sin(w * 1.7f + 1f));
                    moving = true;
                }
                else ShakeAmp = 0f;
            }
            t = now - GustAt;
            if (GustAmp > 0f)
            {
                if (t < GustSeconds * 3f)
                {
                    float a = GustAmp * Mathf.Exp(-Mathf.Max(0f, t) / GustSeconds) * Mathf.Clamp01(t / 0.3f + 0.1f);
                    bend += GustWay * (a * (0.8f + 0.2f * Mathf.Sin(Mathf.Max(0f, t) * 7.3f)));
                    moving = true;
                }
                else GustAmp = 0f;
            }
            Bend = bend;
            return moving;
        }

        static float Smooth(float from, float to, float t) => to <= from ? (t >= to ? 1f : 0f) : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - from) / (to - from)));

        public void Pack(out SceneryInstance s)
        {
            s.Mark = new Vector4(Char, Frost, Wither, Sheen);
            s.Heat = new Vector4(Glow, Bare, Wet, 0f);
            s.Bend = new Vector4(Bend.x, Bend.y, Split, SplitAngle);
            s.Foot = new Vector4(Foot.x, Foot.y, Foot.z, Height);
        }

        // What it is, for the looks that pick: trees and growing things,
        // and the low wrecks and rubble an earthquake shakes loose.
        public void Classify(FeatureDef d)
        {
            Kind = Fracture.KindOf(d);
            string c = (d?.Category ?? "").ToLowerInvariant(), n = (d?.Name ?? "").ToLowerInvariant();
            Plant = Kind == BreakKind.Tree || Has(c, "plant", "grass", "crop", "bush", "vine", "ivy", "flower", "shrub") ||
                    Has(n, "plant", "grass", "crop", "bush", "ivy", "shrub");
            Loose = Has(c, "rubble", "ruin") || Has(n, "rubble", "ruin", "wreck", "debris", "smudge") ||
                    d != null && d.Height > 0f && d.Height < 0.6f && (Kind == BreakKind.Wall || Kind == BreakKind.Hut || Kind == BreakKind.Building);
        }

        static bool Has(string s, params string[] words)
        {
            foreach (var w in words) if (s.Contains(w)) return true;
            return false;
        }
    }
}
