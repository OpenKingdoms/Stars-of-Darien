// MockBackendTests.cs - the fake engine loads a skirmish, runs it, takes
// orders and hands out terrain, models and poses the way the contract says.
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class MockBackendTests
    {
        static MockBackend Loaded(string map = "mock_isles")
        {
            var b = new MockBackend { StageSeconds = 0 };
            var s = new SkirmishSetup { MapId = map, Seed = 3 };
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ARAMON", Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Colour = 1, Team = 1 });
            b.StartSkirmish(s);
            for (int i = 0; i < 20 && !b.PumpLoading().Done; i++) { }
            return b;
        }

        [Test]
        public void LoadingFinishesAndTheGameRuns()
        {
            var b = Loaded();
            Assert.AreEqual(GameStatus.Running, b.Status);
            Assert.AreEqual(2, b.Players.Count);
            Assert.AreEqual(30, b.Advance(30));
            Assert.AreEqual(30u, b.Tick);
            var units = new UnitState[256];
            int n = b.ReadUnits(units);
            Assert.AreEqual(26, n, "each side starts with a lodge, a monarch, seven soldiers, a mage, a healer, a wagon and a flyer");
        }

        // Headings agree with the engine's: a building stands facing south,
        // 180, and so does a sprite feature, a picture facing the camera.
        [Test]
        public void BuildingsAndPicturesFaceSouth()
        {
            var b = Loaded();
            var units = new UnitState[256];
            int n = b.ReadUnits(units), buildings = 0;
            for (int i = 0; i < n; i++)
                if (b.UnitDefs[units[i].Def].IsBuilding) { buildings++; Assert.AreEqual(180f, units[i].Heading, 0.01f, "a building faces south"); }
            Assert.Greater(buildings, 0);
            var fs = new FeatureState[4096];
            int nf = b.ReadFeatures(fs), pictures = 0;
            for (int i = 0; i < nf; i++)
                if (fs[i].Model < 0) { pictures++; Assert.AreEqual(180f, fs[i].Heading, 0.01f, "a picture faces south"); }
            Assert.Greater(pictures, 0);
        }

        // A knight walks out and back. Ground it saw is dimmed after with
        // line of sight on, and stays clear with it off.
        static (int dim, int clear) Scouted(bool lineOfSight)
        {
            var b = new MockBackend { StageSeconds = 0, DamageScale = 0 };
            var s = new SkirmishSetup { MapId = "mock_highlands", Seed = 3, LineOfSight = lineOfSight };
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ARAMON", Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Colour = 1, Team = 1 });
            b.StartSkirmish(s);
            for (int i = 0; i < 20 && !b.PumpLoading().Done; i++) { }
            var units = new UnitState[256];
            int n = b.ReadUnits(units), knight = -1;
            for (int i = 0; i < n && knight < 0; i++)
                if (units[i].Player == b.LocalPlayer && b.RoleOf(units[i].Def) == MockBackend.Role.Knight) knight = i;
            var home = units[knight].Position;
            var out1 = home + (new Vector3(80, 0, -64) - home).normalized * 34f;
            foreach (var to in new[] { out1, home })
            {
                b.Command(GameCommand.To(CommandKind.Move, units[knight].Handle, to));
                b.Advance(1200);
            }
            var fog = new byte[b.ReadFog(null, out _, out _)];
            b.ReadFog(fog, out _, out _);
            int dim = 0, clear = 0;
            foreach (byte f in fog) { if (f == 1) dim++; else if (f == 2) clear++; }
            return (dim, clear);
        }

        [Test]
        public void GroundSeenBeforeIsDimmedOnlyWithLineOfSight()
        {
            var on = Scouted(true);
            var off = Scouted(false);
            Assert.Greater(on.dim, 0, "line of sight on dims ground seen before");
            Assert.AreEqual(0, off.dim, "line of sight off never dims");
            Assert.AreEqual(on.clear + on.dim, off.clear, "it keeps that ground clear instead");
        }

        [Test]
        public void TheTerrainMatchesItsBlocksAndChunks()
        {
            var b = Loaded("mock_highlands");
            var t = b.Terrain;
            Assert.AreEqual(161, t.HeightsW);
            Assert.AreEqual(129, t.HeightsH);
            Assert.AreEqual(new Vector2(160, 128), t.Size);
            Assert.AreEqual(t.BlocksW * t.BlocksH * 3, t.Blocks.Length);
            for (int i = 0; i < t.BlocksW * t.BlocksH; i++)
                Assert.That(t.Blocks[3 * i], Is.InRange(0, t.ChunkCount - 1));
            var chunk = b.TerrainChunk(0);
            Assert.AreEqual(chunk.Width * chunk.Height * 4, chunk.Pixels.Length);
            Assert.AreEqual(t.HeightAt(10, 20), b.GroundHeight(10, -20), 1e-4f);
        }

        [Test]
        public void AMoveOrderMovesTheUnit()
        {
            var b = Loaded();
            var units = new UnitState[256];
            int n = b.ReadUnits(units);
            UnitState knight = default;
            for (int i = 0; i < n; i++)
                if (units[i].Player == 1 && b.UnitDefs[units[i].Def].Name.EndsWith("knight")) { knight = units[i]; break; }
            var goal = knight.Position + new Vector3(3, 0, 0);
            Assert.IsTrue(b.Command(GameCommand.To(CommandKind.Move, knight.Handle, goal)));
            b.Advance(60);
            n = b.ReadUnits(units);
            for (int i = 0; i < n; i++)
                if (units[i].Handle == knight.Handle)
                    Assert.Less(Vector2.Distance(new Vector2(units[i].Position.x, units[i].Position.z), new Vector2(goal.x, goal.z)), 0.5f);
        }

        [Test]
        public void TheMockFlyerTakesOffAndLands()
        {
            var b = Loaded();
            var units = new UnitState[256];
            int n = b.ReadUnits(units);
            UnitState flyer = default;
            for (int i = 0; i < n; i++)
                if (units[i].Player == b.LocalPlayer && b.UnitDefs[units[i].Def].CanFly) flyer = units[i];
            var def = b.UnitDefs[flyer.Def];
            Assert.AreEqual("mockflyer", def.ObjectName);
            Assert.IsFalse(def.Hovers);
            Assert.AreEqual(0f, flyer.Altitude);
            Assert.IsTrue(b.Command(GameCommand.To(CommandKind.Move, flyer.Handle, flyer.Position + new Vector3(12, 0, 0))));

            bool rose = false, cruised = false, landed = false;
            float last = 0f;
            for (int tick = 0; tick < 30 * MockBackend.Tps && !landed; tick++)
            {
                b.Advance(1);
                n = b.ReadUnits(units);
                for (int i = 0; i < n; i++)
                {
                    if (units[i].Handle != flyer.Handle) continue;
                    var u = units[i];
                    bool airborne = (u.Flags & UnitFlags.Airborne) != 0;
                    Assert.AreEqual(b.GroundHeight(u.Position.x, u.Position.z) + u.Altitude, u.Position.y, 1e-4f);
                    Assert.LessOrEqual(Mathf.Abs(u.Altitude - last), def.MaxSpeed / MockBackend.Tps + 1e-4f, "it climbs and descends at top speed");
                    if (airborne && u.Altitude > last) rose = true;
                    if (airborne && u.Altitude >= def.CruiseAltitude - 1e-4f) cruised = true;
                    if (cruised && !airborne && u.Altitude == 0f) landed = true;
                    last = u.Altitude;
                }
            }
            Assert.IsTrue(rose, "it took off");
            Assert.IsTrue(cruised, "it reached its cruise height");
            Assert.IsTrue(landed, "it landed once there");
        }

        [Test]
        public void ModelsPoseEveryPiece()
        {
            var b = Loaded();
            int model = b.LoadModel("aramonknight", 0);
            var m = b.GetModel(model);
            Assert.IsNotNull(m);
            Assert.AreEqual(m.Positions.Length, m.VertexPiece.Length);
            var poses = new PiecePose[32];
            Assert.AreEqual(m.Pieces.Length, b.PoseModel(model, "walk", 0.25f, poses));
            Assert.AreEqual(-1, b.LoadModel("no such thing", 0));
        }

        [Test]
        public void AFightEndsInVictoryOrDefeat()
        {
            var b = Loaded("mock_frost");
            var units = new UnitState[256];
            // Every second, send the player's soldiers at the first enemy left.
            for (int s = 0; s < 1800 && b.Status == GameStatus.Running; s++)
            {
                int n = b.ReadUnits(units), enemy = -1;
                for (int i = 0; i < n && enemy < 0; i++)
                    if (units[i].Player == 2 && (units[i].Flags & UnitFlags.Dying) == 0) enemy = units[i].Handle;
                for (int i = 0; i < n; i++)
                    if (units[i].Player == 1 && enemy >= 0)
                        b.Command(new GameCommand { Kind = CommandKind.Attack, Unit = units[i].Handle, TargetUnit = enemy, BuildDef = -1 });
                b.Advance(30);
            }
            Assert.That(b.Status, Is.EqualTo(GameStatus.Victory).Or.EqualTo(GameStatus.Defeat));
            b.EndGame();
            Assert.AreEqual(GameStatus.Idle, b.Status);
        }
    }
}
