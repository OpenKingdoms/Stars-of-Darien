// FeatureRefreshPlayTests.cs - features are drawn from what they are, not
// how many there are: one taken away and another placed in the same frame
// shows the new one and not the old, a sinking corpse sinks, and features
// that do not change are not rebuilt.
using System.Collections;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class FeatureRefreshPlayTests
    {
        GameRoot root;
        MockBackend mock;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        IEnumerator Begin()
        {
            mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_highlands";
            root.Setup.MapRevealed = true;
            root.Setup.LineOfSight = false;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
            for (int i = 0; i < 5; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator OneTakenAndOnePlacedInAFrameShowTheNewOne()
        {
            yield return Begin();
            var fs = new FeatureState[4096];
            int n = mock.ReadFeatures(fs);
            Assert.Greater(n, 2);
            // A tree drawn as its model, taken away, and another put down far off.
            int k = -1;
            for (int i = 0; i < n && k < 0; i++) if (fs[i].Model >= 0) k = i;
            Assert.GreaterOrEqual(k, 0, "the map has a tree");
            var old = fs[k].Position;
            var ents = root.World.Entities;
            Assert.IsTrue(ents.FeatureDrawnAt(old, 0.3f), "the tree is drawn");
            var size = mock.Terrain.Size;
            int cx = Mathf.RoundToInt(old.x < size.x / 2 ? size.x - 12 : 12), cz = Mathf.RoundToInt(-old.z < size.y / 2 ? size.y - 12 : 12);
            Assert.IsTrue(mock.RemoveFeature(k));
            int placed = mock.PlaceFeature(fs[k].Def, cx, cz);
            Assert.GreaterOrEqual(placed, 0);
            Assert.AreEqual(n, mock.ReadFeatures(fs), "as many features as before");
            var fresh = fs[placed].Position;
            yield return null;
            yield return null;
            Assert.IsTrue(ents.FeatureDrawnAt(fresh, 0.3f), "the new tree is drawn");
            Assert.IsFalse(ents.FeatureDrawnAt(old, 0.3f), "the old one is gone");
        }

        [UnityTest]
        public IEnumerator ASinkingCorpseSinksAndStillFeaturesStay()
        {
            yield return Begin();
            var fs = new FeatureState[4096];
            int n = mock.ReadFeatures(fs);
            int tree = -1;
            for (int i = 0; i < n && tree < 0; i++) if (fs[i].Model >= 0) tree = fs[i].Def;
            var size = mock.Terrain.Size;
            var at = new Vector3(size.x / 2 + 20, 0, -size.y / 2);
            at.y = mock.GroundHeight(at.x, at.z);
            int corpse = mock.AddFeature(tree, at, 0.5f);
            yield return null;
            yield return null;
            var ents = root.World.Entities;
            Assert.IsTrue(ents.FeatureDrawnAt(at, 0.3f), "the corpse is drawn");
            float y0 = ents.FeatureDrawnHeight(at, 0.3f);
            mock.Advance(60);
            yield return null;
            yield return null;
            float y1 = ents.FeatureDrawnHeight(at, 0.3f);
            Assert.Less(y1, y0 - 0.5f, "two seconds at half a unit a second");

            // Nothing else changed, so nothing else is rebuilt.
            for (int i = 0; i < 3; i++) yield return null;
            mock.Advance(1);
            yield return null;
            Assert.LessOrEqual(ents.FeaturesRebuilt, 1, "only the sinking corpse");
        }
    }
}
