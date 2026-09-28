// HudCaptures.cs - the battle HUD on the real engine for every kingdom:
// its monarch selected, then the building it raises that builds the most,
// at 1280x720, 1920x1080 and 3840x2160. Runs only when OKU_CAPTURE_DIR
// names a folder and OKU_CAPTURE_HUD=1, and writes hud-<side>-<unit>-WxH.png.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class HudCaptures
    {
        static readonly Vector2Int[] Sizes = { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(3840, 2160) };

        GameRoot root;

        [TearDown]
        public void CleanUp()
        {
            BattleHud.SizeOverride = null;
            if (root != null) Object.Destroy(root.gameObject);
        }

        [UnityTest, Timeout(3600000)]
        public IEnumerator EveryKingdomsMonarchAndBuilder()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir) || System.Environment.GetEnvironmentVariable("OKU_CAPTURE_HUD") != "1")
                Assert.Ignore("set OKU_CAPTURE_DIR and OKU_CAPTURE_HUD=1");
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            Directory.CreateDirectory(dir);
            var view = GameObject.Find("OpenKingdoms");
            if (view != null) Object.Destroy(view);
            yield return null;
            yield return null;
            root = GameRoot.Boot();
            yield return null;
            root.Options.UiScale = HudLayout.DefaultScale;
            string map = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_MAP");
            if (string.IsNullOrEmpty(map)) map = "two castles";
            var log = new List<string>();
            var only = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_SIDES");
            foreach (var side in root.Backend.Sides.ToList())
            {
                if (!string.IsNullOrEmpty(only) && !only.ToUpperInvariant().Contains(side.Id)) continue;
                string tag = side.Name.ToLowerInvariant();
                yield return Start(side.Id, map);
                if (root.Flow.State != FlowState.Playing) { log.Add($"{tag}: did not start, {root.LastError}"); continue; }
                root.Orders.Frozen = true;
                var b = root.Backend;
                var monarch = Own().FirstOrDefault(u => !b.UnitDefs[u.Def].IsBuilding && b.UnitDefs[u.Def].BuildOptions.Length > 0);
                if (monarch.MaxHealth == 0) { log.Add($"{tag}: no monarch"); continue; }
                var md = b.UnitDefs[monarch.Def];
                log.Add($"{tag}: monarch {md.Name}, {md.BuildOptions.Length} options");
                yield return Shots(monarch, dir, tag + "-monarch");

                // The option that builds the most, raised beside the monarch.
                int best = md.BuildOptions.Where(o => o >= 0 && o < b.UnitDefs.Count).OrderByDescending(o => b.UnitDefs[o].BuildOptions.Length).First();
                var bd = b.UnitDefs[best];
                bool placed = false;
                Vector3 site = default;
                for (int ring = 6; ring < 40 && !placed; ring += 3)
                    for (int k = 0; k < 16 && !placed; k++)
                    {
                        float a = k * Mathf.PI / 8f;
                        var at = monarch.Position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * ring;
                        if (b.CanBuildAt(best, at, 0, out site))
                            placed = b.Command(new GameCommand { Kind = CommandKind.Build, Unit = monarch.Handle, Target = site, TargetUnit = -1, BuildDef = best });
                    }
                if (!placed) { log.Add($"{tag}: nowhere to build {bd.Name}"); continue; }
                UnitState built = default;
                int ticks = 0;
                for (; ticks < 40000; ticks += 120)
                {
                    b.Advance(120);
                    built = Own().FirstOrDefault(u => u.Def == best && u.BuildProgress >= 1f);
                    if (built.MaxHealth > 0) break;
                    if (ticks % 1200 == 0) yield return null;
                }
                if (built.MaxHealth == 0) { log.Add($"{tag}: {bd.Name} not finished after {ticks} ticks"); continue; }
                log.Add($"{tag}: builder {bd.Name}, {bd.BuildOptions.Length} options, built in {ticks} ticks");
                yield return Shots(built, dir, tag + "-builder");
            }
            File.WriteAllLines(Path.Combine(dir, "hud-captures.txt"), log);
            foreach (var line in log) Debug.Log(line);
        }

        IEnumerator Start(string side, string map)
        {
            if (root.Flow.State == FlowState.Playing) root.Flow.Fire(FlowEvent.Pause);
            if (root.Flow.State != FlowState.MainMenu) root.Flow.Fire(FlowEvent.ToMenu);
            yield return null;
            Assert.AreEqual(FlowState.MainMenu, root.Flow.State, "back at the menu for the next kingdom");
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = map;
            root.Setup.MapRevealed = true;
            root.Setup.LineOfSight = false;
            root.Setup.StartMana = 5000;
            root.Setup.Seats[0].Side = side;
            root.Setup.Seats[1].Side = side == "TAROS" ? "ARAMON" : "TAROS";
            root.Setup.Seats[1].Difficulty = AiDifficulty.Easy;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 240f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            for (int i = 0; i < 30; i++) yield return null;
        }

        IEnumerable<UnitState> Own()
        {
            var units = new UnitState[4096];
            int n = root.Backend.ReadUnits(units);
            for (int i = 0; i < n; i++)
                if (units[i].Player == root.Backend.LocalPlayer && (units[i].Flags & UnitFlags.Dying) == 0) yield return units[i];
        }

        IEnumerator Shots(UnitState unit, string dir, string name)
        {
            // The input is held still, so the HUD's selection is set here too.
            root.Backend.Select(new[] { unit.Handle }, false);
            root.World.Entities.Selected.Clear();
            root.World.Entities.Selected.Add(unit.Handle);
            var cam = root.World.Camera;
            cam.focus = unit.Position;
            cam.pitch = OpenKingdomsUnity.Game.World.GameCamera.ClassicPitch;
            cam.yaw = 0;
            cam.Zoom(30f);
            foreach (var size in Sizes)
            {
                BattleHud.SizeOverride = size;
                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForSecondsRealtime(0.4f);
                yield return HudShots.Shoot(Camera.main, size.x, size.y, Path.Combine(dir, $"hud-{name}-{size.x}x{size.y}.png"));
            }
            BattleHud.SizeOverride = null;
        }
    }
}
