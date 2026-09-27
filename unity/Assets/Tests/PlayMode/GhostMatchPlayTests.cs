// GhostMatchPlayTests.cs - a building's placement preview stands where
// the building will: its bounds match the finished building's, on the mock
// and, when the engine and game files are here, for three of the game's
// own structures including a lodestone on its site.
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class GhostMatchPlayTests
    {
        GameRoot root;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        IEnumerator Start(IGameBackend backend, string map)
        {
            root = GameRoot.Boot(backend);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            if (map != null) root.Setup.MapId = map;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 240f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, "the game loaded: " + root.LastError);
            root.Orders.Frozen = true;
        }

        // Builds def at a site with the monarch and compares the preview's
        // bounds there with the finished building's.
        IEnumerator BuildAndCompare(int monarch, int def, Vector3 site, int facing, List<string> report)
        {
            var b = root.Backend;
            Assert.IsTrue(b.CanBuildAt(def, site, facing, out var snapped), $"{b.UnitDefs[def].Name} can stand at {site}");
            var ghost = root.World.Entities.GhostBounds(new EntityRenderer.GhostState { Def = def, At = snapped, Facing = facing, Ok = true });
            Assert.IsTrue(b.Command(new GameCommand { Kind = CommandKind.Build, Unit = monarch, Target = snapped, TargetUnit = -1, BuildDef = def, Facing = facing }),
                $"the monarch takes the order to build {b.UnitDefs[def].Name}");
            var units = new UnitState[4096];
            int built = -1;
            for (int t = 0; t < 30 * 600 && built < 0; t += 30)
            {
                b.Advance(30);
                int n = b.ReadUnits(units);
                for (int i = 0; i < n; i++)
                    if (units[i].Def == def && units[i].BuildProgress >= 1f &&
                        new Vector2(units[i].Position.x - snapped.x, units[i].Position.z - snapped.z).sqrMagnitude < 1f) built = units[i].Handle;
            }
            Assert.GreaterOrEqual(built, 0, $"{b.UnitDefs[def].Name} was finished at the site");
            yield return null;
            yield return null;
            var real = root.World.Entities.UnitBounds(built);
            report.Add($"{b.UnitDefs[def].Name} facing {facing}: ghost {ghost.center} {ghost.size}, built {real.center} {real.size}");
            Assert.Less(Vector3.Distance(ghost.center, real.center), 0.35f, $"{b.UnitDefs[def].Name}: the preview stands where it is built. " + string.Join(" | ", report));
            for (int k = 0; k < 3; k++)
                Assert.AreEqual(real.size[k], ghost.size[k], Mathf.Max(0.3f, real.size[k] * 0.2f), $"{b.UnitDefs[def].Name}: same size on axis {k}. " + string.Join(" | ", report));
        }

        static int Monarch(IGameBackend b, out Vector3 at)
        {
            var units = new UnitState[4096];
            int n = b.ReadUnits(units);
            at = default;
            for (int i = 0; i < n; i++)
            {
                if (units[i].Player != b.LocalPlayer) continue;
                if (b.UnitDefs[units[i].Def].BuildOptions.Length > 0 && !b.UnitDefs[units[i].Def].IsBuilding) { at = units[i].Position; return units[i].Handle; }
            }
            return -1;
        }

        static Vector3 OpenGround(IGameBackend b, int def, Vector3 near, int facing)
        {
            for (int r = 6; r < 60; r += 2)
                for (int a = 0; a < 16; a++)
                {
                    var p = near + Quaternion.Euler(0, a * 22.5f, 0) * Vector3.forward * r;
                    if (b.CanBuildAt(def, p, facing, out var s)) return s;
                }
            Assert.Fail("no open ground for " + b.UnitDefs[def].Name);
            return default;
        }

        [UnityTest]
        public IEnumerator OnTheMockThePreviewMatchesTheBuilding()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            yield return Start(mock, "mock_highlands");
            int monarch = Monarch(mock, out var near);
            int lodge = -1;
            foreach (var d in mock.UnitDefs) if (d.Side == mock.PlayerById(mock.LocalPlayer).Side && d.IsBuilding) lodge = d.Id;
            var report = new List<string>();
            yield return BuildAndCompare(monarch, lodge, OpenGround(mock, lodge, near, 0), 0, report);
            yield return BuildAndCompare(monarch, lodge, OpenGround(mock, lodge, near, 1), 1, report);
            Debug.Log("Ghost match: " + string.Join(" | ", report));
        }

        [UnityTest]
        public IEnumerator OnTheEngineThePreviewMatchesThreeBuildings()
        {
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            var view = GameObject.Find("OpenKingdoms");
            if (view != null) Object.Destroy(view);
            yield return null;
            yield return null;
            yield return Start(null, "abnar's terrace");
            var b = root.Backend;
            int monarch = Monarch(b, out var near);
            Assert.GreaterOrEqual(monarch, 0);
            int[] options = new int[0];
            var units = new UnitState[4096];
            int n = b.ReadUnits(units);
            for (int i = 0; i < n; i++) if (units[i].Handle == monarch) options = b.UnitDefs[units[i].Def].BuildOptions;
            int lode = -1;
            var others = new List<int>();
            foreach (int o in options)
            {
                if (!b.UnitDefs[o].IsBuilding) continue;
                if (b.UnitDefs[o].Name.ToUpperInvariant().Contains("LODE")) lode = o;
                else if (others.Count < 2) others.Add(o);
            }
            Assert.GreaterOrEqual(lode, 0, "the monarch builds lodestones");
            var report = new List<string>();
            // A lodestone on the nearest site.
            var feats = new FeatureState[8192];
            int fc = b.ReadFeatures(feats);
            Vector3 site = default;
            float best = float.MaxValue;
            for (int i = 0; i < fc; i++)
            {
                if (b.FeatureDefs[feats[i].Def].Name.IndexOf("Mana", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                float d = (feats[i].Position - near).sqrMagnitude;
                if (d < best && b.CanBuildAt(lode, feats[i].Position, 0, out _)) { best = d; site = feats[i].Position; }
            }
            Assert.Less(best, float.MaxValue, "a lodestone site that takes a lodestone");
            yield return BuildAndCompare(monarch, lode, site, 0, report);
            foreach (int o in others) yield return BuildAndCompare(monarch, o, OpenGround(b, o, near, 0), 0, report);
            Debug.Log("Ghost match: " + string.Join(" | ", report));
        }
    }
}
