// FlightRigTests.cs - a flyer's fly and soar functions become clips: the
// cycle is found once the function has settled, keys fall where the pose
// changes, every piece the function moves follows the animator's phase,
// a head turner's y turn stays the script's, and the top of the stroke is
// where the table's wing is nearest its up pose.
using System.Collections.Generic;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class FlightRigTests
    {
        const float Rate = 60f;
        const int Cycle = 54, Rest = 20;

        static ModelData Model() => new ModelData
        {
            Name = "synth", Scale = 0.5f,
            Pieces = new[]
            {
                new PieceInfo { Name = "base", Parent = -1 },
                new PieceInfo { Name = "body", Parent = 0, Offset = new Vector3(0, 2, 0) },
                new PieceInfo { Name = "wingl1", Parent = 1, Offset = new Vector3(1, 0, 0) },
                new PieceInfo { Name = "wingl2", Parent = 2, Offset = new Vector3(2, 0, 0) },
                new PieceInfo { Name = "tail", Parent = 1, Offset = new Vector3(0, 0, -2) },
                new PieceInfo { Name = "head", Parent = 1, Offset = new Vector3(0, 0.6f, 1.6f) },
                new PieceInfo { Name = "wingr1", Parent = 1, Offset = new Vector3(-1, 0, 0) },
                new PieceInfo { Name = "claw", Parent = 4, Offset = new Vector3(0, -1, 0) },
            },
        };

        static FlightType Type() => FlightTable.Parse(@"{
            ""classes"": { ""c"": { ""climb"": 0.2, ""sink"": 0.2, ""lower"": -0.4, ""upper"": 0.4 } },
            ""units"": { ""synth"": { ""class"": ""c"", ""period"": 0.9, ""downstroke"": 0.5, ""pieces"": [
              { ""piece"": ""wingl1"", ""mirror"": ""wingr1"", ""down"": [0, -10, -45], ""up"": [0, 5, 40], ""wobble"": 3 } ] } } }")
            .Find("synth", null);

        // A stepped key track: the value of the last key at or before the tick in the cycle.
        static float Track(int tick, params (int at, float v)[] keys)
        {
            int t = ((tick % Cycle) + Cycle) % Cycle;
            float v = keys[keys.Length - 1].v;
            foreach (var k in keys) if (k.at <= t) v = k.v;
            return v;
        }

        // Script turns of every piece at a tick, the way a fly function
        // with turn-now and sleeps sets them, after a stretch at rest. The
        // head also turns about y out of step, as a head turner does.
        static Vector3[] Turns(int tick)
        {
            var t = new Vector3[8];
            var m = new Vector3[8];
            if (tick < Rest) return t;
            tick -= Rest;
            t[1] = new Vector3(Track(tick, (0, 4), (27, -3)), 0, 0);
            t[2] = new Vector3(0, Track(tick, (0, 5), (12, 0), (30, -10), (42, 0)), Track(tick, (0, 40), (12, 10), (30, -45), (42, -10)));
            t[3] = new Vector3(0, 0, Track(tick, (0, 20), (18, 5), (36, -20), (48, 0)));
            t[4] = new Vector3(Track(tick, (0, 10), (30, -10)), 0, 0);
            t[5] = new Vector3(Track(tick, (0, 5), (27, -5)), 25f * ((tick / 97) % 3), 0);
            t[6] = FlightTable.MirrorTurn(t[2]);
            return t;
        }

        static float BodyMove(int tick) => tick < Rest ? 0f : Track(tick - Rest, (0, 0.1f), (27, -0.1f));

        static Matrix4x4[] Locals(ModelData d, Vector3[] turns, float bodyMove)
        {
            var l = new Matrix4x4[d.Pieces.Length];
            for (int p = 0; p < l.Length; p++)
            {
                var at = d.Pieces[p].Offset * d.Scale + (p == 1 ? new Vector3(0, bodyMove, 0) : Vector3.zero);
                l[p] = Matrix4x4.TRS(at, FlightTable.Cob(turns[p]), Vector3.one);
            }
            return l;
        }

        // Model space as a backend gives it: the model's scale in, a mirror in z.
        static int Pose(ModelData d, Matrix4x4[] locals, Matrix4x4 root, PiecePose[] into)
        {
            var world = new Matrix4x4[locals.Length];
            for (int p = 0; p < locals.Length; p++)
            {
                world[p] = d.Pieces[p].Parent < 0 ? root * locals[p] : world[d.Pieces[p].Parent] * locals[p];
                into[p] = new PiecePose { Matrix = world[p] * Matrix4x4.Scale(Vector3.one * d.Scale) };
            }
            return locals.Length;
        }

        static readonly Matrix4x4 ModelRoot = Matrix4x4.Scale(new Vector3(1, 1, -1));

        static FlightRig.Sampler Fly(ModelData d) => (s, into) =>
        {
            int tick = Mathf.RoundToInt(s * Rate);
            return Pose(d, Locals(d, Turns(tick), BodyMove(tick)), ModelRoot, into);
        };

        static FlightRig.Sampler Held(ModelData d, float z) => (s, into) =>
        {
            var t = new Vector3[8];
            t[2] = new Vector3(0, 0, z);
            t[6] = FlightTable.MirrorTurn(t[2]);
            return Pose(d, Locals(d, t, 0f), ModelRoot, into);
        };

        // The drawn pose of the model when the script shows it at a tick.
        static Matrix4x4[] Drawn(ModelData d, int scriptTick, Matrix4x4 unit)
        {
            var into = new PiecePose[8];
            Pose(d, Locals(d, Turns(scriptTick), BodyMove(scriptTick)), unit * ModelRoot, into);
            var m = new Matrix4x4[8];
            for (int p = 0; p < 8; p++) m[p] = into[p].Matrix * Matrix4x4.Scale(Vector3.one / d.Scale);
            return m;
        }

        static Matrix4x4 Local(Matrix4x4[] m, ModelData d, int p) => m[d.Pieces[p].Parent].inverse * m[p];

        static Flyer At(float phase) => new Flyer { Mode = FlightMode.Flap, Phase = phase, Weight = 1f, Lift = 1f, PeriodScale = 1f };

        static readonly Matrix4x4 Unit = Matrix4x4.TRS(new Vector3(30, 8, -40), Quaternion.Euler(0, 70, 0), Vector3.one);

        [Test]
        public void TheCycleAndKeysComeFromTheSettledFunction()
        {
            var d = Model();
            var rig = FlightRig.Bake(Type(), d, Rate, Fly(d), null, out string why);
            Assert.IsNotNull(rig, why);
            Assert.AreEqual(Cycle / Rate, rig.Flap.Seconds, 1e-5f);
            // Every change of pose in the cycle, and none of the head's turns about y.
            var steps = new[] { 0, 12, 18, 27, 30, 36, 42, 48 };
            Assert.AreEqual(steps.Length, rig.Flap.Times.Length);
            var keys = new HashSet<int>();
            foreach (float time in rig.Flap.Times)
            {
                Assert.AreEqual(Mathf.Round(time * Cycle), time * Cycle, 1e-3f, "keys fall on ticks");
                keys.Add(Mathf.RoundToInt(time * Cycle));
            }
            bool matches = false;
            foreach (int from in steps)
            {
                var shifted = new HashSet<int>();
                foreach (int s in steps) shifted.Add((s - from + Cycle) % Cycle);
                matches |= shifted.SetEquals(keys);
            }
            Assert.IsTrue(matches, "the keys are the cycle's changes: " + string.Join(", ", keys));
            Assert.IsNull(rig.Glide);
        }

        [Test]
        public void EveryPieceTheFunctionMovesIsAChannel()
        {
            var d = Model();
            var rig = FlightRig.Bake(Type(), d, Rate, Fly(d), null);
            string[] moving = { "body", "wingl1", "wingl2", "tail", "head", "wingr1" };
            for (int p = 0; p < d.Pieces.Length; p++)
                Assert.AreEqual(System.Array.IndexOf(moving, d.Pieces[p].Name) >= 0, rig.Channel[p] >= 0, d.Pieces[p].Name);
            Assert.IsFalse(rig.KeepY[rig.Channel[2]], "the wing sweeps about y with the beat");
            Assert.IsTrue(rig.KeepY[rig.Channel[5]], "the head's y turn is the head turner's");
            Assert.IsTrue(rig.KeepY[rig.Channel[4]], "the tail never turns about y");
        }

        // The animator shows a frame of the script's own cycle, whatever
        // frame the script is at, and the body, tail and head go with it.
        [Test]
        public void EachKeyDrawsTheScriptsFrame()
        {
            var d = Model();
            var rig = FlightRig.Bake(Type(), d, Rate, Fly(d), null);
            int scriptTick = Rest + 5 * Cycle + 7;
            for (int k = 0; k < rig.Flap.Times.Length; k++)
            {
                var m = Drawn(d, scriptTick, Unit);
                var f = At(Mathf.Repeat(rig.Flap.Times[k] - rig.Top, 1f));
                FlightPose.Apply(f, Type(), rig, d, m, m.Length, 0f, 0f);
                bool found = false;
                for (int t = Rest + Cycle; t < Rest + 2 * Cycle && !found; t++)
                {
                    var want = Drawn(d, t, Unit);
                    found = true;
                    for (int p = 1; p < d.Pieces.Length && found; p++)
                    {
                        var a = Local(m, d, p);
                        var b = Local(want, d, p);
                        if (p == 5)
                        {
                            // The head keeps the script's y turn and takes the clip's x.
                            var turns = Turns(t);
                            turns[5].y = Turns(scriptTick)[5].y;
                            b = Matrix4x4.TRS(b.GetColumn(3), FlightTable.Cob(turns[5]), Vector3.one);
                        }
                        found = Quaternion.Angle(a.rotation, b.rotation) < 0.05f && Vector3.Distance(a.GetColumn(3), b.GetColumn(3)) < 1e-4f;
                    }
                }
                Assert.IsTrue(found, $"key {k} is one of the script's frames");
            }
        }

        [Test]
        public void TheStrokeTopIsTheTablesUpPose()
        {
            var d = Model();
            var t = Type();
            var rig = FlightRig.Bake(t, d, Rate, Fly(d), null);
            Assert.AreEqual(30f / Cycle, rig.Downstroke, 1e-4f, "down from tick 0 to tick 30");
            var m = Drawn(d, Rest + 3 * Cycle + 40, Unit);
            FlightPose.Apply(At(0f), t, rig, d, m, m.Length, 0f, 0f);
            Assert.Less(Quaternion.Angle(FlightTable.Cob(new Vector3(0, 5, 40)), Local(m, d, 2).rotation), 0.05f, "phase 0 is the top");
            m = Drawn(d, Rest + 3 * Cycle + 40, Unit);
            FlightPose.Apply(At(rig.Downstroke), t, rig, d, m, m.Length, 0f, 0f);
            Assert.Less(Quaternion.Angle(FlightTable.Cob(new Vector3(0, -10, -45)), Local(m, d, 2).rotation), 0.05f, "the downstroke ends at the bottom");
        }

        [Test]
        public void TheGlideClipCrossesWithTheFlap()
        {
            var d = Model();
            var rig = FlightRig.Bake(Type(), d, Rate, Fly(d), Held(d, 12f));
            Assert.IsNotNull(rig.Glide);
            Assert.AreEqual(1, rig.Glide.Times.Length, "a held glide is one key");
            var f = At(0.3f);
            f.Mode = FlightMode.Glide;
            f.Glide = 1f;
            var m = Drawn(d, Rest + 2 * Cycle, Unit);
            FlightPose.Apply(f, Type(), rig, d, m, m.Length, 0f, 0f);
            Assert.Less(Quaternion.Angle(FlightTable.Cob(new Vector3(0, 0, 12)), Local(m, d, 2).rotation), 0.05f);
            Assert.Less(Quaternion.Angle(FlightTable.Cob(new Vector3(0, 0, -12)), Local(m, d, 6).rotation), 0.05f);
            Assert.Less(Quaternion.Angle(Quaternion.identity, Local(m, d, 4).rotation), 0.05f, "the tail rests in the glide");
        }

        [Test]
        public void AStillOrRestlessFlapIsNoClip()
        {
            var d = Model();
            Assert.IsNull(FlightRig.Bake(Type(), d, Rate, Held(d, 10f), null), "nothing moves");
            FlightRig.Sampler noise = (s, into) =>
            {
                int tick = Mathf.RoundToInt(s * Rate);
                var turns = new Vector3[8];
                turns[2] = new Vector3(0, 0, Mathf.PerlinNoise(tick * 0.37f, 0.5f) * 80f);
                return Pose(d, Locals(d, turns, 0f), ModelRoot, into);
            };
            Assert.IsNull(FlightRig.Bake(Type(), d, Rate, noise, null), "it never repeats");
            Assert.IsNull(FlightRig.Bake(Type(), d, Rate, (s, into) => 0, null), "the backend cannot pose it");
        }

        // The mock's flyer has a sine flap once a second and a slow rocking glide.
        [Test]
        public void TheMockFlyerBakesFromItsBackend()
        {
            using (var mock = new MockBackend())
            {
                int model = mock.LoadModel("mockflyer", 0);
                var d = mock.GetModel(model);
                var t = FlightTableTests.Committed().Find("mockflyer", null);
                var rig = FlightRig.Bake(t, d, MockBackend.Tps,
                    (s, into) => mock.PoseModel(model, "fly", s, into), (s, into) => mock.PoseModel(model, "soar", s, into));
                Assert.IsNotNull(rig);
                Assert.AreEqual(1f, rig.Flap.Seconds, 1e-4f);
                Assert.AreEqual(2f, rig.Glide.Seconds, 1e-4f);
                Assert.AreEqual(4, rig.Piece.Length, "the four wing pieces");
                Assert.AreEqual(0.5f, rig.Downstroke, 1.01f / MockBackend.Tps);
                rig.Flap.Locate(rig.Top, out int a, out _, out _);
                float z = rig.Flap.Turns[a * rig.Piece.Length + rig.Channel[System.Array.FindIndex(d.Pieces, p => p.Name == "wingl1")]].eulerAngles.z;
                Assert.AreEqual(40f, Mathf.DeltaAngle(0f, z), 1f, "the top of the mock's beat");
            }
        }
    }
}
