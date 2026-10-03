// SelectKeysPlayTests.cs - Ctrl+Z on the real engine, in both schemes:
// with one unit selected it takes every unit of the player's of that type
// and leaves the rest. Needs okengine and the game files, and is ignored
// without them.
using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class SelectKeysPlayTests
    {
        GameRoot root;
        EngineBackend engine;
        readonly UnitState[] units = new UnitState[1024];

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        IEnumerator Begin()
        {
            if (!EngineSettings.EngineAvailable) Assert.Ignore("okengine or the game files are missing");
            engine = new EngineBackend();
            root = GameRoot.Boot(engine);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "two castles";
            root.Setup.MapRevealed = true;
            root.Setup.LineOfSight = false;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 240f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, "the battle loaded: " + root.LastError);
            root.Options.GameSpeed = 0;
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator CtrlZTakesEveryUnitOfTheSelectedTypeInBothSchemes()
        {
            yield return Begin();
            int me = engine.LocalPlayer;
            var defs = engine.UnitDefs;
            int n = engine.ReadUnits(units);
            var monarch = units.Take(n).First(u => u.Player == me && !defs[u.Def].IsBuilding);
            string side = defs[monarch.Def].Side;
            int def = Enumerable.Range(0, defs.Count).First(d => d != monarch.Def && defs[d].Side == side &&
                !defs[d].IsBuilding && !defs[d].CanFly && defs[d].Category != null && defs[d].Category.Contains("MELEE"));
            int a = OkEngine.okx_place_unit(def, me), b = OkEngine.okx_place_unit(def, me);
            Assert.IsTrue(a >= 0 && b >= 0, "two " + defs[def].Name + " placed");
            yield return null;
            foreach (bool classic in new[] { true, false })
            {
                root.Orders.Classic = classic;
                engine.Cancel();
                root.Orders.Selected.Clear();
                if (classic) engine.Select(new[] { a }, false);
                else root.Orders.Selected.Add(a);
                Assert.IsTrue(root.Orders.PressSelectKey(KeyCode.Z, false));
                string scheme = classic ? "classic" : "modern";
                Assert.IsTrue(root.Orders.Selected.Contains(a) && root.Orders.Selected.Contains(b), "both " + defs[def].Name + ", " + scheme);
                Assert.IsFalse(root.Orders.Selected.Contains(monarch.Handle), "not the monarch, " + scheme);
                // Ctrl+M then takes the monarch in place of them.
                Assert.IsTrue(root.Orders.PressSelectKey(KeyCode.M, false));
                Assert.IsTrue(root.Orders.Selected.Contains(monarch.Handle), "the monarch, " + scheme);
                Assert.IsFalse(root.Orders.Selected.Contains(a), "and nothing else, " + scheme);
                yield return null;
            }
        }
    }
}
