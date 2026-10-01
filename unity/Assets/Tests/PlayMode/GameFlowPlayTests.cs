// GameFlowPlayTests.cs - the whole flow on the mock engine, as a player
// goes through it: main menu, skirmish setup, the loading screen, a game
// running for a few hundred frames, the pause menu and back to the menu.
// Any error logged on the way fails the test.
using System.Collections;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class GameFlowPlayTests
    {
        GameRoot root;
        string saves;

        [SetUp]
        public void OwnSaves() => saves = TempSaves.Use();

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
            TempSaves.Drop(saves);
        }

        static IEnumerator Until(System.Func<bool> done, float seconds, string what)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail("timed out waiting for " + what);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator MenuToSkirmishToLoadingToAGameAndBack()
        {
            var mock = new MockBackend { StageSeconds = 0.05f, DamageScale = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            yield return null;
            Assert.AreEqual(FlowState.MainMenu, root.Flow.State);
            Assert.AreEqual("Menu", root.Screens.Visible);
            Assert.IsNull(Object.FindAnyObjectByType<OpenKingdomsUnity.Engine.EngineNotice>(), "the engine notice stays out of the remaster");

            Assert.IsTrue(root.Flow.Fire(FlowEvent.OpenSkirmish));
            yield return null;
            Assert.AreEqual("Skirmish", root.Screens.Visible);
            Assert.IsTrue(root.Screens.Screen("Skirmish").activeInHierarchy);
            root.Setup.MapId = "mock_isles";
            Assert.AreEqual(GameCursor.Normal, root.PointerCursor());
            root.Screens.StartGame();
            Assert.AreEqual(FlowState.Loading, root.Flow.State);
            Assert.AreEqual(GameCursor.Busy, root.PointerCursor(), "the hourglass while the map loads");

            bool sawLoading = false;
            yield return Until(() =>
            {
                sawLoading |= root.Flow.State == FlowState.Loading && root.Screens.Screen("Loading").activeInHierarchy;
                return root.Flow.State == FlowState.Playing;
            }, 30f, "the game to load");
            Assert.IsTrue(sawLoading, "the loading screen showed");
            Assert.AreEqual("Hud", root.Screens.Visible);
            Assert.IsNotNull(root.World);
            Assert.Greater(root.World.Terrain.Regions, 0);
            Assert.IsNotNull(root.World.Terrain.Water, "the isles have a sea");

            uint startTick = mock.Tick;
            for (int i = 0; i < 300; i++) yield return null;
            Assert.GreaterOrEqual(root.FramesPlayed, 300);
            Assert.Greater(mock.Tick, startTick, "the simulation advanced");
            Assert.AreEqual(26, root.World.Entities.UnitCount);
            // Nine own units of seven pieces each at least, whatever the fog hides.
            Assert.Greater(root.World.Entities.Drawn, 60, "unit pieces, trees and sprites were drawn");

            // Selecting the monarch fills the command and build buttons.
            var ents = root.World.Entities;
            for (int i = 0; i < ents.UnitCount; i++)
                if (ents.Units[i].Player == 1 && mock.UnitDefs[ents.Units[i].Def].Name.EndsWith("monarch")) mock.Select(new[] { ents.Units[i].Handle }, false);
            // The panel refreshes ten times a second of real time.
            yield return new WaitForSecondsRealtime(0.4f);
            var orders = GameObject.Find("Orders");
            Assert.IsNotNull(orders, "the sidebar has the orders");
            Assert.AreEqual(mock.SelectionActions().Length, orders.transform.childCount, "the monarch's orders, abilities and stances");
            Assert.AreEqual(3, GameObject.Find("Builds").transform.childCount, "and the three things it builds");
            root.Orders.Arm(CommandKind.Build, mock.UnitDefs[0].BuildOptions[2]);
            yield return null;
            Assert.AreEqual(CommandKind.Build, root.Orders.Armed);
            root.Orders.Disarm();
            mock.Select(new int[0], false);

            Assert.IsTrue(root.Flow.Fire(FlowEvent.Pause));
            uint pausedAt = mock.Tick;
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(pausedAt, mock.Tick, "a paused game does not tick");
            Assert.AreEqual("Hud,Pause", root.Screens.Visible);

            Assert.IsTrue(root.Flow.Fire(FlowEvent.ToMenu));
            yield return null;
            Assert.AreEqual("Menu", root.Screens.Visible);
            Assert.IsNull(root.World);
            Assert.AreEqual(GameStatus.Idle, mock.Status);
        }

        [UnityTest]
        public IEnumerator AGameSavedFromPauseLoadsFromTheMenu()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_highlands";
            root.Screens.StartGame();
            yield return Until(() => root.Flow.State == FlowState.Playing, 30f, "the game to load");
            for (int i = 0; i < 30; i++) yield return null;
            root.Flow.Fire(FlowEvent.Pause);
            Assert.IsTrue(root.SaveNow(out var path), "the save was written");
            try
            {
                root.Flow.Fire(FlowEvent.ToMenu);
                yield return null;
                Assert.IsFalse(root.Flow.Fire(FlowEvent.OpenLoad), "saved games open from the skirmish, not the menu");
                Assert.IsTrue(root.Flow.Fire(FlowEvent.OpenSkirmish));
                Assert.IsTrue(root.Flow.Fire(FlowEvent.OpenLoad));
                yield return null;
                Assert.AreEqual("Load", root.Screens.Visible);
                var saves = root.ListSaves();
                var mine = saves.Find(e => e.Path == path);
                Assert.AreEqual("mock_highlands", mine.Map);
                root.LoadSave(mine);
                yield return Until(() => root.Flow.State == FlowState.Playing, 30f, "the save to load");
                Assert.AreEqual("mock_highlands", root.CurrentMap().Id);
                Assert.AreEqual(26, root.World.Entities.UnitCount);
            }
            finally { System.IO.File.Delete(path); }
        }

        [UnityTest]
        public IEnumerator AWonGameShowsVictory()
        {
            var mock = new MockBackend { StageSeconds = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_frost";
            root.Screens.StartGame();
            yield return Until(() => root.Flow.State == FlowState.Playing, 30f, "the game to load");
            // Kill off the enemy through the mock's own rules: every unit of
            // the player attacks, sped up, until the game ends.
            var units = new UnitState[512];
            Time.timeScale = 20f;
            try
            {
                yield return Until(() =>
                {
                    int n = mock.ReadUnits(units), enemy = -1;
                    for (int i = 0; i < n && enemy < 0; i++)
                        if (units[i].Player == 2 && (units[i].Flags & UnitFlags.Dying) == 0) enemy = units[i].Handle;
                    for (int i = 0; i < n; i++)
                        if (units[i].Player == 1 && enemy >= 0)
                            mock.Command(new GameCommand { Kind = CommandKind.Attack, Unit = units[i].Handle, TargetUnit = enemy, BuildDef = -1 });
                    return root.Flow.State != FlowState.Playing;
                }, 120f, "the game to end");
            }
            finally { Time.timeScale = 1f; }
            Assert.That(root.Flow.State, Is.EqualTo(FlowState.Victory).Or.EqualTo(FlowState.Defeat));
            Assert.AreEqual("Hud,Result", root.Screens.Visible);
            root.Flow.Fire(FlowEvent.ToMenu);
            yield return null;
            Assert.AreEqual("Menu", root.Screens.Visible);
        }
    }
}
