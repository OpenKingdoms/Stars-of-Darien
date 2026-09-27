// EngineBackendTests.cs - IGameBackend on the real engine, the way the
// presentation's screens use it: the catalogue and a map preview before a
// game, then a skirmish loaded by pumping, and the snapshots, poses,
// players, economy and orders of a running battle. Needs okengine and the
// game files, and is ignored without them.
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class EngineBackendTests
    {
        EngineBackend backend;

        [OneTimeSetUp]
        public void Boot()
        {
            if (!EngineSettings.EngineAvailable) Assert.Ignore("okengine or the game files are missing");
            backend = new EngineBackend();
        }

        [OneTimeTearDown]
        public void End() => backend?.Dispose();

        static SkirmishSetup TwoCastles()
        {
            var s = new SkirmishSetup { MapId = "two castles", Seed = 7, LineOfSight = false, MapRevealed = true };
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ARAMON", Colour = 0, Team = 1 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Colour = 1, Team = 2 });
            return s;
        }

        [Test, Order(1)]
        public void TheCatalogueAndAPreviewComeBeforeAnyGame()
        {
            Assert.Greater(backend.Maps.Count, 5);
            MapInfo two = null;
            foreach (var m in backend.Maps) if (m.Id == "two castles") two = m;
            Assert.IsNotNull(two);
            Assert.GreaterOrEqual(two.MaxPlayers, 2);
            var preview = backend.MapPreview("two castles", 256);
            Assert.IsNotNull(preview);
            Assert.AreEqual(preview.Width * preview.Height * 4, preview.Pixels.Length);
            Assert.AreEqual(5, backend.Sides.Count);
        }

        [Test, Order(2)]
        public void ASkirmishLoadsByPumping()
        {
            backend.StartSkirmish(TwoCastles());
            Assert.AreEqual(GameStatus.Loading, backend.Status);
            LoadProgress p = default;
            int pumps = 0;
            for (; pumps < 5000 && !p.Done && !p.Failed; pumps++) p = backend.PumpLoading();
            Assert.Greater(pumps, 2, "the load comes in slices");
            Assert.IsTrue(p.Done, p.Error);
            Assert.AreEqual(GameStatus.Running, backend.Status);
            var t = backend.Terrain;
            Assert.IsNotNull(t);
            Assert.AreEqual(1f, t.CellSize);
            Assert.AreEqual(t.HeightsW * t.HeightsH, t.Heights.Length);
            Assert.IsNotNull(backend.TerrainChunk(t.Blocks[0]));
            Assert.Greater(backend.UnitDefs.Count, 20);
            Assert.GreaterOrEqual(backend.Players.Count, 2);
            Assert.IsTrue(backend.Players[0].IsLocal);
        }

        [Test, Order(3)]
        public void TheBattleReadsAndTakesOrders()
        {
            var units = new UnitState[512];
            int n = backend.ReadUnits(units);
            Assert.GreaterOrEqual(n, 2);
            int me = backend.LocalPlayer;
            int pick = -1;
            for (int i = 0; i < n && pick < 0; i++) if (units[i].Player == me) pick = i;
            Assert.GreaterOrEqual(pick, 0);
            var u = units[pick];
            Assert.AreEqual(u.Position.y, backend.GroundHeight(u.Position.x, u.Position.z), 1f);
            Assert.LessOrEqual(u.Position.z, 0f);

            var model = backend.GetModel(u.Model);
            Assert.IsNotNull(model);
            Assert.AreEqual(model.Positions.Length, model.VertexPiece.Length);
            Assert.Greater(model.Scale, 0f);
            var poses = new PiecePose[128];
            Assert.AreEqual(model.Pieces.Length, backend.ReadUnitPose(u.Handle, poses));
            Vector3 root = poses[0].Matrix.GetColumn(3);
            Assert.AreEqual(u.Position.x, root.x, 4f);
            Assert.AreEqual(u.Position.z, root.z, 4f);

            var def = backend.UnitDefs[u.Def];
            Assert.Greater(def.BuildOptions.Length, 0, "the monarch has a build menu");
            Assert.Greater(backend.UnitDefs[def.BuildOptions[0]].ManaCost, 0);
            var eco = backend.ReadEconomy(me);
            Assert.Greater(eco.Storage, 0f);

            var target = u.Position + new Vector3(30f, 0f, 0f);
            Assert.IsTrue(backend.Command(GameCommand.To(CommandKind.Move, u.Handle, target)));
            backend.Advance(2);
            var order = backend.ReadOrder(u.Handle);
            Assert.AreEqual(OrderKind.Move, order.Kind);
            Assert.AreEqual(target.x, order.Target.x, 2f);

            // A placement ghost: somewhere near the monarch can take its first building.
            int building = -1;
            foreach (int opt in def.BuildOptions) if (backend.UnitDefs[opt].IsBuilding) { building = opt; break; }
            Assert.GreaterOrEqual(building, 0);
            bool placed = false;
            for (int r = 6; r <= 40 && !placed; r += 2)
                for (int k = 0; k < 8 && !placed; k++)
                {
                    var at = u.Position + Quaternion.Euler(0, k * 45f, 0) * new Vector3(r, 0, 0);
                    if (backend.CanBuildAt(building, at, out var snapped))
                    {
                        placed = true;
                        Assert.AreEqual(at.x, snapped.x, 2f);
                        Assert.AreEqual(backend.GroundHeight(snapped.x, snapped.z), snapped.y, 0.01f);
                    }
                }
            Assert.IsTrue(placed, "some ground near the monarch takes a building");
            Assert.GreaterOrEqual(backend.QueuedCount(u.Handle, -1), 0);

            int need = backend.ReadFog(null, out int fw, out int fh);
            Assert.AreEqual(backend.Terrain.HeightsW * backend.Terrain.HeightsH, need);
            var fog = new byte[need];
            backend.ReadFog(fog, out fw, out fh);
            int inSight = 0;
            foreach (var f in fog) if (f == 2) inSight++;
            Assert.Greater(inSight, 0);
            Assert.AreEqual(120, backend.Advance(120));
            n = backend.ReadUnits(units);
            float x = u.Position.x;
            for (int i = 0; i < n; i++) if (units[i].Handle == u.Handle) x = units[i].Position.x;
            Assert.Greater(x, u.Position.x + 3f);
            Assert.GreaterOrEqual(backend.ReadProjectiles(new ProjectileState[64]), 0);

            // The Unit Browser: the monarch's walk, played outside the battle.
            CollectionAssert.Contains(def.Animations, "walk");
            uint before = backend.Tick;
            var a = new PiecePose[128];
            var b = new PiecePose[128];
            int pieces = backend.PoseModel(u.Model, "walk", 1.0f, a);
            Assert.Greater(pieces, 1);
            Assert.AreEqual(pieces, backend.PoseModel(u.Model, "walk", 1.12f, b));
            int moved = 0;
            for (int i = 0; i < pieces; i++) if (a[i].Matrix != b[i].Matrix) moved++;
            Assert.GreaterOrEqual(moved, 2, "the legs swing");
            Assert.AreEqual(before, backend.Tick, "the battle stood still");
            var effects = new EffectState[256];
            int ne = backend.ReadEffects(effects);
            for (int i = 0; i < Mathf.Min(ne, effects.Length); i++)
            {
                Assert.Greater(effects[i].Top, effects[i].Bottom);
                Assert.Greater(effects[i].UvMax.x, effects[i].UvMin.x);
                Assert.IsNotNull(backend.EffectStrip(effects[i].Strip));
            }
            var features = new FeatureState[1024];
            Assert.Greater(backend.ReadFeatures(features), 0);
        }
    }
}
