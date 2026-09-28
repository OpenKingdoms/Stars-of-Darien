// WaterCaptures.cs - pictures of the sea on real maps, for judging the
// water by eye: the open sea, a coast close up, low toward the sun, wide,
// and a coast at the map's edge running on into the ring, in clear
// weather, rain and fog. Runs only with OKU_CAPTURE_DIR and OKU_WATER_MAPS
// (map names split by ';'). A name ending "+fow" also shoots the fog of
// war at the start with the map revealed, and with the start's units
// walked to the shore so their sight lies over the water. One ending
// "+ships" builds a Veruna harbour and ships, shoots them at rest, low from
// the side and in three frames as they bob, and sails them. On the engine
// with OKU_CAPTURE_BACKEND=engine, in the built-in pipeline with
// OKU_CAPTURE_PIPELINE=builtin. Files are water-<tag>-<pipeline>-<map>-<shot>.png.
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
        bool builtin;
        StreamWriter log;

        [UnityTest]
        public IEnumerator CaptureTheSea()
        {
            dir = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_DIR");
            string maps = System.Environment.GetEnvironmentVariable("OKU_WATER_MAPS");
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(maps)) Assert.Ignore("set OKU_CAPTURE_DIR and OKU_WATER_MAPS to capture the sea");
            builtin = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_PIPELINE") == "builtin";
            tag = (System.Environment.GetEnvironmentVariable("OKU_CAPTURE_TAG") ?? "shot") + (builtin ? "-builtin" : "-urp");
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
            var pipeline = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            var quality = QualitySettings.renderPipeline;
            if (builtin)
            {
                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = null;
                QualitySettings.renderPipeline = null;
            }
            try
            {
                yield return MapIn(engine, map, fogOfWar, ships);
            }
            finally
            {
                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = quality;
            }
        }

        IEnumerator MapIn(bool engine, string map, bool fogOfWar, bool ships)
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
                // The start's units walked to the shore, so the lit round of
                // their sight lies over the water, as in the owner's game.
                yield return Shore(root, start, near, slug);
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
                if (EdgeCoast(t, out var edge, out float outward))
                {
                    log.WriteLine($"{map}: coast at the map's edge at {edge}, looking {outward:F0} degrees");
                    gc.focus = edge;
                    yield return View(gc, 45f, 40f, outward);
                    yield return Shoot(cam, $"water-{tag}-{slug}-{wn}-edge.png");
                }
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

        // A point on the map's edge where land meets the sea, a little inside
        // the map, and the camera's yaw looking out over the ring from it.
        static bool EdgeCoast(MapTerrain t, out Vector3 at, out float yaw)
        {
            at = default;
            yaw = 0;
            if (t.SeaLevel <= 0) return false;
            var size = t.Size;
            float step = t.CellSize;
            // North, east, south and west edges, each walked along.
            var edges = new[]
            {
                (from: new Vector2(0, 0), along: new Vector2(1, 0), inward: new Vector2(0, -1), yaw: 0f, length: size.x),
                (from: new Vector2(size.x, 0), along: new Vector2(0, -1), inward: new Vector2(-1, 0), yaw: 90f, length: size.y),
                (from: new Vector2(0, -size.y), along: new Vector2(1, 0), inward: new Vector2(0, 1), yaw: 180f, length: size.x),
                (from: new Vector2(0, 0), along: new Vector2(0, -1), inward: new Vector2(1, 0), yaw: 270f, length: size.y),
            };
            foreach (var e in edges)
            {
                bool? wasWet = null;
                for (float d = 8 * step; d <= e.length - 8 * step; d += step)
                {
                    var p = e.from + e.along * d;
                    bool wet = t.Sample(p.x, p.y) < t.SeaLevel - 0.3f;
                    if (wasWet.HasValue && wet != wasWet.Value)
                    {
                        var q = p + e.inward * 6f;
                        at = new Vector3(q.x, t.SeaLevel, q.y);
                        yaw = e.yaw;
                        return true;
                    }
                    wasWet = wet;
                }
            }
            return false;
        }

        // The local player's units sent to the shore nearest the start, then
        // shot close and wide with line of sight on.
        IEnumerator Shore(GameRoot root, Vector3 start, Vector3 water, string slug)
        {
            var b = root.Backend;
            var t = b.Terrain;
            // The last dry ground on the way from the start to the water.
            var land = start;
            for (float f = 0; f <= 1f; f += 0.01f)
            {
                var p = Vector3.Lerp(start, water, f);
                if (t.Sample(p.x, p.z) < t.SeaLevel + 0.15f) break;
                land = p;
            }
            var units = new UnitState[4096];
            int n = b.ReadUnits(units);
            int sent = 0, lead = -1;
            for (int i = 0; i < n && sent < 6; i++)
            {
                var u = units[i];
                if (u.Player != b.LocalPlayer || b.UnitDefs[u.Def].IsBuilding) continue;
                if (b.Command(GameCommand.To(CommandKind.Move, u.Handle, land + new Vector3((sent % 3) - 1, 0, (sent / 3) - 0.5f) * 1.5f)))
                {
                    if (lead < 0) lead = u.Handle;
                    sent++;
                }
            }
            log.WriteLine($"fog: {sent} units sent from {start} to the shore at {land}");
            if (lead < 0) yield break;
            Vector3 at = start;
            for (int step = 0; step < 400; step++)
            {
                b.Advance(10);
                n = b.ReadUnits(units);
                for (int i = 0; i < n; i++) if (units[i].Handle == lead) at = units[i].Position;
                if ((new Vector2(at.x - land.x, at.z - land.z)).magnitude < 3f) break;
                if (step % 10 == 0) yield return null;
            }
            log.WriteLine($"fog: the lead unit reached {at}");
            root.World.Fog.Update(true);
            var gc = root.World.Camera;
            var cam = gc.GetComponent<Camera>();
            gc.focus = Vector3.Lerp(at, water, 0.35f);
            yield return View(gc, 36f, GameCamera.ClassicPitch, 0f);
            yield return Shoot(cam, $"water-{tag}-{slug}-fow-shore.png");
            yield return View(gc, 60f, 55f, 0f);
            yield return Shoot(cam, $"water-{tag}-{slug}-fow-shore-wide.png");
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
            // Low from the side, to judge the waterline, and three frames of
            // the same to see the ships bob.
            float minPitch = gc.minPitch;
            gc.minPitch = 4f;
            yield return View(gc, 11f, 7f, first.Heading + 90f);
            yield return Shoot(cam, $"water-{tag}-{slug}-ships-side.png");
            for (int k = 1; k <= 3; k++)
            {
                float next = Time.realtimeSinceStartup + 0.45f;
                while (Time.realtimeSinceStartup < next) yield return null;
                yield return Shoot(cam, $"water-{tag}-{slug}-ships-bob-{k}.png");
            }
            gc.minPitch = minPitch;
            for (int k = 0; k < fleet.Count; k++)
                b.Command(GameCommand.To(CommandKind.Move, fleet[k], open + new Vector3(k * 3f, 0, k * 2f)));
            float until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until) yield return null;
            n = b.ReadUnits(units);
            var lead = units.Take(n).First(u => u.Handle == fleet[0]);
            var ground = b.GroundHeight(lead.Position.x, lead.Position.z);
            var hulls = root.World.Entities.Hulls;
            string hullNote = hulls.TryGetValue(fleet[0], out var h) ? $"keel {h.hull.Keel:F2}, draft {h.hull.Draft:F2}, beam {h.hull.HalfBeam:F2}, length {h.hull.HalfLength:F2}, the game's waterline {b.UnitDefs[lead.Def].Waterline:F2}" : "no hull";
            log.WriteLine($"ships: lead at {lead.Position}, ground {ground:F2}, sea {b.Terrain.SeaLevel:F2}, drawn {root.World.Entities.UnitBounds(fleet[0])}, {hullNote}");
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
