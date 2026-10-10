// DebrisTests.cs - chunks fall and come to rest on the ground, wait on their
// hold until the swap, ride a toppling trunk away from the blow, keep to
// the quality's caps, and cost no allocation a frame. A thrown piece is
// hidden on its unit while the unit is there.
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class DebrisTests
    {
        static readonly Vector3[] Cube =
        {
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f),
        };

        Mesh mesh;
        Material mat;
        ChunkDraw[] draws;

        [SetUp]
        public void Make()
        {
            mesh = new Mesh();
            mesh.SetVertices(Cube);
            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6 }, 0);
            mat = Looks.Model(null);
            draws = new[] { new ChunkDraw { Mesh = mesh, Submesh = 0, Material = mat } };
        }

        [TearDown]
        public void Free()
        {
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(mat);
        }

        static Debris Field(float ground = 2f, BreakBudget? budget = null) =>
            new Debris(budget ?? BreakBudget.For(EffectsQuality.High)) { Ground = (x, z) => ground };

        static void Run(Debris d, float seconds)
        {
            for (float t = 0f; t < seconds; t += 0.02f) d.Step(0.02f);
        }

        [Test]
        public void AChunkFallsAndComesToRestOnTheGround()
        {
            var d = Field();
            int i = d.Spawn(draws, Matrix4x4.Translate(new Vector3(0f, 6f, 0f)), Vector3.zero, Cube, new Vector3(1f, 2f, 0f), new Vector3(0f, 0f, 5f));
            Run(d, 0.1f);
            Assert.AreEqual(Debris.State.Flying, d.StateOf(i));
            Run(d, 6f);
            Assert.AreEqual(Debris.State.Resting, d.StateOf(i));
            var m = d.MatrixOf(i);
            float low = float.MaxValue;
            foreach (var c in Cube) low = Mathf.Min(low, m.MultiplyPoint3x4(c).y);
            Assert.AreEqual(2f, low, 0.05f, "it lies on the ground");
            Assert.Greater(d.PositionOf(i).x, 0.5f, "it went the way it was thrown");
            Assert.AreEqual(0, d.Moving);
            Assert.AreEqual(1, d.Lying);
        }

        [Test]
        public void AChunkLiesAWhileAndThenSinksAway()
        {
            var d = Field(0f, BreakBudget.For(EffectsQuality.Low));
            int i = d.Spawn(draws, Matrix4x4.Translate(new Vector3(0f, 1f, 0f)), Vector3.zero, Cube, Vector3.zero, Vector3.zero);
            Run(d, 2f);
            Assert.AreEqual(Debris.State.Resting, d.StateOf(i));
            Run(d, BreakBudget.For(EffectsQuality.Low).Rest + Debris.SinkSeconds + 0.5f);
            Assert.AreEqual(Debris.State.Free, d.StateOf(i));
            Assert.AreEqual(0, d.Active);
        }

        [Test]
        public void AHeldChunkWaitsForTheSwapAndThenFades()
        {
            var d = Field();
            int hold = d.NewHold();
            var at = Matrix4x4.Translate(new Vector3(3f, 2.5f, 0f));
            int i = d.Spawn(draws, at, Vector3.zero, Cube, Vector3.up, Vector3.zero, 0f, hold);
            Run(d, 3f);
            Assert.AreEqual(Debris.State.Waiting, d.StateOf(i));
            Assert.AreEqual(at, d.MatrixOf(i), "it stands where the model stood");
            d.Release(hold, true);
            Run(d, 0.1f);
            Assert.AreEqual(Debris.State.Fading, d.StateOf(i));
            Run(d, Debris.FadeSeconds);
            Assert.AreEqual(Debris.State.Free, d.StateOf(i));
        }

        [Test]
        public void ATrunkTopplesAwayFromTheBlowAndBreaksWhereItLands()
        {
            var d = Field(0f);
            var away = Vector3.right;
            int fall = d.BeginFall(new Vector3(0.2f, 0f, 0f), Vector3.Cross(Vector3.up, away), 4f, 0.4f);
            Assert.GreaterOrEqual(fall, 0);
            var pieces = new int[4];
            for (int k = 0; k < 4; k++)
                pieces[k] = d.Spawn(draws, Matrix4x4.TRS(new Vector3(0f, 0.5f + k, 0f), Quaternion.identity, new Vector3(0.4f, 1f, 0.4f)), Vector3.zero, Cube, Vector3.zero, Vector3.zero, 0f, 0, fall);
            Run(d, 0.3f);
            Assert.AreEqual(Debris.State.Riding, d.StateOf(pieces[3]), "the trunk is still falling");
            Assert.Greater(d.MatrixOf(pieces[3]).GetColumn(3).x, 0.1f, "leaning away from the blow");
            Run(d, 2f);
            Assert.AreNotEqual(Debris.State.Riding, d.StateOf(pieces[3]), "it broke on landing");
            Run(d, 6f);
            Assert.Greater(d.PositionOf(pieces[3]).x, 2.5f, "the top came down far from the foot");
            Assert.Less(d.PositionOf(pieces[3]).y, 0.8f);
        }

        [Test]
        public void NoMoreChunksFlyThanTheQualityAllows()
        {
            var low = BreakBudget.For(EffectsQuality.Low);
            var d = Field(0f, low);
            Assert.IsTrue(d.Room(low.Flying));
            for (int k = 0; k < low.Flying; k++) d.Spawn(draws, Matrix4x4.Translate(Vector3.up * 5f), Vector3.zero, Cube, Vector3.zero, Vector3.zero);
            Assert.IsFalse(d.Room(1), "the Low budget is spent");
            Run(d, 4f);
            Assert.IsTrue(d.Room(low.Flying), "once they lie, more may fly");
            Assert.Less(BreakBudget.For(EffectsQuality.Low).Flying, BreakBudget.For(EffectsQuality.Medium).Flying);
            Assert.Less(BreakBudget.For(EffectsQuality.High).Rubble, BreakBudget.For(EffectsQuality.Ultra).Rubble);
        }

        [Test]
        public void AThrownPieceStaysHiddenOnlyWhileItsUnitIsThere()
        {
            var falls = new PieceFalls(new FractureCache((k, l) => false), Field());
            var model = new PresentedModel
            {
                Data = new ModelData { Name = "m", Pieces = new[] { new PieceInfo { Name = "a", Parent = -1 }, new PieceInfo { Name = "b", Parent = 0 } } },
                Pieces = new Mesh[2], Materials = new Material[2][], Unscale = Matrix4x4.identity,
            };
            falls.Throw(new PieceEvent { Id = 1, Unit = 7, Piece = 1, How = PieceExplode.Fall, Pose = Matrix4x4.identity }, model, false, false);
            Assert.IsTrue(falls.IsThrown(7, 1));
            Assert.IsFalse(falls.IsThrown(7, 0));
            var units = new[] { new UnitState { Handle = 7 } };
            falls.Sweep(units, 1);
            Assert.IsTrue(falls.IsThrown(7, 1), "while the unit is there");
            falls.Sweep(units, 0);
            Assert.IsFalse(falls.IsThrown(7, 1), "gone with its unit, so a handle used again draws whole");
            falls.Throw(new PieceEvent { Id = 2, Unit = 8, Piece = 0, How = PieceExplode.BitmapOnly }, model, false, false);
            Assert.IsFalse(falls.IsThrown(8, 0), "only a picture, so the piece stays");
        }

        [Test]
        public void ChunksAreDrawnNearestFirstUpToTheCapAndOnlyNearOnesCastShadows()
        {
            var budget = BreakBudget.For(EffectsQuality.High);
            budget.Drawn = 100;
            var d = Field(0f, budget);
            // Lying chunks in a row running away from the camera, one a unit.
            for (int k = 0; k < 300; k++)
                d.Spawn(draws, Matrix4x4.Translate(new Vector3(0f, 0.5f, 10f + k)), Vector3.zero, Cube, Vector3.zero, Vector3.zero);
            Run(d, 3f);
            var shadowed = new InstancedDraws();
            var plain = new InstancedDraws();
            var eye = new Vector3(0f, 2f, 0f);
            const float pixels = 1300f;
            // The first frame finds the cut, the next draws by it.
            for (int f = 0; f < 2; f++) { shadowed.Clear(); plain.Clear(); d.Draw(shadowed, plain, null, eye, pixels); }
            Assert.AreEqual(100, d.Drawn, "no more than the cap");
            Assert.AreEqual(100, shadowed.Count + plain.Count);
            Assert.AreEqual(Mathf.FloorToInt(budget.ShadowReach) - 9, shadowed.Count, 1, "only those within the shadow reach cast shadows");
            Assert.Greater(d.OverCap, 0);
            // Without the cap, every one in view.
            Debris.Capped = false;
            try { shadowed.Clear(); plain.Clear(); d.Draw(shadowed, plain, null, eye, pixels); }
            finally { Debris.Capped = true; }
            Assert.AreEqual(300, d.Drawn);
        }

        [Test]
        public void AChunkTooSmallToSeeIsNotDrawn()
        {
            var d = Field(0f);
            d.Spawn(draws, Matrix4x4.Translate(new Vector3(0f, 0.5f, 10f)), Vector3.zero, Cube, Vector3.zero, Vector3.zero);
            d.Spawn(draws, Matrix4x4.Translate(new Vector3(0f, 0.5f, 900f)), Vector3.zero, Cube, Vector3.zero, Vector3.zero);
            Run(d, 3f);
            var into = new InstancedDraws();
            d.Draw(into, null, null, Vector3.zero, 1300f);
            Assert.AreEqual(1, d.Drawn, "the near one, not the one a pixel across");
            Assert.AreEqual(1, d.TooSmall);
        }

        [Test]
        public void SteppingAndDrawingAThousandChunksAllocatesNothing()
        {
            var d = Field(0f);
            for (int k = 0; k < 1000; k++)
                d.Spawn(draws, Matrix4x4.Translate(new Vector3(k % 40, 3f + k % 7, k / 40)), Vector3.zero, Cube, new Vector3(1f, 3f, 0f), new Vector3(2f, 1f, 0f));
            var into = new InstancedDraws();
            for (int f = 0; f < 5; f++) { d.Step(0.02f); into.Clear(); d.Draw(into, null); }
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int f = 0; f < 100; f++) { d.Step(0.02f); into.Clear(); d.Draw(into, null); }
            double ms = clock.Elapsed.TotalMilliseconds / 100;
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Debug.Log($"A thousand chunks: {ms:0.000} ms a frame to step and draw");
            Assert.AreEqual(0, allocated, "no garbage a frame");
            Assert.AreEqual(1000, into.Count);
        }
    }
}
