// MapEditorPlayTests.cs - the in-game map editor on the mock: open a map,
// raise the ground, paint it, place a feature, save it as a new map, and
// play a skirmish on that map with the changes there.
using System.Collections;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class MapEditorPlayTests
    {
        GameRoot root;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        static IEnumerator Until(System.Func<bool> done, float seconds, string what)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail("timed out waiting for " + what);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator EditSaveAndPlayANewMap()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            Assert.IsTrue(root.Flow.Fire(FlowEvent.OpenEditor));
            yield return null;
            Assert.AreEqual("EditorSetup", root.Screens.Visible);
            root.Setup.MapId = "mock_frost";
            root.Setup = GameRoot.DefaultSetup(mock);
            root.Setup.MapId = "mock_frost";
            Assert.IsTrue(root.Flow.Fire(FlowEvent.Start));
            yield return Until(() => root.Flow.State == FlowState.Editing, 30f, "the editor to open");
            Assert.AreEqual("Editor", root.Screens.Visible);
            Assert.IsNotNull(root.Editor);
            uint tick = mock.Tick;

            // Raise a hill in the middle.
            var t = mock.Terrain;
            int cx = t.HeightsW / 2, cz = t.HeightsH / 2;
            float before = t.HeightAt(cx, cz);
            root.Editor.Tool = EditTool.Raise;
            root.Editor.Strength = 8;
            for (int i = 0; i < 5; i++) root.Editor.Sculpt(cx, cz);
            Assert.Greater(t.HeightAt(cx, cz), before + 3f, "the ground rose");

            // Paint the blocks around it.
            root.Editor.PaintChunk = mock.ChunkLibrary()[1];
            int chunksBefore = t.ChunkCount;
            root.Editor.Paint(new Vector3(cx * t.CellSize, 0, -cz * t.CellSize));
            Assert.AreEqual(chunksBefore + 1, t.ChunkCount, "the painted picture joined the ground");

            // A feature on the hill.
            var feats = new FeatureState[8192];
            int featuresBefore = mock.ReadFeatures(feats);
            Assert.GreaterOrEqual(mock.PlaceFeature(0, cx, cz), 0);
            Assert.AreEqual(featuresBefore + 1, mock.ReadFeatures(feats));

            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(tick, mock.Tick, "the world stands still while edited");

            Assert.IsTrue(mock.SaveMap("frost hill"));
            root.Flow.Fire(FlowEvent.ToMenu);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "frost hill";
            root.Screens.StartGame();
            yield return Until(() => root.Flow.State == FlowState.Playing, 30f, "the new map to load");
            Assert.AreEqual("frost hill", root.CurrentMap().Id);
            Assert.Greater(mock.Terrain.HeightAt(cx, cz), before + 3f, "the saved hill is there");
            Assert.AreEqual(featuresBefore + 1, mock.ReadFeatures(feats));
        }
    }
}
