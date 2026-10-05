// SummonsEnginePlayTests.cs - Ctrl on a Beast Tamer's Harpy card on the
// real engine, in both schemes. The one click places the summons and the
// Harpies come from that spot without end, each lifting off it while the
// next frame goes up under it. Needs okengine and the game files, and is
// ignored without them.
using System.Collections;
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
    public class SummonsEnginePlayTests
    {
        GameRoot root;
        EngineBackend engine;
        PointerFrame frame;
        readonly UnitState[] units = new UnitState[1024];

        [TearDown]
        public void CleanUp()
        {
            BattleHud.CtrlKey = () => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
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
            root.Setup.Seats[0].Side = "ZHON";
            root.Setup.Seats[1].Difficulty = AiDifficulty.Easy;
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 240f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, "the battle loaded: " + root.LastError);
            // The battle stands still and the test moves it on.
            root.Options.GameSpeed = 0;
            frame = new PointerFrame { Focused = true, Dpi = 96 };
            root.Orders.Formation.Source = () =>
            {
                var r = frame;
                frame.LeftDown = frame.LeftUp = frame.RightDown = frame.RightUp = false;
                return r;
            };
        }

        int DefNamed(string name)
        {
            var defs = engine.UnitDefs;
            for (int i = 0; i < defs.Count; i++)
                if (string.Equals(defs[i].Name, name, System.StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        UnitState Read(int handle)
        {
            int n = engine.ReadUnits(units);
            for (int i = 0; i < n; i++) if (units[i].Handle == handle) return units[i];
            Assert.Fail("unit " + handle + " is gone");
            return default;
        }

        bool TryProgress(int handle, out float progress)
        {
            int n = engine.ReadUnits(units);
            for (int i = 0; i < n; i++)
                if (units[i].Handle == handle) { progress = units[i].BuildProgress; return true; }
            progress = 0f;
            return false;
        }

        int Made(int def)
        {
            int n = engine.ReadUnits(units), made = 0;
            for (int i = 0; i < n; i++)
                if (units[i].Player == engine.LocalPlayer && units[i].Def == def && units[i].BuildProgress >= 1f) made++;
            return made;
        }

        Camera Cam => root.World.Camera.GetComponent<Camera>();

        IEnumerator Card(string name, System.Action<Button> found)
        {
            Button card = null;
            float deadline = Time.realtimeSinceStartup + 5f;
            while (card == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                var builds = GameObject.Find("Builds");
                if (builds != null)
                    foreach (var x in builds.GetComponentsInChildren<Button>()) if (x.name == "Build " + name) card = x;
            }
            Assert.IsNotNull(card, "the HUD shows " + name);
            found(card);
        }

        IEnumerator ClickAt(Vector3 at)
        {
            var cam = root.World.Camera;
            cam.focus = at;
            cam.yaw = 0f;
            cam.pitch = GameCamera.ClassicPitch;
            cam.Zoom(30f);
            for (int i = 0; i < 3; i++) yield return null;
            frame.Screen = Cam.WorldToScreenPoint(at);
            yield return null;
            yield return null;
            frame.LeftDown = frame.LeftHeld = true;
            yield return null;
            frame.LeftHeld = false;
            frame.LeftUp = true;
            yield return null;
            yield return null;
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator CtrlOnABeastTamersHarpyCardSummonsWithoutEndInTheClassicScheme() => Summons(true);

        [UnityTest, Timeout(1800000)]
        public IEnumerator CtrlOnABeastTamersHarpyCardSummonsWithoutEndInTheModernScheme() => Summons(false);

        IEnumerator Summons(bool classic)
        {
            yield return Begin();
            int me = engine.LocalPlayer;
            int tamer = DefNamed("ZONTRAIN"), harpy = DefNamed("ZONHARP"), lode = DefNamed("ZONMANA");
            if (tamer < 0 || harpy < 0) Assert.Ignore("no Beast Tamer or Harpy in these game files");
            Assert.Contains(harpy, engine.UnitDefs[tamer].BuildOptions, "the Beast Tamer summons Harpies");
            root.Orders.Classic = classic;

            // Thirsha's 5000 mana pays for two Harpies, and a Divine
            // Lodestone on each sacred site near the start for the rest.
            int lodes = 0;
            while (lodes < 4 && lode >= 0 && OkEngine.okx_place_unit(lode, me) >= 0) lodes++;
            int t = OkEngine.okx_place_unit(tamer, me);
            Assert.GreaterOrEqual(t, 0, "a Beast Tamer set down");
            engine.Advance(2);
            engine.Select(new[] { t }, false);
            root.Orders.Selected.Clear();
            root.Orders.Selected.Add(t);

            Button card = null;
            yield return Card(engine.UnitDefs[harpy].Name, b => card = b);
            BattleHud.CtrlKey = () => true;
            card.onClick.Invoke();
            BattleHud.CtrlKey = () => false;
            Assert.AreEqual(CommandKind.Build, root.Orders.Armed);
            Assert.IsTrue(root.Orders.ArmedRepeat, "Ctrl arms a summons");

            var from = Read(t).Position;
            Vector3 site = from;
            bool open = false;
            for (float r = 6; r <= 40 && !open; r += 2)
                for (int k = 0; k < 8 && !open; k++)
                    open = engine.CanBuildAt(harpy, from + Quaternion.Euler(0, k * 45f, 0) * new Vector3(r, 0, 0), 0, out site);
            Assert.IsTrue(open, "open ground for a Harpy");
            site.y = engine.GroundHeight(site.x, site.z);
            int before = Made(harpy);
            yield return ClickAt(site);
            Assert.IsNull(root.Orders.Armed, "placed once");
            engine.Advance(2);
            Assert.AreEqual(harpy, engine.RepeatOf(t), "the Beast Tamer summons Harpies without end");

            // The bug left a Harpy hovering over the spot when its step off
            // fell short of its ring. The builder looked again every 20
            // ticks for a site the Harpy kept, and gave up after 30 looks.
            int made = 0, gap = 0, worst = 0;
            bool waiting = false;
            for (int tick = 0; tick < 60 * 480 && made < 6; tick += 5)
            {
                engine.Advance(5);
                int now = Made(harpy) - before;
                if (now > made) { made = now; waiting = true; gap = 0; }
                if (!waiting) continue;
                int next = engine.ReadOrder(t).Building;
                if (next >= 0 && TryProgress(next, out float done) && done < 1f) waiting = false;
                else worst = Mathf.Max(worst, gap += 5);
            }
            Debug.Log($"{(classic ? "classic" : "modern")}: {made} Harpies, the longest wait for the next frame {worst} ticks, {lodes} Divine Lodestones");
            Assert.GreaterOrEqual(made, 6, "one Harpy after another");
            Assert.LessOrEqual(worst, 60, "the next frame goes up as the last Harpy leaves");
            Assert.AreEqual(harpy, engine.RepeatOf(t), "and the summons goes on");
            Assert.AreEqual(OrderKind.Build, engine.ReadOrder(t).Kind);
        }
    }
}
