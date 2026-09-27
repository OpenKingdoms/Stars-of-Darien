// EnginePerfPlayTests.cs - a perf probe on the real engine, skipped
// without the game files: four computer kingdoms and the player on the
// biggest four-player map, played at game speed for three minutes. It logs
// the frame time's median, 95th percentile and worst, and what the frame's
// parts took, for finding lag rather than failing on it.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class EnginePerfPlayTests
    {
        public static float Seconds =>
            float.TryParse(System.Environment.GetEnvironmentVariable("OKU_PERF_SECONDS"), out var s) ? s : 180f;

        [UnityTest, Timeout(900000)]
        public IEnumerator FourComputersOnABigMap()
        {
            if (System.Environment.GetEnvironmentVariable("OKU_PERF") != "1") Assert.Ignore("set OKU_PERF=1 to run the engine perf probe");
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            var view = GameObject.Find("OpenKingdoms");
            if (view != null) Object.Destroy(view);
            yield return null;
            yield return null;
            var root = GameRoot.Boot();
            try
            {
                yield return null;
                var b = root.Backend;
                // Normal speed, whatever the player last chose.
                root.Options.GameSpeed = 1;
                var map = b.Maps.Where(m => m.MaxPlayers >= 5).OrderByDescending(m => m.Size.x * m.Size.y).FirstOrDefault()
                       ?? b.Maps.OrderByDescending(m => m.Size.x * m.Size.y).First();
                root.Flow.Fire(FlowEvent.OpenSkirmish);
                var setup = GameRoot.DefaultSetup(b);
                setup.MapId = map.Id;
                for (int i = 1; i < setup.Seats.Count; i++) { setup.Seats[i].Kind = SeatKind.Computer; setup.Seats[i].Team = i; }
                while (setup.Seats.Count < 5) setup.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "", Colour = setup.Seats.Count, Team = setup.Seats.Count });
                int seats = Mathf.Min(setup.Seats.Count, Mathf.Max(2, map.MaxPlayers));
                setup.Seats = setup.Seats.Take(seats).ToList();
                root.Setup = setup;
                root.Flow.Fire(FlowEvent.Start);
                float deadline = Time.realtimeSinceStartup + 300f;
                while (root.Flow.State == FlowState.Loading && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.AreEqual(FlowState.Playing, root.Flow.State, "the battle loaded: " + root.LastError);

                string[] names = { "Oku.Sim", "Oku.Render", "Oku.Fog", "Oku.Entities", "Oku.Effects", "Oku.Input", "Oku.Screens", "Oku.Pointer" };
                var recs = names.Select(n => { var r = Recorder.Get(n); r.enabled = true; return r; }).ToArray();
                var sums = new double[names.Length];
                var worst = new double[names.Length];
                var frames = new List<float>();
                float end = Time.realtimeSinceStartup + Seconds;
                int units = 0;
                while (Time.realtimeSinceStartup < end && root.Flow.State == FlowState.Playing)
                {
                    yield return null;
                    frames.Add(Time.unscaledDeltaTime * 1000f);
                    for (int k = 0; k < names.Length; k++)
                    {
                        double ms = recs[k].isValid ? recs[k].elapsedNanoseconds / 1e6 : 0;
                        sums[k] += ms;
                        worst[k] = System.Math.Max(worst[k], ms);
                    }
                    units = root.World.Entities.UnitCount;
                }
                frames.Sort();
                float P(float q) => frames[Mathf.Clamp(Mathf.RoundToInt(q * (frames.Count - 1)), 0, frames.Count - 1)];
                var parts = string.Join(", ", names.Select((n, k) => $"{n} {sums[k] / frames.Count:0.00} avg {worst[k]:0.0} worst"));
                Debug.Log($"EnginePerf: {map.Name}, {seats} kingdoms, {frames.Count} frames, {units} units in the player's sight at the end, tick {b.Tick} at {b.TicksPerSecond} a second; " +
                          $"frame p50 {P(0.5f):0.0} ms, p95 {P(0.95f):0.0} ms, max {frames[frames.Count - 1]:0.0} ms; {parts}");
            }
            finally { Object.Destroy(root.gameObject); }
        }
    }
}
