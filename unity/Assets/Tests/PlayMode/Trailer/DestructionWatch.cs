// DestructionWatch.cs - what the destruction scenes check while they run on
// the real engine: every feature event the renderer reads, whether it found
// the feature the event names after indices shifted, whether each kind
// that broke was split, whether what is drawn after a swap is what the
// engine holds there (stages with no model of their own included), and
// whether a breaking feature's spot on screen blinks for a single frame.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests.Trailer
{
    public sealed partial class TrailerDirector
    {
        // Checks failed over the run, written to checks.log.
        public int Failures { get; private set; }

        string watchScene;
        EntityRenderer watched;
        readonly Dictionary<FeatureEventKind, int> eventCounts = new Dictionary<FeatureEventKind, int>();
        int lookupsAt, missesAt, newsSeen, idGaps, lastNewsId;
        readonly Dictionary<string, string> splitSeen = new Dictionary<string, string>();
        readonly HashSet<string> noModel = new HashSet<string>();
        int fallbackSwaps, fallbackDrawn;
        readonly List<string> problems = new List<string>();

        struct SwapCheck { public Vector3 At; public int From, To; public uint Due; public bool Burnt; }
        readonly List<SwapCheck> swapChecks = new List<SwapCheck>();
        int swapsChecked, swapsMatched;

        struct Site { public Vector3 At; public float Height; public int Frames; public float L0, L1; public int Seen; }
        readonly List<Site> sites = new List<Site>();
        int blips, sitesWatched;
        float frameL0 = -1f, frameL1 = -1f;

        // Starts watching the battle just begun, for the scene named.
        public void StartWatch(string scene)
        {
            EndWatch();
            watchScene = scene;
            watched = Root.World.Entities;
            watched.FeatureNews += OnNews;
            lookupsAt = watched.EntryLookups;
            missesAt = watched.EntryMisses;
            eventCounts.Clear();
            splitSeen.Clear();
            swapChecks.Clear();
            sites.Clear();
            problems.Clear();
            newsSeen = idGaps = lastNewsId = 0;
            fallbackSwaps = fallbackDrawn = swapsChecked = swapsMatched = blips = sitesWatched = 0;
            noModel.Clear();
            foreach (var n in watched.StagesWithoutModels()) noModel.Add(n);
            AfterFrame = Pixels;
            ScarMap.Current?.ResetWorst();
        }

        // Ends the watch and writes what it found.
        public void EndWatch()
        {
            if (watched == null) return;
            CheckSwaps(true);
            watched.FeatureNews -= OnNews;
            AfterFrame = null;
            int lookups = watched.EntryLookups - lookupsAt, misses = watched.EntryMisses - missesAt;
            var lines = new List<string>
            {
                $"== {watchScene} on {Map?.Name}",
                "events: " + string.Join(", ", eventCounts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}")) + $", {idGaps} gaps in the ids",
                $"lookups: {lookups}, {misses} found no feature",
                $"swaps checked: {swapsChecked}, {swapsMatched} drawn as the engine holds them",
                $"stages with no model reached: {fallbackSwaps}, {fallbackDrawn} drawn from an earlier stage",
                $"breaking spots watched on screen: {sitesWatched}, {blips} single-frame blinks",
                "splits: " + string.Join("; ", splitSeen.OrderBy(kv => kv.Key).Select(kv => kv.Key + " " + kv.Value)),
            };
            var scars = ScarMap.Current;
            if (scars != null) lines.Add($"scars: {scars.Stamped} stamped, {scars.Merged} merged, {scars.Dropped} gave way, worst update {scars.WorstMs:0.00} ms ({scars.WorstParts})");
            // A miss is a feature placed and gone within one frame at most.
            if (lookups > 20 && misses * 20 > lookups) problems.Add($"{misses} of {lookups} event lookups found no feature");
            if (swapsChecked > 0 && swapsMatched < swapsChecked) problems.Add($"{swapsChecked - swapsMatched} of {swapsChecked} swaps drew something other than the engine holds");
            if (fallbackSwaps > 0 && fallbackDrawn < fallbackSwaps) problems.Add($"{fallbackSwaps - fallbackDrawn} of {fallbackSwaps} stages with no model drew nothing");
            if (blips > 0) problems.Add($"{blips} single-frame blinks at breaking spots");
            foreach (var p in problems) lines.Add("PROBLEM " + p);
            Failures += problems.Count;
            File.AppendAllLines(Path.Combine(OutDir, "checks.log"), lines);
            foreach (var l in lines) Note(l);
            watched = null;
        }

        void OnNews(FeatureEvent e)
        {
            newsSeen++;
            if (lastNewsId != 0 && e.Id != lastNewsId + 1) idGaps++;
            lastNewsId = e.Id;
            eventCounts[e.Kind] = eventCounts.TryGetValue(e.Kind, out int c) ? c + 1 : 1;
            var defs = B.FeatureDefs;
            var d = e.Def >= 0 && e.Def < defs.Count ? defs[e.Def] : null;
            if (e.Kind == FeatureEventKind.Dying && d != null && !splitSeen.ContainsKey(d.Name))
            {
                var set = watched.Fractures.Get(e.Def);
                var kind = Fracture.KindOf(d);
                splitSeen[d.Name] = set == null ? $"{kind} not split yet" : set.Whole ? $"{kind} whole" : $"{kind} {set.Count} chunks";
            }
            if ((e.Kind == FeatureEventKind.Dead || e.Kind == FeatureEventKind.Burnt) && e.NewDef >= 0)
            {
                uint due = e.Tick + (uint)(B.TicksPerSecond * 3);
                swapChecks.Add(new SwapCheck { At = e.Position, From = e.Def, To = e.NewDef, Due = due, Burnt = e.Kind == FeatureEventKind.Burnt });
            }
            if (e.Kind == FeatureEventKind.Dying && sites.Count < 24 && InFrame(e.Position))
            {
                sites.Add(new Site { At = e.Position, Height = d != null ? Mathf.Max(0.5f, d.Height * 0.5f) : 1f, L0 = -1f, L1 = -1f });
                sitesWatched++;
            }
        }

        // Called every frame the scenes step: swaps whose time came are checked.
        public void WatchFrame() => CheckSwaps(false);

        void CheckSwaps(bool all)
        {
            if (watched == null || swapChecks.Count == 0) return;
            uint now = B.Tick;
            int n = B.ReadFeatures(featBuf);
            for (int i = swapChecks.Count - 1; i >= 0; i--)
            {
                var s = swapChecks[i];
                if (!all && now < s.Due) continue;
                swapChecks.RemoveAt(i);
                // What the engine holds at that spot now: the stage, a later one, or nothing.
                int held = -1;
                for (int k = 0; k < n; k++)
                    if (Mathf.Abs(featBuf[k].Position.x - s.At.x) < 0.01f && Mathf.Abs(featBuf[k].Position.z - s.At.z) < 0.01f) { held = featBuf[k].Def; break; }
                if (held < 0) continue;
                swapsChecked++;
                int drawn = watched.DrawnDefAt(s.At, 0.01f, out bool fallback, out bool breaking);
                var defs = B.FeatureDefs;
                string name = held < defs.Count ? defs[held].Name : held.ToString();
                bool noModelStage = noModel.Contains(name);
                if (noModelStage) fallbackSwaps++;
                if (drawn == held || breaking)
                {
                    swapsMatched++;
                    if (noModelStage && (fallback || drawn == held)) fallbackDrawn++;
                }
                else if (problems.Count < 40)
                {
                    string drawnName = drawn >= 0 && drawn < defs.Count ? defs[drawn].Name : "nothing";
                    problems.Add($"after {(s.Burnt ? "burning" : "breaking")} {defs[s.From].Name} the engine holds {name} at {s.At} but {drawnName} is drawn");
                }
            }
        }

        bool InFrame(Vector3 p)
        {
            var v = Cam.WorldToViewportPoint(p);
            return v.z > 0f && v.x > 0.05f && v.x < 0.95f && v.y > 0.05f && v.y < 0.95f;
        }

        // A spot's mean brightness on the frame just read, 0 to 255.
        float Brightness(Vector3 at, int half)
        {
            var v = Cam.WorldToViewportPoint(at);
            if (v.z <= 0f) return -1f;
            int cx = Mathf.RoundToInt(v.x * W), cy = Mathf.RoundToInt(v.y * H);
            if (cx < half || cy < half || cx >= W - half || cy >= H - half) return -1f;
            var px = tex.GetRawTextureData<byte>();
            long sum = 0;
            int count = 0;
            for (int y = cy - half; y <= cy + half; y += 2)
                for (int x = cx - half; x <= cx + half; x += 2)
                {
                    int i = (y * W + x) * 3;
                    sum += px[i] * 3 + px[i + 1] * 6 + px[i + 2];
                    count++;
                }
            return sum / (10f * count);
        }

        // A spot that jumps on one frame and comes straight back on the next
        // is a blink: the feature or its chunks gone for a frame, or doubled.
        void Pixels(int frame)
        {
            // A flash that lights the whole view is no blink.
            float whole = Whole();
            bool steady = frameL0 >= 0f && Mathf.Abs(whole - frameL1) < 6f && Mathf.Abs(frameL1 - frameL0) < 6f;
            frameL0 = frameL1;
            frameL1 = whole;
            for (int i = sites.Count - 1; i >= 0; i--)
            {
                var s = sites[i];
                float l = Brightness(s.At + Vector3.up * s.Height, Mathf.Clamp(H / 60, 8, 24));
                s.Frames++;
                if (l >= 0f && s.L0 >= 0f && s.L1 >= 0f)
                {
                    float a = s.L1 - s.L0, b = l - s.L1;
                    if (steady && Mathf.Abs(a) > 30f && Mathf.Abs(b) > 30f && Mathf.Sign(a) != Mathf.Sign(b) && Mathf.Abs(l - s.L0) < 8f)
                    {
                        blips++;
                        if (blips <= 6)
                        {
                            string dir = Path.Combine(OutDir, "blinks");
                            Directory.CreateDirectory(dir);
                            File.WriteAllBytes(Path.Combine(dir, $"{watchScene}-{frame:0000}.png"), tex.EncodeToPNG());
                        }
                    }
                }
                s.L0 = s.L1;
                s.L1 = l;
                sites[i] = s;
                if (s.Frames > Fps * 4) sites.RemoveAt(i);
            }
        }

        // The whole frame's mean brightness, from a coarse grid.
        float Whole()
        {
            var px = tex.GetRawTextureData<byte>();
            long sum = 0;
            int count = 0;
            for (int y = H / 16; y < H; y += H / 8)
                for (int x = W / 32; x < W; x += W / 16)
                {
                    int i = (y * W + x) * 3;
                    sum += px[i] * 3 + px[i + 1] * 6 + px[i + 2];
                    count++;
                }
            return sum / (10f * count);
        }

        // Units standing in craters: drawn in them, with their rings on the dented ground.
        public void CheckCraterUnits(IEnumerable<int> handles)
        {
            int inCraters = 0, sitting = 0;
            var lines = new List<string>();
            foreach (var u in Of(handles))
            {
                float dip = ScarMap.GroundOffset(u.Position.x, u.Position.z);
                if (dip > -0.08f || B.UnitDefs[u.Def].CanFly) continue;
                inCraters++;
                var b = watched.UnitBounds(u.Handle);
                float ground = (B.Terrain != null ? B.Terrain.Sample(u.Position.x, u.Position.z) : u.Position.y) + dip;
                float sink = ScarMap.SinkY(u.Position);
                // The model's foot follows the crater floor the ring is draped on.
                bool ok = Mathf.Abs(sink - dip) < 0.05f && b.size.y > 0f && b.min.y < ground + 0.6f;
                if (ok) sitting++;
                if (lines.Count < 8) lines.Add($"  {B.UnitDefs[u.Def].Name} at {u.Position}: dip {dip:0.00}, drawn lowered {sink:0.00}, foot {b.min.y:0.00} over the floor {ground:0.00}");
            }
            lines.Insert(0, $"units in craters: {inCraters}, {sitting} sitting in them");
            if (inCraters > 0 && sitting < inCraters) problems.Add($"{inCraters - sitting} of {inCraters} units in craters not drawn in them");
            File.AppendAllLines(Path.Combine(OutDir, "checks.log"), lines);
            foreach (var l in lines) Note(l);
        }
    }
}
