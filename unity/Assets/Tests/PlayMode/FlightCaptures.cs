// FlightCaptures.cs - pictures of flyers in the air, for judging the wings:
// a takeoff, a few frames of flapping and a glide, with the camera kept on
// the flyer. Runs only with OKU_CAPTURE_DIR and OKU_CAPTURE_FLIGHT=1, on the
// engine with OKU_CAPTURE_BACKEND=engine, where the local player is Zhon so
// the monarch flies. Files are flight-<unit>-*.png and flight.txt.
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
            int handle = -1;
            UnitDef def = null;
            for (int i = 0; i < ents.UnitCount && handle < 0; i++)
            {
                var d = b.UnitDefs[ents.Units[i].Def];
                if (ents.Units[i].Player == b.LocalPlayer && table.Find(d.Name, d.ObjectName) != null) { handle = ents.Units[i].Handle; def = d; }
            }
            if (handle < 0) { Object.Destroy(root.gameObject); Assert.Ignore("the local player has no winged flyer"); }
            string name = def.Name.ToLowerInvariant();
            var log = new List<string> { $"{b.Name} {name} ({def.ObjectName}) on {root.Setup.MapId}, can fly {def.CanFly}, hovers {def.Hovers}" };

            UnitState Now()
            {
                for (int i = 0; i < ents.UnitCount; i++) if (ents.Units[i].Handle == handle) return ents.Units[i];
                return default;
            }
            var home = Now().Position;
            var size = b.Terrain.Size;
            var away = new Vector3(Mathf.Clamp(home.x + (home.x < size.x / 2 ? 40 : -40), 4, size.x - 4), 0, home.z);
            var ends = new[] { away, home };
            int leg = 0;
            b.Command(GameCommand.To(CommandKind.Move, handle, ends[leg]));
            var cam3 = root.World.Camera;
            cam3.pitch = 28f;
            cam3.Zoom(20f);

            int flaps = 0;
            bool tookOff = false, glided = false;
            float nextFlap = 0f;
            deadline = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < deadline && (!glided || flaps < 3))
            {
                yield return null;
                var u = Now();
                if (new Vector2(u.Position.x - ends[leg].x, u.Position.z - ends[leg].z).magnitude < 4f)
                {
                    leg = 1 - leg;
                    b.Command(GameCommand.To(CommandKind.Move, handle, ends[leg]));
                }
                // Look at the flyer, not the ground under it.
                var fwd = cam.transform.forward;
                var flat = new Vector3(fwd.x, 0, fwd.z).normalized;
                cam3.focus = new Vector3(u.Position.x, cam3.focus.y, u.Position.z) + flat * (u.Altitude / Mathf.Tan(cam3.pitch * Mathf.Deg2Rad));
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
                log.Add($"{shot}: {state}");
            }
            File.WriteAllLines(Path.Combine(dir, "flight.txt"), log);
            FogView.Disabled = false;
            Object.Destroy(root.gameObject);
            Assert.IsTrue(tookOff, string.Join("\n", log));
        }
    }
}
