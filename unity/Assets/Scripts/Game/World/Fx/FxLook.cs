// FxLook.cs - the original's rules for how effects look, as plain data and
// pure functions: which art it adds and which it alpha blends, how long a
// frame shows, the beams' colours and jag, and how strong a light is.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public static class FxLook
    {
        // The original counts frame times in 30 Hz game frames.
        public const int LegacyHz = 30;
        public const int DefaultDuration = 2;

        // Ticks a frame of `duration` legacy frames lasts at the backend's rate.
        public static int FrameTicks(int duration, int ticksPerSecond) =>
            Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, duration) * ticksPerSecond / (float)LegacyHz));

        // The retail art by cell size and frame count, from the TAF headers:
        // '+' adds (frame byte 0xb not 0xff, legacy:209700), '-' alpha blends,
        // then the frame time when not 2. Stands in until the engine reports them.
        const string Known =
            "6x6x4- 9x9x4- 10x54x10+ 12x12x10+ 13x12x4- 14x43x22+ 18x17x10+ 18x19x8+ 18x23x6- 19x19x6- 19x20x15+ " +
            "20x19x11+ 20x65x29+ 23x23x10+ 23x25x10+ 26x38x10- 28x73x21+ 28x89x30+ 29x26x19+ 33x41x13+ 34x31x10+ " +
            "34x32x8+ 34x34x10+ 36x34x12+ 38x36x11+ 39x39x10+ 39x43x13+ 40x34x20- 40x44x18+ 41x53x6+ 42x42x10+ " +
            "42x47x12+ 43x46x20- 44x47x12+ 47x109x36+ 49x55x12+ 50x36x4- 50x37x4- 51x46x20+ 51x53x11- 52x47x11+3 " +
            "53x51x18+ 55x50x12+ 55x59x12+ 55x60x7+ 55x71x14+ 56x44x22+ 56x84x10- 57x46x17+ 57x50x12+ 58x59x12- " +
            "58x70x13+ 59x45x16+ 59x120x33+ 59x128x22+ 60x57x18+ 60x66x16+ 61x61x12- 61x63x21+ 62x64x12- " +
            "62x66x11+ 62x67x22+ 63x61x18+ 63x62x12- 63x67x12- 64x64x12+ 64x66x12- 64x68x12+ 65x58x19+ 65x63x12+ " +
            "65x65x10+ 66x64x12+ 67x66x12- 68x68x12- 68x85x14+ 69x63x15+ 69x66x12- 69x67x12+ 69x67x15+ 70x93x14- " +
            "71x138x36+ 72x45x16+ 73x93x15- 74x59x31+1 77x81x6+ 79x62x13+1 79x136x31+ 79x142x31+ 82x162x32+ " +
            "85x75x14+ 88x102x19- 91x71x19+ 95x72x30+ 96x116x14- 97x51x26+ 99x78x14- 100x72x20- 100x101x15- " +
            "101x104x11+ 101x114x15- 102x99x12+ 102x109x12- 103x103x12+ 103x110x11+ 104x78x20- 104x95x20- " +
            "105x102x10+ 106x108x19+ 106x115x14- 106x115x20- 107x110x15+ 108x105x15+ 113x101x14+ 113x103x10+ " +
            "115x111x15- 116x120x29+ 117x94x14+ 118x115x15- 119x100x20+ 122x111x19+ 128x126x15- 128x128x15+";

        public struct ArtRule
        {
            public bool Additive;
            public int Duration;    // legacy frames
        }

        static Dictionary<(int, int, int), ArtRule> known;

        // The retail rule for a strip of `frames` cells each cellW by cellH
        // pixels. False for art the table does not know, such as a mod's.
        public static bool KnownArt(int cellW, int cellH, int frames, out ArtRule rule)
        {
            if (known == null)
            {
                known = new Dictionary<(int, int, int), ArtRule>();
                foreach (var item in Known.Split(' '))
                {
                    int sign = Mathf.Max(item.IndexOf('+'), item.IndexOf('-'));
                    var dims = item.Substring(0, sign).Split('x');
                    int dur = sign + 1 < item.Length ? int.Parse(item.Substring(sign + 1)) : DefaultDuration;
                    known[(int.Parse(dims[0]), int.Parse(dims[1]), int.Parse(dims[2]))] =
                        new ArtRule { Additive = item[sign] == '+', Duration = dur };
                }
            }
            return known.TryGetValue((cellW, cellH, frames), out rule);
        }

        // ── Beams ─────────────────────────────────────────────────────

        // The hweffect colours from the weapons: inner, middle, outer.
        public static void BeamColours(BeamKind kind, out Color32 inner, out Color32 middle, out Color32 outer)
        {
            switch (kind)
            {
                case BeamKind.CreonLightning:
                    inner = new Color32(255, 255, 255, 255); middle = new Color32(230, 230, 255, 255); outer = new Color32(200, 200, 255, 255); break;
                case BeamKind.CreonParalyzer:
                    inner = new Color32(0, 255, 128, 255); middle = new Color32(24, 230, 128, 255); outer = new Color32(48, 200, 128, 255); break;
                case BeamKind.CreonLightbeam:
                    inner = new Color32(255, 255, 150, 255); middle = new Color32(230, 230, 145, 255); outer = new Color32(200, 200, 140, 255); break;
                case BeamKind.Fire:
                    inner = new Color32(255, 240, 180, 255); middle = new Color32(255, 170, 60, 255); outer = new Color32(220, 80, 20, 255); break;
                default:
                    inner = new Color32(255, 255, 255, 255); middle = new Color32(200, 230, 255, 255); outer = new Color32(180, 200, 255, 255); break;
            }
        }

        public const int BeamSegments = 10;
        // The classic client throws each inner point up to 6 pixels aside.
        public const float BeamJag = 6f / 16f;
        // A new jag every 30 Hz frame, as the original draws.
        public static int JagStep(uint tick, int ticksPerSecond) => (int)(tick * LegacyHz / (uint)Mathf.Max(1, ticksPerSecond));

        // How far point s of a beam stands aside, -1 to 1: 0 at both ends,
        // steady for a seed and a step, fresh at the next step.
        public static float Jag(int seed, int step, int s)
        {
            if (s <= 0 || s >= BeamSegments) return 0f;
            uint h = (uint)seed * 2654435761u ^ (uint)step * 40503u ^ (uint)s * 97u;
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return (h % 10000u) / 5000f - 1f;
        }

        // A creon lightbeam is a straight shaft. The others crackle.
        public static bool Straight(BeamKind kind) => kind == BeamKind.CreonLightbeam;

        // ── Light ─────────────────────────────────────────────────────

        // Reach in world units and brightness of a light of each size.
        public static void LightSize(FxLight size, out float range, out float intensity)
        {
            switch (size)
            {
                case FxLight.Small: range = 5f; intensity = 2.5f; break;
                case FxLight.Medium: range = 7.5f; intensity = 3.5f; break;
                case FxLight.Large: range = 10f; intensity = 5f; break;
                default: range = 0f; intensity = 0f; break;
            }
        }

        // The light an added picture casts when the backend does not say:
        // by how big and how bright the art is. A shot glows a little, a
        // blast more, dim or small art not at all.
        public static FxLight AutoLight(bool additive, bool shot, float heightUnits, float brightness)
        {
            if (!additive || brightness < 0.12f) return FxLight.None;
            if (shot) return heightUnits >= 1.5f || brightness > 0.3f ? FxLight.Small : FxLight.None;
            if (heightUnits >= 5.5f) return FxLight.Large;
            if (heightUnits >= 3f) return FxLight.Medium;
            return heightUnits >= 1.6f ? FxLight.Small : FxLight.None;
        }

        // The rise and fall of an impact's light over its life, 0 to 1.
        public static float LightEnvelope(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.08f ? t / 0.08f : Mathf.Pow(1f - (t - 0.08f) / 0.92f, 1.6f);
        }

        // ── Scorch ────────────────────────────────────────────────────

        // An impact leaves a mark on the ground when it is a blast of fire
        // at least this tall, in world units.
        public const float ScorchHeight = 3f;
        public const float ScorchSeconds = 14f;
    }
}
