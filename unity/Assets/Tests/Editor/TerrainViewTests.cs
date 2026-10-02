// TerrainViewTests.cs - the ground built from a backend, and rebuilt in
// place after an edit, as the map editor does.
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class TerrainViewTests
    {
        [Test]
        public void AnEditedCornerIsRebuiltWithItsNewHeight()
        {
            var b = new MockBackend { StageSeconds = 0 };
            var s = GameRoot.DefaultSetup(b);
            s.MapId = "mock_frost";
            b.StartSkirmish(s);
            for (int i = 0; i < 20 && !b.PumpLoading().Done; i++) { }
            var parent = new GameObject("test world");
            var view = new TerrainView();
            try
            {
                view.Build(b, parent.transform);
                int regions = view.Regions;
                Assert.Greater(regions, 0);
                var t = b.Terrain;
                // Raise one sample in the first block, then rebuild that block.
                t.Heights[2 * t.HeightsW + 2] = 40f;
                view.Rebuild(new RectInt(0, 0, 1, 1));
                Assert.AreEqual(regions, view.Regions, "the same regions, rebuilt");
                var mesh = view.Root.transform.Find("Region 0,0/LOD0").GetComponent<MeshFilter>().sharedMesh;
                bool found = false;
                foreach (var v in mesh.vertices) found |= Mathf.Abs(v.y - 40f) < 1e-3f;
                Assert.IsTrue(found, "the rebuilt mesh has the raised sample");
            }
            finally
            {
                view.Dispose();
                Object.DestroyImmediate(parent);
                b.Dispose();
            }
        }

        [Test]
        public void TheGroundBuildsInStepsARegionAtATimeThenTheRingAndTheSea()
        {
            var b = new MockBackend { StageSeconds = 0 };
            var s = GameRoot.DefaultSetup(b);
            s.MapId = "mock_isles";
            b.StartSkirmish(s);
            for (int i = 0; i < 20 && !b.PumpLoading().Done; i++) { }
            var parent = new GameObject("test world");
            var view = new TerrainView();
            try
            {
                var t = b.Terrain;
                int regions = TerrainBuilder.RegionsW(t) * TerrainBuilder.RegionsH(t);
                Assert.Greater(regions, 1);
                var steps = view.Steps(b, parent.transform);
                int k = 0;
                for (int r = 0; r < regions; r++)
                    for (int half = 0; half < 2; half++, k++)
                    {
                        Assert.AreEqual(TerrainView.LandPart, steps[k].part);
                        Assert.IsTrue(steps[k].step(), "a region's step is done in one call");
                        Assert.AreEqual(r + half, view.Regions, "a region counts once its meshes are built");
                        Assert.IsNull(view.Apron, "the ring waits for the land");
                    }
                for (; steps[k].part == TerrainView.EdgePart; k++) Assert.IsTrue(steps[k].step());
                Assert.IsNotNull(view.Apron, "the ring is built");
                Assert.IsNull(view.Sea, "the sea waits for its steps");
                for (; k < steps.Count; k++)
                {
                    Assert.AreEqual(TerrainView.SeaPart, steps[k].part);
                    // The first waits for the depths the worker bakes.
                    var deadline = System.DateTime.Now.AddSeconds(30);
                    while (!steps[k].step() && System.DateTime.Now < deadline) System.Threading.Thread.Sleep(1);
                }
                Assert.IsNotNull(view.Sea, "the isles have their sea");
                Assert.IsTrue(view.Sea.AnyWater);
            }
            finally
            {
                view.Dispose();
                Object.DestroyImmediate(parent);
                b.Dispose();
            }
        }
    }
}
