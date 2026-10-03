// ResultCaptures.cs - pictures of the end of a battle on the real engine,
// for looking at without a window: a duel won, a battle of eight lost and
// a duel lost, every page of the result at 1080p and 1440p. Runs only when
// OKU_CAPTURE_DIR names a folder and the engine and the game files are
// there.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class ResultCaptures
    {
        static readonly Vector2Int[] Sizes = { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440) };
        GameRoot root;

        [TearDown]
        public void CleanUp()
        {
            BattleHud.SizeOverride = null;
            MenuScreens.SizeOverride = null;
            FadeIn.Off = false;
            UiKit.Motion.Off = false;
            if (root != null) Object.Destroy(root.gameObject);
        }

        [UnityTest, Timeout(3600000)]
        public IEnumerator CaptureTheEndOfABattle()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_CAPTURE_DIR to capture the end of a battle");
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            Directory.CreateDirectory(dir);
            FadeIn.Off = true;

            yield return Battle("king of the hill", 2, 300, true);
            yield return Pages(dir, "2p-victory", true);
            yield return Battle(EightMap(), 8, 300, false);
            yield return Pages(dir, "8p-defeat", false);
            yield return Battle("king of the hill", 2, 150, false, strike: false);
            yield return Pages(dir, "2p-defeat", false, onlyTallies: true);
        }

        string EightMap() => root != null
            ? root.Backend.Maps.Where(m => MapCatalog.PlayersOf(m) >= 8).OrderBy(m => m.Size.x * m.Size.y).First().Id
            : null;

        int DefNamed(string name)
        {
            var defs = root.Backend.UnitDefs;
            for (int i = 0; i < defs.Count; i++) if (string.Equals(defs[i].Name, name, System.StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        static bool IsMonarch(UnitDef d)
        {
            var words = (d.Category ?? "").Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            return words.Length >= 2 && string.Equals(words[1], "Monarch", System.StringComparison.OrdinalIgnoreCase);
        }

        // A battle of `kingdoms` played for `seconds`, with a wing of sky
        // knights sent at a computer's monarch, then won or lost: won when
        // they take the last monarch, lost when the player hands the army
        // over.
        IEnumerator Battle(string map, int kingdoms, int seconds, bool win, bool strike = true)
        {
            if (root != null) Object.Destroy(root.gameObject);
            var view = GameObject.Find("OpenKingdoms");
            if (view != null) Object.Destroy(view);
            UiKit.Motion.Off = false;
            MenuScreens.SizeOverride = Sizes[0];
            BattleHud.SizeOverride = Sizes[0];
            yield return null;
            root = GameRoot.Boot();
            yield return null;
            var b = root.Backend;
            Assert.IsFalse(b is MockBackend, "the engine runs: " + root.BackendProblem);
            if (map == null) map = EightMap();
            Assert.IsTrue(root.Flow.Fire(FlowEvent.OpenSkirmish));
            yield return null;
            var s = root.Setup;
            s.MapId = map;
            s.MapRevealed = true;
            s.LineOfSight = false;
            s.RandomStarts = false;
            s.MonarchExpendable = false;
            string[] sides = { "ARAMON", "TAROS", "VERUNA", "ZHON" };
            for (int i = 0; i < s.Seats.Count; i++)
            {
                s.Seats[i].Kind = i == 0 ? SeatKind.Human : i < kingdoms ? SeatKind.Computer : SeatKind.Closed;
                s.Seats[i].Team = SeatTeam.Alone;
                s.Seats[i].Side = sides[i % sides.Length];
                if (i > 0) s.Seats[i].Difficulty = kingdoms == 2 ? AiDifficulty.Easy : AiDifficulty.Normal;
            }
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 300f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);

            // The kingdoms build and the computers fight among themselves.
            for (int t = 0; t < seconds && root.Flow.State == FlowState.Playing; t += 10)
            {
                b.Advance(10 * b.TicksPerSecond);
                yield return null;
            }
            // A computer may have ended it already.
            if (root.Flow.State != FlowState.Playing) strike = false;

            // Flyers with no mana to wait for, over walls and cliffs alike.
            int knight = DefNamed("TARKNIGH");
            Assert.GreaterOrEqual(knight, 0);
            var flight = new List<int>();
            for (int i = 0; i < (strike ? 12 : 0); i++)
            {
                int h = OkEngine.okx_place_unit(knight, b.LocalPlayer);
                if (h >= 0) flight.Add(h);
            }
            // The host reads only what the player can see, and the troop
            // has to find its mark across the map.
            b.SeeAll(true);
            var units = new UnitState[8192];
            for (int t = 0; t < 600 && flight.Count > 0 && root.Flow.State == FlowState.Playing; t++)
            {
                if (t % 3 == 0)
                {
                    int n = b.ReadUnits(units), prey = -1;
                    for (int i = 0; i < n && prey < 0; i++)
                        if (units[i].Player != b.LocalPlayer && IsMonarch(b.UnitDefs[units[i].Def]) && (units[i].Flags & UnitFlags.Dying) == 0)
                            prey = units[i].Handle;
                    if (prey < 0) break;
                    int sent = 0;
                    foreach (int h in flight)
                        if (b.Command(new GameCommand { Kind = CommandKind.Attack, Unit = h, TargetUnit = prey, BuildDef = -1 })) sent++;
                    if (t % 30 == 0)
                    {
                        Vector3 at = Vector3.zero, mark = Vector3.zero;
                        int alive = 0;
                        for (int i = 0; i < n; i++)
                        {
                            if (units[i].Handle == prey) mark = units[i].Position;
                            if (flight.Contains(units[i].Handle)) { at += units[i].Position; alive++; }
                        }
                        Debug.Log($"Strike t={t}: prey {prey} at {mark}, {alive} flyers near {(alive > 0 ? at / alive : at)}, {sent} orders taken");
                    }
                }
                b.Advance(b.TicksPerSecond);
                if (!win && t % 3 == 2 && b.ReadBattle().Kingdoms.Count(k => k.Standing) < kingdoms) break;
                yield return null;
            }
            if (!win && root.Flow.State == FlowState.Playing)
            {
                // A little more, then the army goes to a computer.
                b.Advance(20 * b.TicksPerSecond);
                int other = b.Players.First(p => !p.IsLocal).Index;
                int n = b.ReadUnits(units);
                for (int i = 0; i < n; i++)
                    if (units[i].Player == b.LocalPlayer)
                        b.Command(new GameCommand { Kind = (CommandKind)24, Unit = units[i].Handle, TargetUnit = -1, BuildDef = -1, Arg = other });
            }
            deadline = Time.realtimeSinceStartup + 120f;
            while (root.Flow.State == FlowState.Playing && Time.realtimeSinceStartup < deadline)
            {
                b.Advance(b.TicksPerSecond);
                yield return null;
            }
            b.SeeAll(false);
            Assert.AreEqual(win ? FlowState.Victory : FlowState.Defeat, root.Flow.State, "the battle was decided");
            Debug.Log($"Battle on {map} for {kingdoms} decided at tick {b.Tick}");
        }

        IEnumerator Pages(string dir, string name, bool banner, bool onlyTallies = false)
        {
            var cam = Camera.main;
            if (cam == null) { cam = new GameObject("Main Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; }
            foreach (var size in Sizes)
            {
                MenuScreens.SizeOverride = size;
                BattleHud.SizeOverride = size;
                string at = name + "-" + size.y + "p";
                for (int i = 0; i < 4; i++) yield return null;
                if (banner)
                {
                    // The word over the field, before the page comes.
                    UiKit.Motion.Off = false;
                    FadeIn.Off = false;
                    root.Screens.Results.Opening.Restart(MenuScreens.ResultHold);
                    root.Screens.Results.ShowBanner();
                    for (int i = 0; i < 3; i++) yield return null;
                    yield return HudShots.Shoot(cam, size.x, size.y, Path.Combine(dir, at + "-0-banner.png"));
                }
                UiKit.Motion.Off = true;
                FadeIn.Off = true;
                for (int i = 0; i < 3; i++) yield return null;
                var results = root.Screens.Results;
                results.ShowTab(0);
                yield return null;
                yield return HudShots.Shoot(cam, size.x, size.y, Path.Combine(dir, at + "-1-tallies.png"));
                if (onlyTallies) continue;
                results.ShowTab(1);
                results.ShowChart(1);
                yield return null;
                yield return HudShots.Shoot(cam, size.x, size.y, Path.Combine(dir, at + "-2-graphs-worth.png"));
                results.ShowChart(2);
                yield return null;
                yield return HudShots.Shoot(cam, size.x, size.y, Path.Combine(dir, at + "-2b-graphs-income.png"));
                results.ShowChart(0);
                results.ShowTab(2);
                yield return null;
                yield return HudShots.Shoot(cam, size.x, size.y, Path.Combine(dir, at + "-3-kingdoms.png"));
                results.ShowTab(3);
                yield return null;
                yield return HudShots.Shoot(cam, size.x, size.y, Path.Combine(dir, at + "-4-annals.png"));
                results.ShowTab(0);
            }
        }
    }
}
