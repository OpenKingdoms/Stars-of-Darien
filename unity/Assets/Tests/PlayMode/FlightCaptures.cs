// FlightCaptures.cs - pictures of flyers in the air, for judging the wings:
// a takeoff, a few frames of flapping and a glide, with the camera kept on
// the flyer. Runs only with OKU_CAPTURE_DIR and OKU_CAPTURE_FLIGHT=1, on the
// engine with OKU_CAPTURE_BACKEND=engine. The local player is Zhon, whose
// monarch flies and whose beast handler raises bats. Files are
// flight-<unit>-*.png and flight.txt.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class FlightCaptures
    {
        [UnityTest]
        public IEnumerator CaptureFlyers()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir) || System.Environment.GetEnvironmentVariable("OKU_CAPTURE_FLIGHT") != "1")
                Assert.Ignore("set OKU_CAPTURE_DIR and OKU_CAPTURE_FLIGHT=1 to capture flyers");
            Directory.CreateDirectory(dir);
            GameRoot root;
            if (System.Environment.GetEnvironmentVariable("OKU_CAPTURE_BACKEND") == "engine")
            {
                if (GameRoot.BackendFactory == null) Assert.Ignore("no engine");
                var view = GameObject.Find("OpenKingdoms");
                if (view != null) Object.Destroy(view);
                yield return null;
                yield return null;
                root = GameRoot.Boot();
            }
            else root = GameRoot.Boot(new MockBackend { StageSeconds = 0f, DamageScale = 0f });
            yield return null;
            var cam = Camera.main;
            var canvas = root.GetComponentInChildren<Canvas>();
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            string map = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_MAP");
            if (!string.IsNullOrEmpty(map)) root.Setup.MapId = map;
            foreach (var side in root.Backend.Sides) if (side.Id == "ZHON") root.Setup.Seats[0].Side = side.Id;
            root.Setup.MapRevealed = true;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 240f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, "the game loaded: " + root.LastError);
            FogView.Disabled = true;
            root.World.Fog.Update(true);
            root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            root.Orders.Frozen = true;

            var ents = root.World.Entities;
            var b = root.Backend;
            var table = FlightTable.Load();
            var log = new List<string> { $"{b.Name} on {root.Setup.MapId}" };
            int monarch = -1;
            for (int i = 0; i < ents.UnitCount && monarch < 0; i++)
            {
                var d = b.UnitDefs[ents.Units[i].Def];
                if (ents.Units[i].Player == b.LocalPlayer && d.BuildOptions.Length > 0 && !d.IsBuilding) monarch = ents.Units[i].Handle;
            }
            yield return Raise(root, table, monarch, log);

            // One of each kind of winged flyer the player has.
            var flyers = new List<int>();
            var kinds = new HashSet<int>();
            for (int i = 0; i < ents.UnitCount; i++)
            {
                var u = ents.Units[i];
                var d = b.UnitDefs[u.Def];
                if (u.Player == b.LocalPlayer && (u.Flags & UnitFlags.Building) == 0 && table.Find(d.Name, d.ObjectName) != null && kinds.Add(u.Def))
                    flyers.Add(u.Handle);
            }
            if (flyers.Count == 0) { Object.Destroy(root.gameObject); Assert.Ignore("the local player has no winged flyer"); }
            var took = new List<bool>();
            foreach (int h in flyers) yield return Capture(root, cam, canvas, dir, h, log, took);
            File.WriteAllLines(Path.Combine(dir, "flight.txt"), log);
            FogView.Disabled = false;
            Object.Destroy(root.gameObject);
            Assert.IsTrue(took.Contains(true), string.Join("\n", log));
        }

        static int Finished(GameRoot root, int def)
        {
            var e = root.World.Entities;
            for (int i = 0; i < e.UnitCount; i++)
            {
                var u = e.Units[i];
                if (u.Def == def && u.Player == root.Backend.LocalPlayer && u.Flags == UnitFlags.Active && u.BuildProgress >= 1f) return u.Handle;
            }
            return -1;
        }

        static UnitState Find(GameRoot root, int handle)
        {
            var e = root.World.Entities;
            for (int i = 0; i < e.UnitCount; i++) if (e.Units[i].Handle == handle) return e.Units[i];
            return default;
        }

        static bool Site(IGameBackend b, int def, Vector3 near, out Vector3 site)
        {
            for (int r = 4; r < 40; r += 2)
                for (int a = 0; a < 16; a++)
                    if (b.CanBuildAt(def, near + Quaternion.Euler(0, a * 22.5f, 0) * Vector3.forward * r, 0, out site)) return true;
            site = default;
            return false;
        }

        // Where the monarch's line leads to a winged flyer, raises one: the
        // maker first, then the flyer from it, with the game sped up.
        static IEnumerator Raise(GameRoot root, FlightTable table, int monarch, List<string> log)
        {
            var b = root.Backend;
            if (monarch < 0) yield break;
            var mdef = b.UnitDefs[Find(root, monarch).Def];
            int maker = -1, flyer = -1;
            foreach (int o in mdef.BuildOptions)
                foreach (int f in b.UnitDefs[o].BuildOptions)
                    if (maker < 0 && table.Find(b.UnitDefs[f].Name, b.UnitDefs[f].ObjectName) != null) { maker = o; flyer = f; }
            if (maker < 0) { log.Add("no maker of flyers"); yield break; }
            if (!Site(b, maker, Find(root, monarch).Position, out var site)) { log.Add("no site for " + b.UnitDefs[maker].Name); yield break; }
            b.Command(new GameCommand { Kind = CommandKind.Build, Unit = monarch, Target = site, TargetUnit = -1, BuildDef = maker });
            Time.timeScale = 20f;
            try
            {
                int made = -1;
                float deadline = Time.realtimeSinceStartup + 60f;
                while ((made = Finished(root, maker)) < 0 && Time.realtimeSinceStartup < deadline) yield return null;
                if (made < 0) { log.Add(b.UnitDefs[maker].Name + " was not finished"); yield break; }
                if (b.UnitDefs[maker].IsBuilding)
                    b.Command(new GameCommand { Kind = CommandKind.FactoryEnqueue, Unit = made, TargetUnit = -1, BuildDef = flyer });
                else if (Site(b, flyer, Find(root, made).Position, out var spot))
                    b.Command(new GameCommand { Kind = CommandKind.Build, Unit = made, Target = spot, TargetUnit = -1, BuildDef = flyer });
                deadline = Time.realtimeSinceStartup + 60f;
                while (Finished(root, flyer) < 0 && Time.realtimeSinceStartup < deadline) yield return null;
                log.Add($"raised {b.UnitDefs[maker].Name}, then {b.UnitDefs[flyer].Name}: {(Finished(root, flyer) >= 0 ? "done" : "not finished")}");
            }
            finally { Time.timeScale = 1f; }
        }

        // Flies one flyer back and forth with the camera on it, and takes a
        // takeoff, three flaps and a glide.
        static IEnumerator Capture(GameRoot root, Camera cam, Canvas canvas, string dir, int handle, List<string> log, List<bool> took)
        {
            var b = root.Backend;
            var ents = root.World.Entities;
            var def = b.UnitDefs[Find(root, handle).Def];
            string name = def.Name.ToLowerInvariant();
            log.Add($"{name} ({def.ObjectName}): can fly {def.CanFly}, hovers {def.Hovers}");
            var home = Find(root, handle).Position;
            var size = b.Terrain.Size;
            var away = new Vector3(Mathf.Clamp(home.x + (home.x < size.x / 2 ? 40 : -40), 4, size.x - 4), 0, home.z);
            var ends = new[] { away, home };
            int leg = 0;
            b.Command(GameCommand.To(CommandKind.Move, handle, ends[leg]));
            var cam3 = root.World.Camera;
            cam3.pitch = 45f;
            cam3.yaw = 0f;

            int flaps = 0;
            bool tookOff = false, glided = false;
            float nextFlap = 0f, deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline && (!glided || flaps < 3))
            {
                yield return null;
                var u = Find(root, handle);
                if (u.Handle != handle) break;
                if (new Vector2(u.Position.x - ends[leg].x, u.Position.z - ends[leg].z).magnitude < 4f)
                {
                    leg = 1 - leg;
                    b.Command(GameCommand.To(CommandKind.Move, handle, ends[leg]));
                }
                // The view's centre line runs through the flyer, twelve cells off.
                float p = cam3.pitch * Mathf.Deg2Rad;
                var flat = Quaternion.Euler(0, cam3.yaw, 0) * Vector3.forward;
                cam3.focus = new Vector3(u.Position.x, cam3.focus.y, u.Position.z) + flat * (u.Altitude / Mathf.Tan(p));
                cam3.Zoom(u.Altitude / Mathf.Sin(p) + 12f);
                if (!ents.TryFlight(u.StableId, out var f)) continue;
                string state = $"mode {f.Mode} phase {f.Phase:0.00} glide {f.Glide:0.00} weight {f.Weight:0.00} offset {f.Offset:0.00} forced {f.Forced} altitude {u.Altitude:0.00} speed {u.Speed:0.00} airborne {(u.Flags & UnitFlags.Airborne) != 0}";
                string shot = null;
                if (!tookOff && (u.Flags & UnitFlags.Airborne) != 0 && u.Altitude > 1f) { tookOff = true; shot = "takeoff"; }
                else if (f.Mode == FlightMode.Flap && f.Weight >= 1f && !f.Forced && flaps < 3 && Time.realtimeSinceStartup > nextFlap)
                {
                    shot = "flap-" + (++flaps);
                    nextFlap = Time.realtimeSinceStartup + 0.23f;
                }
                else if (!glided && f.Mode == FlightMode.Glide && f.Glide > 0.95f) { glided = true; shot = "glide"; }
                if (shot == null) continue;
                yield return ScreenCaptures.Shoot(cam, canvas, Path.Combine(dir, $"flight-{name}-{shot}.png"));
                log.Add($"  {shot}: {state}");
            }
            b.Command(GameCommand.To(CommandKind.Stop, handle, default));
            took.Add(tookOff);
        }
    }
}
