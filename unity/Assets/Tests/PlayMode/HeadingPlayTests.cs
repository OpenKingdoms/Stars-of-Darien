// HeadingPlayTests.cs - on the real engine, a drop-in model's front, the
// side the classic camera sees, faces that camera: a lodestone's card
// model on a building standing south, and a cart's model on the map.
// Needs okengine and the game files, and is ignored without them.
using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class HeadingPlayTests
    {
        GameRoot root;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator ADropInsFrontFacesTheClassicCamera()
        {
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            var view = GameObject.Find("OpenKingdoms");
            if (view != null) Object.Destroy(view);
            yield return null;
            root = GameRoot.Boot();
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "two castles";
            root.Setup.MapRevealed = true;
            root.Setup.LineOfSight = false;
            root.Setup.StartMana = 5000;
            root.Setup.Seats[0].Side = "ARAMON";
            root.Setup.Seats[1].Side = "TAROS";
            root.Setup.Seats[1].Difficulty = AiDifficulty.Easy;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 240f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
            root.Orders.Frozen = true;
            var b = root.Backend;
            var units = new UnitState[4096];
            int n = b.ReadUnits(units);
            var monarch = units.Take(n).First(u => u.Player == b.LocalPlayer && !b.UnitDefs[u.Def].IsBuilding && b.UnitDefs[u.Def].BuildOptions.Length > 0);

            // A lodestone on the nearest site.
            int lode = b.UnitDefs[monarch.Def].BuildOptions.First(o => b.UnitDefs[o].IsBuilding && b.UnitDefs[o].Name.ToUpperInvariant().Contains("LODE"));
            var features = new FeatureState[8192];
            int nf = b.ReadFeatures(features);
            Vector3 site = default;
            float best = float.MaxValue;
            for (int i = 0; i < nf; i++)
            {
                if (b.FeatureDefs[features[i].Def].Name.IndexOf("Mana", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                float d = (features[i].Position - monarch.Position).sqrMagnitude;
                if (d < best && b.CanBuildAt(lode, features[i].Position, 0, out var snapped)) { best = d; site = snapped; }
            }
            Assert.Less(best, float.MaxValue, "a lodestone site");
            Assert.IsTrue(b.Command(new GameCommand { Kind = CommandKind.Build, Unit = monarch.Handle, Target = site, TargetUnit = -1, BuildDef = lode }));
            int built = -1;
            for (int t = 0; t < 30000 && built < 0; t += 120)
            {
                b.Advance(120);
                n = b.ReadUnits(units);
                for (int i = 0; i < n; i++)
                    if (units[i].Def == lode && units[i].Player == b.LocalPlayer && units[i].BuildProgress >= 1f) built = units[i].Handle;
                if (t % 1200 == 0) yield return null;
            }
            Assert.GreaterOrEqual(built, 0, "the lodestone stands");

            // A cart set down beside it.
            var cart = b.FeatureDefs.First(d => string.Equals(d.Name, "Aracart01", System.StringComparison.OrdinalIgnoreCase));
            int placed = b.PlaceFeature(cart.Id, Mathf.FloorToInt(site.x) - 6, Mathf.FloorToInt(-site.z) + 1);
            Assert.GreaterOrEqual(placed, 0, "the cart is set down");
            nf = b.ReadFeatures(features);
            var cartAt = features[placed].Position;
            for (int i = 0; i < 5; i++) yield return null;

            // A model's front faces south at rest, toward the classic camera.
            var ents = root.World.Entities;
            Assert.IsTrue(ents.FeatureTurn(cartAt, 0.3f, out var cartTurn), "the cart is drawn as its model");
            Assert.Less((cartTurn * Vector3.back).z, -0.99f, "the cart's front faces the classic camera");
            // The card models are made from the player's own game files.
            if (CardOverride.For(b.UnitDefs[lode].ObjectName) == null) { Debug.Log("No card model for " + b.UnitDefs[lode].ObjectName); yield break; }
            Assert.IsTrue(ents.CardTurns.TryGetValue(built, out var cardTurn), "the lodestone is drawn with its card model");
            Assert.Less((cardTurn * Vector3.back).z, -0.99f, "the card's front faces the classic camera");
        }
    }
}
