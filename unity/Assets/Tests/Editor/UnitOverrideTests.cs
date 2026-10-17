// UnitOverrideTests.cs - a whole unit's drop-in model: found by its names
// however its model was first asked for, and its parts with a material
// named for the team in each player's colour, one material per colour, as
// Studio Mode previews them.
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class UnitOverrideTests
    {
        GameObject template;
        Material cloth, gold;

        [SetUp]
        public void Make()
        {
            OverrideLoader.Reset(new OverrideIndex());
            template = new GameObject("ARAKING");
            var torso = new GameObject("Torso");
            torso.transform.SetParent(template.transform, false);
            var mesh = new Mesh { subMeshCount = 2 };
            mesh.SetVertices(new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward });
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.SetTriangles(new[] { 0, 2, 3 }, 1);
            torso.AddComponent<MeshFilter>().sharedMesh = mesh;
            cloth = Looks.Model(null);
            cloth.name = "teamcolor";
            cloth.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            gold = Looks.Model(null);
            gold.name = "gold";
            torso.AddComponent<MeshRenderer>().sharedMaterials = new[] { cloth, gold };
        }

        [TearDown]
        public void End()
        {
            OverrideLoader.Reset();
            Object.DestroyImmediate(template);
            Object.DestroyImmediate(cloth);
            Object.DestroyImmediate(gold);
        }

        // The loading screen warms each unit's model before the field draws
        // it, by id alone; the override must still be found by the unit's names.
        [Test]
        public void AModelWarmedByIdStillFindsItsOverrideByName()
        {
            OverrideLoader.Reset(OverrideIndex.Build(new[] { "Assets/Overrides/Units/ARALODE.glb" }));
            var mock = new MockBackend();
            var cache = new ModelCache(mock);
            try
            {
                int id = mock.LoadModel("mockflyer", 0);
                var warmed = cache.Get(id);
                Assert.IsNotNull(warmed);
                Assert.IsNull(warmed.Override, "by id alone, no name to find it by");
                var m = cache.Get(id, OverrideKind.Unit, new[] { "ARALODE" });
                Assert.AreSame(warmed, m, "the same model");
                Assert.IsNotNull(m.Override, "found under the unit's name");
                Assert.IsNotNull(m.RestInverse);
                Assert.AreSame(m.Override, cache.Get(id).Override, "and kept");
            }
            finally { cache.Dispose(); }
        }

        [Test]
        public void PartsNamedForTheTeamTakeThePlayersColour()
        {
            var pieces = new[] { new PieceInfo { Name = "AraGP", Parent = -1 }, new PieceInfo { Name = "Torso", Parent = 0 } };
            var o = OverrideModel.From(template, pieces, "Assets/Overrides/Units/ARAKING.glb");
            Assert.AreEqual(2, o.Parts.Count);
            Assert.IsTrue(o.Parts[0].Team, "teamcolor");
            Assert.IsFalse(o.Parts[1].Team, "gold");
            Assert.AreEqual(1, o.Parts[0].Piece, "follows the torso");

            var blue = new Color32(40, 90, 220, 255);
            var red = new Color32(210, 40, 40, 255);
            Assert.IsFalse(OverrideLoader.HasTinted(cloth, blue));
            var b = OverrideLoader.Tinted(cloth, blue);
            Assert.IsTrue(OverrideLoader.HasTinted(cloth, blue));
            Assert.AreSame(b, OverrideLoader.Tinted(cloth, blue), "one material for each colour");
            Assert.AreNotSame(b, OverrideLoader.Tinted(cloth, red));
            Assert.AreEqual(0.8f * 220f / 255f, b.color.b, 1e-4f);
            Assert.AreEqual(0.8f * 40f / 255f, b.color.r, 1e-4f);
            Assert.AreEqual(new Color(0.8f, 0.8f, 0.8f, 1f), cloth.color, "the model's own material is left alone");
            Assert.IsTrue(OverrideLoader.IsTeam(cloth) && !OverrideLoader.IsTeam(gold));
            var enamel = new Material(cloth) { name = "TeamColor_enamel" };
            Assert.IsTrue(OverrideLoader.IsTeam(enamel), "in any case");
            Object.DestroyImmediate(enamel);

            OverrideLoader.Reset(new OverrideIndex());
            Assert.IsFalse(OverrideLoader.HasTinted(cloth, blue), "let go with the models");
        }
    }
}
