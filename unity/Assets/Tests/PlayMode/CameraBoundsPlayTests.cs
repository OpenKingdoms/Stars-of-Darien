// CameraBoundsPlayTests.cs - at its extremes of pan, zoom and tilt, and
// facing every way, the camera only ever frames land: the map or the ring
// past its edge, never the void or the horizon.
using System.Collections;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class CameraBoundsPlayTests
    {
        [UnityTest]
        public IEnumerator TheCameraNeverFramesTheVoid()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            var root = GameRoot.Boot(mock);
            try
            {
                yield return null;
                root.Flow.Fire(FlowEvent.OpenSkirmish);
                root.Setup.MapId = "mock_isles";
                root.Screens.StartGame();
                float deadline = Time.realtimeSinceStartup + 30f;
                while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
                var gc = root.World.Camera;
                var cam = gc.GetComponent<Camera>();
                var size = mock.Terrain.Size;
                var corners = new[] { new Vector3(-50, 0, 50), new Vector3(size.x + 50, 0, 50), new Vector3(-50, 0, -size.y - 50), new Vector3(size.x + 50, 0, -size.y - 50), new Vector3(size.x / 2, 0, 60) };
                foreach (var at in corners)
                    foreach (float yaw in new[] { 0f, 45f, 135f, 225f, 315f })
                    {
                        gc.focus = at;
                        gc.yaw = yaw;
                        gc.pitch = gc.minPitch;
                        gc.Zoom(gc.maxDistance);
                        for (int i = 0; i < 3; i++) yield return null;
                        Assert.IsTrue(gc.CornersOnLand(cam), $"only land in view at {at}, yaw {yaw}: pitch {gc.pitch}, distance {gc.distance}");
                        Assert.That(gc.focus.x, Is.InRange(-gc.focusMargin - 0.01f, size.x + gc.focusMargin + 0.01f));
                        Assert.That(gc.focus.z, Is.InRange(-size.y - gc.focusMargin - 0.01f, gc.focusMargin + 0.01f));
                    }
                // In the middle of the map the classic view is left alone.
                gc.focus = new Vector3(size.x / 2, 0, -size.y / 2);
                gc.yaw = 0;
                gc.pitch = GameCamera.ClassicPitch;
                gc.Zoom(34f);
                for (int i = 0; i < 3; i++) yield return null;
                Assert.AreEqual(GameCamera.ClassicPitch, gc.pitch, 0.01f);
            }
            finally { Object.Destroy(root.gameObject); }
        }
    }
}
