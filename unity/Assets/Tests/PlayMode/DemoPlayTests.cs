// DemoPlayTests.cs - boots the demo as Play does and lets it run, sped up,
// until the computer has raised and sent its first wave. Any error logged
// on the way fails the test.
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using OpenKingdomsUnity.Engine;

namespace OpenKingdomsUnity.Tests
{
    public class DemoPlayTests
    {
        float savedScale;

        [SetUp]
        public void SpeedUp()
        {
            savedScale = Time.timeScale;
            Time.timeScale = 20f;
            // The bootstrap may have started the real engine. This test is
            // about the capsule demo.
            foreach (var e in Object.FindObjectsByType<EngineDriver>(FindObjectsSortMode.None))
                Object.DestroyImmediate(e.gameObject);
        }

        [TearDown]
        public void CleanUp()
        {
            Time.timeScale = savedScale;
            foreach (var d in Object.FindObjectsByType<SimDriver>(FindObjectsSortMode.None))
                Object.Destroy(d.gameObject);
        }

        [UnityTest]
        public IEnumerator TheDemoBootsAndTheFirstWaveSetsOff()
        {
            yield return null;
            var driver = Object.FindAnyObjectByType<SimDriver>();
            if (driver == null) driver = new GameObject("SimDriver").AddComponent<SimDriver>();

            // The first wave appears at tick 300 and sets off 30 ticks later.
            float deadline = Time.realtimeSinceStartup + 120f;
            while ((driver.Sim == null || driver.Sim.Tick < 420) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsNotNull(driver.Sim, "the game never started");
            Assert.GreaterOrEqual(driver.Sim.Tick, 420u, "the sim did not reach tick 420 in time");

            Assert.GreaterOrEqual(driver.CountAlive(SimDriver.Human), 12);
            Assert.AreEqual(driver.aiWaves - 1, driver.AiWavesLeft);
            int red = 0, marching = 0;
            for (int i = 0; i < driver.ViewCount; i++)
            {
                var v = driver.Views[i];
                if (v.player != SimDriver.Computer) continue;
                red++;
                if (v.state == OkNative.UnitMoving || v.target >= 0) marching++;
            }
            Assert.AreEqual(4, red, "the first wave has four units");
            Assert.AreEqual(red, marching, "the whole wave set off");

            // One body per unit, drawn under the Units root.
            var units = GameObject.Find("Units");
            Assert.IsNotNull(units);
            Assert.AreEqual(driver.ViewCount, units.transform.childCount);
            Assert.IsNull(driver.Outcome);
        }
    }
}
