// TrailerKitTests.cs - the trailer director's camera moves and sound
// placement, without the engine.
using NUnit.Framework;
using UnityEngine;

namespace OpenKingdomsUnity.Tests.Trailer
{
    public class TrailerKitTests
    {
        [Test]
        public void EasingStartsAndStopsStill()
        {
            Assert.AreEqual(0f, TrailerKit.Ease(0f), 1e-6f);
            Assert.AreEqual(1f, TrailerKit.Ease(1f), 1e-6f);
            Assert.AreEqual(0.5f, TrailerKit.Ease(0.5f), 1e-6f);
            // Barely moving in the first and last hundredth.
            Assert.Less(TrailerKit.Ease(0.01f), 1e-4f);
            Assert.Greater(TrailerKit.Ease(0.99f), 1f - 1e-4f);
            Assert.AreEqual(0f, TrailerKit.Ease(-1f));
            Assert.AreEqual(1f, TrailerKit.Ease(2f));
        }

        [Test]
        public void APushInKeepsOnePaceByRatio()
        {
            var a = new ShotPose(Vector3.zero, 80f, 30f, 0f);
            var b = new ShotPose(Vector3.zero, 20f, 30f, 0f);
            Assert.AreEqual(40f, ShotPose.Lerp(a, b, 0.5f).Distance, 1e-3f);
        }

        [Test]
        public void YawTurnsTheShortWayRound()
        {
            var a = new ShotPose(Vector3.zero, 30f, 30f, 350f);
            var b = new ShotPose(Vector3.zero, 30f, 30f, 10f);
            float mid = Mathf.Repeat(ShotPose.Lerp(a, b, 0.5f).Yaw, 360f);
            Assert.That(mid < 0.01f || mid > 359.99f, "yaw went the long way: " + mid);
        }

        [Test]
        public void AHoldNeverMoves()
        {
            var p = new ShotPose(new Vector3(3, 1, -4), 25f, 15f, 120f);
            var hold = TrailerKit.Hold(p);
            Assert.AreEqual(p.Focus, hold(0.7f).Focus);
            Assert.AreEqual(p.Yaw, hold(1f).Yaw);
        }

        [Test]
        public void SoundsInViewPlayFullAndPanAcrossIt()
        {
            TrailerKit.Spatial(new Vector3(0.5f, 0.5f, 10f), out int vol, out int pan);
            Assert.AreEqual(0x7f, vol);
            Assert.AreEqual(64, pan);
            TrailerKit.Spatial(new Vector3(1f, 0.5f, 10f), out vol, out pan);
            Assert.AreEqual(0x7f, vol);
            Assert.AreEqual(96, pan);
            TrailerKit.Spatial(new Vector3(0f, 0.5f, 10f), out _, out pan);
            Assert.AreEqual(32, pan);
        }

        [Test]
        public void SoundsOutOfViewPlayAtHalf()
        {
            TrailerKit.Spatial(new Vector3(1.6f, 0.5f, 10f), out int vol, out int pan);
            Assert.AreEqual(0x40, vol);
            Assert.AreEqual(127, pan);
            // Behind the camera the view's left and right swap.
            TrailerKit.Spatial(new Vector3(0.1f, 0.5f, -5f), out vol, out pan);
            Assert.AreEqual(0x40, vol);
            Assert.Greater(pan, 64);
        }

        [Test]
        public void FramesAndSamplesLineUp()
        {
            Assert.AreEqual(0, TrailerKit.SampleOf(0, 60, 48000));
            Assert.AreEqual(800, TrailerKit.SampleOf(1, 60, 48000));
            Assert.AreEqual(48000L * 90, TrailerKit.SampleOf(60 * 90, 60, 48000));
        }

        [Test]
        public void RanksStandAcrossTheHeading()
        {
            var spots = TrailerDirector.Rows(6, Vector3.zero, 0f, 3, 2f);
            Assert.AreEqual(6, spots.Length);
            // Facing north (+z): the front rank is ahead of the back one, and
            // each rank runs west to east about the centre.
            Assert.Greater(spots[0].y, spots[3].y);
            Assert.AreEqual(-2f, spots[0].x, 1e-4f);
            Assert.AreEqual(2f, spots[2].x, 1e-4f);
            Assert.AreEqual(0f, (spots[0].y + spots[3].y) * 0.5f, 1e-4f);
        }
    }
}
