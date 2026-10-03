// AlphaFlowPlayTests.cs - what a playtester of the alpha goes through, on
// the real engine where it matters: every control of the skirmish page,
// a battle fought to its end, the result, Look at the field and back to
// the menu, and options that come back after a restart. The engine tests
// are ignored without okengine and the game files.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Tests
{
    public class AlphaFlowPlayTests
    {
        GameRoot root;
        string saves;
        GameOptions players;

        [SetUp]
        public void Before()
        {
            saves = TempSaves.Use();
            // The options live in PlayerPrefs, so the player's own come back after.
            players = GameOptions.Load();
        }

        [TearDown]
        public void After()
        {
            Time.timeScale = 1f;
            UiKit.Motion.Off = false;
            if (root != null) Object.Destroy(root.gameObject);
            players.Save();
            TempSaves.Drop(saves);
        }

        static IEnumerator Until(System.Func<bool> done, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < deadline) yield return null;
        }

        IEnumerator Boot(IGameBackend backend = null)
        {
            if (root != null) Object.Destroy(root.gameObject);
            var view = GameObject.Find("OpenKingdoms");
            if (view != null) Object.Destroy(view);
            yield return null;
            root = GameRoot.Boot(backend);
            yield return null;
        }

        Button ButtonOn(string screen, string words)
        {
            var go = root.Screens.Screen(screen);
            Assert.IsNotNull(go, screen);
            Assert.IsTrue(go.activeInHierarchy, screen + " is up");
            var b = go.GetComponentsInChildren<Button>().FirstOrDefault(x => x.name == words || x.GetComponentsInChildren<Text>().Any(t => t.text == words));
            Assert.IsNotNull(b, $"{screen} has {words}");
            return b;
        }

        List<Clicker> Clickers(string name) =>
            root.Screens.Lobby.Skirmish.Page.Root.GetComponentsInChildren<Clicker>(true).Where(c => c.name == name).ToList();

        [UnityTest, Timeout(1200000)]
        public IEnumerator EveryLobbyControlThenABattleToItsEndTheFieldAndTheMenu()
        {
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            yield return Boot();
            Assert.IsFalse(root.Backend is MockBackend, "the engine runs: " + root.BackendProblem);
            Assert.IsTrue(root.Flow.Fire(FlowEvent.OpenSkirmish));
            yield return null;
            var page = root.Screens.Lobby.Skirmish;
            var s = root.Setup;
            Assert.AreEqual(LobbyScreens.SeatRows, s.Seats.Count, "the page shows every seat");

            // Each other seat steps through closed and the four computers and back.
            var names = Clickers("Name");
            Assert.AreEqual(LobbyScreens.SeatRows, names.Count);
            for (int i = 1; i < names.Count; i++)
            {
                string Seat() => s.Seats[i].Kind == SeatKind.Closed ? "closed" : s.Seats[i].Difficulty.ToString();
                string was = Seat();
                var seen = new HashSet<string>();
                for (int k = 0; k < 5; k++)
                {
                    names[i].Step(1);
                    seen.Add(Seat());
                }
                Assert.AreEqual(5, seen.Count, $"seat {i} offers closed and four computers");
                Assert.AreEqual(was, Seat(), $"seat {i} comes round again");
            }
            Assert.IsNull(names[0].Step, "your own seat stays yours");

            // Kingdom, colour and team go round and back, both ways.
            var sides = Clickers("Side");
            var colours = Clickers("Colour");
            var teams = Clickers("Team");
            string side = s.Seats[0].Side;
            var kingdoms = new HashSet<string>();
            for (int k = 0; k < root.Backend.Sides.Count + 1; k++) { sides[0].Step(1); kingdoms.Add(s.Seats[0].Side); }
            Assert.GreaterOrEqual(kingdoms.Count, root.Backend.Sides.Count, "every kingdom can be picked");
            for (int k = 0; k < root.Backend.Sides.Count + 1; k++) sides[0].Step(-1);
            Assert.AreEqual(side, s.Seats[0].Side);
            int colour = s.Seats[0].Colour, team = s.Seats[0].Team;
            for (int k = 0; k < 8; k++) colours[0].Step(1);
            Assert.AreEqual(colour, s.Seats[0].Colour, "eight colours round");
            teams[0].Step(1);
            Assert.AreNotEqual(team, s.Seats[0].Team);
            teams[0].Step(-1);
            Assert.AreEqual(team, s.Seats[0].Team);

            // Starts: your seat takes each free one, then any again.
            var starts = Clickers("Start");
            starts[0].Step(1);
            Assert.GreaterOrEqual(s.Seats[0].Start, 0, "a start taken");
            for (int k = 0; k < 16 && s.Seats[0].Start >= 0; k++) starts[0].Step(1);
            Assert.AreEqual(-1, s.Seats[0].Start, "back to any");

            // The four boxes flip and flip back.
            var boxes = page.Page.Root.GetComponentsInChildren<CheckBox>(true);
            Assert.AreEqual(4, boxes.Length);
            foreach (var box in boxes)
            {
                bool on = box.On();
                box.Toggle();
                Assert.AreNotEqual(on, box.On(), box.name);
                box.Toggle();
                Assert.AreEqual(on, box.On(), box.name);
            }

            // Weather, speed and the unit limit.
            var weather = root.Options.Weather;
            for (int k = 0; k < 5; k++) Clickers("Weather")[0].Step(1);
            Assert.AreEqual(weather, root.Options.Weather, "five weathers round");
            int speed = root.Options.GameSpeed;
            Clickers("Speed")[0].Step(1);
            Assert.AreNotEqual(speed, root.Options.GameSpeed);
            Clickers("Speed")[0].Step(1);
            Assert.AreEqual(speed, root.Options.GameSpeed);
            int limit = s.UnitLimit;
            var more = page.Page.Root.GetComponentsInChildren<ArtButton>(true).FirstOrDefault(b => b.name == "More units");
            var fewer = page.Page.Root.GetComponentsInChildren<ArtButton>(true).FirstOrDefault(b => b.name == "Fewer units");
            if (more != null)
            {
                more.Press();
                Assert.AreEqual(limit + 25, s.UnitLimit);
                fewer.Press();
            }
            else
            {
                Clickers("More units")[0].Step(1);
                Assert.AreEqual(limit + 25, s.UnitLimit);
                Clickers("Fewer units")[0].Step(1);
            }
            Assert.AreEqual(limit, s.UnitLimit);

            // A duel of monarchs on a small map, the enemy's start beside yours.
            s.MapId = "king of the hill";
            s.MapRevealed = true;
            s.LineOfSight = false;
            s.MonarchExpendable = false;
            s.RandomStarts = false;
            for (int i = 2; i < s.Seats.Count; i++) s.Seats[i].Kind = SeatKind.Closed;
            s.Seats[0].Side = "ARAMON";
            s.Seats[1].Kind = SeatKind.Computer;
            s.Seats[1].Difficulty = AiDifficulty.Easy;
            s.Seats[1].Side = "ZHON";
            s.Seats[1].Team = s.Seats[0].Team + 1;
            page.Refresh();
            Assert.IsTrue(page.StartButton.Enabled, "the setup can play");
            page.StartButton.Press();
            yield return Until(() => root.Flow.State == FlowState.Playing, 300f);
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);

            var units = new UnitState[4096];
            var b = root.Backend;
            Time.timeScale = 20f;
            yield return Until(() =>
            {
                int n = b.ReadUnits(units), mine = -1, theirs = -1;
                for (int i = 0; i < n; i++)
                {
                    if ((units[i].Flags & UnitFlags.Dying) != 0 || b.UnitDefs[units[i].Def].IsBuilding) continue;
                    if (units[i].Player == b.LocalPlayer) { if (mine < 0 && b.UnitDefs[units[i].Def].BuildOptions.Length > 0) mine = units[i].Handle; }
                    else if (theirs < 0 && b.UnitDefs[units[i].Def].BuildOptions.Length > 0) theirs = units[i].Handle;
                }
                if (mine >= 0 && theirs >= 0 && b.Tick % 60 < 8)
                    b.Command(new GameCommand { Kind = CommandKind.Attack, Unit = mine, TargetUnit = theirs, BuildDef = -1 });
                return root.Flow.State != FlowState.Playing;
            }, 900f);
            Time.timeScale = 1f;
            Debug.Log($"The duel ended in {root.Flow.State} at tick {b.Tick}");
            Assert.That(root.Flow.State, Is.EqualTo(FlowState.Victory).Or.EqualTo(FlowState.Defeat), "the battle was decided");
            bool won = root.Flow.State == FlowState.Victory;

            UiKit.Motion.Off = true;
            yield return null;
            var result = root.Screens.Screen("Result");
            Assert.IsTrue(result.activeSelf, "the result shows");
            var words = result.GetComponentsInChildren<Text>().Select(t => t.text).ToList();
            CollectionAssert.Contains(words, won ? "Victory" : "Defeat");
            CollectionAssert.Contains(words, "You");
            ButtonOn("Result", "Look at the field").onClick.Invoke();
            yield return null;
            Assert.IsFalse(result.activeSelf, "the plaque is put away");
            Assert.IsNotNull(root.World, "the field stays");
            var fog = new byte[root.Backend.ReadFog(null, out _, out _)];
            root.Backend.ReadFog(fog, out _, out _);
            Assert.IsTrue(fog.Length > 0 && fog.All(f => f == 2), "the whole field is clear");
            ButtonOn("Results", "Results").onClick.Invoke();
            yield return null;
            Assert.IsTrue(result.activeSelf, "Results brings it back");
            ButtonOn("Result", "Return to menu").onClick.Invoke();
            yield return null;
            Assert.AreEqual(FlowState.MainMenu, root.Flow.State);
            Assert.IsNull(root.World, "the battle is gone");
            Assert.AreEqual(GameStatus.Idle, root.Backend.Status, "and the engine let it go");

            // And a second battle starts after it.
            Assert.IsTrue(root.Flow.Fire(FlowEvent.OpenSkirmish));
            yield return null;
            page.StartButton.Press();
            yield return Until(() => root.Flow.State == FlowState.Playing, 300f);
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
        }

        [UnityTest]
        public IEnumerator TheLoadingScreenKeepsDrawingWhileTheWorldBuilds()
        {
            double slice = GameRoot.BuildSliceMs;
            GameRoot.BuildSliceMs = 0;
            try
            {
                yield return Boot(new MockBackend { StageSeconds = 0f });
                root.Flow.Fire(FlowEvent.OpenSkirmish);
                root.Screens.StartGame();
                int landFrames = 0, modelFrames = 0, partLand = 0;
                float last = 0f;
                float deadline = Time.realtimeSinceStartup + 60f;
                while (root.Flow.State == FlowState.Loading && Time.realtimeSinceStartup < deadline)
                {
                    if (root.World != null && !root.Loading.Done)
                    {
                        if (root.Loading.Stage == GameRoot.LandStage) landFrames++;
                        if (root.Loading.Stage == GameRoot.PreparingStage) modelFrames++;
                        if (root.World.Terrain.Regions > 0 && root.World.Part == TerrainView.LandPart) partLand++;
                        Assert.That(root.Loading.Fraction, Is.InRange(GameRoot.EngineShare, 1f));
                        Assert.GreaterOrEqual(root.Loading.Fraction, last, "the bar never goes back");
                        last = root.Loading.Fraction;
                    }
                    yield return null;
                }
                Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
                Assert.Greater(landFrames, 2, "the land took several frames, each drawn");
                Assert.Greater(partLand, 0, "a frame was drawn with only some of the land built");
                Assert.Greater(modelFrames, 1, "the models took more than one frame, each drawn");
                Assert.AreEqual(0, root.World.StepsLeft, "the whole world was built before the battle");
                Assert.IsNotNull(root.World.Terrain.Apron, "and the edge ring");
                Assert.IsTrue(root.Loading.Done);
                yield return null;
                Assert.Greater(root.LoadWorstFrameMs, 0.0, "the slowest loading frame was timed");
            }
            finally { GameRoot.BuildSliceMs = slice; }
        }

        // The end screen on the real engine: a battle fought for a minute
        // and lost shows the engine's own tallies for every kingdom, and
        // the graphs and the kingdoms page have its record.
        [UnityTest, Timeout(900000)]
        public IEnumerator ADecidedBattleShowsTheEnginesOwnTallies()
        {
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            yield return Boot();
            Assert.IsFalse(root.Backend is MockBackend, "the engine runs: " + root.BackendProblem);
            var b = root.Backend;
            Assert.IsTrue(root.Flow.Fire(FlowEvent.OpenSkirmish));
            yield return null;
            var s = root.Setup;
            s.MapId = "king of the hill";
            s.MapRevealed = true;
            s.LineOfSight = false;
            s.RandomStarts = false;
            for (int i = 0; i < s.Seats.Count; i++)
            {
                s.Seats[i].Kind = i == 0 ? SeatKind.Human : i == 1 ? SeatKind.Computer : SeatKind.Closed;
                s.Seats[i].Team = SeatTeam.Alone;
            }
            root.Screens.StartGame();
            yield return Until(() => root.Flow.State == FlowState.Playing, 300f);
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);

            // A minute of battle, so the computer builds and the record samples.
            b.Advance(60 * b.TicksPerSecond);
            int other = b.Players.First(p => !p.IsLocal).Index;
            var units = new UnitState[4096];
            int n = b.ReadUnits(units);
            for (int i = 0; i < n; i++)
                if (units[i].Player == b.LocalPlayer)
                    b.Command(new GameCommand { Kind = (CommandKind)24, Unit = units[i].Handle, TargetUnit = -1, BuildDef = -1, Arg = other });
            yield return Until(() => root.Flow.State != FlowState.Playing, 30f);
            Assert.AreEqual(FlowState.Defeat, root.Flow.State);
            UiKit.Motion.Off = true;
            yield return null;
            yield return null;
            var result = root.Screens.Screen("Result");
            Assert.IsTrue(result.activeSelf, "the result shows");

            List<Text> Texts(string name) => result.GetComponentsInChildren<Text>(false).Where(t => t.name == name).ToList();
            int tps = b.TicksPerSecond;
            for (int i = 0; i < b.Players.Count; i++)
            {
                var pl = b.Players[i];
                Assert.AreEqual(0, OkEngine.okx_battle_stats(pl.Index, out var st));
                Assert.AreEqual(MenuScreens.ResultName(pl), Texts("Player")[i].text);
                Assert.AreEqual(st.unitsBuilt.ToString("N0"), Texts("Units")[i].text, "units built, the engine's");
                Assert.AreEqual(st.kills.ToString("N0"), Texts("Kills")[i].text);
                Assert.AreEqual(st.losses.ToString("N0"), Texts("Losses")[i].text);
                Assert.AreEqual(BattleRecord.Clock(st.lastAliveTick, tps), Texts("Time")[i].text);
                Assert.AreEqual(st.score.ToString("N0"), Texts("Score")[i].text);
                if (!pl.IsLocal) Assert.Greater(st.unitsBuilt, 1, "the computer built");
            }

            ButtonOn("Result", "Graphs").onClick.Invoke();
            var graph = result.GetComponentInChildren<LineGraph>();
            Assert.IsNotNull(graph);
            Assert.AreEqual(2, graph.Lines.Count, "a line a kingdom");
            foreach (var line in graph.Lines) Assert.GreaterOrEqual(line.Points.Length, 12, "a minute of samples and now");

            ButtonOn("Result", "Kingdoms").onClick.Invoke();
            Assert.AreEqual(0, OkEngine.okx_battle_stats(b.LocalPlayer, out var mine));
            Assert.AreEqual(Mathf.RoundToInt(mine.manaGathered).ToString("N0"), Texts("Mana gathered").Single().text);
            Assert.AreEqual(mine.fellTick > 0 ? "Fell at " + BattleRecord.Short(mine.fellTick, tps) : "Standing", Texts("Fate").Single().text);

            ButtonOn("Result", "Annals").onClick.Invoke();
            var moments = Texts("Moment").Select(t => t.text).ToList();
            Assert.IsTrue(moments.Contains("You have fallen."), string.Join(" | ", moments));
            ButtonOn("Result", "Return to menu").onClick.Invoke();
            yield return null;
            Assert.AreEqual(FlowState.MainMenu, root.Flow.State);
        }

        // Beaten with two computers still at war, Look at the field shows
        // them fight on, and the defeat stands.
        [UnityTest, Timeout(900000)]
        public IEnumerator ALostBattlePlaysOnWhileTheFieldShows()
        {
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            yield return Boot();
            Assert.IsFalse(root.Backend is MockBackend, "the engine runs: " + root.BackendProblem);
            var b = root.Backend;
            var map = b.Maps.Where(m => MapCatalog.PlayersOf(m) >= 3).OrderBy(m => m.Size.x * m.Size.y).FirstOrDefault();
            Assert.IsNotNull(map, "a map for three");
            Assert.IsTrue(root.Flow.Fire(FlowEvent.OpenSkirmish));
            yield return null;
            var s = root.Setup;
            s.MapId = map.Id;
            s.MapRevealed = true;
            s.LineOfSight = false;
            s.RandomStarts = false;
            for (int i = 0; i < s.Seats.Count; i++)
            {
                s.Seats[i].Kind = i == 0 ? SeatKind.Human : i < 3 ? SeatKind.Computer : SeatKind.Closed;
                s.Seats[i].Team = SeatTeam.Alone;
            }
            root.Screens.StartGame();
            yield return Until(() => root.Flow.State == FlowState.Playing, 300f);
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);

            // You hand your army to a computer and have nothing left.
            int other = b.Players.First(p => !p.IsLocal).Index;
            var units = new UnitState[4096];
            int n = b.ReadUnits(units);
            for (int i = 0; i < n; i++)
                if (units[i].Player == b.LocalPlayer)
                    b.Command(new GameCommand { Kind = (CommandKind)24, Unit = units[i].Handle, TargetUnit = -1, BuildDef = -1, Arg = other });
            yield return Until(() => root.Flow.State != FlowState.Playing, 30f);
            Assert.AreEqual(FlowState.Defeat, root.Flow.State);

            UiKit.Motion.Off = true;
            yield return null;
            var result = root.Screens.Screen("Result");
            Assert.IsTrue(result.activeSelf, "the result shows");
            uint ended = b.Tick;
            ButtonOn("Result", "Look at the field").onClick.Invoke();
            Assert.IsTrue(b.PlaysOn, "the computers fight on");
            yield return Until(() => b.Tick > ended + 120, 60f);
            Assert.Greater(b.Tick, ended + 120, "the battle plays on while the field shows");
            Assert.AreEqual(FlowState.Defeat, root.Flow.State, "and stays lost");
            Assert.AreEqual(GameStatus.Defeat, b.Status);
            Assert.IsFalse(result.activeSelf, "the plaque stays away");
            n = b.ReadUnits(units);
            var standing = new HashSet<int>();
            for (int i = 0; i < n; i++)
                if ((units[i].Flags & UnitFlags.Dying) == 0) standing.Add(units[i].Player);
            Assert.IsFalse(standing.Contains(b.LocalPlayer), "you have nothing on the field");
            Assert.AreEqual(2, standing.Count, "both computers stand");

            ButtonOn("Results", "Results").onClick.Invoke();
            yield return null;
            Assert.IsTrue(result.activeSelf, "Results brings it back");
            CollectionAssert.Contains(result.GetComponentsInChildren<Text>().Select(t => t.text).ToList(), "Defeat");
            ButtonOn("Result", "Return to menu").onClick.Invoke();
            yield return null;
            Assert.AreEqual(FlowState.MainMenu, root.Flow.State);
            Assert.AreEqual(GameStatus.Idle, b.Status, "and the engine let it go");
        }

        [UnityTest]
        public IEnumerator AComputerOpenedInALowerRowIsNotYourAlly()
        {
            yield return Boot(new MockBackend { StageSeconds = 0f });
            Assert.IsTrue(root.Flow.Fire(FlowEvent.OpenSkirmish));
            yield return null;
            var s = root.Setup;
            Assert.AreEqual(LobbyScreens.SeatRows, s.Seats.Count);
            var names = Clickers("Name");
            for (int i = 4; i < s.Seats.Count; i++)
            {
                names[i].Step(1);
                Assert.AreEqual(SeatKind.Computer, s.Seats[i].Kind);
                Assert.AreNotEqual(s.Seats[0].Team, s.Seats[i].Team, $"row {i + 1} opens on another team");
            }
        }

        [UnityTest]
        public IEnumerator OptionsSetOnTheSheetComeBackAfterARestart()
        {
            UiKit.Motion.Off = true;
            yield return Boot(new MockBackend { StageSeconds = 0f });
            Assert.IsTrue(root.Flow.Fire(FlowEvent.OpenOptions));
            yield return null;
            var sheet = root.Screens.Screen("Options");
            CyclePicker Picker(string label) => sheet.GetComponentsInChildren<CyclePicker>(true).First(c => c.name == "Picker " + label);
            ButtonOn("Options", "Defaults").onClick.Invoke();
            var rows = new[] { "Weather", "Game speed", "Controls", "Interface size", "Key letters", "Pointer size", "Sound", "Music", "Shadows", "Post effects" };
            foreach (var r in rows) Picker(r).Step(1);
            var shown = rows.Select(r => Picker(r).Index).ToList();
            var chosen = root.Options;
            string Read(GameOptions o) => $"{o.Weather} {o.GameSpeed} {o.ClassicControls} {o.UiScale} {o.HotkeyLetters} {o.CursorScale} {o.Volume} {o.Music} {o.Shadows} {o.PostEffects}";
            string want = Read(chosen);
            Assert.AreNotEqual(Read(new GameOptions()), want, "every row moved off its default");
            ButtonOn("Options", "Back").onClick.Invoke();
            yield return null;
            Assert.AreEqual(FlowState.MainMenu, root.Flow.State);

            // A new start reads them back, and the sheet shows them.
            yield return Boot(new MockBackend { StageSeconds = 0f });
            Assert.AreEqual(want, Read(root.Options), "the options come back after a restart");
            Assert.IsTrue(root.Flow.Fire(FlowEvent.OpenOptions));
            yield return null;
            sheet = root.Screens.Screen("Options");
            CollectionAssert.AreEqual(shown, rows.Select(r => Picker(r).Index).ToList(), "the sheet shows the choices");
        }
    }
}
