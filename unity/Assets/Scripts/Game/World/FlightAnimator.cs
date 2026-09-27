// FlightAnimator.cs - when a winged flyer beats its wings and when it
// glides. A small visual offset rides on the engine's altitude inside a
// band. Flapping lifts it at the type's climb rate to the top of the band,
// then the wings settle and it sinks at the type's sink rate, its wing
// loading, to the bottom, and flaps again. Takeoff, climbing over rising
// ground, hovering and slow flight force flapping. Presentation only: the
// engine's position and air state are read, never written.
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public struct FlightInput
    {
        public float Altitude;   // world units above the ground under it
        public float Cruise;     // the type's flying height, 0 when unknown
        public float Speed;      // world units per second
        public float TopSpeed;   // 0 when unknown
        public float WorldY;     // for the climb over rising ground
        public bool Airborne, Hovers, Dying;

        public static FlightInput Of(in UnitState u, UnitDef d) => new FlightInput
        {
            Altitude = u.Altitude, Cruise = d.CruiseAltitude, Speed = u.Speed, TopSpeed = d.MaxSpeed,
            WorldY = u.Position.y, Airborne = (u.Flags & UnitFlags.Airborne) != 0, Hovers = d.Hovers,
            Dying = (u.Flags & UnitFlags.Dying) != 0,
        };
    }

    public enum FlightMode { Ground, Flap, Glide, Land }

    public struct Flyer
    {
        public FlightMode Mode;
        public float Offset;        // world units, inside [Lower, Upper]
        public float Phase;         // 0 to 1 through a beat, 0 at the top of the stroke
        public float Glide;         // 0 the flap pose to 1 the glide pose
        public float Weight;        // 0 the script's pose to 1 the animator's
        public float Lift;          // how much of the offset shows, eased on and off
        public float ClimbRate;     // smoothed world climb, units per second
        public float LastY;
        public float TopSeen;       // fastest speed seen, when the type's is unknown
        public float PeriodScale;   // 1 give or take the jitter, fixed per unit
        public float Seed;          // wobble phase, fixed per unit
        public bool Forced;
    }

    public static class FlightAnimator
    {
        public const float Eps = 0.02f;         // world units, under a third of an engine pixel
        public const float HoverSpeed = 0.2f;   // below this a flyer is hovering, whatever its type

        // A flyer seen for the first time. The phase, place in the band,
        // mode and beat come from its StableId, so a flock is out of step
        // and the same unit always starts the same way.
        public static Flyer Start(uint stableId, FlightType t, in FlightInput i)
        {
            uint x = stableId * 2654435761u + 0x9E3779B9u;
            float R()
            {
                x ^= x << 13; x ^= x >> 17; x ^= x << 5;
                return (x & 0xFFFFFF) / 16777216f;
            }
            float phase = R(), band = R(), mode = R(), jitter = R(), seed = R();
            bool aloft = i.Hovers || i.Airborne;
            var f = new Flyer
            {
                Phase = phase,
                Offset = Mathf.Lerp(t.Lower, t.Upper, band),
                Mode = !aloft ? FlightMode.Ground : mode < t.Sink / (t.Climb + t.Sink) ? FlightMode.Flap : FlightMode.Glide,
                PeriodScale = 1f + t.Jitter * (2f * jitter - 1f),
                Seed = seed * 2f * Mathf.PI,
                LastY = i.WorldY,
                Weight = aloft ? 1f : 0f,
                Lift = aloft ? 1f : 0f,
            };
            f.Glide = f.Mode == FlightMode.Glide ? 1f : 0f;
            return f;
        }

        public static void Step(ref Flyer f, in FlightInput i, FlightType t, float dt)
        {
            if (!(dt > 0f)) return;                        // paused: nothing moves
            dt = Mathf.Min(dt, 0.1f);                      // a unit back from the fog does not jump

            float vy = (i.WorldY - f.LastY) / dt;
            f.LastY = i.WorldY;
            f.ClimbRate += (vy - f.ClimbRate) * (1f - Mathf.Exp(-dt / 0.3f));

            bool ground = i.Dying || (!i.Hovers && !i.Airborne && i.Altitude <= Eps);
            bool landing = !i.Hovers && !i.Airborne && i.Altitude > Eps;
            bool takeoff = i.Airborne && i.Altitude < i.Cruise - Eps;
            f.TopSeen = Mathf.Max(f.TopSeen, i.Speed);
            float top = i.TopSpeed > 0f ? i.TopSpeed : f.TopSeen;
            float stall = Mathf.Max(t.Stall * top, HoverSpeed);
            f.Forced = takeoff || i.Speed < stall || f.ClimbRate > t.ClimbForce;

            float target = 1f;
            if (ground) { f.Mode = FlightMode.Ground; target = 0f; if (i.Dying) f.Weight = 0f; }
            else if (landing) { f.Mode = FlightMode.Land; target = 0f; }
            else
            {
                if (f.Mode == FlightMode.Ground || f.Mode == FlightMode.Land) f.Mode = FlightMode.Flap;
                float prev = f.Phase;
                float period = t.Period * f.PeriodScale * (f.Forced ? t.ForcedPeriod : 1f);
                f.Phase = Mathf.Repeat(f.Phase + dt / period, 1f);
                if (f.Mode == FlightMode.Flap)
                {
                    f.Offset = Mathf.Min(f.Offset + t.Climb * dt, t.Upper);
                    // Settle into the glide only as the wings pass level on a downstroke.
                    if (!f.Forced && f.Offset >= t.Upper && Crossed(prev, f.Phase, t.Downstroke * 0.5f))
                        f.Mode = FlightMode.Glide;
                }
                else
                {
                    float k = Mathf.Clamp01((i.Speed - stall) / Mathf.Max(1e-3f, top - stall));
                    f.Offset = Mathf.Max(f.Offset - t.Sink * Mathf.Lerp(t.SinkSlow, 1f, k) * dt, t.Lower);
                    if (f.Forced || f.Offset <= t.Lower)
                    {
                        f.Mode = FlightMode.Flap;
                        f.Phase = (1f + t.Downstroke) * 0.5f;   // mid upstroke, wings near level
                    }
                }
            }
            f.Glide = Mathf.MoveTowards(f.Glide, f.Mode == FlightMode.Glide ? 1f : 0f, dt / Mathf.Max(1e-4f, t.Ease));
            f.Weight = Mathf.MoveTowards(f.Weight, target, dt / Mathf.Max(1e-4f, t.Blend));
            f.Lift = Mathf.MoveTowards(f.Lift, ground || landing ? 0f : 1f, dt / Mathf.Max(1e-4f, t.Blend));
        }

        // Whether a phase stepping from prev to now passed at, wrapping at 1.
        public static bool Crossed(float prev, float now, float at) =>
            prev <= now ? prev < at && at <= now : prev < at || at <= now;

        // How far above the engine's height to draw the flyer. It grows
        // through takeoff and shrinks to 0 at touchdown, never below ground.
        public static float VisualOffset(in Flyer f, in FlightInput i)
        {
            float k = i.Hovers || i.Cruise <= Eps ? 1f : Mathf.Clamp01(i.Altitude / i.Cruise);
            return Mathf.Max(f.Offset * k * f.Lift, -i.Altitude);
        }
    }
}
