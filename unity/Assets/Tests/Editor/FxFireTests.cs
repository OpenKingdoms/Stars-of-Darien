// FxFireTests.cs - the shape of a fire and the marks fire and magic leave
// on scenery, without a battle: a fire takes hold, burns and dies down,
// a forest keeps to its share of the particles, rain shortens flames and
// whitens smoke, frost melts over a minute, withering stays, a sheen and a
// shake fade, and scenery is told apart for the looks that pick.
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class FxFireTests
    {
        [Test]
        public void AFireTakesHoldBurnsAndDiesDown()
        {
            Assert.AreEqual(0f, FxFire.Intensity(0f));
            Assert.Less(FxFire.Intensity(0.03f), 0.5f, "it takes a moment to take hold");
            Assert.AreEqual(1f, FxFire.Intensity(0.4f));
            Assert.Less(FxFire.Intensity(0.99f), 0.5f, "it dies down before its burnt stage");
            Assert.Less(FxFire.Coverage(0f), 0.3f, "it starts where it caught");
            Assert.AreEqual(1f, FxFire.Coverage(0.5f), 1e-4f, "and covers the model");
            Assert.Less(FxFire.CharAt(0.1f), 0.1f);
            Assert.Greater(FxFire.CharAt(1f), 0.85f, "the model is charred through by the end");
            Assert.AreEqual(0f, FxFire.BareAt(0.2f), "a crown keeps its leaves at first");
            Assert.Greater(FxFire.BareAt(1f), 0.8f, "and has lost most by the end");
            Assert.Greater(FxFire.GlowAt(0.8f), 0.8f, "embers glow in the char");
        }

        [Test]
        public void AForestKeepsToItsShareOfTheParticles()
        {
            foreach (var level in new[] { EffectsQuality.Low, EffectsQuality.Medium, EffectsQuality.High, EffectsQuality.Ultra })
            {
                int cap = FxQuality.For(level).Particles;
                Assert.AreEqual(1f, FxFire.BudgetScale(cap * 0.1f, cap), level + ": a few fires make all they ask");
                // Four hundred trees burning at full, each asking for its flames, embers and glow.
                float asked = 400 * (FxFire.FlameRate * FxFire.FlameLife + FxFire.TongueRate * FxFire.TongueLife + FxFire.EmberRate * FxFire.EmberLife + FxFire.HaloRate * FxFire.HaloLife);
                float made = asked * FxFire.BudgetScale(asked, cap);
                Assert.LessOrEqual(made, FxFire.ParticleShare * cap + 0.5f, level + ": a forest keeps to its share");
                Assert.Greater(made, FxFire.ParticleShare * cap * 0.99f, level + ": and uses it");
            }
        }

        [Test]
        public void RainShortensTheFlamesAndWhitensTheSmoke()
        {
            Assert.AreEqual(1f, FxFire.FlameLifeIn(WeatherChoice.Off));
            Assert.Less(FxFire.FlameLifeIn(WeatherChoice.Rain), 0.6f);
            Assert.Less(FxFire.FlameLifeIn(WeatherChoice.Rain), FxFire.FlameLifeIn(WeatherChoice.Snow));
            Assert.AreEqual(0f, FxFire.SteamIn(WeatherChoice.Off));
            Assert.Greater(FxFire.SteamIn(WeatherChoice.Rain), 0.7f);
        }

        [Test]
        public void FrostRimesAndMeltsOverAMinute()
        {
            var look = new SceneryLook { FrostPeak = 1f, FrostAt = 10f };
            Assert.IsTrue(look.Step(11f));
            Assert.AreEqual(1f, look.Frost, 1e-4f, "white at first");
            look.Step(40f);
            Assert.Greater(look.Frost, 0.1f);
            Assert.Less(look.Frost, 0.9f, "melting by half a minute");
            Assert.IsFalse(look.Step(71f), "settled after a minute");
            Assert.AreEqual(0f, look.Frost);
            Assert.IsFalse(look.Shows);
        }

        [Test]
        public void WitheringGrowsAndStays()
        {
            var look = new SceneryLook { WitherTo = 0.9f, WitherAt = 5f };
            look.Step(5.5f);
            Assert.Greater(look.Wither, 0f);
            Assert.Less(look.Wither, 0.9f, "it creeps in");
            Assert.IsFalse(look.Step(600f));
            Assert.AreEqual(0.9f, look.Wither, 1e-4f, "and stays the battle");
            Assert.IsTrue(look.Shows);
        }

        [Test]
        public void ASheenAWettingAndAShakeAllFade()
        {
            var look = new SceneryLook { SheenPeak = 1f, SheenAt = 0f, WetPeak = 1f, WetAt = 0f, ShakeAmp = 0.5f, ShakeAt = 0f, ShakeWay = Vector2.right };
            look.Step(0.4f);
            Assert.Greater(look.Sheen, 0.4f);
            Assert.Greater(look.Wet, 0.9f);
            Assert.Greater(look.Bend.magnitude, 0.01f, "the quake throws it about");
            look.Step(8f);
            Assert.Less(look.Sheen, 0.05f, "the sheen is brief");
            Assert.Greater(look.Wet, 0.5f, "the wet stays a while");
            Assert.IsFalse(look.Step(70f));
            Assert.AreEqual(0f, look.Wet);
            Assert.AreEqual(Vector2.zero, look.Bend);
        }

        [Test]
        public void AGlowFromAFireHoldsWhileItBurnsAndDiesAfter()
        {
            var look = new SceneryLook { FireGlow = 0.8f };
            Assert.IsTrue(look.Step(1f));
            Assert.AreEqual(0.8f, look.Glow, 1e-4f);
            look.FireGlow = 0f;
            look.GlowPeak = 0.8f;
            look.GlowAt = 1f;
            look.GlowFor = 20f;
            look.Step(11f);
            Assert.Greater(look.Glow, 0.1f);
            Assert.IsFalse(look.Step(30f));
            Assert.AreEqual(0f, look.Glow);
        }

        [Test]
        public void SceneryIsToldApartForTheLooks()
        {
            SceneryLook Of(string name, string category, float height = 2f)
            {
                var l = new SceneryLook();
                l.Classify(new FeatureDef { Name = name, Category = category, Height = height, HitPoints = 100 });
                return l;
            }
            var tree = Of("AraTree01", "trees");
            Assert.AreEqual(BreakKind.Tree, tree.Kind);
            Assert.IsTrue(tree.Plant);
            Assert.IsTrue(Of("mock_bush", "plants").Plant, "a bush grows");
            Assert.IsFalse(Of("AraRock01", "rocks").Plant, "a rock does not");
            Assert.IsTrue(Of("mock_rubble", "walls", 0.4f).Loose, "rubble shakes loose");
            Assert.IsFalse(Of("mock_wall", "walls", 2f).Loose, "a standing wall does not");
        }

        [Test]
        public void EachKindOfMagicReachesScenery()
        {
            foreach (var kind in new[] { BlastKind.Frost, BlastKind.Dark, BlastKind.Lightning, BlastKind.Holy, BlastKind.Earth, BlastKind.Water, BlastKind.Wind, BlastKind.Fire })
                Assert.Greater(FxMagic.ReachOf(kind, 0f), 0.5f, kind + " reaches what is beside it");
            Assert.AreEqual(0f, FxMagic.ReachOf(BlastKind.Gunpowder, 3f), "gunpowder breaks rather than marks");
            Assert.Greater(FxMagic.ReachOf(BlastKind.Frost, 4f), 4f, "a big spell reaches past its radius");
        }

        [Test]
        public void ALookPacksWhatTheShaderReads()
        {
            var look = new SceneryLook { Char = 0.5f, Bare = 0.3f, Split = 0.1f, SplitAngle = 1f, Foot = new Vector3(3f, 1f, -4f) };
            look.Bounds = new Bounds(new Vector3(3f, 2.5f, -4f), new Vector3(1f, 3f, 1f));
            look.Step(0f);
            look.Pack(out var s);
            Assert.AreEqual(0.5f, s.Mark.x);
            Assert.AreEqual(0.3f, s.Heat.y);
            Assert.AreEqual(0.1f, s.Bend.z);
            Assert.AreEqual(new Vector4(3f, 1f, -4f, 3f), s.Foot, "its foot and height");
        }
    }
}
