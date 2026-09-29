// EngineFlightTests.cs - flight on the real engine, with Zhon's monarch,
// who flies: the catalogue says which types fly and hover, a flyer's
// altitude, speed and air state follow it through a takeoff and a landing,
// its type's cruise height and top speed are learned, every winged flyer's
// fly and soar functions bake into clips that replay the script's frames,
// its glide coming from its soar, cut to a cycle or held, or from its flap
// slowed when it has no soar, a clip carries a piece's turn about y only
// where it beats it, and neither reading flight nor baking moves the
// battle. Needs okengine and the game files, and is ignored without them.
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

        // A function's poses as the studio played them, each piece's turn and
        // offset from its parent, a tick at a time.
        sealed class Played
        {
            public readonly List<Matrix4x4[]> Frames = new List<Matrix4x4[]>();

            public FlightRig.Sampler Record(EngineBackend backend, int model, string function, ModelData d) => (sec, into) =>
            {
                int n = backend.PoseModel(model, function, sec, into);
                var l = new Matrix4x4[d.Pieces.Length];
                Locals(d, into, l);
                Frames.Add(l);
                return n;
            };
        }

        // Each winged flyer's rig, baked from its script through the studio,
        // handed with the poses it was baked from to check, then let go.
        int BakeAll(List<string> report, Action<UnitDef, FlightRig, FlightType, ModelData, string, Played, string, Played> check = null)
        {
            var table = FlightTableTests.Committed();
            int count = 0;
            foreach (var d in backend.UnitDefs)
            {
                var t = table.Find(d.Name, d.ObjectName);
                if (t == null) continue;
                string fly = Array.Find(d.Animations, a => string.Equals(a, t.Clip("flap", "fly"), StringComparison.OrdinalIgnoreCase));
                string soar = Array.Find(d.Animations, a => string.Equals(a, t.Clip("glide", "soar"), StringComparison.OrdinalIgnoreCase));
                int model = backend.LoadModel(d.ObjectName, 0);
                var data = backend.GetModel(model);
                Played flew = new Played(), soared = new Played();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string why = "no fly function";
                var rig = fly == null ? null : FlightRig.Bake(t, data, backend.TicksPerSecond,
                    flew.Record(backend, model, fly, data), soar == null ? null : soared.Record(backend, model, soar, data), out why);
                if (rig == null) { report.Add($"{d.Name}: no rig, {why}"); continue; }
                int keepY = 0;
                foreach (bool k in rig.KeepY) if (k) keepY++;
                report.Add($"{d.Name}: flap {rig.Flap.Seconds:0.000} s in {rig.Flap.Times.Length} keys (table {t.Period}), glide " +
                           (rig.Glide != null ? $"from {rig.GlideFrom}, {rig.Glide.Seconds:0.000} s in {rig.Glide.Times.Length} keys" : "none") +
                           $", {rig.Piece.Length} pieces, {keepY} keep y, top {rig.Top:0.00}, downstroke {rig.Downstroke:0.00} (table {t.Downstroke}), {sw.Elapsed.TotalMilliseconds:0} ms");
                count++;
                check?.Invoke(d, rig, t, data, fly, flew, soar, soar != null ? soared : flew);
            }
            return count;
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
            var wrong = new List<string>();
            int rigs = BakeAll(report, (def, rig, t, d, fly, flew, soar, soared) =>
            {
                string name = def.Name;
                if (Mathf.Abs(rig.Flap.Seconds - t.Period) > 0.2f * t.Period) wrong.Add($"{name} beats in {rig.Flap.Seconds:0.000} s, the table says {t.Period}");
                // It glides on its soar, cut to a cycle or held, and on its flap slowed when it has none.
                bool fits = soar != null ? rig.GlideFrom == GlideSource.Soar || rig.GlideFrom == GlideSource.HeldSoar : rig.GlideFrom == GlideSource.SlowedFlap;
                if (rig.Glide == null || !fits) { wrong.Add($"{name} glides from {rig.GlideFrom} with {(soar != null ? "a" : "no")} soar"); return; }
                string off = KeyOffScript(rig, t, d, flew, false);
                if (off != null) wrong.Add($"{name}: flap {off} of {fly}");
                off = KeyOffScript(rig, t, d, soared, true);
                if (off != null) wrong.Add($"{name}: glide {off} of {soar ?? fly}");
                // A clip carries a piece's turn about y only where it beats it:
                // the flap where fly does, the glide where soar or fly does.
                for (int j = 0; j < rig.Piece.Length; j++)
                {
                    bool flapTurns = TurnsAboutY(rig, rig.Flap, j), glideTurns = TurnsAboutY(rig, rig.Glide, j);
                    string piece = d.Pieces[rig.Piece[j]].Name;
                    if (!rig.KeepY[j] && !flapTurns) wrong.Add($"{name}: the flap holds {piece}'s turn about y, which fly never turns");
                    if (!rig.GlideKeepY[j] && !glideTurns && !flapTurns) wrong.Add($"{name}: the glide holds {piece}'s turn about y, which neither turns");
                }
            });
            // The scripts move nothing in fly until BeginFlight has run. A
            // studio that does not run it poses no flyer, and the table stands in.
            if (rigs == 0 && report.Exists(r => r.EndsWith("moves nothing")))
                Assert.Ignore("this okengine's studio does not begin a flyer's flight:\n" + string.Join("\n", report));
            Debug.Log("Flight rigs:\n" + string.Join("\n", report));
            Assert.GreaterOrEqual(rigs, 15, string.Join("\n", report));
            Assert.IsEmpty(wrong, string.Join("\n", wrong));
        }

        // Whether a channel's turn about y changes through a clip's keys.
        static bool TurnsAboutY(FlightRig rig, FlightRig.Clip clip, int j)
        {
            int c = rig.Piece.Length;
            float first = 0f;
            for (int k = 0; k < clip.Times.Length; k++)
            {
                var q = clip.Turns[k * c + j];
                var m = Matrix4x4.Rotate(q);
                float y = Mathf.Atan2(m.m02, m.m22) * Mathf.Rad2Deg;
                if (k == 0) first = y;
                else if (Mathf.Abs(Mathf.DeltaAngle(first, y)) > 0.05f) return true;
            }
            return false;
        }

        // The first key of the flap or glide clip that is none of the frames
        // the function played while it was baked, or null. Posed at rest in
        // the studio, a script's idles leave pieces turned a little
        // differently from one run to the next, so the frames are that run's.
        string KeyOffScript(FlightRig rig, FlightType t, ModelData d, Played played, bool glide)
        {
            var clip = glide ? rig.Glide : rig.Flap;
            var keep = glide ? rig.GlideKeepY : rig.KeepY;
            var frames = played.Frames;
            var drawn = new Matrix4x4[d.Pieces.Length];
            var local = new Matrix4x4[d.Pieces.Length];
            var start = new Matrix4x4[d.Pieces.Length];
            // Any settled frame will do to draw on: the world pose from its locals.
            var at = frames[frames.Count - 1];
            for (int p = 0; p < start.Length; p++)
            {
                int parent = d.Pieces[p].Parent;
                start[p] = parent < 0 || parent >= p ? Matrix4x4.identity : start[parent] * at[p];
            }
            for (int k = 0; k < clip.Times.Length; k++)
            {
                Array.Copy(start, drawn, drawn.Length);
                var f = new Flyer { Mode = FlightMode.Flap, Weight = 1f, Lift = 1f, PeriodScale = 1f, Phase = Mathf.Repeat(clip.Times[k] - rig.Top, 1f) };
                if (glide) { f.Mode = FlightMode.Glide; f.Glide = 1f; f.GlidePhase = clip.Times[k]; }
                FlightPose.Apply(f, t, rig, d, drawn, drawn.Length, 0f, 0f);
                for (int p = 0; p < drawn.Length; p++)
                {
                    int parent = d.Pieces[p].Parent;
                    local[p] = parent < 0 || parent >= p ? Matrix4x4.identity : drawn[parent].inverse * drawn[p];
                }
                // The nearest frame: the one with the fewest pieces apart.
                string nearest = null;
                int fewest = int.MaxValue;
                foreach (var frame in frames)
                {
                    int apart = 0;
                    string first = null;
                    for (int j = 0; j < rig.Piece.Length && apart < fewest; j++)
                    {
                        int p = rig.Piece[j];
                        var a = local[p].rotation;
                        var b = frame[p].rotation;
                        if (keep[j])
                        {
                            // The y turn is the script's at the posed frame; compare the rest.
                            a = Quaternion.AngleAxis(-Mathf.Atan2(local[p].m02, local[p].m22) * Mathf.Rad2Deg, Vector3.up) * a;
                            b = Quaternion.AngleAxis(-Mathf.Atan2(frame[p].m02, frame[p].m22) * Mathf.Rad2Deg, Vector3.up) * b;
                        }
                        float turn = Quaternion.Angle(a, b), move = Vector3.Distance(local[p].GetColumn(3), frame[p].GetColumn(3));
                        if (turn < 0.1f && move < 2e-3f) continue;
                        apart++;
                        first = first ?? $"{d.Pieces[p].Name} {turn:0.00} deg {move:0.0000} off{(keep[j] ? " (y kept)" : "")}";
                    }
                    if (apart < fewest) { fewest = apart; nearest = first; }
                    if (fewest == 0) break;
                }
                if (fewest > 0) return $"key {k} of {clip.Times.Length} is none of the frames: {fewest} pieces apart in the nearest, first {nearest}";
            }
            return null;
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
                    if (read && t == 300) BakeAll(new List<string>());
                }
                return OkEngine.okx_sim_hash();
            }
            uint quiet = Play(false), watched = Play(true);
            Assert.AreEqual(quiet, watched);
        }
    }
}
