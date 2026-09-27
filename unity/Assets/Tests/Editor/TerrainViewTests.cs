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
    }
}
