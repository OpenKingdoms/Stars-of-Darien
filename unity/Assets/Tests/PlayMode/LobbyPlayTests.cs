// LobbyPlayTests.cs - the lobby's screens draw and fit at 720p, 1080p and
// 4K, the map filters read clearly at each, a click on a start jewel takes that start and the battle begins
// there, the map browser narrows and orders the list, and a room's host
// moves another seat's start from the seat row.
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Tests
{
    public class LobbyPlayTests
    {
        GameRoot root;
        Camera uiCam;
        RenderTexture rt;

        [TearDown]
        public void CleanUp()
        {
            MenuScreens.SizeOverride = null;
            FadeIn.Off = false;
            if (root != null) Object.Destroy(root.gameObject);
            if (uiCam != null) Object.Destroy(uiCam.gameObject);
            if (rt != null) rt.Release();
        }

        IEnumerator Boot(int w, int h)
        {
            MenuScreens.SizeOverride = new Vector2Int(w, h);
            FadeIn.Off = true;
            root = GameRoot.Boot(new MockBackend { StageSeconds = 0f, DamageScale = 0f, ArtDir = null });
            yield return null;
        }

        // Draws the menus into a picture of w by h, as a window that size would.
        IEnumerator ViewAt(int w, int h)
        {
            if (rt != null) rt.Release();
            rt = new RenderTexture(w, h, 24);
            if (uiCam == null)
            {
                uiCam = new GameObject("UI camera").AddComponent<Camera>();
                uiCam.clearFlags = CameraClearFlags.SolidColor;
                uiCam.backgroundColor = Color.black;
            }
            uiCam.targetTexture = rt;
            var canvas = root.Screens.Canvas;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = uiCam;
            canvas.planeDistance = 1f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
        }

        static Rect WorldRect(RectTransform r)
        {
            var c = new Vector3[4];
            r.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        // Every gadget lies on the screen, text is at least 12 pixels and
        // none is cut off, and the picture is not blank.
        void CheckFits(string screen, int w, int h)
        {
            var go = root.Screens.Screen(screen);
            Assert.IsTrue(go.activeInHierarchy, screen + " is up");
            var canvas = root.Screens.Canvas;
            var box = WorldRect((RectTransform)canvas.transform);
            float slack = box.width * 0.002f;
            box = Rect.MinMaxRect(box.xMin - slack, box.yMin - slack, box.xMax + slack, box.yMax + slack);
            int texts = 0;
            foreach (var g in go.GetComponentsInChildren<Graphic>(false))
            {
                if (g.name == "Margin" || g.name == "Dim" || g.name == "Light" || g.name == "Plaque" || g.name.EndsWith("Glow")) continue;
                var r = WorldRect(g.rectTransform);
                if (g is Text t)
                {
                    if (string.IsNullOrEmpty(t.text)) continue;
                    texts++;
                    float px = t.fontSize * canvas.scaleFactor;
                    Assert.GreaterOrEqual(px, 11.9f, $"{screen} at {w}x{h}: '{t.text}' is {px:0.0} px");
                    Assert.LessOrEqual(t.preferredHeight, t.rectTransform.rect.height * 1.08f + 1f, $"{screen} at {w}x{h}: '{t.text}' is cut off");
                }
                if (g.name == "Band" || g.name == "Knot" || g.name == "Boss") continue;
                Assert.IsTrue(box.Contains(r.min) && box.Contains(r.max), $"{screen} at {w}x{h}: {g.name} ({r}) lies off the screen ({box})");
            }
            Assert.Greater(texts, 3, screen + " has its words");

            RenderTexture.active = rt;
            uiCam.Render();
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            var px32 = tex.GetPixels32();
            Object.Destroy(tex);
            double sum = 0, sum2 = 0;
            for (int i = 0; i < px32.Length; i += 97)
            {
                float l = (px32[i].r + px32[i].g + px32[i].b) / 3f;
                sum += l; sum2 += l * l;
            }
            int n = (px32.Length + 96) / 97;
            double mean = sum / n, varc = sum2 / n - mean * mean;
            Assert.Greater(varc, 150.0, $"{screen} at {w}x{h} draws a picture, not a flat fill");
        }

        static readonly Vector2Int[] Sizes = { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(3840, 2160) };

        [UnityTest]
        public IEnumerator EveryLobbyScreenDrawsAndFitsAtEverySize()
        {
            foreach (var s in Sizes)
            {
                yield return Boot(s.x, s.y);
                yield return ViewAt(s.x, s.y);
                CheckFits("Menu", s.x, s.y);
                root.Flow.Fire(FlowEvent.OpenSkirmish);
                root.Setup.MapId = "mock_marches";
                root.Screens.Show(FlowState.Skirmish);
                yield return ViewAt(s.x, s.y);
                CheckFits("Skirmish", s.x, s.y);
                root.Flow.Fire(FlowEvent.Back);
                root.Flow.Fire(FlowEvent.OpenMultiplayer);
                var rooms = root.Backend.Rooms;
                rooms.Connect("mock://relay", "Zach");
                yield return null;
                yield return null;
                root.Screens.Show(FlowState.Multiplayer);
                root.Screens.Lobby.Net.List.Selected = 1;
                root.Screens.Lobby.Net.Refresh();
                yield return ViewAt(s.x, s.y);
                CheckFits("Multiplayer", s.x, s.y);
                Assert.IsTrue(rooms.CreateRoom("", "mock_marches", RoomRules.LineOfSight));
                root.Flow.Fire(FlowEvent.EnterRoom);
                yield return null;
                yield return null;
                root.Screens.Show(FlowState.Room);
                yield return ViewAt(s.x, s.y);
                CheckFits("Room", s.x, s.y);
                root.Flow.Fire(FlowEvent.ChooseMap);
                yield return ViewAt(s.x, s.y);
                CheckFits("MapChoice", s.x, s.y);
                Object.Destroy(root.gameObject);
                root = null;
                yield return null;
            }
        }

        // WCAG's contrast ratio of text over a well drawn on white, the
        // lightest page it could sit on, blended in linear light.
        static float Contrast(Color text, Color well)
        {
            Color back = well.linear * well.a + Color.white * (1f - well.a);
            Color front = text.linear * text.a + back * (1f - text.a);
            float a = Luma(front) + 0.05f, b = Luma(back) + 0.05f;
            return Mathf.Max(a, b) / Mathf.Min(a, b);
        }

        static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        void CheckReadable(Text t, Color well, string at)
        {
            float px = t.fontSize * root.Screens.Canvas.scaleFactor;
            Assert.GreaterOrEqual(px, 15f, $"{at}: '{t.text}' is {px:0.0} px");
            Assert.AreNotSame(LobbyInk.Caps, t.font, $"{at}: '{t.text}' is in mixed case, not small capitals");
            Assert.LessOrEqual(t.preferredWidth, t.rectTransform.rect.width, $"{at}: '{t.text}' fits on one line");
            Assert.LessOrEqual(t.preferredHeight, t.rectTransform.rect.height, $"{at}: '{t.text}' fits its height");
            Assert.GreaterOrEqual(Contrast(t.color, well), 4.5f, $"{at}: '{t.text}' stands out from its well");
        }

        // Every choice of every filter and the search's hint, and no box
        // over another or over the list.
        void CheckFilters(LobbyScreens.MapBrowser b, string at)
        {
            var boxes = new List<(string name, Rect r)>();
            var search = b.Search.GetComponent<Image>();
            CheckReadable((Text)b.Search.placeholder, search.color, at);
            boxes.Add(("Search", WorldRect(search.rectTransform)));
            foreach (var (c, n) in new[] { (b.Players, 8), (b.Size, 5), (b.Sort, 6) })
            {
                var well = c.transform.parent.GetComponent<Image>();
                Assert.IsNotNull(well, $"{at}: {c.name} sits in a well");
                boxes.Add((c.name, WorldRect(well.rectTransform)));
                for (int i = 0; i < n; i++)
                {
                    CheckReadable(c.Label, well.color, at);
                    c.Step(1);
                }
            }
            var list = WorldRect((RectTransform)b.List.transform);
            for (int i = 0; i < boxes.Count; i++)
            {
                Assert.IsFalse(boxes[i].r.Overlaps(list), $"{at}: {boxes[i].name} over the list");
                for (int j = i + 1; j < boxes.Count; j++)
                    Assert.IsFalse(boxes[i].r.Overlaps(boxes[j].r), $"{at}: {boxes[i].name} over {boxes[j].name}");
            }
        }

        [UnityTest]
        public IEnumerator TheMapFiltersReadClearlyAtEverySize()
        {
            foreach (var s in Sizes)
            {
                yield return Boot(s.x, s.y);
                root.Flow.Fire(FlowEvent.OpenSkirmish);
                root.Screens.Show(FlowState.Skirmish);
                yield return ViewAt(s.x, s.y);
                CheckFilters(root.Screens.Lobby.Skirmish.Browser, $"skirmish at {s.x}x{s.y}");
                root.Flow.Fire(FlowEvent.Back);
                root.Flow.Fire(FlowEvent.OpenMultiplayer);
                var rooms = root.Backend.Rooms;
                rooms.Connect("mock://relay", "Zach");
                yield return null;
                yield return null;
                Assert.IsTrue(rooms.CreateRoom("", "mock_marches", RoomRules.LineOfSight));
                root.Flow.Fire(FlowEvent.EnterRoom);
                yield return null;
                yield return null;
                root.Flow.Fire(FlowEvent.ChooseMap);
                yield return ViewAt(s.x, s.y);
                CheckFilters(root.Screens.Lobby.Choice.Browser, $"map choice at {s.x}x{s.y}");
                Object.Destroy(root.gameObject);
                root = null;
                yield return null;
            }
        }

        static void Click(GameObject go, PointerEventData.InputButton button = PointerEventData.InputButton.Left)
        {
            var e = new PointerEventData(EventSystem.current) { button = button };
            ExecuteEvents.Execute(go, e, ExecuteEvents.pointerClickHandler);
        }

        [UnityTest]
        public IEnumerator AClickOnAStartJewelTakesItAndTheBattleBeginsThere()
        {
            yield return Boot(1920, 1080);
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_crossing";
            root.Screens.Show(FlowState.Skirmish);
            yield return null;
            var sk = root.Screens.Lobby.Skirmish;
            Assert.AreEqual(6, sk.Map.Markers.Count);
            Click(sk.Map.Markers[4].gameObject);
            Assert.AreEqual(4, root.Setup.Seats[0].Start, "the fifth jewel is yours");
            Click(sk.Map.Markers[4].gameObject);
            Assert.AreEqual(-1, root.Setup.Seats[0].Start, "a second click gives it back");
            Click(sk.Map.Markers[4].gameObject);
            var map = root.CurrentMap();
            root.Screens.StartGame();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
            var units = new UnitState[512];
            int n = root.Backend.ReadUnits(units);
            var home = new Vector2(map.Starts[4].x, -map.Starts[4].y);
            var computer = new Vector2(map.Starts[0].x, -map.Starts[0].y);
            float mine = float.MaxValue, theirs = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var at = new Vector2(units[i].Position.x, units[i].Position.z);
                if (units[i].Player == root.Backend.LocalPlayer) mine = Mathf.Min(mine, (at - home).magnitude);
                else theirs = Mathf.Min(theirs, (at - computer).magnitude);
            }
            Assert.Less(mine, 12f, "your army stands at the start you took");
            Assert.Less(theirs, 12f, "the computer took the first start left");
        }

        [UnityTest]
        public IEnumerator TheBrowserSearchesFiltersAndSorts()
        {
            yield return Boot(1920, 1080);
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            yield return null;
            var b = root.Screens.Lobby.Skirmish.Browser;
            int all = root.Backend.Maps.Count;
            Assert.AreEqual(all, b.Shown.Count);
            b.Search.text = "fen";
            Assert.AreEqual(1, b.Shown.Count);
            Assert.AreEqual("Seven Fens", b.Shown[0].Name);
            int showing = 0;
            foreach (var row in b.List.Rows) if (row.gameObject.activeSelf) showing++;
            Assert.AreEqual(1, showing, "one row shows");
            b.Search.text = "";
            for (int i = 0; i < 7; i++) Click(b.Players.gameObject);
            Assert.AreEqual("8 players", b.Players.Label.text);
            CollectionAssert.AreEquivalent(new[] { "Border Marches", "Wide Steppe" }, b.Shown.ConvertAll(m => m.Name));
            Click(b.Players.gameObject);
            Click(b.Size.gameObject);
            Assert.AreEqual("Small", b.Size.Label.text);
            foreach (var m in b.Shown) Assert.AreEqual(MapSize.Small, MapCatalog.SizeOf(m));
            Click(b.Size.gameObject, PointerEventData.InputButton.Right);
            for (int i = 0; i < 5; i++) Click(b.Sort.gameObject);
            Assert.AreEqual("Largest", b.Sort.Label.text);
            Assert.AreEqual("Wide Steppe", b.Shown[0].Name);
            int top = b.List.Top;
            Click(b.List.Rows[1].gameObject);
            Assert.AreEqual(b.Shown[top + 1].Id, root.Setup.MapId, "a click on a row picks its map");
        }

        [UnityTest]
        public IEnumerator TheHostMovesAnotherSeatFromItsRow()
        {
            yield return Boot(1920, 1080);
            root.Flow.Fire(FlowEvent.OpenMultiplayer);
            var rooms = root.Backend.Rooms;
            rooms.Connect("mock://relay", "Zach");
            yield return null;
            Assert.IsTrue(rooms.CreateRoom("", "mock_crossing", RoomRules.LineOfSight));
            root.Flow.Fire(FlowEvent.EnterRoom);
            yield return null;
            yield return null;
            int guest = rooms.Room.Seats.FindIndex(s => s.Name == MockRooms.GuestName);
            Assert.Greater(guest, 0);
            root.Screens.Lobby.RoomScreen.Refresh();
            var row = root.Screens.Screen("Room").GetComponentsInChildren<Clicker>();
            Clicker start = null;
            int seen = 0;
            foreach (var c in row) if (c.name == "Start" && seen++ == guest) start = c;
            Assert.IsNotNull(start, "the guest's row has a start");
            Click(start.gameObject);
            Assert.AreEqual(0, rooms.Room.Seats[guest].Start, "the host moved the guest to the first start");
            var map = root.Screens.Lobby.RoomScreen.Map;
            Click(map.Markers[0].gameObject);
            Assert.AreEqual(0, rooms.Room.Seats[0].Start, "the host's click on the guest's jewel swaps them");
            Assert.AreEqual(-1, rooms.Room.Seats[guest].Start);
            // Ready, then the battle loads on the room's map.
            rooms.StartMatch();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, root.LastError);
            Assert.AreEqual("mock_crossing", root.Setup.MapId);
        }
    }
}
