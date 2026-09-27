// CameraBoundsPlayTests.cs - the player's camera is the player's: a tilt
// and a zoom they set stay as set, even at the edges, and no view shows an
// unfilled gap (land, haze and sky are all fine). Panning across cliffs
// and ridges moves the camera's height smoothly, with no jumps.
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class CameraBoundsPlayTests
    {
        GameRoot root;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        IEnumerator Start(IGameBackend backend, string map)
        {
            root = GameRoot.Boot(backend);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = map;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 240f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, "the battle loaded: " + root.LastError);
            root.Orders.Frozen = true;
        }

        // Every ray through the screen's edge meets something drawn: ground,
        // the ring, the haze plain under it, or goes up into the sky.
        static bool NoGap(Camera cam, float plainY)
        {
            for (int k = 0; k < 16; k++)
            {
                float u = (k % 4) / 3f, v = (k / 4) / 3f;
                var ray = cam.ViewportPointToRay(new Vector3(u, v, 0));
                if (ray.direction.y >= -1e-4f) continue;            // sky
                float t = (plainY - ray.origin.y) / ray.direction.y;
                if (t > cam.farClipPlane) return false;              // a gap before the plain
            }
            return true;
        }

        [UnityTest]
        public IEnumerator ThePlayersTiltAndZoomStandAndNoGapShows()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            yield return Start(mock, "mock_isles");
            var gc = root.World.Camera;
            var cam = gc.GetComponent<Camera>();
            var size = mock.Terrain.Size;
            float plain = EdgeRing.Shelf(mock.Terrain);
            var spots = new[] { new Vector3(-50, 0, 50), new Vector3(size.x + 50, 0, 50), new Vector3(-50, 0, -size.y - 50), new Vector3(size.x + 50, 0, -size.y - 50), new Vector3(size.x / 2, 0, -size.y / 2) };
            foreach (var at in spots)
                foreach (float yaw in new[] { 0f, 135f, 270f })
                    foreach (float pitch in new[] { gc.minPitch, 40f, gc.maxPitch })
                    {
                        gc.focus = at;
                        gc.yaw = yaw;
                        gc.pitch = pitch;
                        gc.Zoom(gc.maxDistance);
                        for (int i = 0; i < 3; i++) yield return null;
                        Assert.AreEqual(pitch, gc.pitch, 1e-3f, "the tilt the player set stays");
                        Assert.AreEqual(gc.maxDistance, gc.distance, 1e-3f, "the zoom the player set stays");
                        Assert.IsTrue(NoGap(cam, plain), $"nothing unfilled in view at {at}, yaw {yaw}, pitch {pitch}");
                        Assert.That(gc.focus.x, Is.InRange(-gc.focusMargin - 0.01f, size.x + gc.focusMargin + 0.01f));
                        Assert.That(gc.focus.z, Is.InRange(-size.y - gc.focusMargin - 0.01f, gc.focusMargin + 0.01f));
                    }
        }

        // Pans at a steady speed along a line across the map and records the
        // camera's height every frame.
        static readonly List<string> trace = new List<string>();

        IEnumerator Pan(Vector3 from, Vector3 to, List<(float t, float y)> heights)
        {
            trace.Clear();
            var gc = root.World.Camera;
            gc.pitch = 45f;
            gc.yaw = 0;
            gc.Zoom(30f);
            gc.focus = from;
            // Let the height settle after the jump to the start.
            float settle = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < settle) yield return null;
            float speed = 12f, start = Time.unscaledTime;
            float length = Vector3.Distance(from, to);
            while (true)
            {
                // The focus set in this frame is applied in this frame's late
                // update, so the height read next frame belongs to this
                // frame's time.
                float now = Time.unscaledTime, t = now - start;
                float d = Mathf.Min(length, t * speed);
                var p = Vector3.Lerp(from, to, d / length);
                gc.focus = new Vector3(p.x, gc.focus.y, p.z);
                yield return null;
                heights.Add((now, gc.transform.position.y));
                trace.Add($"t={now:0.0000} y={gc.transform.position.y:0.000} fy={gc.focus.y:0.000} fx={gc.focus.x:0.00} fz={gc.focus.z:0.00}");
                Assert.AreEqual(45f, gc.pitch, 1e-3f);
                Assert.AreEqual(30f, gc.distance, 1e-3f);
                if (d >= length) break;
            }
        }

        // Height change a second, from each frame to the next, on the game's
        // own frame clock. A slow or loaded machine takes longer frames, so
        // the rate is what is judged, and a step at 60 frames a second is
        // the rate over 60.
        static void AssertSmooth(List<(float t, float y)> h, string where)
        {
            float worstRate = 0;
            int worstAt = 0;
            for (int i = 1; i < h.Count; i++)
            {
                float dy = Mathf.Abs(h[i].y - h[i - 1].y), dt = Mathf.Max(1e-4f, h[i].t - h[i - 1].t);
                if (dy / dt > worstRate) { worstRate = dy / dt; worstAt = i; }
            }
            if (worstRate >= 5f)
                for (int k = Mathf.Max(0, worstAt - 3); k < Mathf.Min(trace.Count, worstAt + 3); k++) Debug.Log("Camera trace " + trace[k]);
            Debug.Log($"Camera height on {where}: worst rate {worstRate:0.00} a second, a step of {worstRate / 60f:0.000} at 60 frames a second, {h.Count} frames");
            Assert.Less(worstRate, 5f, $"the camera's height changes gently on {where}, no more than about 8 cm a frame at 60 frames a second");
        }

        [UnityTest]
        public IEnumerator TheHeightIsSmoothOverTheMocksHills()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            yield return Start(mock, "mock_frost");
            var size = mock.Terrain.Size;
            var h = new List<(float, float)>();
            yield return Pan(new Vector3(4, 0, -size.y / 2), new Vector3(size.x - 4, 0, -size.y / 2), h);
            AssertSmooth(h, "frost pass");
        }

        [UnityTest]
        public IEnumerator TheHeightIsSmoothOverRealCliffsAndRidges()
        {
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            var view = GameObject.Find("OpenKingdoms");
            if (view != null) Object.Destroy(view);
            yield return null;
            yield return null;
            foreach (var map in new[] { "abnar's terrace", "black heart jungle" })
            {
                yield return Start(null, map);
                var size = root.Backend.Terrain.Size;
                var h = new List<(float, float)>();
                yield return Pan(new Vector3(4, 0, -size.y * 0.3f), new Vector3(size.x - 4, 0, -size.y * 0.7f), h);
                AssertSmooth(h, map);
                Object.Destroy(root.gameObject);
                root = null;
                yield return null;
                yield return null;
            }
        }
    }
}
