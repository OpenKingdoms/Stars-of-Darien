// RotationPlayTests.cs - a building turned a quarter in the placement
// preview goes up turned, covering its footprint with width and depth
// swapped, and the next placement of that kind starts at the same facing.
using System.Collections;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class RotationPlayTests
    {
        [UnityTest]
        public IEnumerator ALodgeTurnedAQuarterCoversItsSwappedFootprint()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            var root = GameRoot.Boot(mock);
            try
            {
                yield return null;
                root.Flow.Fire(FlowEvent.OpenSkirmish);
                root.Setup.MapId = "mock_highlands";
                root.Screens.StartGame();
                float deadline = Time.realtimeSinceStartup + 30f;
                while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.AreEqual(FlowState.Playing, root.Flow.State);

                var units = new UnitState[512];
                int n = mock.ReadUnits(units), monarch = -1;
                Vector3 near = default;
                for (int i = 0; i < n; i++)
                    if (units[i].Player == 1 && mock.UnitDefs[units[i].Def].Name.EndsWith("monarch")) { monarch = units[i].Handle; near = units[i].Position; }
                Assert.GreaterOrEqual(monarch, 0);
                int lodge = -1;
                foreach (var d in mock.UnitDefs) if (d.Side == "ARAMON" && d.Name.EndsWith("lodge")) lodge = d.Id;
                var fp = mock.UnitDefs[lodge].Footprint;
                Assert.AreNotEqual(fp.x, fp.y, "the mock lodge is not square");

                // Select the monarch and arm the lodge, then turn it once.
                mock.Select(new[] { monarch }, false);
                root.Orders.Arm(CommandKind.Build, lodge);
                Assert.IsTrue(root.Orders.Rotate(1));
                Assert.AreEqual(1, root.Orders.Facing);

                // Find open ground to the east and place it there.
                Vector3 site = default;
                bool found = false;
                for (int dx = 8; dx < 40 && !found; dx += 2)
                    found = mock.CanBuildAt(lodge, near + new Vector3(dx, 0, 0), 1, out site);
                Assert.IsTrue(found, "open ground for the lodge");
                Assert.IsTrue(mock.Command(new GameCommand { Kind = CommandKind.Build, Unit = monarch, Target = site, TargetUnit = -1, BuildDef = lodge, Facing = root.Orders.Facing }));
                for (int i = 0; i < 30 * 8; i++) mock.Advance(1);

                n = mock.ReadUnits(units);
                int built = -1;
                for (int i = 0; i < n; i++)
                    if (units[i].Def == lodge && (units[i].Position - site).sqrMagnitude < 0.01f) { built = i; break; }
                Assert.GreaterOrEqual(built, 0, "the lodge went up at the site");
                Assert.AreEqual(1, units[built].Facing);
                Assert.AreEqual(270f, units[built].Heading, 0.01f, "a quarter turn round from south, it faces west");
                var cells = mock.Occupied(units[built].Handle);
                Assert.AreEqual(new Vector2Int(fp.y, fp.x), cells.size, "width and depth are swapped");

                // A site overlapping the turned lodge is refused, whatever its facing.
                Assert.IsFalse(mock.CanBuildAt(lodge, site, 0, out _));
                Assert.IsFalse(mock.CanBuildAt(lodge, site, 1, out _));

                // The next placement of the lodge starts turned the same way.
                root.Orders.Disarm();
                root.Orders.Arm(CommandKind.Build, lodge);
                Assert.AreEqual(1, root.Orders.Facing, "the facing is remembered per building type");
                root.Orders.Disarm();
            }
            finally { Object.Destroy(root.gameObject); }
        }
    }
}
