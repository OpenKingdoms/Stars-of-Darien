// FxQuality.cs - the Battle effects option and the budgets it sets. Every
// part of a battle's look that can grow without bound reads its share here:
// explosions and their particles, breaking scenery, scars and fire.
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Game.World
{
    public enum EffectsQuality { Low, Medium, High, Ultra }

    // What the ground shows of a scar: its colour, its colour lit through
    // normals, or both with the ground mesh dipped by it.
    public enum ScarGround { Colour, Normals, Dips }

    public sealed class FxQuality
    {
        public EffectsQuality Level;
        // Scenery chunks in flight at once, and settled ones kept as rubble.
        public int FlyingChunks, RubbleKept;
        // Point lights the effects may light at once.
        public int Lights;
        // World pixels to a texel of the scar map, 16 to a world unit.
        public int ScarTexelPixels;
        public ScarGround Ground;
        // Low lets scars fade, the rest keep them the whole battle.
        public bool ScarsFade;
        // Particles alive at once, blast debris in flight or settling, and
        // the share of each blast's particles that is made.
        public int Particles, Debris;
        public float Emission;
        // How long smoke lasts against its normal life.
        public float SmokeLife;

        public static FxQuality For(EffectsQuality level)
        {
            switch (level)
            {
                case EffectsQuality.Low:
                    return new FxQuality
                    {
                        Level = level, FlyingChunks = 150, RubbleKept = 300, Lights = 4, ScarTexelPixels = 16, Ground = ScarGround.Colour,
                        ScarsFade = true, Particles = 1000, Debris = 120, Emission = 0.35f, SmokeLife = 0.6f,
                    };
                case EffectsQuality.Medium:
                    return new FxQuality
                    {
                        Level = level, FlyingChunks = 600, RubbleKept = 1500, Lights = 8, ScarTexelPixels = 8, Ground = ScarGround.Normals,
                        Particles = 2500, Debris = 400, Emission = 0.65f, SmokeLife = 0.85f,
                    };
                case EffectsQuality.Ultra:
                    return new FxQuality
                    {
                        Level = level, FlyingChunks = 3000, RubbleKept = 8000, Lights = 48, ScarTexelPixels = 4, Ground = ScarGround.Dips,
                        Particles = 10000, Debris = 1600, Emission = 1.35f, SmokeLife = 1.5f,
                    };
                default:
                    return new FxQuality
                    {
                        Level = EffectsQuality.High, FlyingChunks = 1500, RubbleKept = 4000, Lights = 24, ScarTexelPixels = 4, Ground = ScarGround.Dips,
                        Particles = 5000, Debris = 800, Emission = 1f, SmokeLife = 1f,
                    };
            }
        }

        // The budgets in force. The world sets them from the options when it
        // builds and the Options sheet when the player changes them.
        public static FxQuality Current { get; private set; } = For(EffectsQuality.High);

        public static void Use(EffectsQuality level)
        {
            if (Current.Level != level) Current = For(level);
        }

        // The setting a machine starts on, from its graphics card.
        public static EffectsQuality Detect() =>
            Pick(SystemInfo.graphicsDeviceType, SystemInfo.graphicsMemorySize, SystemInfo.graphicsDeviceName ?? "");

        // Graphics built into the processor, or a card with little memory of
        // its own, get Low. Small cards get Medium and the rest High. Ultra is
        // only ever chosen by the player. A run with no graphics gets High,
        // as there is nothing to judge.
        public static EffectsQuality Pick(GraphicsDeviceType device, int memoryMb, string card)
        {
            if (device == GraphicsDeviceType.Null) return EffectsQuality.High;
            string name = card.ToLowerInvariant();
            if (name.Contains("apple"))
                return name.Contains("pro") || name.Contains("max") || name.Contains("ultra") ? EffectsQuality.High : EffectsQuality.Medium;
            if (name.Contains("intel") || name.Contains("basic render") || name.Contains("llvmpipe") || memoryMb < 1500) return EffectsQuality.Low;
            if (memoryMb < 3500) return EffectsQuality.Medium;
            return EffectsQuality.High;
        }
    }
}
