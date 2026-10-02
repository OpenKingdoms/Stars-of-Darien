// SmokeRun.cs - "-okSmoke <seconds>" on a player's command line starts a
// skirmish on the smallest map against one computer player, lets it run
// that long, logs the frames drawn and the simulation's ticks, and quits.
// The exit code is 0 when the engine ran the battle and says why not
// otherwise. "-okSmokeMap <name>" picks the map. "-okSmokeViews <views>"
// reveals the map and, with a GPU, saves SceneryViews pictures of each view
// in the shots folder. Every line it logs starts with OKSMOKE.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public sealed class SmokeRun : MonoBehaviour
    {
        public const string Flag = "-okSmoke", MapFlag = "-okSmokeMap", Prefix = "OKSMOKE ";
        public const float DefaultSeconds = 20f, LoadLimit = 300f;

        public enum Result { Passed = 0, NoEngine = 2, NoMap = 3, LoadFailed = 4, NoTicks = 5, NoModels = 6 }

        // "-okSmokeShots <folder>" is where the pictures go, with a GPU.
        public const string ShotsFlag = "-okSmokeShots";
        public const string ViewsFlag = "-okSmokeViews";
        public const int ShotWidth = 1280, ShotHeight = 720;
        // A pixel counts as drawn by a model when it differs this much, summed
        // over red, green and blue, from the same frame without the models.
        public const int PixelDelta = 48;
        // The share of the monarch's rect that must change for it to count as drawn.
        public const float MonarchShare = 0.04f;

        // The seconds asked for, or null without the flag.
        public static float? Seconds(string[] args)
        {
            int i = Find(args, Flag);
            if (i < 0) return null;
            if (i + 1 < args.Length && float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float s) && s > 0f) return s;
            return DefaultSeconds;
        }

        public static string MapArg(string[] args)
        {
            int i = Find(args, MapFlag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        public static List<SceneryViews.Spec> ViewsArg(string[] args)
        {
            int i = Find(args, ViewsFlag);
            return SceneryViews.Parse(i >= 0 && i + 1 < args.Length ? args[i + 1] : null);
        }

        public static int Find(string[] args, string flag)
        {
            if (args == null) return -1;
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        // The map named, in any case, or else the smallest that holds two
        // players, by area and then by name.
        public static MapInfo PickMap(IReadOnlyList<MapInfo> maps, string named)
        {
            MapInfo best = null;
            foreach (var m in maps)
            {
                if (!string.IsNullOrEmpty(named))
                {
                    if (string.Equals(m.Id, named, StringComparison.OrdinalIgnoreCase) || string.Equals(m.Name, named, StringComparison.OrdinalIgnoreCase)) return m;
                    continue;
                }
                if (MapCatalog.PlayersOf(m) < 2) continue;
                if (best == null) { best = m; continue; }
                float a = MapCatalog.AreaOf(m), b = MapCatalog.AreaOf(best);
                if (a < b || a == b && string.CompareOrdinal(m.Id, best.Id) < 0) best = m;
            }
            return best;
        }

        // You in seat 0 and one computer player, the rest closed.
        public static void Seat(SkirmishSetup s, MapInfo map)
        {
            s.MapId = map.Id;
            s.Seed = 12345;
            for (int i = 0; i < s.Seats.Count; i++)
            {
                s.Seats[i].Start = -1;
                if (i >= 2) s.Seats[i].Kind = SeatKind.Closed;
            }
            s.Seats[0].Kind = SeatKind.Human;
            s.Seats[1].Kind = SeatKind.Computer;
        }

        GameRoot root;
        float seconds;

        public static SmokeRun Begin(GameRoot root, float seconds)
        {
            var run = root.gameObject.AddComponent<SmokeRun>();
            run.root = root;
            run.seconds = seconds;
            return run;
        }

        static void Say(string line) => Debug.Log(Prefix + line);

        void Quit(Result r, string why)
        {
            Say($"RESULT {(r == Result.Passed ? "PASS" : "FAIL")} {r}: {why}");
            enabled = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit((int)r);
#endif
        }

        IEnumerator Start()
        {
            Application.targetFrameRate = 60;
            var b = root.Backend;
            Say($"start {BuildStamp.Version ?? "development build"}, {seconds:0.#} s, batch {Application.isBatchMode}, graphics {SystemInfo.graphicsDeviceType}");
            Say($"engine {b.Name}, {b.Maps.Count} maps, {b.Sides.Count} sides, game folder {GameRoot.GameFolder?.Invoke() ?? "(editor setting)"}");
            if (b is MockBackend)
            {
                Quit(Result.NoEngine, "the engine did not start, so the stand-in world runs. " + (root.BackendProblem ?? "See the lines above for why."));
                yield break;
            }
            var map = PickMap(b.Maps, MapArg(Environment.GetCommandLineArgs()));
            if (map == null)
            {
                Quit(Result.NoMap, "no map for two players");
                yield break;
            }
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            Seat(root.Setup, map);
            var views = ViewsArg(Environment.GetCommandLineArgs());
            if (views.Count > 0)
            {
                root.Setup.MapRevealed = true;
                root.Setup.LineOfSight = false;
            }
            Say($"skirmish on {map.Id} ({map.Size.x:0}x{map.Size.y:0}, {MapCatalog.PlayersOf(map)} starts) against one computer player");
            root.Flow.Fire(FlowEvent.Start);

            float began = Time.realtimeSinceStartup;
            while (root.Flow.State == FlowState.Loading && Time.realtimeSinceStartup - began < LoadLimit) yield return null;
            if (root.Flow.State != FlowState.Playing)
            {
                Quit(Result.LoadFailed, $"the battle did not start: state {root.Flow.State}, {root.LastError ?? "no error given"}");
                yield break;
            }
            Say($"skirmish started after {Time.realtimeSinceStartup - began:0.0} s, status {b.Status}, tick {b.Tick}");

            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                // A few seconds in, the models have loaded and the camera settled.
                float settle = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < settle && GameFlow.InGame(root.Flow.State)) yield return null;
                string why = null;
                yield return Picture(root, ShotsDir(Environment.GetCommandLineArgs()), "smoke-battle", true, (r, share) => why = r);
                if (why != null)
                {
                    Quit(Result.NoModels, why);
                    yield break;
                }
                if (views.Count > 0) yield return Views(map, views);
            }
            else Say("no graphics device, so no picture: run without -nographics to check what draws");

            uint firstTick = b.Tick;
            int firstFrame = root.FramesPlayed;
            float playFrom = Time.realtimeSinceStartup, nextLine = 5f;
            while (Time.realtimeSinceStartup - playFrom < seconds && GameFlow.InGame(root.Flow.State))
            {
                float t = Time.realtimeSinceStartup - playFrom;
                if (t >= nextLine)
                {
                    Say($"at {t:0} s: frames {root.FramesPlayed - firstFrame}, tick {b.Tick}, units in sight {Units(b)}, {Players(b)}");
                    nextLine += 5f;
                }
                yield return null;
            }
            float ran = Time.realtimeSinceStartup - playFrom;
            int frames = root.FramesPlayed - firstFrame;
            uint ticks = b.Tick - firstTick;
            Say($"done: {frames} frames and {ticks} sim ticks in {ran:0.0} s ({frames / Mathf.Max(0.01f, ran):0.0} fps, {ticks / Mathf.Max(0.01f, ran):0.0} ticks a second, {b.TicksPerSecond} expected), state {root.Flow.State}, units in sight {Units(b)}, {Players(b)}");
            if (ticks == 0) Quit(Result.NoTicks, "the simulation did not advance");
            else Quit(Result.Passed, $"{ticks} ticks in {ran:0.0} s");
        }

        static readonly UnitState[] unitBuf = new UnitState[2048];

        // Draws the battle camera's view twice into pictures, with the
        // models and without, and checks the monarch shows where it stands.
        // done gets why not, or null, and the share of the monarch drawn.
        // The pictures are <name>.png and, with bare, <name>-no-models.png.
        public static IEnumerator Picture(GameRoot root, string dir, string name, bool bare, Action<string, float> done)
        {
            var world = root.World;
            var cam = world?.Camera != null ? world.Camera.GetComponent<Camera>() : null;
            if (cam == null) { done("the battle has no camera", 0f); yield break; }
            System.IO.Directory.CreateDirectory(dir);

            yield return null;
            var with = Grab(cam);
            var monarch = Monarch(root.Backend, world, cam, out string who);
            world.Entities.HideModels = true;
            yield return null;
            var without = Grab(cam);
            world.Entities.HideModels = false;

            string a = System.IO.Path.Combine(dir, name + ".png"), plain = System.IO.Path.Combine(dir, name + "-no-models.png");
            System.IO.File.WriteAllBytes(a, with.EncodeToPNG());
            if (bare) System.IO.File.WriteAllBytes(plain, without.EncodeToPNG());
            int whole = Changed(with, without, new RectInt(0, 0, with.width, with.height), out int wholeArea);
            Say($"pictures {ShotWidth}x{ShotHeight}: {a}{(bare ? " and " + plain : "")}; the models change {whole} of {wholeArea} pixels");
            string why = null;
            float share = 0f;
            if (monarch is RectInt m)
            {
                int inRect = Changed(with, without, m, out int area);
                share = inRect / (float)Mathf.Max(1, area);
                Say($"monarch {who} in the picture at x {m.x}..{m.xMax}, y {m.y}..{m.yMax} from the bottom: {inRect} of {area} pixels drawn ({share:P0})");
                if (share < MonarchShare) why = $"the monarch does not draw: {share:P1} of its rect changes with the models, {MonarchShare:P0} expected";
            }
            else why = "no monarch of yours in the camera's view: " + who;
            if (why == null && whole < wholeArea / 200) why = $"the models change only {whole} pixels of the picture";
            Destroy(with);
            Destroy(without);
            done(why, share);
        }

        // Each view's pictures as <map>-<label>-<angle>.png in the shots folder.
        IEnumerator Views(MapInfo map, List<SceneryViews.Spec> views)
        {
            string dir = ShotsDir(Environment.GetCommandLineArgs());
            System.IO.Directory.CreateDirectory(dir);
            root.World.Atmosphere.SetWeather(WeatherChoice.Off);
            string slug = (map.Name ?? map.Id).Replace("'", "").Replace(' ', '-').ToLowerInvariant();
            foreach (var v in views)
            {
                var spot = SceneryViews.Spot(root.Backend, v);
                if (spot == null) { Say($"view {v.Label}: no feature matches {v.Anchor}"); continue; }
                Say($"view {v.Label} at ({spot.Value.x:0.0}, {spot.Value.z:0.0})");
                yield return SceneryViews.Take(root.World, spot.Value, (angle, png) =>
                {
                    string path = System.IO.Path.Combine(dir, $"{slug}-{v.Label}-{angle}.png");
                    System.IO.File.WriteAllBytes(path, png);
                    Say("picture " + path);
                });
            }
        }

        public static string ShotsDir(string[] args)
        {
            int i = Find(args, ShotsFlag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : System.IO.Path.Combine(Application.persistentDataPath, "Smoke");
        }

        // The camera's view now, in a picture the size of a 720p screen.
        static Texture2D Grab(Camera cam)
        {
            var rt = RenderTexture.GetTemporary(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32);
            var was = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = was;
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
            tex.Apply(false);
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        // Where your first unit, the monarch at the start, stands in the
        // picture: its drawn height and girth projected, origin bottom left.
        static RectInt? Monarch(IGameBackend b, World.WorldView world, Camera cam, out string who)
        {
            int n = b.ReadUnits(unitBuf);
            for (int i = 0; i < n; i++)
            {
                var u = unitBuf[i];
                if (u.Player != b.LocalPlayer) continue;
                who = $"handle {u.Handle} at {u.Position}";
                Vector2 size = world.Entities.DrawnSize.TryGetValue(u.Handle, out var s) ? s : new Vector2(2f, 0.8f);
                float h = Mathf.Max(0.5f, size.x), r = Mathf.Max(0.3f, size.y);
                float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
                foreach (var dx in new[] { -r, r })
                    foreach (var dz in new[] { -r, r })
                        foreach (var dy in new[] { 0f, h })
                        {
                            var p = cam.WorldToViewportPoint(u.Position + new Vector3(dx, dy, dz));
                            if (p.z <= 0f) continue;
                            xMin = Mathf.Min(xMin, p.x); xMax = Mathf.Max(xMax, p.x);
                            yMin = Mathf.Min(yMin, p.y); yMax = Mathf.Max(yMax, p.y);
                        }
                if (xMin > xMax) return null;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(xMin * ShotWidth), 0, ShotWidth - 1), x1 = Mathf.Clamp(Mathf.CeilToInt(xMax * ShotWidth), 1, ShotWidth);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(yMin * ShotHeight), 0, ShotHeight - 1), y1 = Mathf.Clamp(Mathf.CeilToInt(yMax * ShotHeight), 1, ShotHeight);
                if (x1 - x0 < 2 || y1 - y0 < 2) return null;
                return new RectInt(x0, y0, x1 - x0, y1 - y0);
            }
            who = "you have no units";
            return null;
        }

        // How many pixels in the rect differ by PixelDelta or more.
        public static int Changed(Texture2D a, Texture2D b, RectInt r, out int area)
        {
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            int count = 0, w = a.width;
            area = r.width * r.height;
            for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++)
                {
                    int i = y * w + x;
                    if (Math.Abs(pa[i].r - pb[i].r) + Math.Abs(pa[i].g - pb[i].g) + Math.Abs(pa[i].b - pb[i].b) >= PixelDelta) count++;
                }
            return count;
        }

        // Each player's mana and what it spends, which shows the computer at work.
        static string Players(IGameBackend b)
        {
            var parts = new List<string>();
            try
            {
                foreach (var p in b.Players)
                {
                    var e = b.ReadEconomy(p.Index);
                    parts.Add($"{(p.IsLocal ? "you" : p.IsComputer ? "computer" : "player " + p.Index)} {p.Side}{(p.Alive ? "" : " (out)")} mana {e.Mana:0} spending {e.Expense:0.#}/s");
                }
            }
            catch (Exception e) { parts.Add("players unreadable: " + e.Message); }
            return string.Join("; ", parts);
        }

        static int Units(IGameBackend b)
        {
            try { return b.ReadUnits(unitBuf); }
            catch (Exception) { return -1; }
        }
    }
}
