// FlightPoseTests.cs - the animator's wing poses land on the drawn piece
// matrices: a driven piece takes the table's turn at its rest offset,
// children ride it, the right wing mirrors the left, moves land where the
// engine puts them, and nothing changes at weight 0. On a sixty-piece
// model the pose matches the plain way of computing it at well under its cost.
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Debug = UnityEngine.Debug;

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
            FlightPose.Apply(Flapping(0f), t, null, d, m, m.Length, 0f, 0f);
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
            FlightPose.Apply(Flapping(0.3f), Type(), null, d, m, m.Length, 0f, 0f);
            Near(before, Local(m, d, 3), 1e-5f, "the tip keeps its own pose under the new wing");
        }

        [Test]
        public void TheRightWingIsTheMirror()
        {
            var d = Model();
            var m = ScriptPose(d);
            FlightPose.Apply(Flapping(0.63f), Type(), null, d, m, m.Length, 0f, 0f);
            var mirror = Matrix4x4.Scale(new Vector3(-1, 1, 1));
            Near(mirror * Local(m, d, 2) * mirror, Local(m, d, 4), 1e-5f, "the right wing");
        }

        [Test]
        public void MovesLandWhereTheEngineWouldPutThem()
        {
            var d = Model();
            var m = ScriptPose(d);
            FlightPose.Apply(Flapping(0.2f), Type(@", ""downMove"": [1, 2, 3], ""upMove"": [1, 2, 3]"), null, d, m, m.Length, 0f, 0f);
            var at = (Vector3)Local(m, d, 2).GetColumn(3);
            Assert.Less(Vector3.Distance(new Vector3(0.5f, 0, 0) * Scale + new Vector3(-1, 2, -3) / 16f, at), 1e-5f, at.ToString("F4"));
        }

        [Test]
        public void TheOffsetLiftsEveryPiece()
        {
            var d = Model();
            var m = ScriptPose(d);
            var before = (Matrix4x4[])m.Clone();
            FlightPose.Apply(new Flyer(), Type(), null, d, m, m.Length, 0.3f, 0f);
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
            FlightPose.Apply(f, Type(), null, d, m, m.Length, 0f, 0f);
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
                for (int i = 0; i < 1000; i++) FlightPose.Apply(f, t, null, d, m, m.Length, 0.2f, i * 0.01f);
            };
            run();
            Assert.That(run, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }

        // ---- A dragon-sized model ----

        // Sixty pieces: hip and torso, two wings of four, a neck, two arms,
        // two legs, a tail and emitters, as a big flyer has.
        static ModelData Dragon()
        {
            var list = new List<PieceInfo>();
            int Add(string name, int parent, Vector3 at)
            {
                list.Add(new PieceInfo { Name = name, Parent = parent, Offset = at });
                return list.Count - 1;
            }
            void Chain(string name, int parent, int count, Vector3 step)
            {
                for (int i = 1; i <= count; i++) parent = Add(name + i, parent, step * (1f + 0.1f * i));
            }
            int root = Add("dragon", -1, Vector3.zero);
            int hip = Add("hip", root, new Vector3(0, 28, -0.4f));
            int torso = Add("torso", hip, new Vector3(0, 1, -0.5f));
            Chain("wingl", torso, 4, new Vector3(9, -0.5f, -1));
            Chain("wingr", torso, 4, new Vector3(-9, -0.5f, -1));
            Chain("neck", torso, 3, new Vector3(0, 4, -6));
            Chain("arml", torso, 5, new Vector3(1.5f, -5, -1));
            Chain("armr", torso, 5, new Vector3(-1.5f, -5, -1));
            Chain("legl", hip, 6, new Vector3(1.6f, -6, 3));
            Chain("legr", hip, 6, new Vector3(-1.6f, -6, 3));
            Chain("tail", hip, 7, new Vector3(0, -1, 11));
            while (list.Count < 60) Add("emit" + list.Count, list.Count % 2 == 0 ? torso : hip, new Vector3(0, -list.Count * 0.3f, 1));
            return new ModelData { Name = "dragon", Scale = 1f / 16f, Pieces = list.ToArray() };
        }

        static Matrix4x4[] DragonPose(ModelData d, int seed)
        {
            var world = Matrix4x4.TRS(new Vector3(10, 3, -20), Quaternion.Euler(0, 30, 0), Vector3.one) * Matrix4x4.Scale(new Vector3(1, 1, -1));
            var m = new Matrix4x4[d.Pieces.Length];
            for (int p = 0; p < m.Length; p++)
            {
                var local = Matrix4x4.TRS(d.Pieces[p].Offset * d.Scale, Quaternion.Euler(3 + p + seed, 5 - seed, 7 - p), Vector3.one);
                m[p] = d.Pieces[p].Parent < 0 ? world * local : m[d.Pieces[p].Parent] * local;
            }
            return m;
        }

        static readonly Matrix4x4[] plainOrig = new Matrix4x4[EntityRenderer.MaxPieces];
        static readonly bool[] plainMoved = new bool[EntityRenderer.MaxPieces];

        // The plain way: every piece under a posed one takes the script's
        // local transform by a full inverse, and the lift is a matrix.
        static void PlainApply(in Flyer f, FlightType t, ModelData d, Matrix4x4[] posed, int n, float offset, float seconds)
        {
            var drive = FlightPose.Bind(t, d);
            if (f.Weight > 0f)
            {
                System.Array.Copy(posed, plainOrig, n);
                for (int p = 0; p < n; p++)
                {
                    plainMoved[p] = false;
                    int parent = d.Pieces[p].Parent;
                    if (parent < 0 || parent >= p) continue;
                    if (drive[p] < 0 && !plainMoved[parent]) continue;
                    var script = plainOrig[parent].inverse * plainOrig[p];
                    Matrix4x4 local;
                    if (drive[p] < 0) local = script;
                    else
                    {
                        FlightPose.Target(f, t, t.Pieces[drive[p]], d.Pieces[p].Offset * d.Scale, seconds, out var q, out var at);
                        local = Matrix4x4.TRS(at, q, Vector3.one);
                        if (f.Weight < 1f)
                            local = Matrix4x4.TRS(Vector3.Lerp(script.GetColumn(3), local.GetColumn(3), f.Weight),
                                Quaternion.Slerp(script.rotation, local.rotation, f.Weight), Vector3.one);
                    }
                    posed[p] = posed[parent] * local;
                    plainMoved[p] = true;
                }
            }
            var lift = Matrix4x4.Translate(Vector3.up * offset);
            for (int p = 0; p < n; p++) posed[p] = lift * posed[p];
        }

        [Test]
        public void TheQuickPoseMatchesThePlainOne()
        {
            var d = Dragon();
            var t = FlightTableTests.Committed().Find("aradrag", null);
            float worst = 0f;
            foreach (float w in new[] { 1f, 0.4f })
                for (int i = 0; i < 40; i++)
                {
                    var f = Flapping(i / 40f);
                    f.Weight = w;
                    f.Glide = (i % 5) / 4f;
                    var a = DragonPose(d, i);
                    var b = DragonPose(d, i);
                    PlainApply(f, t, d, a, a.Length, 0.3f, i * 0.1f);
                    FlightPose.Apply(f, t, null, d, b, b.Length, 0.3f, i * 0.1f);
                    for (int p = 0; p < a.Length; p++)
                        for (int k = 0; k < 16; k++) worst = Mathf.Max(worst, Mathf.Abs(a[p][k] - b[p][k]));
                }
            Assert.Less(worst, 1e-4f);
        }

        // Hundreds of big flyers on screen: the pose must cost well under
        // the plain way, which took 13.6 ms a frame for 400 of them.
        [Test]
        public void ThePoseCostsFarLessThanThePlainOne()
        {
            var d = Dragon();
            var t = FlightTableTests.Committed().Find("aradrag", null);
            var rest = DragonPose(d, 0);
            var m = new Matrix4x4[rest.Length];
            const int flyers = 400, frames = 5;
            var fs = new Flyer[flyers];
            for (int i = 0; i < flyers; i++) fs[i] = Flapping(i / (float)flyers);
            double Time(bool plain)
            {
                double best = double.MaxValue;
                for (int round = 0; round < 5; round++)
                {
                    var sw = Stopwatch.StartNew();
                    for (int frame = 0; frame < frames; frame++)
                        for (int i = 0; i < flyers; i++)
                        {
                            System.Array.Copy(rest, m, rest.Length);
                            if (plain) PlainApply(fs[i], t, d, m, m.Length, 0.2f, frame * 0.016f);
                            else FlightPose.Apply(fs[i], t, null, d, m, m.Length, 0.2f, frame * 0.016f);
                        }
                    best = System.Math.Min(best, sw.Elapsed.TotalMilliseconds / frames);
                }
                return best;
            }
            Time(false);
            Time(true);
            double quick = Time(false), slow = Time(true);
            Debug.Log($"Flight pose, {flyers} sixty-piece flyers: {quick:0.00} ms a frame against {slow:0.00} ms the plain way");
            Assert.Less(quick, slow * 0.7, $"{quick:0.00} ms against {slow:0.00} ms");
        }
    }
}
