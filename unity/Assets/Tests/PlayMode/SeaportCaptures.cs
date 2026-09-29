// SeaportCaptures.cs - pictures of a monarch raising a building and of the
// sea running past the map's edge, each from the classic camera and a low
// three-quarter view. Runs only with OKU_CAPTURE_DIR and OKU_CAPTURE_SEAPORT=1,
// on the engine with OKU_CAPTURE_BACKEND=engine, for each map in
// OKU_CAPTURE_MAP split by ';'. OKU_CAPTURE_TAG starts each file name.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class SeaportCaptures
    {
        GameRoot root;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }

        [UnityTest, Timeout(3600000)]
        public IEnumerator BuildLightAndSeaEdge()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir) || System.Environment.GetEnvironmentVariable("OKU_CAPTURE_SEAPORT") != "1")
                Assert.Ignore("set OKU_CAPTURE_DIR and OKU_CAPTURE_SEAPORT=1");
            Directory.CreateDirectory(dir);
            LogAssert.ignoreFailingMessages = true;
            string tag = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_TAG") ?? "seaport";
            var maps = (System.Environment.GetEnvironmentVariable("OKU_CAPTURE_MAP") ?? "abel's seaport").Split(';');
            bool engine = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_BACKEND") == "engine";
            if (engine)
            {
                if (GameRoot.BackendFactory == null) Assert.Ignore("no engine");
                var view = GameObject.Find("OpenKingdoms");
                if (view != null) Object.Destroy(view);
                yield return null;
                yield return null;
                root = GameRoot.Boot();
            }
            else root = GameRoot.Boot(new MockBackend { StageSeconds = 0f });
            yield return null;
            var log = new List<string>();
            foreach (var raw in maps)
            {
                string want = raw.Trim().ToLowerInvariant();
                if (want.Length == 0) continue;
                var info = root.Backend.Maps.FirstOrDefault(m => m.Id.ToLowerInvariant() == want || (m.Name ?? "").ToLowerInvariant() == want);
                if (info == null)
                {
                    string word = want.Split(' ', (char)39)[0];
                    log.Add($"{want}: no such map, near {string.Join(", ", root.Backend.Maps.Where(m => (m.Name ?? m.Id).ToLowerInvariant().Contains(word)).Select(m => m.Name))}");
                    continue;
                }
                string map = info.Id;
                string name = tag + "-" + new string(map.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
                yield return Start(map);
                if (root.Flow.State != FlowState.Playing) { log.Add($"{map}: did not start, {root.LastError}"); continue; }
                root.Orders.Frozen = true;
                root.Screens.Screen("Hud").SetActive(false);
                var b = root.Backend;
                var cam = Camera.main;
                var gc = root.World.Camera;

                var monarch = Own().Where(u => !b.UnitDefs[u.Def].IsBuilding).OrderByDescending(u => b.UnitDefs[u.Def].BuildOptions.Length).FirstOrDefault();
                if (monarch.MaxHealth > 0 && Place(monarch))
                {
                    // Until the monarch stands at the site and works on it.
                    float until = Time.realtimeSinceStartup + 60f;
                    UnitState now = monarch;
                    while (Time.realtimeSinceStartup < until)
                    {
                        now = Own().FirstOrDefault(u => u.Handle == monarch.Handle);
                        if (now.MaxHealth == 0 || b.ReadOrder(monarch.Handle).Building >= 0) break;
                        yield return null;
                    }
                    yield return new WaitForSecondsRealtime(1.5f);
                    now = Own().FirstOrDefault(u => u.Handle == monarch.Handle);
                    var fx = root.World.Effects;
                    log.Add($"{map}: monarch {b.UnitDefs[monarch.Def].Name} building {b.ReadOrder(monarch.Handle).Building >= 0}, effects {fx?.Count}, lights {fx?.LightsLit}");
                    yield return Shot(gc, cam, now.Position, 50f, GameCamera.ClassicPitch, 0f, Path.Combine(dir, name + "-build-classic.png"));
                    yield return Shot(gc, cam, now.Position, 36f, 30f, 35f, Path.Combine(dir, name + "-build-low.png"));
                }
                else log.Add($"{map}: no build");

                if (SeaEdge(b.Terrain, out var at, out float outward))
                {
                    var inward = Quaternion.Euler(0, outward, 0) * Vector3.back;
                    log.Add($"{map}: sea edge at {at}, facing {outward}");
                    yield return Shot(gc, cam, at + inward * 8f, 45f, GameCamera.ClassicPitch, 0f, Path.Combine(dir, name + "-edge-classic.png"));
                    yield return Shot(gc, cam, at + inward * 10f, 40f, 24f, outward, Path.Combine(dir, name + "-edge-low.png"));
                }
                else
                {
                    // A land edge, which keeps its look.
                    var west = new Vector3(0, 0, -b.Terrain.Size.y / 2);
                    log.Add($"{map}: no sea edge, the west edge instead");
                    yield return Shot(gc, cam, west + Vector3.right * 8f, 45f, GameCamera.ClassicPitch, 0f, Path.Combine(dir, name + "-edge-classic.png"));
                    yield return Shot(gc, cam, west + Vector3.right * 10f, 40f, 24f, -90f, Path.Combine(dir, name + "-edge-low.png"));
                }
            }
            File.WriteAllLines(Path.Combine(dir, tag + "-seaport.txt"), log);
            foreach (var line in log) Debug.Log(line);
        }

        IEnumerator Start(string map)
        {
            if (root.Flow.State == FlowState.Playing) root.Flow.Fire(FlowEvent.Pause);
            if (root.Flow.State != FlowState.MainMenu) root.Flow.Fire(FlowEvent.ToMenu);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = map;
            root.Setup.MapRevealed = true;
            root.Setup.LineOfSight = false;
            root.Setup.StartMana = 5000;
            root.Setup.Seats[0].Side = "TAROS";
            root.Setup.Seats[1].Side = "ARAMON";
            root.Setup.Seats[1].Difficulty = AiDifficulty.Easy;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 240f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            if (root.Flow.State == FlowState.Playing) root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            for (int i = 0; i < 30; i++) yield return null;
        }

        // The cheapest building the monarch makes, raised near him.
        bool Place(UnitState monarch)
        {
            var b = root.Backend;
            var md = b.UnitDefs[monarch.Def];
            foreach (int def in md.BuildOptions.Where(o => o >= 0 && o < b.UnitDefs.Count && b.UnitDefs[o].IsBuilding).OrderBy(o => b.UnitDefs[o].ManaCost))
                for (int ring = 5; ring < 48; ring += 3)
                    for (int k = 0; k < 24; k++)
                    {
                        float a = k * Mathf.PI / 12f;
                        var at = monarch.Position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * ring;
                        if (!b.CanBuildAt(def, at, 0, out var site)) continue;
                        if (b.Command(new GameCommand { Kind = CommandKind.Build, Unit = monarch.Handle, Target = site, TargetUnit = -1, BuildDef = def }))
                            return true;
                    }
            return false;
        }

        // The middle of the longest run of sea along any edge, and the
        // camera yaw that looks out over it.
        static bool SeaEdge(MapTerrain t, out Vector3 at, out float outward)
        {
            at = default;
            outward = 0f;
            if (t == null || t.SeaLevel <= 0) return false;
            var size = t.Size;
            int best = 0;
            var edges = new (Vector2 from, Vector2 dir, float len, float yaw)[]
            {
                (new Vector2(0, 0), new Vector2(0, -1), size.y, -90f),
                (new Vector2(size.x, 0), new Vector2(0, -1), size.y, 90f),
                (new Vector2(0, 0), new Vector2(1, 0), size.x, 0f),
                (new Vector2(0, -size.y), new Vector2(1, 0), size.x, 180f),
            };
            foreach (var e in edges)
            {
                int run = 0;
                for (float s = 0; s <= e.len; s += 1f)
                {
                    var p = e.from + e.dir * s;
                    if (t.Sample(p.x, p.y) < t.SeaLevel - 0.5f) run++; else run = 0;
                    if (run > best)
                    {
                        best = run;
                        var mid = e.from + e.dir * (s - run * 0.5f);
                        at = new Vector3(mid.x, t.SeaLevel, mid.y);
                        outward = e.yaw;
                    }
                }
            }
            return best >= 6;
        }

        IEnumerable<UnitState> Own()
        {
            var units = new UnitState[4096];
            int n = root.Backend.ReadUnits(units);
            for (int i = 0; i < n; i++)
                if (units[i].Player == root.Backend.LocalPlayer && (units[i].Flags & UnitFlags.Dying) == 0) yield return units[i];
        }

        static IEnumerator Shot(GameCamera gc, Camera cam, Vector3 focus, float distance, float pitch, float yaw, string path)
        {
            gc.focus = focus;
            gc.pitch = pitch;
            gc.yaw = yaw;
            gc.Zoom(distance);
            for (int i = 0; i < 20; i++) yield return null;
            var rt = RenderTexture.GetTemporary(1920, 1080, 24);
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = old;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
