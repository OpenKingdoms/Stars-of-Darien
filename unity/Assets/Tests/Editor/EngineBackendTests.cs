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
            // As GameRoot's default lineup has it: teams from 0, closed seats after.
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ARAMON", Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Colour = 1, Team = 1 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Closed, Side = "", Colour = 4, Team = 2 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Closed, Side = "", Colour = 5, Team = 3 });
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
            // Two sides on teams 0 and 1 are enemies, so nobody has won yet.
            Assert.AreEqual(GameStatus.Running, backend.Status);
            backend.Advance(30);
            Assert.AreEqual(GameStatus.Running, backend.Status);
            Assert.AreNotEqual(backend.Players[0].Team, backend.Players[1].Team);
            var t = backend.Terrain;
            Assert.IsNotNull(t);
            Assert.AreEqual(1f, t.CellSize);
            Assert.AreEqual(t.HeightsW * t.HeightsH, t.Heights.Length);
            Assert.IsNotNull(backend.TerrainChunk(t.Blocks[0]));
            Assert.Greater(backend.UnitDefs.Count, 20);
            Assert.GreaterOrEqual(backend.Players.Count, 2);
            Assert.IsTrue(backend.Players[0].IsLocal);
            // The lineup is the lobby's own.
            Assert.AreEqual("ARAMON", backend.Players[0].Side);
            Assert.AreEqual("TAROS", backend.Players[1].Side);
            Assert.AreEqual(1, backend.Players[1].Colour);
            Assert.IsTrue(backend.Players[1].IsComputer);
        }

        [Test, Order(5)]
        public void TheGamesOwnClickSelectsAndOrders()
        {
            var units = new UnitState[512];
            int n = backend.ReadUnits(units);
            int me = backend.LocalPlayer, mine = -1;
            for (int i = 0; i < n && mine < 0; i++)
                if (units[i].Player == me && (units[i].Flags & UnitFlags.Active) != 0) mine = i;
            Assert.GreaterOrEqual(mine, 0);
            var u = units[mine];
            backend.Cancel();
            backend.Click(u.Position, u.Handle, false);
            var sel = new int[8];
            Assert.AreEqual(1, backend.ReadSelection(sel));
            Assert.AreEqual(u.Handle, sel[0]);
            backend.Click(u.Position + new Vector3(18f, 0f, 0f), -1, false);
            backend.Advance(3);
            Assert.AreEqual(OrderKind.Move, backend.ReadOrder(u.Handle).Kind);
            Assert.IsTrue(backend.OrderSelection(CommandKind.Stop));
            backend.Advance(3);
            Assert.AreEqual(OrderKind.None, backend.ReadOrder(u.Handle).Kind);
            backend.Cancel();
            Assert.AreEqual(0, backend.ReadSelection(sel));
        }

        [Test, Order(6)]
        public void ThePointerIsTheGamesPickWithItsOwnArt()
        {
            var units = new UnitState[512];
            int n = backend.ReadUnits(units);
            int me = backend.LocalPlayer, mine = -1, theirs = -1;
            for (int i = 0; i < n; i++)
            {
                if ((units[i].Flags & UnitFlags.Active) == 0) continue;
                if (units[i].Player == me && mine < 0) mine = i;
                if (units[i].Player != me && theirs < 0) theirs = i;
            }
            Assert.IsTrue(mine >= 0 && theirs >= 0);
            var u = units[mine];
            var e = units[theirs];
            backend.Cancel();
            backend.Cancel();
            Assert.AreEqual(GameCursor.Select, backend.CursorAt(u.Position, u.Handle, out _));
            backend.Select(new[] { u.Handle }, false);
            Assert.AreEqual(GameCursor.Attack, backend.CursorAt(e.Position, e.Handle, out _));
            backend.Arm(CommandKind.Patrol);
            Assert.AreEqual(GameCursor.Patrol, backend.CursorAt(u.Position + new Vector3(20f, 0f, 0f), -1, out _));
            backend.Cancel();
            backend.Cancel();

            foreach (GameCursor c in System.Enum.GetValues(typeof(GameCursor)))
            {
                var art = backend.CursorArt(c);
                Assert.IsNotNull(art, c.ToString());
                foreach (var f in art)
                {
                    Assert.Greater(f.Image.Width, 0);
                    Assert.IsTrue(f.Hotspot.x >= 0 && f.Hotspot.x < f.Image.Width && f.Hotspot.y >= 0 && f.Hotspot.y < f.Image.Height, c.ToString());
                    Assert.Greater(f.Millis, 0);
                }
            }
            Assert.Greater(backend.CursorArt(GameCursor.Revive).Length, 1, "the revive pointer moves");
        }

        [Test, Order(4)]
        public void ASavedGameComesBack()
        {
            backend.Advance(60);
            var units = new UnitState[512];
            int n = backend.ReadUnits(units);
            uint tick = backend.Tick;
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "okunity-test.sav");
            Assert.IsTrue(backend.SaveGame(path));
            Assert.IsTrue(backend.SaveInfo(path, out string map, out uint savedTick, out _));
            Assert.AreEqual("two castles", map);
            Assert.AreEqual(tick, savedTick);
            backend.Advance(120);
            Assert.IsTrue(backend.LoadGame(path));
            LoadProgress p = default;
            for (int i = 0; i < 5000 && !p.Done && !p.Failed; i++) p = backend.PumpLoading();
            Assert.IsTrue(p.Done, p.Error);
            Assert.AreEqual(tick, backend.Tick);
            var again = new UnitState[512];
            Assert.AreEqual(n, backend.ReadUnits(again));
            for (int i = 0; i < n; i++) Assert.AreEqual(units[i].Position, again[i].Position);
            System.IO.File.Delete(path);
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
            Assert.IsNotEmpty(backend.UnitDefs[def.BuildOptions[0]].Title);
            var pic = backend.UnitPicture(def.BuildOptions[0]);
            Assert.IsNotNull(pic, "the build button has the game's picture");
            Assert.AreEqual(pic.Width * pic.Height * 4, pic.Pixels.Length);
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
            StringAssert.StartsWith("walk", backend.UnitAnimation(u.Handle), "a marching unit is walking");
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
            // The map editor: a height edit shows in Terrain and the ground at once.
            int cellsNeed = backend.ReadCells(null, out int cw, out int ch);
            Assert.AreEqual(cw * ch, cellsNeed);
            var hill = new byte[] { 220, 220, 220, 220 };
            int hx = cw / 3, hz = ch / 3;
            Assert.IsTrue(backend.EditCells(hx, hz, 2, 2, hill));
            Assert.AreEqual(220f / 16f, backend.Terrain.HeightAt(hx, hz), 0.001f);
            Assert.AreEqual(220f / 16f, backend.GroundHeight(hx + 0.5f, -(hz + 0.5f)), 0.5f);
            var features = new FeatureState[1024];
            Assert.Greater(backend.ReadFeatures(features), 0);
        }
    }
}
