// FormationCrowdPlayTests.cs - five hundred mock units with a formation drag
// of 256 held and swept every frame: the frame stays in the crowd test's
// budget, and the formation's own share is logged and bounded.
using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class FormationCrowdPlayTests
    {
        [UnityTest]
        public IEnumerator FiveHundredUnitsWithADragHeld()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f, ExtraSoldiers = 250 };
            var root = GameRoot.Boot(mock);
            try
            {
                yield return null;
                root.Flow.Fire(FlowEvent.OpenSkirmish);
                root.Setup.MapId = "mock_highlands";
                root.Screens.StartGame();
                float deadline = Time.realtimeSinceStartup + 60f;
                while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.AreEqual(FlowState.Playing, root.Flow.State);
                root.Orders.Classic = false;
                var units = new UnitState[1024];
                int n = mock.ReadUnits(units);
                var mine = units.Take(n).Where(u => u.Player == mock.LocalPlayer && !mock.UnitDefs[u.Def].IsBuilding).Take(256).Select(u => u.Handle).ToArray();
                Assert.AreEqual(256, mine.Length);
                mock.Select(mine, false);
                root.Orders.Selected.Clear();
                root.Orders.Selected.UnionWith(mine);
                var c = units.Take(n).Where(u => mine.Contains(u.Handle)).Aggregate(Vector3.zero, (s, u) => s + u.Position) / mine.Length;
                var cam3 = root.World.Camera;
                cam3.focus = c + new Vector3(0, 0, 14);
                cam3.Zoom(70f);
                for (int i = 0; i < 10; i++) yield return null;
                var cam = cam3.GetComponent<Camera>();
                Vector2 ScreenOf(Vector3 p) => cam.WorldToScreenPoint(new Vector3(p.x, mock.GroundHeight(p.x, p.z), p.z));

                var a = c + new Vector3(-20, 0, 24);
                var frame = new PointerFrame { Focused = true, Dpi = 96, Screen = ScreenOf(a), RightDown = true, RightHeld = true };
                var f = root.Orders.Formation;
                f.Shape = FormationShape.Line;
                f.Source = () => { var r = frame; frame.RightDown = frame.RightUp = false; return r; };
                yield return null;
                frame.Screen = ScreenOf(a + new Vector3(30, 0, 0));
                yield return null;
                Assert.IsTrue(f.Live);
                Assert.AreEqual(256, f.SlotCount);

                var rec = UnityEngine.Profiling.Recorder.Get("Oku.Formation");
                rec.enabled = true;
                double formation = 0;
                int recomputes = f.Recomputes;
                float start = Time.realtimeSinceStartup;
                const int frames = 120;
                for (int i = 0; i < frames; i++)
                {
                    // A new end point every frame, so every frame lays it out again.
                    frame.Screen = ScreenOf(a + new Vector3(30 + (i % 20) * 0.3f, 0, (i % 7) * 0.3f));
                    yield return null;
                    formation += rec.isValid ? rec.elapsedNanoseconds / 1e6 : 0;
                }
                float ms = (Time.realtimeSinceStartup - start) * 1000f / frames;
                double per = formation / frames;
                Debug.Log($"FormationCrowd: {root.World.Entities.UnitCount} units, 256 in the drag, {ms:0.0} ms a frame, formation {per:0.00} ms a frame, {f.Recomputes - recomputes} layouts, {f.Preview.DrawCalls} draw calls");
                Assert.Less(ms, 100f, "a frame with 500 units and a drag held took too long");
                Assert.Less(per, 4.0, "the formation's own share of a frame");
                frame.RightHeld = false;
                frame.RightUp = true;
                yield return null;
                Assert.AreEqual(256, mock.FormationCalls.Sum(k => k.Accepted), "every unit took its order");
            }
            finally { Object.Destroy(root.gameObject); }
        }
    }
}
