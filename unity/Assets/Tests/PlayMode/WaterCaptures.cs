// WaterCaptures.cs - pictures of the sea on real maps, for judging the
// water by eye: the open sea, a coast close up, low toward the sun and
// wide, in clear weather, rain and fog. Runs only with OKU_CAPTURE_DIR and
// OKU_WATER_MAPS (map names split by ';'). A name ending "+fow" also shoots
// the fog of war at the start with the map revealed, one ending "+ships"
// builds a Veruna harbour and ships and sails them. On the engine with
// OKU_CAPTURE_BACKEND=engine. Files are water-<tag>-<map>-<shot>.png.
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
    public class WaterCaptures
    {
        string dir, tag;
        StreamWriter log;

        [UnityTest]
        public IEnumerator CaptureTheSea()
        {
            dir = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_DIR");
            string maps = System.Environment.GetEnvironmentVariable("OKU_WATER_MAPS");
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(maps)) Assert.Ignore("set OKU_CAPTURE_DIR and OKU_WATER_MAPS to capture the sea");
            tag = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_TAG") ?? "shot";
            Directory.CreateDirectory(dir);
            bool engine = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_BACKEND") == "engine";
            if (engine && GameRoot.BackendFactory == null) Assert.Ignore("no engine");
            using (log = new StreamWriter(Path.Combine(dir, $"water-{tag}.txt"), true))
            {
                if (engine)
                {
                    var view = GameObject.Find("OpenKingdoms");
                    if (view != null) Object.Destroy(view);
                    yield return null;
                    yield return null;
                }
                foreach (var entry in maps.Split(';'))
                {
                    string name = entry.Trim();
                    if (name.Length == 0) continue;
                    bool fow = name.EndsWith("+fow"), ships = name.EndsWith("+ships");
                    if (fow || ships) name = name.Substring(0, name.LastIndexOf('+'));
                    log.WriteLine($"== {name} {System.DateTime.Now:HH:mm:ss}");
                    yield return Map(engine, name, false, ships);
                    if (fow) yield return Map(engine, name, true, false);
                    log.Flush();
                }
            }
        }

        static string Slug(string s) => new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());

        IEnumerator Map(bool engine, string map, bool fogOfWar, bool ships)
        {
            var root = engine ? GameRoot.Boot() : GameRoot.Boot(new MockBackend { StageSeconds = 0f });
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = map;
            // The fog of war as on the owner's Castle game: the map revealed,
            // so ground out of sight shows as seen before, and line of sight on.
            root.Setup.MapRevealed = true;
            root.Setup.LineOfSight = true;
            if (ships && root.Setup.Seats.Count > 0) root.Setup.Seats[0].Side = "VERUNA";
            root.Setup.StartMana = 20000;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 300f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            if (root.Flow.State != FlowState.Playing)
            {
                log.WriteLine($"{map}: did not load: {root.LastError}");
                Object.Destroy(root.gameObject);
                yield return null;
                yield break;
            }
            root.Orders.Frozen = true;
            root.Screens.Screen("Hud").SetActive(false);
            var t = root.Backend.Terrain;
            string slug = Slug(map);
            if (t.SeaLevel <= 0) log.WriteLine($"{map}: no sea");
            FindWater(t, out var open, out var coast, out float openReach);
            log.WriteLine($"{map}: sea {t.SeaLevel:F2}, size {t.Size}, open sea at {open} ({openReach:F1} cells from land), coast at {coast}");
            var gc = root.World.Camera;
            var cam = gc.GetComponent<Camera>();
            if (fogOfWar)
            {
                // The fog of war from the start: water in sight round the
                // start, and water seen before, dimmed, farther off.
                var start = gc.focus;
                var near = NearestWater(t, start);
                gc.focus = Vector3.Lerp(start, near, 0.7f);
                float until = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < until) yield return null;
                yield return View(gc, 40f, GameCamera.ClassicPitch, 0f);
                yield return Shoot(cam, $"water-{tag}-{slug}-fow.png");
                yield return View(gc, 70f, 55f, 0f);
                yield return Shoot(cam, $"water-{tag}-{slug}-fow-wide.png");
                Object.Destroy(root.gameObject);
                yield return null;
                yield break;
            }
            FogView.Disabled = true;
            root.World.Fog.Update(true);
            if (ships) yield return Ships(root, open, slug);
            foreach (var w in new[] { WeatherChoice.Off, WeatherChoice.Rain, WeatherChoice.Fog })
            {
                root.World.Atmosphere.SetWeather(w);
                var sea = root.World.Terrain.Sea;
                if (sea != null) sea.Apply(sea.Target(w), 1f);
                string wn = w == WeatherChoice.Off ? "clear" : w.ToString().ToLowerInvariant();
                gc.focus = open;
                yield return View(gc, 34f, GameCamera.ClassicPitch, 0f);
                yield return Shoot(cam, $"water-{tag}-{slug}-{wn}-classic.png");
                gc.focus = coast;
                yield return View(gc, 16f, 45f, 20f);
                yield return Shoot(cam, $"water-{tag}-{slug}-{wn}-coast.png");
                if (w != WeatherChoice.Off) continue;
                gc.focus = open;
                yield return View(gc, 30f, 26f, -30f);
                yield return Shoot(cam, $"water-{tag}-{slug}-{wn}-sunward.png");
                gc.focus = open;
                yield return View(gc, 85f, 55f, 0f);
                yield return Shoot(cam, $"water-{tag}-{slug}-{wn}-wide.png");
            }
            FogView.Disabled = false;
            Object.Destroy(root.gameObject);
            yield return null;
        }

        // The wettest point (farthest from land and from the map's edge) and
        // a shallow coast, from the height grid.
        static void FindWater(MapTerrain t, out Vector3 open, out Vector3 coast, out float reach)
        {
            int w = t.HeightsW, h = t.HeightsH;
            var wet = new bool[w * h];
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    wet[z * w + x] = t.SeaLevel > 0 && t.HeightAt(x, z) < t.SeaLevel && x > 0 && z > 0 && x < w - 1 && z < h - 1;
            var toLand = WaterTextures.Distance(wet, w, h, false);
            int best = -1, shore = -1;
            float bestD = -1, shallowest = float.MaxValue;
            for (int i = 0; i < w * h; i++)
            {
                if (!wet[i]) continue;
                int x = i % w, z = i / w;
                if (x < 3 || z < 3 || x > w - 4 || z > h - 4) continue;
                if (toLand[i] > bestD) { bestD = toLand[i]; best = i; }
                if (toLand[i] >= 2.5f && toLand[i] <= 3.5f)
                {
                    float d = t.SeaLevel - t.HeightAt(x, z);
                    if (d < shallowest) { shallowest = d; shore = i; }
                }
            }
            reach = bestD;
            Vector3 At(int i) => i < 0 ? new Vector3(t.Size.x / 2, 0, -t.Size.y / 2)
                : new Vector3((i % w) * t.CellSize, Mathf.Max(0, t.SeaLevel), -(i / w) * t.CellSize);
            open = At(best);
            coast = At(shore >= 0 ? shore : best);
        }

        static Vector3 NearestWater(MapTerrain t, Vector3 from)
        {
            var best = from;
            float bd = float.MaxValue;
            for (int z = 0; z < t.HeightsH; z += 2)
                for (int x = 0; x < t.HeightsW; x += 2)
                {
                    if (t.HeightAt(x, z) >= t.SeaLevel - 0.3f) continue;
                    var p = new Vector3(x * t.CellSize, t.SeaLevel, -z * t.CellSize);
                    float d = (p - from).sqrMagnitude;
                    if (d < bd) { bd = d; best = p; }
                }
            return best;
        }

        // A Veruna harbour on the water near the monarch, three ships from
        // it, and the ships sent out across the open sea.
        IEnumerator Ships(GameRoot root, Vector3 open, string slug)
        {
            var b = root.Backend;
            var units = new UnitState[4096];
            int Find(string name) { for (int i = 0; i < b.UnitDefs.Count; i++) if (string.Equals(b.UnitDefs[i].Name, name, System.StringComparison.OrdinalIgnoreCase)) return i; return -1; }
            int yard = Find("verasy");
            var shipDefs = new[] { Find("verman"), Find("verscout"), Find("verharp") }.Where(d => d >= 0).ToArray();
            int n = b.ReadUnits(units);
            int builder = -1;
            Vector3 at = Vector3.zero;
            for (int i = 0; i < n; i++)
                if (units[i].Player == b.LocalPlayer && b.UnitDefs[units[i].Def].BuildOptions.Contains(yard)) { builder = units[i].Handle; at = units[i].Position; }
            log.WriteLine($"ships: yard def {yard}, ship defs {string.Join(",", shipDefs)}, builder {builder} at {at}");
            if (yard < 0 || builder < 0 || shipDefs.Length == 0) yield break;
            bool placed = false;
            Vector3 site = at;
            for (float r = 4; r <= 70 && !placed; r += 2)
                for (int k = 0; k < 24 && !placed; k++)
                {
                    float a = k * Mathf.PI * 2 / 24;
                    var p = at + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                    if (b.CanBuildAt(yard, p, 0, out var snapped)) { placed = true; site = snapped; }
                }
            log.WriteLine($"ships: harbour site {(placed ? site.ToString() : "none")}");
            if (!placed) yield break;
            bool ordered = b.Command(new GameCommand { Kind = CommandKind.Build, Unit = builder, Target = site, TargetUnit = -1, BuildDef = yard });
            log.WriteLine($"ships: build ordered {ordered}");
            int harbour = -1;
            for (int step = 0; step < 600 && harbour < 0; step++)
            {
                b.Advance(20);
                n = b.ReadUnits(units);
                for (int i = 0; i < n; i++)
                    if (units[i].Def == yard && units[i].Player == b.LocalPlayer && (units[i].Flags & UnitFlags.Building) == 0) harbour = units[i].Handle;
                if (step % 25 == 0) yield return null;
            }
            log.WriteLine($"ships: harbour {harbour} after {b.Tick} ticks, mana {b.ReadEconomy(b.LocalPlayer).Mana:F0}");
            if (harbour < 0) yield break;
            for (int k = 0; k < 3; k++)
                b.Command(new GameCommand { Kind = CommandKind.FactoryEnqueue, Unit = harbour, TargetUnit = -1, BuildDef = shipDefs[k % shipDefs.Length] });
            var fleet = new List<int>();
            for (int step = 0; step < 1500 && fleet.Count < 3; step++)
            {
                b.Advance(20);
                n = b.ReadUnits(units);
                fleet.Clear();
                for (int i = 0; i < n; i++)
                    if (shipDefs.Contains(units[i].Def) && units[i].Player == b.LocalPlayer && (units[i].Flags & UnitFlags.Building) == 0) fleet.Add(units[i].Handle);
                if (step % 25 == 0) yield return null;
            }
            log.WriteLine($"ships: {fleet.Count} ships after {b.Tick} ticks");
            if (fleet.Count == 0) yield break;
            var gc = root.World.Camera;
            var cam = gc.GetComponent<Camera>();
            // At rest by the harbour, once the trails of the fast build fade,
            // then under way.
            float calm = Time.realtimeSinceStartup + WaterWakes.Life + 0.5f;
            while (Time.realtimeSinceStartup < calm) yield return null;
            n = b.ReadUnits(units);
            var first = units.Take(n).First(u => u.Handle == fleet[0]);
            gc.focus = first.Position;
            yield return View(gc, 22f, 50f, 15f);
            yield return Shoot(cam, $"water-{tag}-{slug}-ships-harbour.png");
            yield return View(gc, 12f, 35f, 40f);
            yield return Shoot(cam, $"water-{tag}-{slug}-ships-close.png");
            for (int k = 0; k < fleet.Count; k++)
                b.Command(GameCommand.To(CommandKind.Move, fleet[k], open + new Vector3(k * 3f, 0, k * 2f)));
            float until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until) yield return null;
            n = b.ReadUnits(units);
            var lead = units.Take(n).First(u => u.Handle == fleet[0]);
            var ground = b.GroundHeight(lead.Position.x, lead.Position.z);
            log.WriteLine($"ships: lead at {lead.Position}, ground {ground:F2}, sea {b.Terrain.SeaLevel:F2}, drawn {root.World.Entities.UnitBounds(fleet[0])}");
            gc.focus = lead.Position;
            yield return View(gc, 26f, 55f, 0f);
            yield return Shoot(cam, $"water-{tag}-{slug}-ships-underway.png");
            yield return View(gc, 14f, 38f, 150f);
            yield return Shoot(cam, $"water-{tag}-{slug}-ships-wake.png");
        }

        static IEnumerator View(GameCamera c, float distance, float pitch, float yaw)
        {
            c.pitch = pitch;
            c.yaw = yaw;
            c.Zoom(distance);
            for (int i = 0; i < 20; i++) yield return null;
        }

        static int W => int.TryParse(System.Environment.GetEnvironmentVariable("OKU_CAPTURE_W"), out int w) ? w : 1920;
        static int H => int.TryParse(System.Environment.GetEnvironmentVariable("OKU_CAPTURE_H"), out int h) ? h : 1080;

        IEnumerator Shoot(Camera cam, string file)
        {
            yield return null;
            var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = old;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
            Object.Destroy(tex);
            log.WriteLine("shot " + file);
        }
    }
}
