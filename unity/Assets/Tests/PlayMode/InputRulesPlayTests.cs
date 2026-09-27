// InputRulesPlayTests.cs - the battle's input rules on the mock: Escape
// cancels and never pauses, F1 and F2 open the menu and its options, a
// unit in the fog cannot be picked, a tall unit is picked where it shows,
// and a second factory of a kind builds into itself.
using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Tests
{
    public class InputRulesPlayTests
    {
        GameRoot root;
        MockBackend mock;

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        IEnumerator Begin(bool revealed = true, string map = "mock_highlands")
        {
            mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f };
            root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = map;
            root.Setup.MapRevealed = revealed;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State);
            for (int i = 0; i < 10; i++) yield return null;
        }

        UnitState[] Units()
        {
            var u = new UnitState[512];
            int n = mock.ReadUnits(u);
            return u.Take(n).ToArray();
        }

        [UnityTest]
        public IEnumerator EscapeCancelsAndF1AndF2OpenTheMenus()
        {
            yield return Begin();
            var knight = Units().First(u => u.Player == mock.LocalPlayer && mock.RoleOf(u.Def) == MockBackend.Role.Knight);
            mock.Select(new[] { knight.Handle }, false);
            root.Orders.Arm(CommandKind.Attack);
            root.BattleKey(KeyCode.Escape);
            Assert.IsNull(root.Orders.Armed, "Escape lets go of the armed attack");
            Assert.AreEqual(FlowState.Playing, root.Flow.State, "and does not pause");
            Assert.AreEqual(1, mock.ReadSelection(new int[8]), "the selection stays");
            root.BattleKey(KeyCode.Escape);
            Assert.AreEqual(0, mock.ReadSelection(new int[8]), "a second Escape deselects");
            Assert.AreEqual(FlowState.Playing, root.Flow.State);

            root.BattleKey(KeyCode.F1);
            Assert.AreEqual(FlowState.Paused, root.Flow.State, "F1 is the game menu");
            root.Flow.Fire(FlowEvent.Resume);
            root.BattleKey(KeyCode.F2);
            Assert.AreEqual(FlowState.Options, root.Flow.State, "F2 opens the options over the battle");
            Assert.IsTrue(root.Flow.InGameNow);
            root.Flow.Fire(FlowEvent.Back);
            Assert.AreEqual(FlowState.Paused, root.Flow.State);
            root.Flow.Fire(FlowEvent.Resume);
            root.BattleKey(KeyCode.Pause);
            Assert.AreEqual(FlowState.Paused, root.Flow.State, "the Pause key still pauses");
        }

        [UnityTest]
        public IEnumerator AUnitInTheFogCannotBePicked()
        {
            yield return Begin(false);
            var cam = root.World.Camera.GetComponent<Camera>();
            var enemy = Units().First(u => u.Player != mock.LocalPlayer && mock.RoleOf(u.Def) == MockBackend.Role.Knight);
            root.World.Camera.focus = enemy.Position;
            for (int i = 0; i < 10; i++) yield return null;
            var at = cam.WorldToScreenPoint(enemy.Position + Vector3.up * 0.6f);
            Assert.AreEqual(-1, root.Orders.UnitAt(at), "an enemy out of sight is not there to pick");

            var own = Units().First(u => u.Player == mock.LocalPlayer && mock.RoleOf(u.Def) == MockBackend.Role.Knight);
            root.World.Camera.focus = own.Position;
            for (int i = 0; i < 10; i++) yield return null;
            own = Units().First(u => u.Handle == own.Handle);
            at = cam.WorldToScreenPoint(own.Position + Vector3.up * 0.6f);
            // Soldiers stand close together, so the pick may be a neighbour,
            // the nearest to the camera, but it is one of the player's.
            int picked = root.Orders.UnitAt(at);
            Assert.AreNotEqual(-1, picked, "a unit in sight is there to pick");
            Assert.AreEqual(mock.LocalPlayer, Units().First(u => u.Handle == picked).Player);
        }

        [UnityTest]
        public IEnumerator ATallBuildingIsPickedWhereItShows()
        {
            yield return Begin();
            var cam = root.World.Camera.GetComponent<Camera>();
            var lodge = Units().First(u => u.Player == mock.LocalPlayer && mock.RoleOf(u.Def) == MockBackend.Role.Lodge);
            foreach (float zoom in new[] { root.World.Camera.minDistance, root.World.Camera.maxDistance })
            {
                root.World.Camera.focus = lodge.Position;
                root.World.Camera.Zoom(zoom);
                for (int i = 0; i < 10; i++) yield return null;
                Assert.IsTrue(root.World.Entities.DrawnSize.TryGetValue(lodge.Handle, out var size));
                // A point two thirds of the way up the building.
                var at = cam.WorldToScreenPoint(lodge.Position + Vector3.up * size.x * 0.66f);
                Assert.AreEqual(lodge.Handle, root.Orders.UnitAt(at), $"the lodge's upper part picks it at zoom {zoom}");
            }
        }

        [UnityTest]
        public IEnumerator ASecondLodgeBuildsIntoItself()
        {
            yield return Begin();
            var units = Units();
            var monarch = units.First(u => u.Player == mock.LocalPlayer && mock.RoleOf(u.Def) == MockBackend.Role.Monarch);
            var first = units.First(u => u.Player == mock.LocalPlayer && mock.RoleOf(u.Def) == MockBackend.Role.Lodge);
            // The monarch raises a second lodge of the same kind.
            Vector3 site = default;
            bool found = false;
            for (int dx = 8; dx < 40 && !found; dx += 2) found = mock.CanBuildAt(first.Def, monarch.Position + new Vector3(dx, 0, 0), 0, out site);
            Assert.IsTrue(found);
            Assert.IsTrue(mock.Command(new GameCommand { Kind = CommandKind.Build, Unit = monarch.Handle, Target = site, TargetUnit = -1, BuildDef = first.Def }));
            for (int i = 0; i < 30 * 8; i++) mock.Advance(1);
            var second = Units().First(u => u.Def == first.Def && u.Handle != first.Handle && u.Player == mock.LocalPlayer);

            mock.Select(new[] { first.Handle }, false);
            yield return new WaitForSecondsRealtime(0.3f);
            mock.Select(new[] { second.Handle }, false);
            yield return new WaitForSecondsRealtime(0.3f);
            var grid = GameObject.Find("Commands");
            var button = grid.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<Text>() != null && b.GetComponentInChildren<Text>().text.Contains("Knight"));
            button.onClick.Invoke();
            Assert.AreEqual(1, mock.QueuedCount(second.Handle, -1), "the second lodge takes the order");
            Assert.AreEqual(0, mock.QueuedCount(first.Handle, -1), "the first does not");
        }
    }
}
