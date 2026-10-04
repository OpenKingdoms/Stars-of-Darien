// FlyerOverSeaPlayTests.cs - an Aramon dragon on the real engine, flown
// out over the open water farthest from shore and stopped there, stays up
// through the search for ground and is drawn over the water, every piece
// of it above the sea, at the height it cruised. A flyer lands only on dry
// ground and the engine holds it over the sea, not the sea floor. Needs
// okengine and the game files, and is ignored without them.
using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class FlyerOverSeaPlayTests
    {
        GameRoot root;
        EngineBackend engine;
        readonly UnitState[] units = new UnitState[1024];

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        UnitState Read(int handle)
        {
            int n = engine.ReadUnits(units);
            for (int i = 0; i < n; i++) if (units[i].Handle == handle) return units[i];
            Assert.Fail("unit " + handle + " is gone");
            return default;
        }

        // The open water farthest from any dry ground or the map's edge, and
        // that distance in world units.
        static Vector3 FarthestFromShore(MapTerrain t, out float clear)
        {
            int w = t.HeightsW, h = t.HeightsH;
            var d = new int[w * h];
            var q = new System.Collections.Generic.Queue<int>();
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    int i = z * w + x;
                    d[i] = int.MaxValue;
                    if (t.Sample(x * t.CellSize, -z * t.CellSize) >= t.SeaLevel) { d[i] = 0; q.Enqueue(i); }
                }
            while (q.Count > 0)
            {
                int i = q.Dequeue(), x = i % w, z = i / w;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                        int n = nz * w + nx;
                        if (d[n] > d[i] + 1) { d[n] = d[i] + 1; q.Enqueue(n); }
                    }
            }
            int best = -1, bestD = 0;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    int e = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(z, h - 1 - z));
                    int c = Mathf.Min(d[z * w + x], e);
                    if (c > bestD) { bestD = c; best = z * w + x; }
                }
            Assert.Greater(best, -1, "the map has open water");
            clear = bestD * t.CellSize;
            float bx = best % w * t.CellSize, bz = -(best / w) * t.CellSize;
            return new Vector3(bx, t.Sample(bx, bz), bz);
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator ADragonStoppedOverOpenSeaIsDrawnOverTheWater()
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
            // The battle stands still and the test moves it on.
            root.Options.GameSpeed = 0;

            var defs = engine.UnitDefs;
            int drag = Enumerable.Range(0, defs.Count).First(d => string.Equals(defs[d].Name, "aradrag", System.StringComparison.OrdinalIgnoreCase));
            int h = OkEngine.okx_place_unit(drag, engine.LocalPlayer);
            Assert.GreaterOrEqual(h, 0, "a dragon is placed");
            float sea = engine.Terrain.SeaLevel;
            Assert.Greater(sea, 0f, "two castles has a sea");
            int tps = Mathf.Max(1, engine.TicksPerSecond);

            // On past the open water, stopped as it flies over it.
            var at = FarthestFromShore(engine.Terrain, out float clear);
            Assert.Greater(clear, 20f, "two castles has open sea");
            var u = Read(h);
            var way = new Vector3(at.x - u.Position.x, 0f, at.z - u.Position.z).normalized;
            Assert.IsTrue(engine.Command(GameCommand.To(CommandKind.Move, h, at + way * 20f)));
            bool stopped = false;
            float cruise = 0f;
            for (int t = 0; t < 240 * tps && !stopped; t++)
            {
                engine.Advance(1);
                u = Read(h);
                cruise = Mathf.Max(cruise, u.Altitude);
                bool air = (u.Flags & UnitFlags.Airborne) != 0;
                if (air && new Vector2(u.Position.x - at.x, u.Position.z - at.z).magnitude < 3f)
                    stopped = engine.Command(GameCommand.To(CommandKind.Stop, h, u.Position));
            }
            Assert.IsTrue(stopped, "it flew out over the sea and took the stop");
            Assert.Greater(cruise, 1f, "it climbed to its cruise height");

            // Twenty seconds on, looked at each second: up through the first
            // search, never under the sea, down only on dry ground, and
            // drawn over the water at its cruise height while over it.
            var ents = root.World.Entities;
            ents.Watch = h;
            var cam = root.World.Camera;
            for (int s = 1; s <= 20; s++)
            {
                engine.Advance(tps);
                u = Read(h);
                cam.focus = new Vector3(u.Position.x, sea, u.Position.z);
                for (int f = 0; f < 3; f++) yield return null;
                bool air = (u.Flags & UnitFlags.Airborne) != 0;
                float ground = engine.GroundHeight(u.Position.x, u.Position.z);
                string when = " " + s + " s after the stop";
                if (s <= 3) Assert.IsTrue(air, "still in the air" + when);
                if (!air) Assert.GreaterOrEqual(ground, sea, "down only on dry ground" + when);
                else if (ground < sea) Assert.AreEqual(cruise, u.Altitude, 0.05f, "at its cruise height over the sea" + when);
                Assert.IsTrue(ents.IsDrawn(h), "drawn" + when);
                Assert.Greater(u.Position.y + ents.VisualLift(h), sea, "drawn over the water" + when);
                Assert.Greater(ents.WatchedCount, 0);
                for (int p = 0; p < ents.WatchedCount; p++)
                    Assert.Greater(ents.Watched[p].GetColumn(3).y, sea, "piece " + p + " drawn over the water" + when);
            }
        }
    }
}
