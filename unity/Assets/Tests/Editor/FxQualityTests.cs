using NUnit.Framework;
using OpenKingdomsUnity.Game.World;
using UnityEngine.Rendering;

namespace OpenKingdomsUnity.Tests
{
    public class FxQualityTests
    {
        [Test]
        public void EachSettingHasThePlansBudgets()
        {
            var want = new[]
            {
                (EffectsQuality.Low, 150, 300, 4, 16, ScarGround.Colour, true),
                (EffectsQuality.Medium, 600, 1500, 8, 8, ScarGround.Normals, false),
                (EffectsQuality.High, 1500, 4000, 24, 4, ScarGround.Dips, false),
                (EffectsQuality.Ultra, 3000, 8000, 48, 4, ScarGround.Dips, false),
            };
            foreach (var (level, chunks, rubble, lights, texel, ground, fade) in want)
            {
                var q = FxQuality.For(level);
                Assert.AreEqual(level, q.Level);
                Assert.AreEqual(chunks, q.FlyingChunks, level + " chunks");
                Assert.AreEqual(rubble, q.RubbleKept, level + " rubble");
                Assert.AreEqual(lights, q.Lights, level + " lights");
                Assert.AreEqual(texel, q.ScarTexelPixels, level + " scar texels");
                Assert.AreEqual(ground, q.Ground, level + " ground");
                Assert.AreEqual(fade, q.ScarsFade, level + " scars fade");
            }
        }

        [Test]
        public void EachStepUpShowsMore()
        {
            for (var level = EffectsQuality.Low; level < EffectsQuality.Ultra; level++)
            {
                FxQuality a = FxQuality.For(level), b = FxQuality.For(level + 1);
                Assert.Less(a.Particles, b.Particles, level + " particles");
                Assert.Less(a.Debris, b.Debris, level + " debris");
                Assert.Less(a.Emission, b.Emission, level + " emission");
                Assert.Less(a.SmokeLife, b.SmokeLife, level + " smoke");
            }
        }

        [Test]
        public void AWeakCardStartsOnLowAndOnlyThePlayerPicksUltra()
        {
            var dx = GraphicsDeviceType.Direct3D11;
            Assert.AreEqual(EffectsQuality.Low, FxQuality.Pick(dx, 1024, "Intel(R) UHD Graphics 620"));
            Assert.AreEqual(EffectsQuality.Low, FxQuality.Pick(dx, 512, "AMD Radeon(TM) Graphics"));
            Assert.AreEqual(EffectsQuality.Low, FxQuality.Pick(dx, 0, "Microsoft Basic Render Driver"));
            Assert.AreEqual(EffectsQuality.Medium, FxQuality.Pick(dx, 2048, "NVIDIA GeForce GTX 1050"));
            Assert.AreEqual(EffectsQuality.High, FxQuality.Pick(dx, 8192, "NVIDIA GeForce RTX 3070"));
            Assert.AreEqual(EffectsQuality.High, FxQuality.Pick(dx, 24576, "NVIDIA GeForce RTX 4090"), "never Ultra by itself");
            Assert.AreEqual(EffectsQuality.Medium, FxQuality.Pick(GraphicsDeviceType.Metal, 5461, "Apple M1"));
            Assert.AreEqual(EffectsQuality.High, FxQuality.Pick(GraphicsDeviceType.Metal, 21845, "Apple M2 Pro"));
            Assert.AreEqual(EffectsQuality.High, FxQuality.Pick(GraphicsDeviceType.Null, 0, "Null Device"), "nothing to judge without graphics");
        }

        [Test]
        public void TheSettingInForceFollowsTheOption()
        {
            var before = FxQuality.Current.Level;
            try
            {
                FxQuality.Use(EffectsQuality.Low);
                Assert.AreEqual(4, FxQuality.Current.Lights);
                FxQuality.Use(EffectsQuality.Ultra);
                Assert.AreEqual(48, FxQuality.Current.Lights);
            }
            finally
            {
                FxQuality.Use(before);
            }
        }
    }
}
