// FlightPoseTests.cs - the animator's wing poses land on the drawn piece
// matrices: a driven piece takes the table's turn at its rest offset,
// children ride it, the right wing mirrors the left, moves land where the
// engine puts them, and nothing changes at weight 0.
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools.Constraints;

namespace OpenKingdomsUnity.Tests
{
    public class FlightPoseTests
    {
        const float Scale = 0.5f;

        // A root plate, a body, a left wing with a tip and a right wing.
        static ModelData Model() => new ModelData
        {
            Name = "testflyer", Scale = Scale,
            Pieces = new[]
            {
                new PieceInfo { Name = "base", Parent = -1 },
                new PieceInfo { Name = "body", Parent = 0, Offset = new Vector3(0, 1, 0) },
                new PieceInfo { Name = "WingL1", Parent = 1, Offset = new Vector3(0.5f, 0, 0) },
                new PieceInfo { Name = "tip", Parent = 2, Offset = new Vector3(1, 0, 0) },
                new PieceInfo { Name = "wingr1", Parent = 1, Offset = new Vector3(-0.5f, 0, 0) },
            },
        };

        static FlightType Type(string extra = "") => FlightTable.Parse(@"{
            ""classes"": { ""c"": { ""climb"": 0.2, ""sink"": 0.2, ""lower"": -0.4, ""upper"": 0.4 } },
            ""units"": { ""testflyer"": { ""class"": ""c"", ""period"": 1, ""downstroke"": 0.4, ""pieces"": [
              { ""piece"": ""wingl1"", ""mirror"": ""wingr1"", ""down"": [5, -10, 30], ""up"": [10, 20, -50], ""glide"": [0, 0, -5] " + extra + @" } ] } } }")
            .Find("testflyer", null);

        // The script's pose under a unit matrix with the engine's z flip,
        // the wings turned the same way on both sides.
        static Matrix4x4[] ScriptPose(ModelData d)
        {
            var world = Matrix4x4.TRS(new Vector3(10, 3, -20), Quaternion.Euler(0, 30, 0), Vector3.one) * Matrix4x4.Scale(new Vector3(1, 1, -1));
            var m = new Matrix4x4[d.Pieces.Length];
            for (int p = 0; p < m.Length; p++)
            {
                var turn = p == 2 ? Quaternion.Euler(0, 0, 25) : p == 4 ? Quaternion.Euler(0, 0, -25) : p == 3 ? Quaternion.Euler(0, 10, 0) : Quaternion.identity;
                var local = Matrix4x4.TRS(d.Pieces[p].Offset * d.Scale, turn, Vector3.one);
                m[p] = d.Pieces[p].Parent < 0 ? world * local : m[d.Pieces[p].Parent] * local;
            }
            return m;
        }

        static Matrix4x4 Local(Matrix4x4[] m, ModelData d, int p) => m[d.Pieces[p].Parent].inverse * m[p];

        static Flyer Flapping(float phase) => new Flyer { Mode = FlightMode.Flap, Phase = phase, Weight = 1f, Lift = 1f, PeriodScale = 1f };

        static void Near(Matrix4x4 want, Matrix4x4 got, float eps, string what)
        {
            for (int i = 0; i < 16; i++) Assert.AreEqual(want[i], got[i], eps, $"{what}: element {i}");
        }

        [Test]
        public void ADrivenPieceTakesTheTablePose()
        {
            var d = Model();
            var t = Type();
            var m = ScriptPose(d);
            FlightPose.Apply(Flapping(0f), t, d, m, m.Length, 0f, 0f);
            var local = Local(m, d, 2);
            Assert.Less(Quaternion.Angle(FlightTable.Cob(new Vector3(10, 20, -50)), local.rotation), 0.01f, "the top of the stroke is the table's up");
            Assert.Less(Vector3.Distance(new Vector3(0.5f, 0, 0) * Scale, local.GetColumn(3)), 1e-5f, "at its rest offset");
        }

        [Test]
        public void ChildrenFollowTheirDrivenParent()
        {
            var d = Model();
            var m = ScriptPose(d);
            var before = Local(m, d, 3);
            FlightPose.Apply(Flapping(0.3f), Type(), d, m, m.Length, 0f, 0f);
            Near(before, Local(m, d, 3), 1e-5f, "the tip keeps its own pose under the new wing");
        }

        [Test]
        public void TheRightWingIsTheMirror()
        {
            var d = Model();
            var m = ScriptPose(d);
            FlightPose.Apply(Flapping(0.63f), Type(), d, m, m.Length, 0f, 0f);
            var mirror = Matrix4x4.Scale(new Vector3(-1, 1, 1));
            Near(mirror * Local(m, d, 2) * mirror, Local(m, d, 4), 1e-5f, "the right wing");
        }

        [Test]
        public void MovesLandWhereTheEngineWouldPutThem()
        {
            var d = Model();
            var m = ScriptPose(d);
            FlightPose.Apply(Flapping(0.2f), Type(@", ""downMove"": [1, 2, 3], ""upMove"": [1, 2, 3]"), d, m, m.Length, 0f, 0f);
            var at = (Vector3)Local(m, d, 2).GetColumn(3);
            Assert.Less(Vector3.Distance(new Vector3(0.5f, 0, 0) * Scale + new Vector3(-1, 2, -3) / 16f, at), 1e-5f, at.ToString("F4"));
        }

        [Test]
        public void TheOffsetLiftsEveryPiece()
        {
            var d = Model();
            var m = ScriptPose(d);
            var before = (Matrix4x4[])m.Clone();
            FlightPose.Apply(new Flyer(), Type(), d, m, m.Length, 0.3f, 0f);
            for (int p = 0; p < m.Length; p++)
                Near(Matrix4x4.Translate(new Vector3(0, 0.3f, 0)) * before[p], m[p], 1e-5f, d.Pieces[p].Name);
        }

        [Test]
        public void WeightZeroChangesNothing()
        {
            var d = Model();
            var m = ScriptPose(d);
            var before = (Matrix4x4[])m.Clone();
            var f = Flapping(0.4f);
            f.Weight = 0f;
            FlightPose.Apply(f, Type(), d, m, m.Length, 0f, 0f);
            for (int p = 0; p < m.Length; p++) Near(before[p], m[p], 1e-6f, d.Pieces[p].Name);
        }

        [Test]
        public void ApplyDoesNotAllocate()
        {
            var d = Model();
            var t = Type();
            var m = ScriptPose(d);
            var f = Flapping(0.1f);
            f.Glide = 0.5f;
            TestDelegate run = () =>
            {
                for (int i = 0; i < 1000; i++) FlightPose.Apply(f, t, d, m, m.Length, 0.2f, i * 0.01f);
            };
            run();
            Assert.That(run, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }
    }
}
