// EnginePlayTests.cs - the real game: boots OpenKingdoms through okengine
// on a real map, lets it run, and checks the ground, the units and their
// pieces are drawn and a move order reaches a unit. Needs okengine and
// the game files, and says so when they are missing. With OK_CAPTURE_DIR
// set it also saves a few views as PNG for a person to look at.
using System.Collections;
using System.IO;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class EnginePlayTests
    {
        [TearDown]
        public void CleanUp()
        {
            foreach (var d in Object.FindObjectsByType<EngineDriver>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
        }

        [UnityTest]
        public IEnumerator ARealMapComesUpWithUnitsThatMoveAndAnimate()
        {
            if (!EngineSettings.EngineAvailable)
            {
                Assert.Ignore("okengine or the game files are missing");
                yield break;
            }
            yield return null;
            var driver = Object.FindAnyObjectByType<EngineDriver>();
            if (driver == null) driver = new GameObject("OpenKingdoms").AddComponent<EngineDriver>();
            float deadline = Time.realtimeSinceStartup + 180f;
            while (!driver.Running && driver.Error == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsNull(driver.Error, driver.Error);
            Assert.IsTrue(driver.Running, "the engine did not start in time");
            Assert.Greater(driver.Terrain.ChunkTextures, 0);
            Assert.Greater(driver.Features.Models.Count + driver.Features.SpriteCount, 0);

            for (int i = 0; i < 30; i++) yield return null;
            Assert.GreaterOrEqual(driver.UnitCount, 2, "the armies are on the map");
            Assert.Greater(driver.PiecesDrawn, driver.UnitCount, "units are drawn piece by piece");

            // Send the local player's first unit east and watch it go.
            int me = OkEngine.okx_local_player();
            OkxUnit u = default;
            bool found = false;
            for (int i = 0; i < driver.UnitCount && !found; i++)
                if (driver.Units[i].player == me && driver.Units[i].state == OkEngine.UnitActive) { u = driver.Units[i]; found = true; }
            Assert.IsTrue(found, "the local player has a unit");
            Assert.AreEqual(0, OkEngine.okx_command((int)OkxCmd.Move, u.handle, (int)u.x + 480, (int)u.z, -1, -1, 0));
            uint t0 = OkEngine.okx_tick_count();
            float until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Assert.Greater(OkEngine.okx_tick_count(), t0 + 60, "the engine ticks in real time");
            float x = u.x;
            for (int i = 0; i < driver.UnitCount; i++)
                if (driver.Units[i].handle == u.handle) x = driver.Units[i].x;
            Assert.Greater(x, u.x + 60f, "the unit walked east");

            string dir = System.Environment.GetEnvironmentVariable("OK_CAPTURE_DIR");
            if (!string.IsNullOrEmpty(dir)) yield return Capture(driver, dir, u);
        }

        static IEnumerator Capture(EngineDriver driver, string dir, OkxUnit u)
        {
            Directory.CreateDirectory(dir);
            var cam = Camera.main;
            var rig = cam.GetComponent<RtsCamera>();
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            var shots = new (string name, float height, float pitch, float yaw)[]
            {
                ("classic", 36f, 60f, 0f),
                ("close", 10f, 40f, 30f),
                ("wide", 120f, 70f, 0f),
            };
            foreach (var s in shots)
            {
                rig.focus = EngineSettings.ToUnity(u.x + 240, u.y, u.z);
                rig.height = s.height;
                rig.pitch = s.pitch;
                rig.yaw = s.yaw;
                for (int i = 0; i < 20; i++) yield return null;
                // Batchmode never reaches the end of a frame, so render by
                // hand, after this frame's draws are in.
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                string tag = System.Environment.GetEnvironmentVariable("OK_CAPTURE_TAG") ?? "";
                File.WriteAllBytes(Path.Combine(dir, $"engine-{s.name}{tag}.png"), tex.EncodeToPNG());
                Object.Destroy(tex);
            }
            cam.targetTexture = null;
            rt.Release();
        }
    }
}
