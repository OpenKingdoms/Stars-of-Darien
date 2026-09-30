// SceneryCaptures.cs - the editor's pictures of SceneryViews, to lay beside
// a built player's (-okSmokeViews). Runs only with OKU_SCENERY_DIR, on the
// engine. OKU_SCENERY_PLAN lists "map|views" entries split by "||", views
// as SceneryViews reads them, and each picture is written as
// <map>-<label>-<angle>.png, the player's names.
using System.Collections;
using System.IO;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class SceneryCaptures
    {
        GameRoot root;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator CaptureScenery()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_SCENERY_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_SCENERY_DIR to capture scenery");
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            string plan = System.Environment.GetEnvironmentVariable("OKU_SCENERY_PLAN") ?? "";
            Directory.CreateDirectory(dir);
            var view = GameObject.Find("OpenKingdoms");
            if (view != null) Object.Destroy(view);
            yield return null;
            yield return null;
            foreach (var entry in plan.Split(new[] { "||" }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Split('|');
                if (parts.Length < 2) continue;
                root = GameRoot.Boot();
                yield return null;
                var map = SmokeRun.PickMap(root.Backend.Maps, parts[0].Trim());
                Assert.IsNotNull(map, "no map " + parts[0]);
                root.Flow.Fire(FlowEvent.OpenSkirmish);
                SmokeRun.Seat(root.Setup, map);
                root.Setup.MapRevealed = true;
                root.Setup.LineOfSight = false;
                root.Flow.Fire(FlowEvent.Start);
                float deadline = Time.realtimeSinceStartup + 240f;
                while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
                root.World.Atmosphere.SetWeather(WeatherChoice.Off);
                float settle = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < settle) yield return null;
                string slug = (map.Name ?? map.Id).Replace("'", "").Replace(' ', '-').ToLowerInvariant();
                foreach (var v in SceneryViews.Parse(parts[1]))
                {
                    var spot = SceneryViews.Spot(root.Backend, v);
                    if (spot == null) { Debug.Log($"Scenery: {map.Id} {v.Label}: no feature matches {v.Anchor}"); continue; }
                    Debug.Log($"Scenery: {map.Id} {v.Label} at ({spot.Value.x:0.0}, {spot.Value.z:0.0})");
                    yield return SceneryViews.Take(root.World, spot.Value, (angle, png) =>
                        File.WriteAllBytes(Path.Combine(dir, $"{slug}-{v.Label}-{angle}.png"), png));
                }
                Object.Destroy(root.gameObject);
                root = null;
                yield return null;
                yield return null;
            }
        }
    }
}
