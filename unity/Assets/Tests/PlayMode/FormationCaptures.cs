// FormationCaptures.cs - pictures of the formation drag on the mock: the
// preview in each shape, snapped slots at a shore, the readout, and the
// formation the units march into. Runs only when OKU_FORMATION_SHOTS names
// a folder, and writes formation-*.png there.
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Tests
{
    public class FormationCaptures
    {
        GameRoot root;
        MockBackend mock;
        PointerFrame frame;
        Text readout;
        Image readoutBack;

        [UnityTest]
        public IEnumerator CaptureTheFormationDrag()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_FORMATION_SHOTS");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_FORMATION_SHOTS to capture the formation drag");
            Directory.CreateDirectory(dir);
            mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f, ExtraSoldiers = 16 };
            root = GameRoot.Boot(mock);
            try
            {
                yield return null;
                root.Flow.Fire(FlowEvent.OpenSkirmish);
                root.Setup.MapId = "mock_highlands";
                root.Setup.MapRevealed = true;
                root.Screens.StartGame();
                float deadline = Time.realtimeSinceStartup + 60f;
                while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.AreEqual(FlowState.Playing, root.Flow.State);
                root.World.Atmosphere.SetWeather(WeatherChoice.Off);
                root.Orders.Classic = false;
                var f = root.Orders.Formation;
                f.Shape = FormationShape.Line;
                f.Pace = true;
                frame = new PointerFrame { Focused = true, Dpi = 96 };
                f.Source = () =>
                {
                    var r = frame;
                    frame.RightDown = frame.RightUp = false;
                    return r;
                };
                MakeReadout();

                // Everyone the local player has that walks.
                var units = Units();
                var mine = units.Where(u => u.Player == mock.LocalPlayer && !mock.UnitDefs[u.Def].IsBuilding).Select(u => u.Handle).ToArray();
                mock.Select(mine, false);
                root.Orders.Selected.Clear();
                root.Orders.Selected.UnionWith(mine);
                var c = units.Where(u => mine.Contains(u.Handle)).Aggregate(Vector3.zero, (s, u) => s + u.Position) / mine.Length;
                var a = new Vector2(c.x - 11, c.z + 13);
                var b = new Vector2(c.x + 11, c.z + 13);
                var view = new Vector2(c.x, c.z + 4);
                Look(view, 50f, 60f);
                for (int i = 0; i < 20; i++) yield return null;

                yield return Drag(a, b);
                foreach (var shape in new[] { FormationShape.Line, FormationShape.Block, FormationShape.Wedge, FormationShape.Loose })
                {
                    f.Shape = shape;
                    for (int i = 0; i < 3; i++) yield return null;
                    yield return Shoot(Path.Combine(dir, $"formation-preview-{shape.ToString().ToLowerInvariant()}.png"));
                }
                f.Shape = FormationShape.Line;
                frame.Alt = true;
                for (int i = 0; i < 3; i++) yield return null;
                yield return Shoot(Path.Combine(dir, "formation-preview-as-they-stand.png"));
                frame.Alt = false;
                for (int i = 0; i < 3; i++) yield return null;

                // Released: the order goes, and they march into their places.
                frame.RightHeld = false;
                frame.RightUp = true;
                yield return null;
                for (int t = 0; t < 30 * 15; t++) mock.Advance(1);
                Look(new Vector2(c.x, c.z + 10), 40f, 60f);
                for (int i = 0; i < 10; i++) yield return null;
                yield return Shoot(Path.Combine(dir, "formation-result.png"));

                // A queued formation's faint markers, and a new drag beyond them.
                var a2 = a + new Vector2(3, 10);
                var b2 = b + new Vector2(-3, 10);
                Look(new Vector2(c.x, c.z + 20), 50f, 60f);
                for (int i = 0; i < 5; i++) yield return null;
                yield return Drag(a2, b2);
                frame.Shift = true;
                frame.RightHeld = false;
                frame.RightUp = true;
                yield return null;
                frame.Shift = false;
                yield return Drag(a2 + new Vector2(-2, 10), b2 + new Vector2(2, 14));
                yield return Shoot(Path.Combine(dir, "formation-queued.png"));
                root.Orders.Formation.Abort();

                // At a shore, facing the water: slots on the water step back onto land.
                var (shore, into) = Shore(new Vector2(c.x, c.z));
                var along = new Vector2(into.y, -into.x);
                var s0 = shore + into * 1.5f - along * 10f;
                var s1 = shore + into * 1.5f + along * 10f;
                Look(shore - into * 4f, 36f, 60f);
                // The haze plain past the map lies at the edge's height and
                // would cover a lake lower than that.
                var haze = GameObject.Find("Haze plain");
                if (haze != null) haze.SetActive(false);
                for (int i = 0; i < 10; i++) yield return null;
                yield return Drag(s0, s1);
                var g = f.Layers[0];
                int good = 0, snapped = 0, nowhere = 0;
                for (int i = 0; i < g.Count; i++)
                {
                    if (g.Slots[i].State == SlotState.Good) good++;
                    else if (g.Slots[i].State == SlotState.Snapped) snapped++;
                    else nowhere++;
                }
                yield return Shoot(Path.Combine(dir, "formation-preview-snapped.png"));
                root.Orders.Formation.Abort();
                File.WriteAllText(Path.Combine(dir, "formation.txt"),
                    $"units {mine.Length}, shore {shore} toward {into}, slots {good} good, {snapped} snapped, {nowhere} nowhere, last order {mock.LastFormation?.Units.Length} units heading {mock.LastFormation?.Heading}");
            }
            finally { Object.Destroy(root.gameObject); }
        }

        UnitState[] Units()
        {
            var u = new UnitState[1024];
            return u.Take(mock.ReadUnits(u)).ToArray();
        }

        // The deep water nearest a point, then the first point toward it
        // where a walker stops, and the way into the water from there.
        (Vector2 shore, Vector2 into) Shore(Vector2 from)
        {
            var t = mock.Terrain;
            var deep = from;
            float best = float.MaxValue;
            for (int y = 0; y < (int)t.Size.y; y++)
                for (int x = 0; x < (int)t.Size.x; x++)
                {
                    var q = new Vector2(x + 0.5f, -(y + 0.5f));
                    float d = (q - from).sqrMagnitude;
                    if (d < best && t.Sample(q.x, q.y) < t.SeaLevel - 1f) { best = d; deep = q; }
                }
            var dir = (deep - from).normalized;
            for (float s = 0; s < 400; s += 0.25f)
            {
                var q = from + dir * s;
                if (t.Sample(q.x, q.y) < t.SeaLevel - 0.3f) return (q, dir);
            }
            return (deep, dir);
        }

        Camera Cam => root.World.Camera.GetComponent<Camera>();

        Vector2 ScreenOf(Vector2 g) => Cam.WorldToScreenPoint(new Vector3(g.x, mock.GroundHeight(g.x, g.y), g.y));

        void Look(Vector2 at, float distance, float pitch)
        {
            var c = root.World.Camera;
            c.focus = new Vector3(at.x, mock.GroundHeight(at.x, at.y), at.y);
            c.yaw = 0;
            c.pitch = pitch;
            c.Zoom(distance);
        }

        IEnumerator Drag(Vector2 from, Vector2 to)
        {
            frame.Screen = ScreenOf(from);
            frame.RightDown = frame.RightHeld = true;
            yield return null;
            for (int i = 1; i <= 8; i++)
            {
                frame.Screen = ScreenOf(Vector2.Lerp(from, to, i / 8f));
                yield return null;
            }
            for (int i = 0; i < 3; i++) yield return null;
        }

        // The readout as OnGUI draws it beside the pointer, rebuilt on the
        // canvas because OnGUI does not reach a camera's picture.
        void MakeReadout()
        {
            var canvas = root.GetComponentInChildren<Canvas>();
            var back = new GameObject("formation readout", typeof(RectTransform), typeof(Image));
            back.transform.SetParent(canvas.transform, false);
            readoutBack = back.GetComponent<Image>();
            readoutBack.color = new Color(0f, 0f, 0f, 0.55f);
            readoutBack.raycastTarget = false;
            var rt = readoutBack.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(620, 62);
            readout = UiKit.Label(back.transform, "", 21, new Color(0.92f, 1f, 0.9f), TextAnchor.UpperLeft);
            var tr = readout.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(10, 4);
            tr.offsetMax = new Vector2(-8, -5);
            back.SetActive(false);
        }

        // The camera into a picture, with the readout beside the line's end
        // as OnGUI draws it beside the pointer.
        IEnumerator Shoot(string path)
        {
            var f = root.Orders.Formation;
            readout.text = f.Live ? f.ReadoutText : "";
            readoutBack.gameObject.SetActive(f.Live);
            yield return null;
            const int W = 1600, H = 900;
            var cam = Camera.main;
            var canvas = root.GetComponentInChildren<Canvas>();
            var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
            var mode = canvas.renderMode;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = cam.nearClipPlane + 0.1f;
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            var end = f.LineTo;
            Vector2 sp = cam.WorldToScreenPoint(new Vector3(end.x, mock.GroundHeight(end.x, end.y), end.y));
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, sp + new Vector2(22, -18), cam, out var local))
            {
                // Kept on the picture, as OnGUI keeps it on the screen.
                var half = ((RectTransform)canvas.transform).rect.size * 0.5f;
                var size = readoutBack.rectTransform.sizeDelta;
                local.x = Mathf.Min(local.x, half.x - size.x - 8);
                local.y = Mathf.Max(local.y, -half.y + size.y + 8);
                readoutBack.rectTransform.localPosition = local;
            }
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = old;
            canvas.renderMode = mode;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
