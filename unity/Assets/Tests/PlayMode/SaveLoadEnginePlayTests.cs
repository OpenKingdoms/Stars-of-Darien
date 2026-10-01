// SaveLoadEnginePlayTests.cs - on the real engine, a skirmish saved from
// the pause menu loads again from the skirmish page's Load button, through
// the screens a player uses, and resumes at the same tick with the same
// units. Needs okengine and the game files, and is ignored without them.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Tests
{
    public class SaveLoadEnginePlayTests
    {
        GameRoot root;
        string saved, saves;

        [SetUp]
        public void OwnSaves() => saves = TempSaves.Use();

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
            TempSaves.Drop(saves);
            saved = null;
        }

        static IEnumerator Until(System.Func<bool> done, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < deadline) yield return null;
        }

        // The button a screen shows with these words on it.
        Button ButtonOn(string screen, System.Func<string, bool> words)
        {
            var go = root.Screens.Screen(screen);
            Assert.IsTrue(go.activeInHierarchy, screen + " is up");
            var b = go.GetComponentsInChildren<Button>().FirstOrDefault(x => x.GetComponentsInChildren<Text>().Any(t => words(t.text)));
            Assert.IsNotNull(b, $"{screen} has the button");
            return b;
        }

        // Every unit, by its id that is never reused, as the engine reports it.
        Dictionary<uint, UnitState> Units()
        {
            var units = new UnitState[8192];
            int n = root.Backend.ReadUnits(units);
            return units.Take(n).ToDictionary(u => u.StableId);
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator ASkirmishSavedFromPauseLoadsFromTheSkirmishPageWhereItStopped()
        {
            if (GameRoot.BackendFactory == null) Assert.Ignore("the engine or the game files are missing");
            var view = GameObject.Find("OpenKingdoms");
            if (view != null) Object.Destroy(view);
            yield return null;
            root = GameRoot.Boot();
            yield return null;

            // A skirmish from the skirmish page, played for half a minute.
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "two castles";
            // Revealed, so the engine reports the computer's units too.
            root.Setup.MapRevealed = true;
            root.Setup.LineOfSight = false;
            root.Setup.StartMana = 5000;
            root.Setup.Seats[0].Side = "ARAMON";
            root.Setup.Seats[1].Side = "TAROS";
            yield return null;
            var sk = root.Screens.Lobby.Skirmish;
            sk.StartButton.Press();
            yield return Until(() => root.Flow.State == FlowState.Playing, 300f);
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
            uint run = (uint)(30 * root.Backend.TicksPerSecond);
            yield return Until(() => root.Backend.Tick > run, 300f);
            Assert.Greater(root.Backend.Tick, run, "the battle ran");

            // The monarch puts up a building, saved half built.
            var eng = root.Backend;
            var king = Units().Values.First(u => u.Player == eng.LocalPlayer && !eng.UnitDefs[u.Def].IsBuilding && eng.UnitDefs[u.Def].BuildOptions.Length > 0);
            bool ordered = false;
            foreach (int def in eng.UnitDefs[king.Def].BuildOptions.Where(d => eng.UnitDefs[d].IsBuilding))
            {
                for (int r = 6; r < 40 && !ordered; r += 2)
                    for (int a = 0; a < 16 && !ordered; a++)
                        if (eng.CanBuildAt(def, king.Position + Quaternion.Euler(0, a * 22.5f, 0) * Vector3.forward * r, 0, out var site))
                            ordered = eng.Command(new GameCommand { Kind = CommandKind.Build, Unit = king.Handle, Target = site, TargetUnit = -1, BuildDef = def });
                if (ordered) break;
            }
            Assert.IsTrue(ordered, "the monarch took a build order");
            yield return Until(() => Units().Values.Any(u => u.Player == eng.LocalPlayer && u.BuildProgress > 0.05f && u.BuildProgress < 0.6f), 120f);

            // Paused, the battle stands still, and Save game writes it.
            root.Flow.Fire(FlowEvent.Pause);
            yield return null;
            uint tick = root.Backend.Tick;
            var before = Units();
            Assert.GreaterOrEqual(before.Values.Select(u => u.Player).Distinct().Count(), 2, "both kingdoms have units");
            Assert.Greater(before.Count, 2, "more than the monarchs");
            Assert.IsTrue(before.Values.Any(u => u.BuildProgress < 1f), "a building is half built");
            var orders = before.ToDictionary(kv => kv.Key, kv => root.Backend.ReadOrder(kv.Value.Handle));
            var kingdoms = root.Backend.Players.Select(p => $"{p.Index} {p.Side} {p.Colour} {p.IsLocal}").ToList();
            var old = new HashSet<string>(root.ListSaves().Select(e => e.Path));
            ButtonOn("Pause", t => t == "Save game").onClick.Invoke();
            var mine = root.ListSaves().Where(e => !old.Contains(e.Path)).ToList();
            Assert.AreEqual(1, mine.Count, "one new save");
            saved = mine[0].Path;
            Assert.AreEqual(tick, mine[0].Tick, "the save is of the paused tick");
            Assert.AreEqual(tick, root.Backend.Tick, "saving stops nothing");

            // Quit to the menu, back to the skirmish page, and its Load button.
            ButtonOn("Pause", t => t == "Quit to menu").onClick.Invoke();
            yield return null;
            ButtonOn("Leave", t => t == "Leave").onClick.Invoke();
            yield return null;
            Assert.AreEqual(FlowState.MainMenu, root.Flow.State);
            Assert.IsNull(root.World, "the battle is gone");
            Assert.IsTrue(root.Flow.Fire(FlowEvent.OpenSkirmish));
            yield return null;
            var load = sk.Page.Root.GetComponentsInChildren<ArtButton>(true).First(b => b.name == "Load");
            load.Press();
            yield return null;
            Assert.AreEqual(FlowState.LoadList, root.Flow.State);

            // The battle as it stands the moment it is back, before it runs on.
            uint loadedTick = 0;
            Dictionary<uint, UnitState> after = null;
            root.Flow.Changed += (was, now) =>
            {
                if (now != FlowState.Playing || after != null) return;
                loadedTick = root.Backend.Tick;
                after = Units();
            };
            string file = Path.GetFileName(saved);
            Dictionary<uint, UnitOrder> ordersAfter = null;
            root.Flow.Changed += (was, now) =>
            {
                if (now == FlowState.Playing && ordersAfter == null && after != null)
                    ordersAfter = after.ToDictionary(kv => kv.Key, kv => root.Backend.ReadOrder(kv.Value.Handle));
            };
            var entry = root.Screens.Screen("Load").GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "Save " + file);
            Assert.IsNotNull(entry, "the save is in the list");
            entry.onClick.Invoke();
            yield return Until(() => root.Flow.State == FlowState.Playing, 300f);
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
            Assert.IsNotNull(after);

            Assert.AreEqual(tick, loadedTick, "the battle resumes at the tick it was saved");
            Assert.AreEqual("two castles", root.CurrentMap().Id, "the saved map, as the lobby names it");
            CollectionAssert.AreEqual(kingdoms, root.Backend.Players.Select(p => $"{p.Index} {p.Side} {p.Colour} {p.IsLocal}").ToList(), "the same kingdoms");
            Debug.Log($"Saved at tick {tick} with {before.Count} units of {kingdoms.Count} kingdoms in {new FileInfo(saved).Length} bytes, and loaded at tick {loadedTick} with {after.Count}");
            CollectionAssert.AreEquivalent(before.Keys, after.Keys, "the same units");
            foreach (var kv in before)
            {
                var a = kv.Value;
                var b = after[kv.Key];
                string who = $"unit {kv.Key} ({root.Backend.UnitDefs[a.Def].Name})";
                Assert.AreEqual(a.Def, b.Def, who);
                Assert.AreEqual(a.Player, b.Player, who);
                Assert.AreEqual(a.Health, b.Health, who);
                Assert.AreEqual(a.BuildProgress, b.BuildProgress, 1e-4f, who);
                Assert.Less((a.Position - b.Position).magnitude, 0.01f, who + " stands where it stood");
                Assert.AreEqual(orders[kv.Key].Kind, ordersAfter[kv.Key].Kind, who + " keeps its order");
            }

            // And it plays on from there.
            yield return Until(() => root.Backend.Tick > tick + 30, 60f);
            Assert.Greater(root.Backend.Tick, tick + 30, "the loaded battle runs");
            Assert.AreEqual(GameStatus.Running, root.Backend.Status);
        }
    }
}
