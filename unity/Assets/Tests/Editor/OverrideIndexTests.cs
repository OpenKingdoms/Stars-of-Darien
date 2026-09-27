// OverrideIndexTests.cs - drop-in models are found by name in any case,
// the better file type wins, hand-made beats generated, and files in
// other places are ignored.
using NUnit.Framework;
using OpenKingdomsUnity.Game;

namespace OpenKingdomsUnity.Tests
{
    public class OverrideIndexTests
    {
        [Test]
        public void NamesMatchInAnyCase()
        {
            var i = OverrideIndex.Build(new[] { "Assets/Overrides/Units/AraKing.glb", "Assets/Overrides/Features/verhenge01.fbx" });
            Assert.AreEqual("Assets/Overrides/Units/AraKing.glb", i.Find(OverrideKind.Unit, "araking"));
            Assert.AreEqual("Assets/Overrides/Features/verhenge01.fbx", i.Find(OverrideKind.Feature, "VERHENGE01"));
            Assert.IsNull(i.Find(OverrideKind.Feature, "araking"), "a unit override is not a feature override");
            Assert.IsNull(i.Find(OverrideKind.Unit, "nothing"));
        }

        [Test]
        public void ThePrefabBeatsTheGlbWhichBeatsTheFbx()
        {
            var i = OverrideIndex.Build(new[]
            {
                "Assets/Overrides/Units/knight.fbx", "Assets/Overrides/Units/knight.glb", "Assets/Overrides/Units/knight.prefab",
                "Assets/Overrides/Units/archer.fbx", "Assets/Overrides/Units/archer.gltf",
            });
            Assert.AreEqual("Assets/Overrides/Units/knight.prefab", i.Find(OverrideKind.Unit, "knight"));
            Assert.AreEqual("Assets/Overrides/Units/archer.gltf", i.Find(OverrideKind.Unit, "archer"));
        }

        [Test]
        public void HandMadeFeaturesBeatGeneratedOnes()
        {
            var i = OverrideIndex.Build(new[]
            {
                "Assets/Overrides/Generated/AraTree01.glb", "Assets/Overrides/Features/AraTree01.fbx",
                "Assets/Overrides/Generated/AraTree03.glb",
            });
            Assert.AreEqual("Assets/Overrides/Features/AraTree01.fbx", i.Find(OverrideKind.Feature, "aratree01"));
            Assert.AreEqual("Assets/Overrides/Generated/AraTree03.glb", i.Find(OverrideKind.Feature, "aratree03"));
            Assert.IsTrue(i.IsGenerated("Assets/Overrides/Generated/AraTree03.glb"));
            Assert.IsNull(i.Find(OverrideKind.Unit, "aratree03"), "generated models are features only");
        }

        [Test]
        public void TheFirstNameWithAnOverrideWins()
        {
            var i = OverrideIndex.Build(new[] { "Assets/Overrides/Features/verhenge.glb", "Assets/Overrides/Features/verhenge01.glb" });
            Assert.AreEqual("Assets/Overrides/Features/verhenge01.glb", i.Find(OverrideKind.Feature, null, "", "verhenge01", "verhenge"));
        }

        [Test]
        public void OtherFilesAndFoldersAreIgnored()
        {
            var i = OverrideIndex.Build(new[]
            {
                "Assets/Overrides/Units/knight.png", "Assets/Overrides/Units/Sub/knight.glb", "Assets/Art/knight.fbx",
                "Assets\\Overrides\\Units\\archer.glb",
            });
            Assert.AreEqual(1, i.Count);
            Assert.AreEqual("Assets/Overrides/Units/archer.glb", i.Find(OverrideKind.Unit, "archer"));
        }
    }
}
