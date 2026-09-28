// FlightTableTests.cs - the flight table reads its numbers and poses the
// way the scripts write them: turns compose as the engine's do, the right
// wing mirrors the left, merges run defaults, class, unit, and bad entries
// are skipped with a warning.
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class FlightTableTests
    {
        // The shipped classes, the pegasus, spyhawk and harpy seeds, and a
        // quick beat that barely waits for level wings.
        public const string Json = @"{
  ""version"": 1,
  ""defaults"": { ""amplitude"": 1.0, ""forcedAmplitude"": 1.1, ""forcedPeriod"": 0.85, ""climbForce"": 0.3, ""ease"": 0.35, ""blend"": 0.3, ""jitter"": 0.05, ""wobbleHz"": 0.4 },
  ""classes"": {
    ""heavy"": { ""climb"": 0.15, ""sink"": 0.45, ""sinkSlow"": 1.6, ""lower"": -0.3, ""upper"": 0.5, ""stall"": 0.45 },
    ""medium"": { ""climb"": 0.2, ""sink"": 0.25, ""sinkSlow"": 1.5, ""lower"": -0.4, ""upper"": 0.4, ""stall"": 0.4 },
    ""light"": { ""climb"": 0.3, ""sink"": 0.12, ""sinkSlow"": 1.4, ""lower"": -0.5, ""upper"": 0.5, ""stall"": 0.3 }
  },
  ""units"": {
    ""arafly"": { ""class"": ""heavy"", ""period"": 1.624, ""downstroke"": 0.75,
      ""pieces"": [ { ""piece"": ""wingl1"", ""mirror"": ""wingr1"", ""down"": [2.0, -10.8, 23.2], ""up"": [7.4, -22.5, -60.7] } ] },
    ""arafast"": { ""class"": ""light"", ""period"": 1.179, ""downstroke"": 0.72,
      ""pieces"": [ { ""piece"": ""wingl1"", ""mirror"": ""wingr1"", ""down"": [2.0, -10.8, 23.2], ""up"": [7.4, -22.5, -60.7], ""glide"": [15.0, -0.5, -20.1], ""wobble"": 3 } ] },
    ""quick"": { ""class"": ""medium"", ""period"": 0.3, ""downstroke"": 0.5, ""pieces"": [] },
    ""zonharp"": { ""class"": ""medium"", ""period"": 1.032, ""downstroke"": 0.31,
      ""pieces"": [ { ""piece"": ""harwingl1"", ""mirror"": ""harwingr1"", ""down"": [11.0, -37.8, 26.0], ""up"": [30.6, 39.8, -47.5], ""glide"": [40.9, -3.0, -1.9] } ] }
  }
}";

        public static FlightTable Table() => FlightTable.Parse(Json);

        public static FlightTable Committed() => FlightTable.Parse(File.ReadAllText(FlightTable.PathIn(OverrideLoader.ProjectDir)));

        static FlightPiece Piece(FlightType t, string name) => t.Pieces.First(p => p.Name == name);

        [Test]
        public void CobTurnsMatchTheEngine()
        {
            // The engine's lr[] rows for these turns (render/units.c).
            var cases = new (Vector3 turn, float[] lr)[]
            {
                (new Vector3(10, 20, 30), new[] { 0.784102f, 0.521281f, -0.336824f, -0.492404f, 0.852869f, 0.173648f, 0.377786f, 0.029696f, 0.925417f }),
                (new Vector3(-45, 5, 90), new[] { 0.061628f, 0.996195f, -0.061628f, -0.707107f, 0f, -0.707107f, -0.704416f, 0.087156f, 0.704416f }),
                (new Vector3(0, 0, -60), new[] { 0.5f, -0.866025f, 0f, 0.866025f, 0.5f, 0f, 0f, 0f, 1f }),
            };
            foreach (var c in cases)
            {
                var m = Matrix4x4.Rotate(FlightTable.Cob(c.turn));
                for (int r = 0; r < 3; r++)
                    for (int k = 0; k < 3; k++)
                        Assert.AreEqual(c.lr[r * 3 + k], m[r, k], 1e-5f, $"turn {c.turn}, row {r} column {k}");
            }
        }

        [Test]
        public void TheSeededSpyhawkWingHingesLikeTheResearch()
        {
            var t = Table();
            var left = Piece(t.Find("arafast", null), "wingl1");
            AssertAxis(new Vector3(0.23f, 0.19f, 0.95f), 85.6f, left);
            var right = Piece(t.Find("arafast", null), "wingr1");
            AssertAxis(new Vector3(0.23f, -0.19f, -0.95f), 85.6f, right);
            AssertAxis(new Vector3(-0.22f, -0.50f, 0.84f), 83.1f, Piece(t.Find("zonharp", null), "harwingl1"));
        }

        static void AssertAxis(Vector3 axis, float degrees, FlightPiece p)
        {
            Assert.AreEqual(axis.x, p.Hinge.x, 0.01f, p.Name + " hinge x");
            Assert.AreEqual(axis.y, p.Hinge.y, 0.01f, p.Name + " hinge y");
            Assert.AreEqual(axis.z, p.Hinge.z, 0.01f, p.Name + " hinge z");
            Assert.AreEqual(degrees, p.Sweep, 0.2f, p.Name + " sweep");
        }

        [Test]
        public void MirrorNegatesYAndZTurnsAndXMoves()
        {
            var t = FlightTable.Parse(@"{ ""classes"": { ""c"": { ""climb"": 0.2, ""sink"": 0.2, ""lower"": -0.4, ""upper"": 0.4 } },
                ""units"": { ""u"": { ""class"": ""c"", ""period"": 1, ""downstroke"": 0.5, ""pieces"": [
                  { ""piece"": ""l"", ""mirror"": ""r"", ""down"": [10, 20, 30], ""up"": [-5, 15, -25], ""glide"": [1, 2, 3],
                    ""downMove"": [1, 2, 3], ""upMove"": [4, 5, 6], ""glideMove"": [7, 8, 9] } ] } } }").Find("u", null);
            var r = Piece(t, "r");
            AssertTurn(FlightTable.Cob(new Vector3(10, -20, -30)), r.Down);
            AssertTurn(FlightTable.Cob(new Vector3(-5, -15, 25)), r.Up);
            AssertTurn(FlightTable.Cob(new Vector3(1, -2, -3)), r.Glide);
            Assert.AreEqual(FlightTable.MoveToLocal(new Vector3(-1, 2, 3)), r.DownMove);
            Assert.AreEqual(FlightTable.MoveToLocal(new Vector3(-4, 5, 6)), r.UpMove);
            Assert.AreEqual(FlightTable.MoveToLocal(new Vector3(-7, 8, 9)), r.GlideMove);
            Assert.AreEqual(new Vector3(-1, 2, -3) / 16f, Piece(t, "l").DownMove, "a move lands at (-x, y, -z), a pixel a sixteenth of a cell");
        }

        static void AssertTurn(Quaternion want, Quaternion got) =>
            Assert.Less(Quaternion.Angle(want, got), 0.01f, $"{want.eulerAngles} against {got.eulerAngles}");

        [Test]
        public void AMissingGlideIsTheMidpoint()
        {
            var t = FlightTable.Parse(@"{ ""classes"": { ""c"": { ""climb"": 0.2, ""sink"": 0.2, ""lower"": -0.4, ""upper"": 0.4 } },
                ""units"": { ""u"": { ""class"": ""c"", ""period"": 1, ""downstroke"": 0.5, ""pieces"": [
                  { ""piece"": ""p"", ""down"": [10, 20, 30], ""up"": [30, 0, -10], ""downMove"": [2, 4, 6], ""upMove"": [0, 0, 2] } ] } } }").Find("u", null);
            var p = Piece(t, "p");
            AssertTurn(FlightTable.Cob(new Vector3(20, 10, 10)), p.Glide);
            Assert.AreEqual(FlightTable.MoveToLocal(new Vector3(1, 2, 4)), p.GlideMove);
            Assert.IsTrue(p.HasMove);
        }

        [Test]
        public void UnitKeysBeatClassKeysBeatDefaults()
        {
            var t = FlightTable.Parse(@"{ ""defaults"": { ""ease"": 0.5, ""blend"": 0.7, ""climb"": 0.1 },
                ""classes"": { ""c"": { ""ease"": 0.4, ""climb"": 0.2, ""sink"": 0.2, ""lower"": -0.4, ""upper"": 0.4 } },
                ""units"": { ""u"": { ""class"": ""c"", ""climb"": 0.3, ""period"": 1, ""downstroke"": 0.5, ""pieces"": [] } } }").Find("u", null);
            Assert.AreEqual(0.3f, t.Climb, 1e-6f, "the unit's own");
            Assert.AreEqual(0.4f, t.Ease, 1e-6f, "the class's");
            Assert.AreEqual(0.7f, t.Blend, 1e-6f, "the default");
        }

        [Test]
        public void LookupTriesTheUnitNameThenTheObjectName()
        {
            var t = Table();
            Assert.AreEqual("arafast", t.Find("no such unit", "ARAFAST")?.Name);
            Assert.AreEqual("zonharp", t.Find("ZonHarp", "arafast")?.Name, "the unit name comes first");
            Assert.IsNull(t.Find("knight", "araknigh"));
        }

        [Test]
        public void ABadEntryIsSkippedWithAWarning()
        {
            var t = FlightTable.Parse(@"{ ""classes"": { ""c"": { ""climb"": 0.2, ""sink"": 0.2, ""lower"": -0.4, ""upper"": 0.4 } },
                ""units"": {
                  ""good"": { ""class"": ""c"", ""period"": 1, ""downstroke"": 0.5, ""pieces"": [] },
                  ""noclass"": { ""class"": ""x"", ""period"": 1, ""downstroke"": 0.5 },
                  ""noperiod"": { ""class"": ""c"", ""period"": 0, ""downstroke"": 0.5 },
                  ""stroke"": { ""class"": ""c"", ""period"": 1, ""downstroke"": 1.0 },
                  ""band"": { ""class"": ""c"", ""period"": 1, ""downstroke"": 0.5, ""lower"": 0.4, ""upper"": 0.4 },
                  ""jitter"": { ""class"": ""c"", ""period"": 1, ""downstroke"": 0.5, ""jitter"": 1.0 },
                  ""pose"": { ""class"": ""c"", ""period"": 1, ""downstroke"": 0.5, ""pieces"": [ { ""piece"": ""p"", ""down"": [0, 0, 0] } ] } } }");
            Assert.AreEqual(1, t.Count);
            Assert.IsNotNull(t.Find("good", null));
            Assert.AreEqual(6, t.Warnings.Count, string.Join("\n", t.Warnings));
        }

        [Test]
        public void TheCommittedTableIsSane()
        {
            string path = FlightTable.PathIn(OverrideLoader.ProjectDir);
            Assert.IsTrue(File.Exists(path), path);
            var t = FlightTable.Parse(File.ReadAllText(path));
            Assert.IsEmpty(t.Warnings, string.Join("\n", t.Warnings));
            Assert.GreaterOrEqual(t.Count, 21, "every winged flyer and the mock's");
            foreach (var f in t.Types)
            {
                Assert.That(f.Class, Is.EqualTo("heavy").Or.EqualTo("medium").Or.EqualTo("light"), f.Name);
                Assert.Less(f.Lower, 0f, f.Name);
                Assert.Greater(f.Upper, 0f, f.Name);
                Assert.Greater(f.Climb, 0f, f.Name);
                Assert.Greater(f.Sink, 0f, f.Name);
                Assert.That(f.Downstroke, Is.GreaterThan(0f).And.LessThan(1f), f.Name);
                Assert.IsNotEmpty(f.Pieces, f.Name);
                Assert.That(f.Jitter, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f), f.Name);
            }
            Assert.IsFalse(t.Find("tarang", null).Glides, "the fallen angel's script never glides");
            Assert.IsFalse(t.Find("lifbird", null).Glides, "nor the bird's");
            Assert.IsTrue(t.Find("zonharp", null).Glides);
        }

        [Test]
        public void GlidesAndClipsAreRead()
        {
            var t = FlightTable.Parse(@"{ ""classes"": { ""c"": { ""climb"": 0.2, ""sink"": 0.2, ""lower"": -0.4, ""upper"": 0.4 } },
                ""units"": { ""u"": { ""class"": ""c"", ""glides"": false, ""period"": 1, ""downstroke"": 0.5, ""clips"": { ""flap"": ""fly_wings"" }, ""pieces"": [] } } }").Find("u", null);
            Assert.IsFalse(t.Glides);
            Assert.AreEqual("fly_wings", t.Clip("flap", "fly"));
            Assert.AreEqual("soar", t.Clip("glide", "soar"), "a state with no clip named takes the usual function");
        }
    }
}
