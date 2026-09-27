// BillboardTests.cs - effects and sprite feature cards face the camera, so
// looking almost straight down they keep the height they show from the
// side, and at the classic tilt they lose none of it.
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class BillboardTests
    {
        Camera cam;

        [SetUp]
        public void MakeCamera()
        {
            cam = new GameObject("billboard camera").AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.aspect = 16f / 9f;
        }

        [TearDown]
        public void DropCamera() => Object.DestroyImmediate(cam.gameObject);

        void Look(Vector3 at, float pitch)
        {
            cam.transform.rotation = Quaternion.Euler(pitch, 30f, 0f);
            cam.transform.position = at - cam.transform.forward * 30f;
        }

        float Tall(Vector3[] c)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in c) { float y = cam.WorldToViewportPoint(p).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
            return hi - lo;
        }

        float EffectTall(EffectState e, float pitch)
        {
            Look(e.Position, pitch);
            var c = new Vector3[4];
            EffectRenderer.Corners(e, cam.transform, c);
            return Tall(c);
        }

        float CardTall(Vector3 at, float pitch)
        {
            Look(at, pitch);
            var m = EntityRenderer.CardMatrix(at, 2f, -0.2f, 3f, 1f, cam.transform);
            var c = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            for (int i = 0; i < 4; i++) c[i] = m.MultiplyPoint3x4(c[i]);
            return Tall(c);
        }

        [Test]
        public void AnExplosionKeepsItsHeightLookingDown()
        {
            var e = new EffectState { Position = new Vector3(10, 2, -10), Width = 2f, OffsetX = 1f, Bottom = 0f, Top = 2.5f };
            float side = EffectTall(e, 45f);
            Assert.Greater(EffectTall(e, 85f) / side, 0.9f, "at pitch 85");
            Assert.Greater(EffectTall(e, 62f) / side, 0.97f, "at the classic pitch");
        }

        [Test]
        public void AFeatureCardKeepsItsHeightLookingDown()
        {
            var at = new Vector3(20, 1, -30);
            float side = CardTall(at, 45f);
            Assert.Greater(CardTall(at, 85f) / side, 0.9f, "at pitch 85");
            Assert.Greater(CardTall(at, 62f) / side, 0.97f, "at the classic pitch");
        }
    }
}
