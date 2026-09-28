// ScreenCaptures.cs - pictures of each screen on the mock engine, for
// looking at the look without a window. Runs only when OKU_CAPTURE_DIR
// names a folder, and writes 1920 by 1080 PNGs there (OKU_CAPTURE_W and
// OKU_CAPTURE_H for another size).
using System.Collections;
using System.IO;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class ScreenCaptures
    {
        [TearDown]
        public void CleanUp() => OpenKingdomsUnity.Game.UI.BattleHud.SizeOverride = null;

        [UnityTest]
        public IEnumerator CaptureEveryScreen()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_CAPTURE_DIR to capture screens");
            Directory.CreateDirectory(dir);
            OpenKingdomsUnity.Game.UI.BattleHud.SizeOverride = new Vector2Int(W, H);
            bool engine = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_BACKEND") == "engine";
            string map = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_MAP");
            if (string.IsNullOrEmpty(map)) map = null;
            GameRoot root;
            if (engine)
            {
                if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
                // The engine's own view boots into the test scene. Take it away
                // first, as the engine runs one game at a time.
                var view = GameObject.Find("OpenKingdoms");
                if (view != null) Object.Destroy(view);
                yield return null;
                yield return null;
                root = GameRoot.Boot();
            }
            else root = GameRoot.Boot(new MockBackend { StageSeconds = 0.3f });
            if (string.IsNullOrEmpty(map)) map = root.Backend.Maps[0].Id;
            var mapLines = new System.Collections.Generic.List<string>();
            foreach (var m in root.Backend.Maps) mapLines.Add($"{m.Id}	{m.Climate}	{m.Size.x}x{m.Size.y}	{m.MaxPlayers}");
            File.WriteAllLines(Path.Combine(dir, "maps.txt"), mapLines);
            yield return null;
            var cam = Camera.main;
            if (cam == null) { cam = new GameObject("Main Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; }

            yield return Shoot(cam, Path.Combine(dir, "1-menu.png"));
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = map;
            yield return null;
            root.Screens.Show(FlowState.Skirmish);
            yield return Shoot(cam, Path.Combine(dir, "2-skirmish.png"));
            root.Screens.StartGame();
            yield return null;
            yield return null;
            yield return Shoot(cam, Path.Combine(dir, "3-loading.png"));
            float deadline = Time.realtimeSinceStartup + 240f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, "the game loaded: " + root.LastError);
            for (int i = 0; i < 90; i++) yield return null;
            // Select a few of the player's units, so rings and bars show.
            var e = root.World.Entities;
            var pick = new System.Collections.Generic.List<int>();
            for (int i = 0; i < e.UnitCount && pick.Count < 6; i++)
                if (e.Units[i].Player == root.Backend.LocalPlayer) pick.Add(e.Units[i].Handle);
            root.Backend.Select(pick.ToArray(), false);
            if (root.Orders != null && !root.Orders.Classic) e.Selected.UnionWith(pick);
            // Which features this map has, and what draws each, for the record.
            var feats = new FeatureState[8192];
            int fc = root.Backend.ReadFeatures(feats);
            var seen = new System.Collections.Generic.SortedDictionary<string, int>();
            for (int i = 0; i < fc; i++)
            {
                var d = root.Backend.FeatureDefs[feats[i].Def];
                string how = feats[i].Model >= 0 ? "model" : feats[i].Sprite >= 0 ? "sprite" : "none";
                string over = OpenKingdomsUnity.Game.World.OverrideLoader.EnsureIndex().Find(OverrideKind.Feature, d.Name, d.SequenceName, d.ObjectName);
                string key = $"{d.Name} {how}{(feats[i].Flat ? " flat" : "")} seq={d.SequenceName} {(over ?? "-")}";
                seen[key] = seen.TryGetValue(key, out int c) ? c + 1 : 1;
            }
            var lines = new System.Collections.Generic.List<string> { "map " + map };
            foreach (var kv in seen) lines.Add(kv.Value + " x " + kv.Key);
            File.WriteAllLines(Path.Combine(dir, "features.txt"), lines);
            var cam3 = root.World.Camera;
            if (System.Environment.GetEnvironmentVariable("OKU_CAPTURE_GHOST") == "1")
            {
                // A lodestone placed on the nearest site, then beside it.
                int lode = -1;
                foreach (var d in root.Backend.UnitDefs) if (d.Name.ToUpperInvariant().Contains("LODE") && lode < 0) lode = d.Id;
                Vector3 site = cam3.focus;
                float bestD = float.MaxValue;
                for (int i = 0; i < fc; i++)
                {
                    if (root.Backend.FeatureDefs[feats[i].Def].Name.IndexOf("Mana", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    float dd = (feats[i].Position - cam3.focus).sqrMagnitude;
                    if (dd < bestD) { bestD = dd; site = feats[i].Position; }
                }
                OpenKingdomsUnity.Game.World.FogView.Disabled = true;
                root.World.Fog.Update(true);
                root.Orders.Frozen = true;
                cam3.focus = site;
                bool ok = root.Backend.CanBuildAt(lode, site, 0, out var snapped);
                root.World.Entities.Ghost = new OpenKingdomsUnity.Game.World.EntityRenderer.GhostState { Def = lode, At = snapped, Ok = ok };
                yield return View(cam3, 22f, 50f, 0f);
                yield return Shoot(cam, Path.Combine(dir, "ghost-on-site.png"));
                var off = site + new Vector3(5, 0, 3);
                ok = root.Backend.CanBuildAt(lode, off, 0, out snapped);
                root.World.Entities.Ghost = new OpenKingdomsUnity.Game.World.EntityRenderer.GhostState { Def = lode, At = snapped, Ok = ok };
                yield return null;
                yield return Shoot(cam, Path.Combine(dir, "ghost-off-site.png"));
                root.World.Entities.Ghost = null;
                root.Orders.Frozen = false;
                OpenKingdomsUnity.Game.World.FogView.Disabled = false;
                File.WriteAllText(Path.Combine(dir, "ghost.txt"), $"lodestone def {lode} {(lode >= 0 ? root.Backend.UnitDefs[lode].Name : "")}, site {site}");
            }
            string focusOn = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_FOCUS") ?? (System.Environment.GetEnvironmentVariable("OKU_CAPTURE_TREES") == "1" ? "Tree" : null);
            if (focusOn != null)
            {
                // Close over the thickest stand of trees, with the fog off.
                OpenKingdomsUnity.Game.World.FogView.Disabled = true;
                root.World.Fog.Update(true);
                root.World.Atmosphere.SetWeather(WeatherChoice.Off);
                Vector3 best = cam3.focus;
                int bestCount = -1;
                for (int i = 0; i < fc; i++)
                {
                    var d = root.Backend.FeatureDefs[feats[i].Def];
                    if (d.Name.IndexOf(focusOn, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    int near = 0;
                    for (int j = 0; j < fc; j++)
                        if ((feats[j].Position - feats[i].Position).sqrMagnitude < 64f &&
                            root.Backend.FeatureDefs[feats[j].Def].Name.IndexOf(focusOn, System.StringComparison.OrdinalIgnoreCase) >= 0) near++;
                    if (near > bestCount) { bestCount = near; best = feats[i].Position; }
                }
                cam3.focus = best;
                yield return View(cam3, 22f, 34f, 20f);
                yield return Shoot(cam, Path.Combine(dir, "trees-close.png"));
                yield return View(cam3, 40f, 45f, -30f);
                yield return Shoot(cam, Path.Combine(dir, "trees-mid.png"));
                OpenKingdomsUnity.Game.World.FogView.Disabled = false;
            }
            // The classic view, then close and low, then far and wide.
            yield return Shoot(cam, Path.Combine(dir, "4-classic.png"));
            yield return View(cam3, 14f, 38f, 25f);
            yield return Shoot(cam, Path.Combine(dir, "5-close.png"));
            yield return View(cam3, 95f, 50f, -20f);
            yield return Shoot(cam, Path.Combine(dir, "6-wide.png"));
            yield return View(cam3, 34f, OpenKingdomsUnity.Game.World.GameCamera.ClassicPitch, 0f);
            root.Flow.Fire(FlowEvent.Pause);
            yield return null;
            yield return Shoot(cam, Path.Combine(dir, "7-pause.png"));

            if (System.Environment.GetEnvironmentVariable("OKU_CAPTURE_EDITOR") == "1")
            {
                // The map editor on the same map: a raised hill, a painted
                // patch and the paint palette open.
                root.Flow.Fire(FlowEvent.ToMenu);
                yield return null;
                root.Flow.Fire(FlowEvent.OpenEditor);
                root.Setup = GameRoot.DefaultSetup(root.Backend);
                root.Setup.MapId = map;
                root.Flow.Fire(FlowEvent.Start);
                deadline = Time.realtimeSinceStartup + 240f;
                while (root.Flow.State != FlowState.Editing && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.AreEqual(FlowState.Editing, root.Flow.State, "the editor opened: " + root.LastError);
                var ed = root.Editor;
                var t = root.Backend.Terrain;
                var focus = root.World.Camera.focus;
                int cx = Mathf.RoundToInt(focus.x / t.CellSize) + 6, cz = Mathf.RoundToInt(-focus.z / t.CellSize) - 4;
                ed.Tool = OpenKingdomsUnity.Game.World.EditTool.Raise;
                ed.Radius = 6;
                ed.Strength = 8;
                for (int i = 0; i < 6; i++) ed.Sculpt(cx, cz);
                var lib = root.Backend.ChunkLibrary();
                if (lib.Length > 3)
                {
                    ed.PaintChunk = lib[lib.Length / 3];
                    ed.Radius = 4;
                    ed.Paint(new Vector3((cx - 10) * t.CellSize, 0, -(cz + 6) * t.CellSize));
                }
                ed.Tool = OpenKingdomsUnity.Game.World.EditTool.Paint;
                root.Screens.Show(FlowState.Editing);
                yield return View(root.World.Camera, 40f, 45f, 0f);
                yield return Shoot(cam, Path.Combine(dir, "8-editor.png"));
            }
            Object.Destroy(root.gameObject);
            OpenKingdomsUnity.Game.UI.BattleHud.SizeOverride = null;
        }

        static IEnumerator View(OpenKingdomsUnity.Game.World.GameCamera c, float distance, float pitch, float yaw)
        {
            c.pitch = pitch;
            c.yaw = yaw;
            c.Zoom(distance);
            for (int i = 0; i < 20; i++) yield return null;
        }

        static int W => int.TryParse(System.Environment.GetEnvironmentVariable("OKU_CAPTURE_W"), out int w) ? w : 1920;
        static int H => int.TryParse(System.Environment.GetEnvironmentVariable("OKU_CAPTURE_H"), out int h) ? h : 1080;

        static IEnumerator Shoot(Camera cam, string path) => HudShots.Shoot(cam, W, H, path);
    }
}
