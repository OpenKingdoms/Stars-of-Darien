// GatePlayTests.cs - on the real engine, a gate turns a quarter in the
// placement preview like any other building: R turns the armed gate, the
// preview stands turned where the pointer is, the click places it there,
// and the gate goes up turned, filling the preview. Every shipped gate
// can turn. Needs okengine and the game files, and is ignored without them.
using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class GatePlayTests
    {
        GameRoot root;
        PointerFrame frame;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator AGateTurnedInThePreviewGoesUpTurned()
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
            var b = root.Backend;

            var gates = b.UnitDefs.Where(d => d.IsBuilding && d.Name.ToUpperInvariant().EndsWith("GATE")).ToArray();
            Assert.GreaterOrEqual(gates.Length, 3, "the shipped gates");
            foreach (var g in gates) Assert.IsTrue(b.CanRotate(g.Id), g.Name + " turns");

            root.Orders.Classic = true;
            frame = new PointerFrame { Focused = true, Dpi = 96 };
            root.Orders.Formation.Source = () =>
            {
                var r = frame;
                frame.LeftDown = frame.LeftUp = frame.RightDown = frame.RightUp = false;
                return r;
            };
            var units = new UnitState[4096];
            int n = b.ReadUnits(units);
            var king = units.Take(n).First(u => u.Player == b.LocalPlayer && !b.UnitDefs[u.Def].IsBuilding && b.UnitDefs[u.Def].BuildOptions.Length > 0);
            int gate = b.UnitDefs[king.Def].BuildOptions.First(o => gates.Any(g => g.Id == o));
            var fp = b.UnitDefs[gate].Footprint;
            Assert.AreNotEqual(fp.x, fp.y, "the gate is longer than it is deep");

            // Open ground for the gate turned, with the camera on it.
            Vector3 site = default;
            bool found = false;
            for (int r = 8; r < 60 && !found; r += 2)
                for (int a = 0; a < 16 && !found; a++)
                    found = b.CanBuildAt(gate, king.Position + Quaternion.Euler(0, a * 22.5f, 0) * Vector3.forward * r, 1, out site);
            Assert.IsTrue(found, "open ground for the turned gate");
            var cam = root.World.Camera;
            cam.focus = site;
            cam.yaw = 0f;
            cam.pitch = GameCamera.ClassicPitch;
            cam.Zoom(40f);
            b.Select(new[] { king.Handle }, false);
            for (int i = 0; i < 3; i++) yield return null;

            // The card arms it, the pointer rests on the site, and R turns it.
            root.Orders.Arm(CommandKind.Build, gate);
            frame.Screen = cam.GetComponent<Camera>().WorldToScreenPoint(site);
            yield return null;
            yield return null;
            Assert.AreEqual(0, root.Orders.Facing);
            Assert.IsTrue(root.Orders.Rotate(1), "R turns the gate");
            yield return null;
            yield return null;
            Assert.AreEqual(CommandKind.Build, root.Orders.Armed, "still placing the gate");
            Assert.AreEqual(1, root.Orders.Facing);
            Assert.IsTrue(root.World.Entities.Ghost.HasValue, "the preview shows");
            var ghost = root.World.Entities.Ghost.Value;
            Assert.AreEqual(1, ghost.Facing, "the preview stands turned");
            Assert.IsTrue(root.Orders.GhostOk, "the turned gate fits there");
            Assert.Less(Vector2.Distance(new Vector2(root.Orders.GhostAt.x, root.Orders.GhostAt.z), new Vector2(site.x, site.z)), 0.01f, "the preview is on the site");

            // The click places it, and it goes up turned where the preview stood.
            frame.LeftDown = frame.LeftHeld = true;
            yield return null;
            frame.LeftHeld = false;
            frame.LeftUp = true;
            yield return null;
            yield return null;
            root.Orders.Frozen = true;
            int built = -1;
            for (int t = 0; t < 40000 && built < 0; t += 120)
            {
                b.Advance(120);
                n = b.ReadUnits(units);
                for (int i = 0; i < n; i++)
                    if (units[i].Def == gate && units[i].Player == b.LocalPlayer && units[i].BuildProgress >= 1f) built = i;
                if (t % 1200 == 0) yield return null;
            }
            Assert.GreaterOrEqual(built, 0, "the gate went up");
            yield return null;
            yield return null;
            var g0 = units[built];
            Assert.AreEqual(1, g0.Facing, "it stands turned");
            Assert.AreEqual(270f, g0.Heading, 0.01f, "a quarter round from south, it faces west");
            Assert.Less(Vector2.Distance(new Vector2(g0.Position.x, g0.Position.z), new Vector2(site.x, site.z)), 0.01f, "where the preview stood");
            // The wind turns the gate's flags as the original's does, and the
            // preview stands at rest, so the two are matched without them.
            var turned = PreviewPieces.Turned(b, g0.Handle);
            var preview = root.World.Entities.GhostBounds(ghost, turned);
            var real = root.World.Entities.UnitBounds(g0.Handle, turned);
            Assert.Less(Vector3.Distance(preview.center, real.center), 0.35f, $"preview {preview} built {real}");
            for (int k = 0; k < 3; k++)
                Assert.AreEqual(real.size[k], preview.size[k], Mathf.Max(0.3f, real.size[k] * 0.2f), $"axis {k}: preview {preview} built {real}");
            Assert.Greater(real.size.z, real.size.x, "turned, the gate runs north and south");
        }
    }
}
