// MinimapRoutePlayTests.cs - the minimap on the real engine, through the
// event system's own raycast: a press anywhere on it reaches it, and its
// look button moves the view with a unit selected, in both schemes.
// Needs okengine and the game files, and is ignored without them.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class MinimapRoutePlayTests
    {
        GameRoot root;
        EngineBackend engine;
        readonly UnitState[] units = new UnitState[1024];

        [TearDown]
        public void CleanUp()
        {
            if (root != null) Object.Destroy(root.gameObject);
        }

        IEnumerator Begin()
        {
            if (!EngineSettings.EngineAvailable) Assert.Ignore("okengine or the game files are missing");
            engine = new EngineBackend();
            root = GameRoot.Boot(engine);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "two castles";
            root.Setup.MapRevealed = true;
            root.Setup.LineOfSight = false;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 240f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, "the battle loaded: " + root.LastError);
            root.Options.GameSpeed = 0;
            for (int i = 0; i < 5; i++) yield return null;
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator APressOnTheMinimapReachesItAndItsLookButtonMovesTheView()
        {
            yield return Begin();
            var input = Object.FindAnyObjectByType<MinimapInput>();
            Assert.IsNotNull(input, "the minimap is up");
            var corners = new Vector3[4];
            ((RectTransform)input.transform).GetWorldCorners(corners);
            Vector2 At(float u, float v) => new Vector2(Mathf.Lerp(corners[0].x, corners[2].x, u), Mathf.Lerp(corners[0].y, corners[2].y, v));
            var es = EventSystem.current;
            Assert.IsNotNull(es, "an event system");
            Debug.Log($"minimap route: screen {Screen.width}x{Screen.height}, minimap {corners[0]}..{corners[2]}");

            GameObject Top(Vector2 p, out RaycastResult first)
            {
                var hits = new List<RaycastResult>();
                es.RaycastAll(new PointerEventData(es) { position = p }, hits);
                Debug.Log($"minimap route: at {p}: " + string.Join(", ", hits.Select(h =>
                    $"{h.gameObject.name} [{h.module.GetType().Name} on {h.module.name}, prio {h.module.sortOrderPriority}/{h.module.renderOrderPriority}, order {h.sortingOrder}, depth {h.depth}]")));
                first = hits.Count > 0 ? hits[0] : default;
                return hits.Count > 0 ? hits[0].gameObject : null;
            }

            foreach (var (u, v) in new[] { (0.5f, 0.5f), (0.2f, 0.8f), (0.8f, 0.2f) })
            {
                var top = Top(At(u, v), out _);
                var handler = top != null ? ExecuteEvents.GetEventHandler<IPointerDownHandler>(top) : null;
                Assert.AreSame(input.gameObject, handler, $"a press at {u},{v} of the minimap reaches it, not {(top != null ? top.name : "nothing")}");
            }

            int me = engine.LocalPlayer;
            var defs = engine.UnitDefs;
            int n = engine.ReadUnits(units);
            var monarch = units.Take(n).First(x => x.Player == me && !defs[x.Def].IsBuilding);
            var size = engine.Terrain.Size;
            var cam = root.World.Camera;

            foreach (bool classic in new[] { true, false })
            {
                string scheme = classic ? "classic" : "modern";
                root.Orders.Classic = classic;
                engine.Cancel();
                root.World.Entities.Selected.Clear();
                if (classic) engine.Select(new[] { monarch.Handle }, false);
                else root.World.Entities.Selected.Add(monarch.Handle);
                yield return null;
                Assert.IsTrue(root.World.Entities.Selected.Contains(monarch.Handle), "the monarch selected, " + scheme);
                var look = classic ? PointerEventData.InputButton.Right : PointerEventData.InputButton.Left;

                cam.focus = new Vector3(size.x * 0.5f, cam.focus.y, -size.y * 0.5f);
                var press = At(0.2f, 0.8f);
                var top = Top(press, out var first);
                var data = new PointerEventData(es) { button = look, position = press, pressPosition = press, pointerPressRaycast = first, pointerCurrentRaycast = first };
                ExecuteEvents.ExecuteHierarchy(top, data, ExecuteEvents.pointerDownHandler);
                yield return null;
                Debug.Log($"minimap route: {scheme} press, focus {cam.focus}, want {0.2f * size.x},{-0.2f * size.y}");
                Assert.Less(Mathf.Abs(cam.focus.x - 0.2f * size.x) + Mathf.Abs(cam.focus.z + 0.2f * size.y), 2f, scheme + ": a press with the look button moves the view there");

                data.position = At(0.7f, 0.3f);
                ExecuteEvents.Execute(input.gameObject, data, ExecuteEvents.dragHandler);
                yield return null;
                Debug.Log($"minimap route: {scheme} drag, focus {cam.focus}, want {0.7f * size.x},{-0.7f * size.y}");
                Assert.Less(Mathf.Abs(cam.focus.x - 0.7f * size.x) + Mathf.Abs(cam.focus.z + 0.7f * size.y), 2f, scheme + ": a drag with the look button keeps the view under the pointer");
                Assert.IsTrue(root.World.Entities.Selected.Contains(monarch.Handle), "the selection stays, " + scheme);
            }
        }
    }
}
