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
            Assert.AreEqual(24, n, "each side starts with a lodge, a monarch, seven soldiers, a mage, a healer and a wagon");
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
                if (units[i].Player == 0 && b.UnitDefs[units[i].Def].Name.EndsWith("knight")) { knight = units[i]; break; }
            var goal = knight.Position + new Vector3(3, 0, 0);
            Assert.IsTrue(b.Command(GameCommand.To(CommandKind.Move, knight.Handle, goal)));
            b.Advance(60);
            n = b.ReadUnits(units);
            for (int i = 0; i < n; i++)
                if (units[i].Handle == knight.Handle)
                    Assert.Less(Vector2.Distance(new Vector2(units[i].Position.x, units[i].Position.z), new Vector2(goal.x, goal.z)), 0.5f);
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
                    if (units[i].Player == 1 && (units[i].Flags & UnitFlags.Dying) == 0) enemy = units[i].Handle;
                for (int i = 0; i < n; i++)
                    if (units[i].Player == 0 && enemy >= 0)
                        b.Command(new GameCommand { Kind = CommandKind.Attack, Unit = units[i].Handle, TargetUnit = enemy, BuildDef = -1 });
                b.Advance(30);
            }
            Assert.That(b.Status, Is.EqualTo(GameStatus.Victory).Or.EqualTo(GameStatus.Defeat));
            b.EndGame();
            Assert.AreEqual(GameStatus.Idle, b.Status);
        }
    }
}
