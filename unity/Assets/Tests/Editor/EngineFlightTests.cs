// EngineFlightTests.cs - flight on the real engine, with Zhon's monarch,
// who flies: the catalogue says which types fly and hover, a flyer's
// altitude, speed and air state follow it through a takeoff and a landing,
// and reading them never moves the battle. Needs okengine and the game
// files, and is ignored without them.
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
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
                }
                return OkEngine.okx_sim_hash();
            }
            uint quiet = Play(false), watched = Play(true);
            Assert.AreEqual(quiet, watched);
        }
    }
}
