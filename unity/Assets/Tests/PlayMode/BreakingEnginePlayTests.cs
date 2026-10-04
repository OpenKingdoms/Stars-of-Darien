// BreakingEnginePlayTests.cs - on the real engine, skipped without the game
// files: taking a feature away on the map with the most scenery rebuilds no
// other, and what a frame of that costs beside building every feature again.
using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class BreakingEnginePlayTests
    {
        static bool Named(MapInfo m, string name) =>
            string.Equals(m.Name, name, System.StringComparison.OrdinalIgnoreCase) || string.Equals(m.Id, name, System.StringComparison.OrdinalIgnoreCase);

        [UnityTest, Timeout(600000)]
        public IEnumerator TakingAFeatureAwayOnAFieldOfScenery()
        {
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            var view = GameObject.Find("OpenKingdoms");
            if (view != null) Object.Destroy(view);
            yield return null;
            var root = GameRoot.Boot();
            try
            {
                yield return null;
                var b = root.Backend;
                // The maps with the most scenery in the game, then the biggest.
                var map = b.Maps.FirstOrDefault(m => Named(m, "ulasem arena")) ?? b.Maps.FirstOrDefault(m => Named(m, "grimm farms")) ??
                          b.Maps.OrderByDescending(m => m.Size.x * m.Size.y).First();
                root.Flow.Fire(FlowEvent.OpenSkirmish);
                var setup = GameRoot.DefaultSetup(b);
                setup.MapId = map.Id;
                root.Setup = setup;
                root.Flow.Fire(FlowEvent.Start);
                float deadline = Time.realtimeSinceStartup + 300f;
                while (root.Flow.State == FlowState.Loading && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.AreEqual(FlowState.Playing, root.Flow.State, "the battle loaded: " + root.LastError);
                root.Options.GameSpeed = 0;
                for (int i = 0; i < 5; i++) yield return null;
                var ents = root.World.Entities;
                var fs = new FeatureState[EntityRenderer.MaxFeatures];
                int n = b.ReadFeatures(fs);
                Assert.Greater(n, 500, "a map full of scenery");
                yield return null;
                double still = root.RenderMs;
                ents.RebuildFeatures();
                yield return null;
                double all = root.RenderMs;
                Assert.AreEqual(n, ents.FeaturesRebuilt);
                yield return null;
                Assert.IsTrue(b.RemoveFeature(0), "the map editor's removal");
                yield return null;
                double first = root.RenderMs;
                Assert.AreEqual(0, ents.FeaturesRebuilt, "the rest stay as they were");
                Debug.Log($"{map.Name}: {n} features, a still frame {still:0.0} ms, building every feature {all:0.0} ms, taking the first away {first:0.0} ms");
            }
            finally { Object.Destroy(root.gameObject); }
        }
    }
}
