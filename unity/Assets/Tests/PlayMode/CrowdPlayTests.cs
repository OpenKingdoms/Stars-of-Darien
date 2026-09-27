// CrowdPlayTests.cs - hundreds of units on the mock, drawn with instancing,
// keep the frame time sane and log what it was.
using System.Collections;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class CrowdPlayTests
    {
        [UnityTest]
        public IEnumerator FiveHundredUnitsDrawInBoundedTime()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f, ExtraSoldiers = 250 };
            var root = GameRoot.Boot(mock);
            try
            {
                yield return null;
                root.Flow.Fire(FlowEvent.OpenSkirmish);
                root.Setup.MapId = "mock_highlands";
                root.Screens.StartGame();
                float deadline = Time.realtimeSinceStartup + 60f;
                while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.AreEqual(FlowState.Playing, root.Flow.State);
                root.World.Camera.Zoom(110f);
                for (int i = 0; i < 10; i++) yield return null;
                Assert.GreaterOrEqual(root.World.Entities.UnitCount, 500);
                float start = Time.realtimeSinceStartup;
                const int frames = 120;
                for (int i = 0; i < frames; i++) yield return null;
                float ms = (Time.realtimeSinceStartup - start) * 1000f / frames;
                Debug.Log($"Crowd: {root.World.Entities.UnitCount} units, {root.World.Entities.Drawn} instances, {ms:0.0} ms a frame");
                Assert.Less(ms, 100f, "a frame with 500 units took too long");
            }
            finally { Object.Destroy(root.gameObject); }
        }
    }
}
