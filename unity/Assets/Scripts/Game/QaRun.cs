// QaRun.cs - long checks a built player runs from its command line, with
// no window when it starts with -batchmode. "-okMapSweep <out.csv>" loads
// every map as a skirmish against one computer player, runs each for
// SweepSeconds of game time and writes a row per map as it goes, so a
// crash still leaves the rows before it. "-okSweepFrom <n>" starts at the
// nth map, counting from 0, and "-okSweepOnly <ids>" takes only the maps
// named, split by semicolons. "-okSoak <minutes>" plays the largest map
// for eight with every other seat a computer, with the map revealed so
// everything draws, and writes a row per game minute to
// "-okSoakOut <csv>". "-okSoakMap <name>" picks another map. Both run
// the simulation as fast as frames come. Every line logged starts OKQA.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public sealed class QaRun : MonoBehaviour
    {
        public const string SweepFlag = "-okMapSweep", FromFlag = "-okSweepFrom", OnlyFlag = "-okSweepOnly", SoakFlag = "-okSoak",
            SoakOutFlag = "-okSoakOut", SoakMapFlag = "-okSoakMap", Prefix = "OKQA ";
        public const float SweepSeconds = 20f, DefaultSoakMinutes = 30f;
        // The most ticks GameRoot runs in one frame. With every frame
        // counted as this many ticks of time, the game runs flat out.
        public const int TicksPerFrame = 8;
        // A frame this slow counts as a stall.
        public const float StallMs = 250f;
        public const int SoakSeats = 8;

        public static string Arg(string[] args, string flag)
        {
            int i = SmokeRun.Find(args, flag);
            return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : null;
        }

        public static float? SoakMinutes(string[] args)
        {
            if (SmokeRun.Find(args, SoakFlag) < 0) return null;
            return float.TryParse(Arg(args, SoakFlag), NumberStyles.Float, CultureInfo.InvariantCulture, out float m) && m > 0f ? m : DefaultSoakMinutes;
        }

        // The maps named by -okSweepOnly, in any case, or null for every map.
        public static HashSet<string> SweepOnly(string[] args)
        {
            string only = Arg(args, OnlyFlag);
            if (string.IsNullOrWhiteSpace(only)) return null;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in only.Split(';')) if (id.Trim().Length > 0) set.Add(id.Trim());
            return set;
        }

        public static int SweepFrom(string[] args) =>
            int.TryParse(Arg(args, FromFlag), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : 0;

        // Starts a run when the command line asks for one.
        public static bool Begin(GameRoot root, string[] args)
        {
            string sweep = SmokeRun.Find(args, SweepFlag) >= 0 ? Arg(args, SweepFlag) ?? "map-sweep.csv" : null;
            float? soak = SoakMinutes(args);
            if (sweep == null && soak == null) return false;
            var run = root.gameObject.AddComponent<QaRun>();
            run.root = root;
            run.args = args;
            run.sweepCsv = sweep;
            run.soakMinutes = soak ?? 0f;
            return true;
        }

        // The largest map with starts for this many, by area then by name.
        public static MapInfo LargestFor(IReadOnlyList<MapInfo> maps, int players)
        {
            MapInfo best = null;
            foreach (var m in maps)
            {
                if (MapCatalog.PlayersOf(m) < players) continue;
                if (best == null) { best = m; continue; }
                float a = MapCatalog.AreaOf(m), b = MapCatalog.AreaOf(best);
                if (a > b || a == b && string.CompareOrdinal(m.Id, best.Id) < 0) best = m;
            }
            return best;
        }

        // You in seat 0, since the engine plays seat 0 as the local player,
        // and a computer at Hard in every other seat, each its own team but
        // the first, which guards your idle monarch. The engine stops the
        // battle once your side is out.
        public static void SeatAll(SkirmishSetup s, MapInfo map, int seats)
        {
            s.MapId = map.Id;
            s.Seed = 12345;
            s.Seats.Clear();
            for (int i = 0; i < seats; i++)
                s.Seats.Add(new SeatSetup
                {
                    Kind = i == 0 ? SeatKind.Human : SeatKind.Computer, Side = "", Colour = i, Team = i == 1 ? 0 : i,
                    Difficulty = AiDifficulty.Hard, Start = -1
                });
            s.MapRevealed = true;
            s.LineOfSight = false;
        }

        // A field for a CSV row: commas and quotes kept apart.
        public static string Field(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        // Warnings, errors and exceptions logged, with the first of the worst.
        public sealed class Tally
        {
            public int Warnings, Errors, Exceptions;
            public string First => firstError ?? firstWarning;
            public readonly Dictionary<string, int> Lines = new Dictionary<string, int>();
            readonly object gate = new object();
            string firstError, firstWarning;

            public void Reset()
            {
                lock (gate) { Warnings = Errors = Exceptions = 0; firstError = firstWarning = null; }
            }

            public void Hear(string message, string stack, LogType type)
            {
                if (type == LogType.Log || message == null || message.StartsWith(Prefix) || message.StartsWith(SmokeRun.Prefix)) return;
                lock (gate)
                {
                    if (type == LogType.Warning) Warnings++;
                    else if (type == LogType.Exception) Exceptions++;
                    else Errors++;
                    string line = type + ": " + message.Split('\n')[0];
                    if (line.Length > 300) line = line.Substring(0, 300);
                    if (type == LogType.Warning) firstWarning = firstWarning ?? line;
                    else firstError = firstError ?? line;
                    Lines[line] = Lines.TryGetValue(line, out int n) ? n + 1 : 1;
                }
            }
        }

        GameRoot root;
        string[] args;
        string sweepCsv;
        float soakMinutes;
        readonly Tally tally = new Tally();
        int failures;

        static void Say(string line) => Debug.Log(Prefix + line);

        void OnEnable() => Application.logMessageReceivedThreaded += tally.Hear;
        void OnDisable() => Application.logMessageReceivedThreaded -= tally.Hear;

        IEnumerator Start()
        {
            var b = root.Backend;
            Say($"start {BuildStamp.Version ?? "development build"}, batch {Application.isBatchMode}, graphics {SystemInfo.graphicsDeviceType}, {b.Maps.Count} maps");
            if (b is MockBackend)
            {
                Finish(2, "the engine did not start, so the stand-in world runs. " + (root.BackendProblem ?? ""));
                yield break;
            }
            if (sweepCsv != null) yield return Sweep(sweepCsv);
            else yield return Soak(soakMinutes, Arg(args, SoakOutFlag) ?? "soak.csv");
        }

        void Finish(int code, string why)
        {
            Fast(false);
            Say($"RESULT {(code == 0 ? "PASS" : "FAIL")}: {why}");
            var worst = new List<KeyValuePair<string, int>>(tally.Lines);
            worst.Sort((x, y) => y.Value.CompareTo(x.Value));
            for (int i = 0; i < worst.Count && i < 40; i++) Say($"logged {worst[i].Value} times: {worst[i].Key}");
            enabled = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(code);
#endif
        }

        // Every frame counts as TicksPerFrame ticks at the fast speed, and
        // frames come as fast as they can.
        void Fast(bool on)
        {
            if (root == null || root.Options == null) return;
            int tps = Mathf.Max(1, root.Backend.TicksPerSecond);
            if (on) root.Options.GameSpeed = 2;
            Time.captureDeltaTime = on ? (TicksPerFrame + 0.02f) / (2f * tps) : 0f;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = on ? -1 : 60;
        }

        IEnumerator Leave()
        {
            Fast(false);
            if (root.Flow.State == FlowState.Playing) root.Flow.Fire(FlowEvent.Pause);
            if (GameFlow.InGame(root.Flow.State)) root.Flow.Fire(FlowEvent.ToMenu);
            yield return null;
        }

        static readonly UnitState[] unitBuf = new UnitState[8192];

        static int Units(IGameBackend b)
        {
            try { return b.ReadUnits(unitBuf); }
            catch (Exception) { return -1; }
        }

        static int Alive(IGameBackend b)
        {
            int n = 0;
            foreach (var p in b.Players) if (p.Alive) n++;
            return n;
        }

        static double Mb(long bytes) => bytes / 1048576.0;

        // The process's private bytes, the engine's own allocations with
        // Unity's. Unity's Mono reports none through Process, so Windows is asked.
        static long PrivateBytes()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                var c = new MemoryCounters { cb = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryCounters>() };
                if (GetProcessMemoryInfo(GetCurrentProcess(), ref c, c.cb)) return (long)c.PrivateUsage.ToUInt64();
            }
            catch (Exception) { }
#endif
            return 0;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct MemoryCounters
        {
            public uint cb, PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
                QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")]
        static extern bool GetProcessMemoryInfo(IntPtr process, ref MemoryCounters counters, uint size);
#endif

        static void Append(string path, string line)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(path, line + "\n", new UTF8Encoding(false));
        }

        static string F(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

        // ---- Every map ----

        public const string SweepHeader = "index,map,name,width,height,starts,result,load_s,load_worst_ms,ticks,real_s,fps,units,units_no_model,features,feature_cards,features_empty,monarch_drawn,state,warnings,errors,exceptions,managed_mb,system_mb,private_mb,card_names,first_problem";

        IEnumerator Sweep(string csv)
        {
            var b = root.Backend;
            var maps = new List<MapInfo>(b.Maps);
            int from = SweepFrom(args);
            var only = SweepOnly(args);
            int swept = 0;
            if (from == 0 || !File.Exists(csv))
            {
                if (File.Exists(csv)) File.Delete(csv);
                Append(csv, SweepHeader);
            }
            string shots = Arg(args, SmokeRun.ShotsFlag);
            bool graphics = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            using (var used = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory"))
            {
                for (int i = from; i < maps.Count; i++)
                {
                    var map = maps[i];
                    if (only != null && !only.Contains(map.Id)) continue;
                    Say($"map {i} of {maps.Count}: {map.Id}");
                    tally.Reset();
                    if (root.Flow.State == FlowState.MainMenu) root.Flow.Fire(FlowEvent.OpenSkirmish);
                    SmokeRun.Seat(root.Setup, map);
                    float began = Time.realtimeSinceStartup;
                    root.Flow.Fire(FlowEvent.Start);
                    while (root.Flow.State == FlowState.Loading && Time.realtimeSinceStartup - began < SmokeRun.LoadLimit) yield return null;
                    float load = Time.realtimeSinceStartup - began;
                    // The frame after loading ends closes the slowest-frame count.
                    yield return null;
                    double worst = root.LoadWorstFrameMs;
                    string result = "PASS", state = root.Flow.State.ToString(), drawn = "";
                    int units = 0, noModel = 0, features = 0, cards = 0, empty = 0, frames = 0;
                    var cardNames = new List<string>();
                    uint ticks = 0;
                    float real = 0f;
                    if (root.Flow.State == FlowState.Loading)
                    {
                        Append(csv, Row(i, map, "FAIL load stuck", load, worst, 0, 0, 0, 0, 0, 0, 0, 0, "", state, used, ""));
                        Finish(1, $"{map.Id} was still loading after {SmokeRun.LoadLimit:0} s, so the sweep stops");
                        yield break;
                    }
                    if (root.Flow.State != FlowState.Playing)
                        result = "FAIL load: " + (root.LastError ?? "the battle did not start");
                    else
                    {
                        if (graphics && shots != null)
                        {
                            float settle = Time.realtimeSinceStartup + 2f;
                            while (Time.realtimeSinceStartup < settle && root.Flow.State == FlowState.Playing) yield return null;
                            string why = null;
                            float share = 0f;
                            yield return SmokeRun.Picture(root, shots, $"{i:000}-{Slug(map)}", false, (w, s) => { why = w; share = s; });
                            drawn = share.ToString("0.00", CultureInfo.InvariantCulture);
                            if (why != null) result = "FAIL picture: " + why;
                        }
                        int tps = Mathf.Max(1, b.TicksPerSecond);
                        uint first = b.Tick, want = (uint)(SweepSeconds * tps);
                        float from0 = Time.realtimeSinceStartup;
                        Fast(true);
                        while (GameFlow.InGame(root.Flow.State) && b.Tick - first < want && Time.realtimeSinceStartup - from0 < 120f)
                        {
                            frames++;
                            yield return null;
                        }
                        Fast(false);
                        ticks = b.Tick - first;
                        real = Time.realtimeSinceStartup - from0;
                        state = root.Flow.State.ToString();
                        units = Units(b);
                        if (root.World != null)
                        {
                            noModel = root.World.Entities.UnitsWithoutModel;
                            root.World.Entities.FeatureCoverage(out features, out cards, out empty, cardNames);
                        }
                        if (ticks < want && result == "PASS") result = $"FAIL stalled: {ticks} of {want} ticks";
                        yield return Leave();
                    }
                    if (tally.Exceptions > 0 && result == "PASS") result = "FAIL exception";
                    if (result != "PASS") failures++;
                    Append(csv, Row(i, map, result, load, worst, ticks, real, frames, units, noModel, features, cards, empty, drawn, state, used, string.Join(" ", cardNames)));
                    swept++;
                    Say($"map {i} {map.Id}: {result}, loaded in {load:0.0} s with no frame over {worst:0} ms, {ticks} ticks in {real:0.0} s, {units} units, {tally.Warnings} warnings, {tally.Errors} errors, {tally.Exceptions} exceptions");
                    if (root.Flow.State != FlowState.Skirmish && root.Flow.State != FlowState.MainMenu)
                    {
                        Finish(1, $"the sweep could not get back to the menu from {root.Flow.State} after {map.Id}");
                        yield break;
                    }
                }
                Finish(failures == 0 ? 0 : 1, $"{swept} maps, {failures} failed");
            }
        }

        string Row(int i, MapInfo map, string result, float load, double worst, uint ticks, float real, int frames, int units, int noModel,
            int features, int cards, int empty, string drawn, string state, ProfilerRecorder used, string cardNames)
        {
            long managed = GC.GetTotalMemory(false);
            return string.Join(",", new[]
            {
                i.ToString(CultureInfo.InvariantCulture), Field(map.Id), Field(map.Name),
                F(map.Size.x), F(map.Size.y), MapCatalog.PlayersOf(map).ToString(CultureInfo.InvariantCulture), Field(result),
                F(load), worst.ToString("0", CultureInfo.InvariantCulture), ticks.ToString(CultureInfo.InvariantCulture), F(real), F(frames / Mathf.Max(0.01f, real)),
                units.ToString(CultureInfo.InvariantCulture), noModel.ToString(CultureInfo.InvariantCulture),
                features.ToString(CultureInfo.InvariantCulture), cards.ToString(CultureInfo.InvariantCulture), empty.ToString(CultureInfo.InvariantCulture),
                drawn, state, tally.Warnings.ToString(CultureInfo.InvariantCulture), tally.Errors.ToString(CultureInfo.InvariantCulture),
                tally.Exceptions.ToString(CultureInfo.InvariantCulture), F(Mb(managed)), F(Mb(used.Valid ? used.LastValue : 0)), F(Mb(PrivateBytes())),
                Field(cardNames), Field(tally.First)
            });
        }

        static string Slug(MapInfo map) => (map.Name ?? map.Id).Replace("'", "").Replace(' ', '-').ToLowerInvariant();

        // ---- The soak ----

        public const string SoakHeader = "minute,tick,real_s,frames,fps,frame_avg_ms,frame_p95_ms,frame_max_ms,stalls,sim_avg_ms,sim_max_ms,render_avg_ms,ticks_per_s,units,alive,state,managed_mb,gc_reserved_mb,total_used_mb,gfx_used_mb,system_mb,private_mb,warnings,errors,exceptions";

        IEnumerator Soak(float minutes, string csv)
        {
            var b = root.Backend;
            string named = Arg(args, SoakMapFlag);
            MapInfo map = null;
            if (named != null) map = SmokeRun.PickMap(b.Maps, named);
            map = map ?? LargestFor(b.Maps, SoakSeats);
            if (map == null)
            {
                Finish(3, $"no map for {SoakSeats}");
                yield break;
            }
            if (File.Exists(csv)) File.Delete(csv);
            Append(csv, SoakHeader);
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            SeatAll(root.Setup, map, Mathf.Min(SoakSeats, MapCatalog.PlayersOf(map)));
            Say($"soak on {map.Id} ({map.Size.x:0}x{map.Size.y:0}, {MapCatalog.PlayersOf(map)} starts) with {root.Setup.Seats.Count} seats for {minutes:0.#} game minutes");
            float began = Time.realtimeSinceStartup;
            root.Flow.Fire(FlowEvent.Start);
            while (root.Flow.State == FlowState.Loading && Time.realtimeSinceStartup - began < SmokeRun.LoadLimit) yield return null;
            if (root.Flow.State != FlowState.Playing)
            {
                Finish(4, $"the battle did not start: {root.Flow.State}, {root.LastError}");
                yield break;
            }
            Say($"started after {Time.realtimeSinceStartup - began:0.0} s, {Units(b)} units, {b.Players.Count} players, {b.TicksPerSecond} ticks a second");
            tally.Reset();

            var total = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
            var gfx = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
            var system = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            var gcReserved = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Reserved Memory");
            try
            {
                int tps = Mathf.Max(1, b.TicksPerSecond);
                uint start = b.Tick, perMinute = (uint)(60 * tps), end = start + (uint)(minutes * 60f * tps);
                uint next = start + perMinute;
                int minute = 0, stalls = 0;
                var frameMs = new List<float>();
                double simSum = 0, simMax = 0, renderSum = 0;
                double last = Time.realtimeSinceStartupAsDouble, minuteFrom = last, soakFrom = last;
                uint minuteTick = start;
                string ended = null;
                int still = 0;
                Fast(true);
                while (b.Tick < end)
                {
                    uint before = b.Tick;
                    yield return null;
                    double now = Time.realtimeSinceStartupAsDouble;
                    float ms = (float)((now - last) * 1000.0);
                    last = now;
                    frameMs.Add(ms);
                    if (ms > StallMs) stalls++;
                    double sim = root.SimMs;
                    var flow = root.Flow.State;
                    if (flow == FlowState.Victory || flow == FlowState.Defeat)
                    {
                        // The battle is decided for you, and the rest play on.
                        if (ended == null)
                        {
                            ended = flow.ToString();
                            Say($"{flow} at tick {b.Tick}, the soak runs the battle on");
                        }
                        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                        b.Advance(TicksPerFrame);
                        sim = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                        // The engine stands the battle still once it is decided for you.
                        still = b.Tick == before ? still + 1 : 0;
                        if (still >= 120)
                        {
                            Say($"the engine stopped the battle at tick {b.Tick}, {(b.Tick - start) / (60f * tps):0.0} game minutes in");
                            break;
                        }
                    }
                    else if (flow != FlowState.Playing)
                    {
                        Finish(5, $"the battle left play for {flow} at tick {b.Tick}");
                        yield break;
                    }
                    simSum += sim;
                    simMax = Math.Max(simMax, sim);
                    renderSum += root.RenderMs;
                    if (b.Tick < next && b.Tick < end) continue;

                    minute++;
                    next += perMinute;
                    frameMs.Sort();
                    int n = frameMs.Count;
                    float avg = 0f;
                    foreach (var f in frameMs) avg += f;
                    avg /= Mathf.Max(1, n);
                    float p95 = n > 0 ? frameMs[Mathf.Min(n - 1, (int)(n * 0.95f))] : 0f, max = n > 0 ? frameMs[n - 1] : 0f;
                    double real = now - minuteFrom;
                    int units = Units(b);
                    string row = string.Join(",", new[]
                    {
                        minute.ToString(CultureInfo.InvariantCulture), b.Tick.ToString(CultureInfo.InvariantCulture), F(now - soakFrom),
                        n.ToString(CultureInfo.InvariantCulture), F(n / Math.Max(0.01, real)), F(avg), F(p95), F(max),
                        stalls.ToString(CultureInfo.InvariantCulture), F(simSum / Math.Max(1, n)), F(simMax), F(renderSum / Math.Max(1, n)),
                        F((b.Tick - minuteTick) / Math.Max(0.01, real)), units.ToString(CultureInfo.InvariantCulture),
                        Alive(b).ToString(CultureInfo.InvariantCulture), ended ?? flow.ToString(),
                        F(Mb(GC.GetTotalMemory(false))), F(Mb(gcReserved.LastValue)), F(Mb(total.LastValue)), F(Mb(gfx.LastValue)),
                        F(Mb(system.LastValue)), F(Mb(PrivateBytes())),
                        tally.Warnings.ToString(CultureInfo.InvariantCulture), tally.Errors.ToString(CultureInfo.InvariantCulture),
                        tally.Exceptions.ToString(CultureInfo.InvariantCulture)
                    });
                    Append(csv, row);
                    Say($"minute {minute}: {row}");
                    frameMs.Clear();
                    simSum = simMax = renderSum = 0;
                    stalls = 0;
                    minuteFrom = now;
                    minuteTick = b.Tick;
                }
                Fast(false);
                Finish(tally.Exceptions == 0 ? 0 : 1, $"{(b.Tick - start) / (60f * tps):0.0} game minutes ({minute} rows) in {Time.realtimeSinceStartupAsDouble - soakFrom:0} s, {tally.Warnings} warnings, {tally.Errors} errors, {tally.Exceptions} exceptions");
            }
            finally
            {
                total.Dispose();
                gfx.Dispose();
                system.Dispose();
                gcReserved.Dispose();
            }
        }
    }
}
