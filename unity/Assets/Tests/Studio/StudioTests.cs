// StudioTests.cs - the studio windows open and close on the mock engine,
// and the sprite catalog reads as extract.py writes it.
using NUnit.Framework;
using OpenKingdomsUnity.Studio;
using UnityEditor;

namespace OpenKingdomsUnity.Tests
{
    public class StudioTests
    {
        bool savedMock;

        [SetUp] public void UseMock() { savedMock = StudioBackend.PreferMock; StudioBackend.PreferMock = true; }
        [TearDown] public void Restore() { StudioBackend.PreferMock = savedMock; StudioBackend.Release(); }

        [Test]
        public void TheMockBackendServesTheStudio()
        {
            var b = StudioBackend.Get();
            Assert.AreEqual("Mock", b.Name);
            Assert.Greater(b.UnitDefs.Count, 0);
            int model = b.LoadModel(b.UnitDefs[1].ObjectName, 0);
            var pm = StudioBackend.Models.Get(model);
            Assert.IsNotNull(pm);
            Assert.Greater(pm.RestBounds.size.y, 0.5f);
        }

        [Test]
        public void EveryWindowOpensAndCloses()
        {
            foreach (var w in new EditorWindow[] { EditorWindow.GetWindow<UnitBrowser>(), EditorWindow.GetWindow<MapBrowser>(), EditorWindow.GetWindow<SpriteReplacement>(), EditorWindow.GetWindow<AnimationEditor>() })
            {
                Assert.IsNotNull(w);
                w.Repaint();
                w.Close();
            }
        }

        [Test]
        public void TheCatalogReads()
        {
            string json = "[{\"name\": \"AraTree01\", \"world\": \"aramon\", \"description\": \"Tree\", \"category\": \"trees\", " +
                "\"gaf\": \"AraTree\", \"seq\": \"AraTree01\", \"footprint\": [1, 2], \"height\": 226, " +
                "\"sprite\": {\"w\": 45, \"h\": 115, \"hotspot\": [21, 112]}, \"maps\": 21, \"status\": \"sprite\"}]";
            var list = SpriteReplacement.ReadCatalog(json, out var error);
            Assert.IsNull(error);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("AraTree01", list[0].Name);
            Assert.AreEqual(new UnityEngine.Vector2Int(1, 2), list[0].Footprint);
            Assert.AreEqual(21, list[0].Maps);
            Assert.AreEqual(226, list[0].Height);
            SpriteReplacement.ReadCatalog("{nope", out error);
            Assert.IsNotNull(error);
        }
    }
}
