// SmokeRun.cs - "-okSmoke <seconds>" on a player's command line starts a
// skirmish on the smallest map against one computer player, lets it run
// that long, logs the frames drawn and the simulation's ticks, and quits.
// The exit code is 0 when the engine ran the battle and says why not
// otherwise. "-okSmokeMap <name>" picks the map. Every line it logs starts
// with OKSMOKE.
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

        public enum Result { Passed = 0, NoEngine = 2, NoMap = 3, LoadFailed = 4, NoTicks = 5 }

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

        static int Find(string[] args, string flag)
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
