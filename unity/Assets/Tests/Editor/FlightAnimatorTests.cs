// FlightAnimatorTests.cs - the flap and glide band: a flyer flaps up to
// the top of its band and glides down to the bottom at its sink rate,
// heavy types flap far more than light ones, and takeoff, slow flight,
// rising ground and hovering force the wings to beat.
using System.Collections.Generic;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class FlightAnimatorTests
    {
        const float Dt = 1f / 60f, Cruise = 6f, Top = 8f, Ground = 10f;

        static FlightType Type(string name) => FlightTableTests.Table().Find(name, null);

        static FlightInput Aloft(float speed = Top) => new FlightInput
        {
            Altitude = Cruise, Cruise = Cruise, Speed = speed, TopSpeed = Top, WorldY = Ground + Cruise, Airborne = true,
        };

        static FlightInput OnGround() => new FlightInput { Cruise = Cruise, TopSpeed = Top, WorldY = Ground };

        // The share of steps spent flapping over some seconds.
        static float FlapShare(FlightType t, ref Flyer f, FlightInput i, float seconds)
        {
            int flap = 0, steps = Mathf.RoundToInt(seconds / Dt);
            for (int s = 0; s < steps; s++)
            {
                FlightAnimator.Step(ref f, i, t, Dt);
                if (f.Mode == FlightMode.Flap) flap++;
                Assert.That(f.Offset, Is.InRange(t.Lower - 1e-5f, t.Upper + 1e-5f));
            }
            return (float)flap / steps;
        }

        [Test]
        public void CruiseAlternatesFlapAndGlideAtTheSinkShare()
        {
            var t = Type("quick");
            var f = FlightAnimator.Start(7, t, Aloft());
            float share = FlapShare(t, ref f, Aloft(), 120f);
            Assert.AreEqual(t.Sink / (t.Climb + t.Sink), share, 0.05f);
        }

        [Test]
        public void APegasusFlapsMoreThanASpyhawk()
        {
            var heavy = Type("arafly");
            var light = Type("arafast");
            var a = FlightAnimator.Start(11, heavy, Aloft());
            var b = FlightAnimator.Start(11, light, Aloft());
            float pegasus = FlapShare(heavy, ref a, Aloft(), 120f), spyhawk = FlapShare(light, ref b, Aloft(), 120f);
            Assert.Greater(pegasus, 0.7f, "a heavy flyer beats its wings most of the time");
            Assert.Less(spyhawk, 0.4f, "a light flyer glides most of the time");
            Assert.Greater(pegasus - spyhawk, 0.35f);
        }

        [Test]
        public void BelowStallItNeverGlides()
        {
            var t = Type("arafast");
            var f = FlightAnimator.Start(3, t, Aloft(0f));
            for (int s = 0; s < 30 * 60; s++)
            {
                FlightAnimator.Step(ref f, Aloft(0f), t, Dt);
                Assert.AreEqual(FlightMode.Flap, f.Mode);
                Assert.IsTrue(f.Forced);
            }
            Assert.AreEqual(t.Upper, f.Offset, 1e-5f, "it holds at the top of the band");
            // At speed again it glides off from the top within a beat.
            for (int s = 0; s < Mathf.CeilToInt(t.Period * 1.2f / Dt) && f.Mode == FlightMode.Flap; s++)
                FlightAnimator.Step(ref f, Aloft(), t, Dt);
            Assert.AreEqual(FlightMode.Glide, f.Mode);
            Assert.Greater(f.Offset, t.Upper - 0.02f);
        }

        [Test]
        public void TakeoffForcesFlapAndBlendsIn()
        {
            var t = Type("zonharp");
            var f = FlightAnimator.Start(5, t, OnGround());
            FlightAnimator.Step(ref f, OnGround(), t, Dt);
            Assert.AreEqual(FlightMode.Ground, f.Mode);
            Assert.AreEqual(0f, f.Weight);
            float alt = 0f;
            for (float time = 0f; time < t.Blend + 2 * Dt; time += Dt)
            {
                alt += 4f * Dt;
                var i = new FlightInput { Altitude = alt, Cruise = Cruise, Speed = 2f, TopSpeed = Top, WorldY = Ground + alt, Airborne = true };
                FlightAnimator.Step(ref f, i, t, Dt);
                Assert.AreEqual(FlightMode.Flap, f.Mode);
                Assert.IsTrue(f.Forced);
            }
            Assert.AreEqual(1f, f.Weight, 1e-5f);
        }

        [Test]
        public void LandingHandsBackToTheScript()
        {
            var t = Type("zonharp");
            var f = FlightAnimator.Start(9, t, Aloft());
            for (int s = 0; s < 60; s++) FlightAnimator.Step(ref f, Aloft(), t, Dt);
            float alt = Cruise, elapsed = 0f;
            FlightInput i = default;
            while (alt > 0f)
            {
                alt = Mathf.Max(0f, alt - 4f * Dt);
                elapsed += Dt;
                i = new FlightInput { Altitude = alt, Cruise = Cruise, Speed = 0f, TopSpeed = Top, WorldY = Ground + alt };
                FlightAnimator.Step(ref f, i, t, Dt);
                if (alt > FlightAnimator.Eps) Assert.AreEqual(FlightMode.Land, f.Mode);
                if (elapsed > t.Blend + Dt) Assert.AreEqual(0f, f.Weight, 1e-5f, "the script's land plays");
            }
            Assert.AreEqual(0f, FlightAnimator.VisualOffset(f, i), 1e-6f);
            FlightAnimator.Step(ref f, i, t, Dt);
            Assert.AreEqual(FlightMode.Ground, f.Mode);
        }

        [Test]
        public void AnUnknownCruiseStillSettlesOnTheGround()
        {
            // A flyer whose type reports no cruise height flies at ground level.
            var t = Type("arafly");
            var air = new FlightInput { Speed = Top, TopSpeed = Top, WorldY = Ground, Airborne = true };
            var f = FlightAnimator.Start(4, t, air);
            for (int s = 0; s < 120; s++) FlightAnimator.Step(ref f, air, t, Dt);
            Assert.GreaterOrEqual(FlightAnimator.VisualOffset(f, air), 0f, "never below the ground");
            var down = new FlightInput { TopSpeed = Top, WorldY = Ground };
            for (int s = 0; s < Mathf.CeilToInt(t.Blend / Dt) + 1; s++) FlightAnimator.Step(ref f, down, t, Dt);
            Assert.AreEqual(0f, FlightAnimator.VisualOffset(f, down), 1e-6f, "on the ground it stands on the ground");
        }

        [Test]
        public void RisingGroundForcesFlap()
        {
            var t = Type("arafast");
            var f = FlightAnimator.Start(12, t, Aloft());
            float y = Ground + Cruise;
            for (int s = 0; s < 5 * 60; s++)
            {
                y += 1f * Dt;
                var i = Aloft();
                i.WorldY = y;
                FlightAnimator.Step(ref f, i, t, Dt);
                if (s > 60)
                {
                    Assert.IsTrue(f.Forced);
                    Assert.AreEqual(FlightMode.Flap, f.Mode);
                }
            }
        }

        [Test]
        public void PausedMeansFrozen()
        {
            var t = Type("zonharp");
            var f = FlightAnimator.Start(21, t, Aloft());
            for (int s = 0; s < 100; s++) FlightAnimator.Step(ref f, Aloft(), t, Dt);
            var before = f;
            FlightAnimator.Step(ref f, Aloft(), t, 0f);
            Assert.AreEqual(before, f);
        }

        [Test]
        public void GlideStartsAtLevelWings()
        {
            var t = Type("zonharp");
            var f = FlightAnimator.Start(30, t, Aloft());
            f.Mode = FlightMode.Flap;
            for (int s = 0; s < 60 * 60; s++)
            {
                float prev = f.Phase;
                var was = f.Mode;
                FlightAnimator.Step(ref f, Aloft(), t, Dt);
                if (was != FlightMode.Flap || f.Mode != FlightMode.Glide) continue;
                Assert.IsTrue(FlightAnimator.Crossed(prev, f.Phase, t.Downstroke * 0.5f), $"{prev} to {f.Phase}");
                Assert.AreEqual(0.5f, FlightPose.Wave(f.Phase, t.Downstroke), 0.1f, "halfway through the stroke");
                return;
            }
            Assert.Fail("it never glided");
        }

        [Test]
        public void FlocksDoNotSync()
        {
            var t = Type("zonharp");
            var phases = new HashSet<float>();
            var offsets = new HashSet<float>();
            for (uint id = 1; id <= 50; id++)
            {
                var f = FlightAnimator.Start(id, t, Aloft());
                phases.Add(f.Phase);
                offsets.Add(f.Offset);
                Assert.AreEqual(f, FlightAnimator.Start(id, t, Aloft()), "the same unit starts the same way");
            }
            Assert.AreEqual(50, phases.Count);
            Assert.AreEqual(50, offsets.Count);
        }

        [Test]
        public void SinkRisesTowardStall()
        {
            var t = Type("zonharp");
            float stall = t.Stall * Top;
            Assert.AreEqual(t.Sink * t.SinkSlow, SinkRate(t, stall), 0.002f, "at stall speed");
            Assert.AreEqual(t.Sink, SinkRate(t, Top), 0.002f, "at top speed");
        }

        static float SinkRate(FlightType t, float speed)
        {
            var f = new Flyer { Mode = FlightMode.Glide, Offset = t.Upper, Glide = 1f, Weight = 1f, Lift = 1f, LastY = Ground + Cruise, PeriodScale = 1f };
            const float seconds = 0.5f;
            for (int s = 0; s < Mathf.RoundToInt(seconds / Dt); s++) FlightAnimator.Step(ref f, Aloft(speed), t, Dt);
            Assert.AreEqual(FlightMode.Glide, f.Mode);
            return (t.Upper - f.Offset) / seconds;
        }

        [Test]
        public void DyingHandsBackAtOnce()
        {
            var t = Type("zonharp");
            var f = FlightAnimator.Start(8, t, Aloft());
            FlightAnimator.Step(ref f, Aloft(), t, Dt);
            Assert.AreEqual(1f, f.Weight);
            var dying = Aloft();
            dying.Dying = true;
            FlightAnimator.Step(ref f, dying, t, Dt);
            Assert.AreEqual(0f, f.Weight);
        }

        [Test]
        public void HoverOnlyFlyersNeverLand()
        {
            var t = Type("arafast");
            var hover = new FlightInput { Altitude = Cruise, Cruise = Cruise, TopSpeed = Top, WorldY = Ground + Cruise, Hovers = true };
            var f = FlightAnimator.Start(2, t, hover);
            for (int s = 0; s < 30 * 60; s++)
            {
                FlightAnimator.Step(ref f, hover, t, Dt);
                Assert.AreEqual(FlightMode.Flap, f.Mode);
            }
            Assert.AreEqual(1f, f.Weight);
        }
    }
}
