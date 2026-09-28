// EngineFlightTests.cs - flight on the real engine, with Zhon's monarch,
// who flies: the catalogue says which types fly and hover, a flyer's
// altitude, speed and air state follow it through a takeoff and a landing,
// its type's cruise height and top speed are learned, every winged flyer's
// fly and soar functions bake into clips that replay the script's frames,
// and neither reading flight nor baking moves the battle. Needs okengine
// and the game files, and is ignored without them.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class EngineFlightTests
    {
        EngineBackend backend;
        readonly UnitState[] units = new UnitState[512];

        [OneTimeSetUp]
        public void Boot()
        {
            if (!EngineSettings.EngineAvailable) Assert.Ignore("okengine or the game files are missing");
            backend = new EngineBackend();
        }

        [OneTimeTearDown]
        public void End() => backend?.Dispose();

        void Start()
        {
            var s = new SkirmishSetup { MapId = "two castles", Seed = 11, LineOfSight = false, MapRevealed = true };
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ZHON", Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Colour = 1, Team = 1 });
            backend.StartSkirmish(s);
            LoadProgress p = default;
            for (int i = 0; i < 5000 && !p.Done && !p.Failed; i++) p = backend.PumpLoading();
            Assert.IsTrue(p.Done, p.Error);
        }

        UnitState Monarch()
        {
            int n = backend.ReadUnits(units);
            for (int i = 0; i < n; i++)
                if (units[i].Player == backend.LocalPlayer && backend.UnitDefs[units[i].Def].CanFly) return units[i];
            Assert.Fail("Zhon's monarch flies");
            return default;
        }

        UnitState Read(int handle)
        {
            int n = backend.ReadUnits(units);
            for (int i = 0; i < n; i++) if (units[i].Handle == handle) return units[i];
            return default;
        }

        UnitDef Def(string name)
        {
            foreach (var d in backend.UnitDefs) if (string.Equals(d.Name, name, System.StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        [Test, Order(1)]
        public void TheCatalogueSaysWhoFlies()
        {
            Start();
            var harpy = Def("zonharp");
            Assert.IsNotNull(harpy);
            Assert.IsTrue(harpy.CanFly);
            Assert.IsFalse(harpy.Hovers);
            var knight = Def("araknigh");
            Assert.IsNotNull(knight);
            Assert.IsFalse(knight.CanFly);
            var bird = Def("lifbird");
            if (bird != null) Assert.IsTrue(bird.Hovers, "the bird flies but never takes off");
        }

        [Test, Order(2)]
        public void AFlyerTakesOffAndLands()
        {
            var m = Monarch();
            Assert.AreEqual(0f, m.Altitude, 1e-3f);
            Assert.AreEqual(0, (int)(m.Flags & UnitFlags.Airborne));
            Assert.IsTrue(backend.Command(GameCommand.To(CommandKind.Move, m.Handle, m.Position + new Vector3(30f, 0f, 0f))));
            bool rose = false, moved = false, landed = false;
            float top = 0f, last = 0f;
            for (int t = 0; t < 30 * 60 && !landed; t++)
            {
                backend.Advance(1);
                var u = Read(m.Handle);
                Assert.AreEqual(u.Position.y - backend.GroundHeight(u.Position.x, u.Position.z), u.Altitude, 1e-3f, "altitude is the height above the ground");
                bool air = (u.Flags & UnitFlags.Airborne) != 0;
                if (air && u.Altitude > last) rose = true;
                if (air && u.Speed > 0.5f) moved = true;
                top = Mathf.Max(top, u.Altitude);
                if (rose && top > 1f && !air && u.Altitude == 0f) landed = true;
                last = u.Altitude;
            }
            Assert.IsTrue(rose, "it took off");
            Assert.IsTrue(moved, "it flew");
            Assert.Greater(top, 1f, "it climbed to its cruise height");
            Assert.IsTrue(landed, "it came down at the end of its flight");
            var def = backend.UnitDefs[m.Def];
            Assert.AreEqual(top, def.CruiseAltitude, 0.05f, "its type's cruise height is the height it flew at");
            Assert.Greater(def.MaxSpeed, 0.5f, "and its top speed is known");
        }

        [Test, Order(4)]
        public void EveryTablePieceIsInItsModel()
        {
            var table = OpenKingdomsUnity.Game.World.FlightTable.Parse(System.IO.File.ReadAllText(
                OpenKingdomsUnity.Game.World.FlightTable.PathIn(OpenKingdomsUnity.Game.World.OverrideLoader.ProjectDir)));
            int checkedUnits = 0;
            var missing = new System.Collections.Generic.List<string>();
            foreach (var d in backend.UnitDefs)
            {
                var t = table.Find(d.Name, d.ObjectName);
                if (t == null) continue;
                var m = backend.GetModel(backend.LoadModel(d.ObjectName, 0));
                Assert.IsNotNull(m, d.Name);
                checkedUnits++;
                foreach (var p in t.Pieces)
                {
                    int at = System.Array.FindIndex(m.Pieces, q => string.Equals(q.Name, p.Name, System.StringComparison.OrdinalIgnoreCase));
                    if (at < 0 || m.Pieces[at].Parent < 0) missing.Add($"{d.Name}/{p.Name}");
                }
            }
            Assert.GreaterOrEqual(checkedUnits, 15, "most winged flyers are in the game");
            Assert.IsEmpty(missing, string.Join(", ", missing));
        }

        // Each winged flyer's rig, baked from its script through the studio.
        Dictionary<UnitDef, (FlightRig rig, FlightType type, int model, string fly)> BakeAll(List<string> report)
        {
            var table = FlightTableTests.Committed();
            var rigs = new Dictionary<UnitDef, (FlightRig, FlightType, int, string)>();
            foreach (var d in backend.UnitDefs)
            {
                var t = table.Find(d.Name, d.ObjectName);
                if (t == null) continue;
                string fly = Array.Find(d.Animations, a => string.Equals(a, t.Clip("flap", "fly"), StringComparison.OrdinalIgnoreCase));
                string soar = Array.Find(d.Animations, a => string.Equals(a, t.Clip("glide", "soar"), StringComparison.OrdinalIgnoreCase));
                int model = backend.LoadModel(d.ObjectName, 0);
                var data = backend.GetModel(model);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string why = "no fly function";
                var rig = fly == null ? null : FlightRig.Bake(t, data, backend.TicksPerSecond,
                    (sec, into) => backend.PoseModel(model, fly, sec, into),
                    soar == null ? null : (FlightRig.Sampler)((sec, into) => backend.PoseModel(model, soar, sec, into)), out why);
                if (rig == null) { report.Add($"{d.Name}: no rig, {why}"); continue; }
                int keepY = 0;
                foreach (bool k in rig.KeepY) if (k) keepY++;
                report.Add($"{d.Name}: flap {rig.Flap.Seconds:0.000} s in {rig.Flap.Times.Length} keys (table {t.Period}), glide " +
                           (rig.Glide != null ? $"{rig.Glide.Seconds:0.000} s in {rig.Glide.Times.Length} keys" : "none") +
                           $", {rig.Piece.Length} pieces, {keepY} keep y, top {rig.Top:0.00}, downstroke {rig.Downstroke:0.00} (table {t.Downstroke}), {sw.Elapsed.TotalMilliseconds:0} ms");
                rigs[d] = (rig, t, model, fly);
            }
            return rigs;
        }

        static void Locals(ModelData d, PiecePose[] pose, Matrix4x4[] into)
        {
            var unscale = Matrix4x4.Scale(Vector3.one / d.Scale);
            for (int p = 0; p < d.Pieces.Length && p < into.Length; p++)
            {
                int parent = d.Pieces[p].Parent;
                into[p] = parent < 0 || parent >= p ? Matrix4x4.identity : (pose[parent].Matrix * unscale).inverse * (pose[p].Matrix * unscale);
            }
        }

        [Test, Order(5)]
        public void EveryWingedFlyerPlaysItsScriptsFrames()
        {
            var report = new List<string>();
            var rigs = BakeAll(report);
            Debug.Log("Flight rigs:\n" + string.Join("\n", report));
            Assert.GreaterOrEqual(rigs.Count, 15, string.Join("\n", report));
            var wrong = new List<string>();
            var pose = new PiecePose[EntityRenderer.MaxPieces];
            foreach (var kv in rigs)
            {
                var (rig, t, model, fly) = kv.Value;
                var d = backend.GetModel(model);
                if (Mathf.Abs(rig.Flap.Seconds - t.Period) > 0.2f * t.Period) wrong.Add($"{kv.Key.Name} beats in {rig.Flap.Seconds:0.000} s, the table says {t.Period}");
                if (Array.Exists(kv.Key.Animations, a => string.Equals(a, "soar", StringComparison.OrdinalIgnoreCase)) && rig.Glide == null)
                    wrong.Add($"{kv.Key.Name} has a soar but no glide clip");
                // One settled cycle of the script's own frames.
                int rate = backend.TicksPerSecond, cycle = Mathf.RoundToInt(rig.Flap.Seconds * rate), from = 12 * rate;
                var frames = new List<Matrix4x4[]>();
                for (int i = 0; i < cycle; i++)
                {
                    backend.PoseModel(model, fly, (from + i) / (float)rate, pose);
                    var l = new Matrix4x4[d.Pieces.Length];
                    Locals(d, pose, l);
                    frames.Add(l);
                }
                var drawn = new Matrix4x4[d.Pieces.Length];
                var local = new Matrix4x4[d.Pieces.Length];
                var unscale = Matrix4x4.Scale(Vector3.one / d.Scale);
                var start = new Matrix4x4[d.Pieces.Length];
                backend.PoseModel(model, fly, (from + cycle / 3) / (float)rate, pose);
                for (int p = 0; p < start.Length; p++) start[p] = pose[p].Matrix * unscale;
                for (int k = 0; k < rig.Flap.Times.Length; k++)
                {
                    Array.Copy(start, drawn, drawn.Length);
                    var f = new Flyer { Mode = FlightMode.Flap, Weight = 1f, Lift = 1f, PeriodScale = 1f, Phase = Mathf.Repeat(rig.Flap.Times[k] - rig.Top, 1f) };
                    FlightPose.Apply(f, t, rig, d, drawn, drawn.Length, 0f, 0f);
                    for (int p = 0; p < drawn.Length; p++)
                    {
                        int parent = d.Pieces[p].Parent;
                        local[p] = parent < 0 || parent >= p ? Matrix4x4.identity : drawn[parent].inverse * drawn[p];
                    }
                    bool found = false;
                    foreach (var frame in frames)
                    {
                        found = true;
                        for (int j = 0; j < rig.Piece.Length && found; j++)
                        {
                            int p = rig.Piece[j];
                            var a = local[p].rotation;
                            var b = frame[p].rotation;
                            if (rig.KeepY[j])
                            {
                                // The y turn is the script's at the posed frame; compare the rest.
                                a = Quaternion.AngleAxis(-Mathf.Atan2(local[p].m02, local[p].m22) * Mathf.Rad2Deg, Vector3.up) * a;
                                b = Quaternion.AngleAxis(-Mathf.Atan2(frame[p].m02, frame[p].m22) * Mathf.Rad2Deg, Vector3.up) * b;
                            }
                            found = Quaternion.Angle(a, b) < 0.1f && Vector3.Distance(local[p].GetColumn(3), frame[p].GetColumn(3)) < 2e-3f;
                        }
                        if (found) break;
                    }
                    if (!found) { wrong.Add($"{kv.Key.Name} key {k} of {rig.Flap.Times.Length} is none of the script's frames"); break; }
                }
            }
            Assert.IsEmpty(wrong, string.Join("\n", wrong));
        }

        [Test, Order(3)]
        public void ReadingFlightMovesNothing()
        {
            uint Play(bool read)
            {
                Start();
                var m = Monarch();
                backend.Command(GameCommand.To(CommandKind.Move, m.Handle, m.Position + new Vector3(30f, 0f, 0f)));
                for (int t = 0; t < 600; t++)
                {
                    backend.Advance(1);
                    if (read) backend.ReadUnits(units);
                    if (read && t == 300) Assert.Greater(BakeAll(new List<string>()).Count, 0);
                }
                return OkEngine.okx_sim_hash();
            }
            uint quiet = Play(false), watched = Play(true);
            Assert.AreEqual(quiet, watched);
        }
    }
}
