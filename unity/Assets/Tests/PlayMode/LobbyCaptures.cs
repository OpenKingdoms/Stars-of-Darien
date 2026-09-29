// LobbyCaptures.cs - pictures of every lobby screen at 720p, 1080p and 4K,
// drawn off screen, with the original's art (OKU_ART_DIR, the game's anims
// folder) and with the painted stand-in. Runs only when OKU_LOBBY_CAPTURE_DIR
// names a folder.
using System.Collections;
using System.IO;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class LobbyCaptures
    {
        [TearDown]
        public void CleanUp()
        {
            MenuScreens.SizeOverride = null;
            FadeIn.Off = false;
        }

        // The menus alone, drawn by a camera at the origin, where the float
        // precision keeps every edge on its pixel.
        static IEnumerator Shoot(GameRoot root, int w, int h, string path)
        {
            var canvas = root.Screens.Canvas;
            var cam = new GameObject("Lobby capture camera").AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            var mode = canvas.renderMode;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply(false);
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
            canvas.renderMode = mode;
            canvas.worldCamera = null;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Object.Destroy(cam.gameObject);
        }

        [UnityTest]
        public IEnumerator CaptureEveryLobbyScreen()
        {
            string dir = System.Environment.GetEnvironmentVariable("OKU_LOBBY_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_LOBBY_CAPTURE_DIR to capture the lobby");
            Directory.CreateDirectory(dir);
            string artDir = System.Environment.GetEnvironmentVariable("OKU_ART_DIR");
            FadeIn.Off = true;
            foreach (var look in string.IsNullOrEmpty(artDir) ? new[] { "painted" } : new[] { "art", "painted" })
                foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(3840, 2160) })
                {
                    MenuScreens.SizeOverride = size;
                    var root = GameRoot.Boot(new MockBackend { StageSeconds = 0f, ArtDir = look == "art" ? artDir : null });
                    yield return null;
                    string tag = $"{size.y}p-{look}";
                    yield return Shoot(root, size.x, size.y, Path.Combine(dir, $"menu-{tag}.png"));

                    root.Flow.Fire(FlowEvent.OpenSkirmish);
                    var s = root.Setup;
                    s.MapId = "mock_marches";
                    root.Screens.Show(FlowState.Skirmish);
                    s.Seats[1].Difficulty = AiDifficulty.Hard;
                    s.Seats[2].Kind = SeatKind.Computer; s.Seats[2].Side = "VERUNA"; s.Seats[2].Team = 1;
                    s.Seats[3].Kind = SeatKind.Computer; s.Seats[3].Side = "ZHON";
                    s.Seats[0].Side = "ARAMON";
                    StartPositions.Claim(s.Seats, 0, 2, 8);
                    root.Screens.Show(FlowState.Skirmish);
                    yield return null;
                    yield return Shoot(root, size.x, size.y, Path.Combine(dir, $"skirmish-{tag}.png"));
                    var b = root.Screens.Lobby.Skirmish.Browser;
                    b.Search.text = "e";
                    var q = root.Screens.Lobby.Skirmish.Browser;
                    for (int i = 0; i < 5; i++) q.Sort.Step(1);
                    yield return null;
                    yield return Shoot(root, size.x, size.y, Path.Combine(dir, $"skirmish-search-{tag}.png"));
                    b.Search.text = "";
                    for (int i = 0; i < 1; i++) q.Sort.Step(1);

                    root.Flow.Fire(FlowEvent.Back);
                    root.Flow.Fire(FlowEvent.OpenMultiplayer);
                    var rooms = root.Backend.Rooms;
                    rooms.Connect("mock://relay", "Zach");
                    yield return null;
                    yield return null;
                    root.Screens.Lobby.Net.List.Selected = 1;
                    root.Screens.Show(FlowState.Multiplayer);
                    yield return null;
                    yield return Shoot(root, size.x, size.y, Path.Combine(dir, $"multiplayer-{tag}.png"));

                    rooms.CreateRoom("", "mock_marches", RoomRules.LineOfSight | RoomRules.MapRevealed);
                    root.Flow.Fire(FlowEvent.EnterRoom);
                    yield return null;
                    yield return null;
                    rooms.Edit(RoomEdit.Start, -1, 6);
                    int guest = rooms.Room.Seats.FindIndex(x => x.Name == MockRooms.GuestName);
                    rooms.Edit(RoomEdit.MoveStart, guest, 1);
                    rooms.Edit(RoomEdit.AddComputer, 3, 0);
                    rooms.Chat("Veruna it is. I will take the north.");
                    root.Screens.Show(FlowState.Room);
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return Shoot(root, size.x, size.y, Path.Combine(dir, $"room-{tag}.png"));

                    root.Flow.Fire(FlowEvent.ChooseMap);
                    yield return null;
                    yield return Shoot(root, size.x, size.y, Path.Combine(dir, $"mapchoice-{tag}.png"));
                    Object.Destroy(root.gameObject);
                    yield return null;
                    yield return null;
                }
        }
    }
}
