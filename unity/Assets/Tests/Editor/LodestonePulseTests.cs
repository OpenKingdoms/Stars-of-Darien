// LodestonePulseTests.cs - a lodestone's breath as the owner asked for it:
// slow, easing between 80% and 115% of rest and never a blink, one soft
// ring a breath that grows from nothing and fades to nothing, gentler from
// the classic camera and in a dim scene, and in the crystal's own colour.
using System.Collections.Generic;
using NUnit.Framework;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class LodestonePulseTests
    {
        const float Frame = 1f / 60f;

        [Test]
        public void TheBreathIsSlowAndEasesBetweenItsBoundsWithNoStep()
        {
            Assert.That(LodestonePulse.Period, Is.InRange(5f, 7f));
            float lo = float.MaxValue, hi = float.MinValue, last = LodestonePulse.Gain(0f, 0.3f), steepest = 0f;
            for (float t = Frame; t <= LodestonePulse.Period * 2f; t += Frame)
            {
                float g = LodestonePulse.Gain(t, 0.3f);
                lo = Mathf.Min(lo, g);
                hi = Mathf.Max(hi, g);
                steepest = Mathf.Max(steepest, Mathf.Abs(g - last));
                last = g;
            }
            Assert.AreEqual(0.8f, lo, 0.002f, "its dimmest");
            Assert.AreEqual(1.15f, hi, 0.002f, "its brightest");
            // A cosine over six seconds moves at most 0.35 * pi / 6 a second.
            Assert.Less(steepest, 0.004f, "no frame jumps");
        }

        [Test]
        public void OneRingABreathGrowsFromNothingAndFadesToNothing()
        {
            int born = 0;
            float lastAge = -1f, lastRadius = 0f;
            for (float t = 0f; t < LodestonePulse.Period * 10f; t += Frame)
            {
                float age = LodestonePulse.RingAge(t, 0.1f);
                if (age >= 0f && (lastAge < 0f || age < lastAge))
                {
                    born++;
                    Assert.Less(LodestonePulse.RingStrength(age), 0.05f, "a ring starts from nothing");
                    lastRadius = 0f;
                }
                if (age >= 0f)
                {
                    float r = LodestonePulse.RingRadius(age, 0.5f, LodestonePulse.RingReach);
                    Assert.GreaterOrEqual(r, lastRadius, "a ring only spreads");
                    lastRadius = r;
                }
                if (age < 0f && lastAge >= 0f) Assert.Less(LodestonePulse.RingStrength(lastAge), 0.01f, "and ends at nothing");
                lastAge = age;
            }
            Assert.That(born, Is.InRange(10, 11), "one ring a breath");
            // Born at the swell, when the crystal is brightest.
            float peak = 0.5f * LodestonePulse.Period;
            Assert.AreEqual(1.15f, LodestonePulse.Gain(peak, 0f), 0.001f);
            Assert.AreEqual(0f, LodestonePulse.RingAge(peak + 0.001f, 0f), 0.01f);
        }

        [Test]
        public void TheClassicCameraAndADimSceneAreGentler()
        {
            Assert.AreEqual(1f, LodestonePulse.ViewCalm(30f), 1e-4f);
            Assert.Less(LodestonePulse.ViewCalm(GameCamera.ClassicPitch), 0.8f);
            Assert.AreEqual(1f, LodestonePulse.LightCalm(1f), 1e-4f);
            float night = LodestonePulse.LightCalm(0.2f);
            Assert.Less(night, 0.7f);
            for (float t = 0f; t < LodestonePulse.Period; t += 0.1f)
                Assert.That(LodestonePulse.Gain(t, 0f, night), Is.InRange(0.88f, 1.09f), "a dim scene swings less");
        }

        [Test]
        public void NeighboursKeepTheirOwnTime()
        {
            var a = new Vector3(40f, 0f, -40f);
            foreach (var step in new[] { Vector3.right * 4f, Vector3.forward * 4f, new Vector3(4f, 0f, 4f) })
            {
                float d = Mathf.Abs(LodestonePulse.Phase(a) - LodestonePulse.Phase(a + step));
                Assert.Greater(Mathf.Min(d, 1f - d), 0.1f, $"a lodestone {step} away breathes apart");
            }
        }

        [Test]
        public void OnlyTheLodestonesBreathe()
        {
            foreach (var n in new[] { "ARALODE", "ARAMANA", "TARLODE", "TARMANA", "VERLODE", "vermana", "ZONLODE", "ZONMANA" })
                Assert.IsTrue(LodestonePulse.IsLodestone(n), n);
            foreach (var n in new[] { "ZONFIRE", "ZONGLYPH", "NPCTHESH", "ARAKNIGHT", "", null })
                Assert.IsFalse(LodestonePulse.IsLodestone(n), n ?? "null");
        }

        [Test]
        public void TheGlowIsTheCrystalsOwnColourWhereTheCrystalIs()
        {
            var made = new List<Object>();
            try
            {
                var root = new GameObject("lodestone");
                made.Add(root);
                var stoneMat = Looks.Model(null);
                stoneMat.color = new Color(0.5f, 0.5f, 0.5f);
                var crystalMat = Looks.Model(null);
                crystalMat.color = new Color(0.1f, 0.8f, 0.3f);
                crystalMat.SetFloat("_Emission", 1.5f);
                made.Add(stoneMat);
                made.Add(crystalMat);
                var stone = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stone.transform.SetParent(root.transform, false);
                stone.transform.localScale = new Vector3(3f, 1f, 3f);
                stone.GetComponent<MeshRenderer>().sharedMaterial = stoneMat;
                var crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crystal.transform.SetParent(root.transform, false);
                crystal.transform.localPosition = new Vector3(0f, 2f, 0f);
                crystal.transform.localScale = new Vector3(0.8f, 1.2f, 0.8f);
                crystal.GetComponent<MeshRenderer>().sharedMaterial = crystalMat;

                var glow = LodestoneGlow.Of(OverrideModel.From(root, null, "test"));
                Assert.IsNotNull(glow);
                Assert.AreEqual(2f, glow.Centre.y, 0.05f, "at the crystal, not the stone");
                Assert.AreEqual(0.4f, glow.Radius, 0.05f);
                Assert.AreEqual(1f, glow.Colour.g, 1e-3f, "in the crystal's green");
                Assert.Less(glow.Colour.r, 0.3f);

                crystalMat.SetFloat("_Emission", 0f);
                Assert.IsNull(LodestoneGlow.Of(OverrideModel.From(root, null, "test")), "nothing glows, no breath");
            }
            finally
            {
                foreach (var o in made) Object.DestroyImmediate(o);
            }
        }
    }
}
