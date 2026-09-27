// GameCursorViewTests.cs - the pointer: sharp whole-number scaling, frames
// at their own pace, and the mock's version of the game's pick.
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class GameCursorViewTests
    {
        static RgbaImage TwoByTwo()
        {
            var img = new RgbaImage(2, 2);
            // Top left red, top right green, bottom left blue, bottom right clear.
            byte[][] px = { new byte[] { 255, 0, 0, 255 }, new byte[] { 0, 255, 0, 255 }, new byte[] { 0, 0, 255, 255 }, new byte[] { 0, 0, 0, 0 } };
            for (int i = 0; i < 4; i++) System.Array.Copy(px[i], 0, img.Pixels, i * 4, 4);
            return img;
        }

        [Test]
        public void ScalingKeepsEveryPixelWhole()
        {
            var tex = GameCursorView.Upscale(TwoByTwo(), 3);
            Assert.AreEqual(6, tex.width);
            Assert.AreEqual(6, tex.height);
            Assert.AreEqual(FilterMode.Point, tex.filterMode);
            // A texture's row 0 is its bottom, so the top left is (x, 5).
            for (int y = 3; y < 6; y++)
                for (int x = 0; x < 3; x++)
                    Assert.AreEqual(new Color32(255, 0, 0, 255), (Color32)tex.GetPixel(x, y));
            Assert.AreEqual(new Color32(0, 255, 0, 255), (Color32)tex.GetPixel(5, 5));
            Assert.AreEqual(new Color32(0, 0, 255, 255), (Color32)tex.GetPixel(0, 0));
            Assert.AreEqual(0, ((Color32)tex.GetPixel(5, 0)).a);
            Object.DestroyImmediate(tex);
        }

        [Test]
        public void TheScaleFitsTheScreen()
        {
            Assert.AreEqual(1, GameCursorView.AutoScale(720));
            Assert.AreEqual(1, GameCursorView.AutoScale(1080));
            Assert.AreEqual(2, GameCursorView.AutoScale(1440));
            Assert.AreEqual(3, GameCursorView.AutoScale(2160));
            Assert.AreEqual(4, GameCursorView.AutoScale(4320));
            Assert.AreEqual(1, GameCursorView.AutoScale(480));
        }

        [Test]
        public void FramesPlayAtTheirOwnPaceAndLoop()
        {
            var frames = new[]
            {
                new CursorFrame { Millis = 100 },
                new CursorFrame { Millis = 50 },
                new CursorFrame { Millis = 100 },
            };
            Assert.AreEqual(0, GameCursorView.FrameAt(frames, 0f));
            Assert.AreEqual(0, GameCursorView.FrameAt(frames, 0.099f));
            Assert.AreEqual(1, GameCursorView.FrameAt(frames, 0.12f));
            Assert.AreEqual(2, GameCursorView.FrameAt(frames, 0.16f));
            Assert.AreEqual(0, GameCursorView.FrameAt(frames, 0.26f));
            Assert.AreEqual(0, GameCursorView.FrameAt(new[] { new CursorFrame { Millis = 33 } }, 5f));
        }

        static CursorFrame[] TwoFrames(GameCursor c)
        {
            if (c != GameCursor.Revive) return null;
            return new[]
            {
                new CursorFrame { Image = TwoByTwo(), Hotspot = new Vector2Int(1, 0), Millis = 100 },
                new CursorFrame { Image = TwoByTwo(), Hotspot = new Vector2Int(0, 1), Millis = 100 },
            };
        }

        [Test]
        public void TheViewShowsTheFrameForTheTimeAtTheChosenScale()
        {
            var view = new GameCursorView(TwoFrames, 2);
            try
            {
                view.Show(GameCursor.Revive, 10f);
                Assert.AreEqual(0, view.Frame);
                Assert.AreEqual(4, view.Shown.width, "two pixels at twice");
                view.Show(GameCursor.Revive, 10.15f);
                Assert.AreEqual(1, view.Frame, "frames count from when the pointer came up");
                view.ScaleSetting = 3;
                view.Show(GameCursor.Revive, 10.15f);
                Assert.AreEqual(6, view.Shown.width);
                // Without art the system pointer comes back.
                view.Show(GameCursor.Attack, 11f);
                Assert.IsNull(view.Shown);
                Assert.AreEqual(GameCursor.Attack, view.Current);
            }
            finally { view.Dispose(); }
        }

        [Test]
        public void TheMockPicksAsTheGameDoes()
        {
            var b = new MockBackend { StageSeconds = 0 };
            var s = new SkirmishSetup { MapId = "mock_isles", Seed = 3 };
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Human, Side = "ARAMON", Colour = 0, Team = 0 });
            s.Seats.Add(new SeatSetup { Kind = SeatKind.Computer, Side = "TAROS", Colour = 1, Team = 1 });
            b.StartSkirmish(s);
            for (int i = 0; i < 20 && !b.PumpLoading().Done; i++) { }
            var units = new UnitState[256];
            int n = b.ReadUnits(units), mine = -1, theirs = -1;
            for (int i = 0; i < n; i++)
            {
                if (units[i].Player == b.LocalPlayer && mine < 0) mine = i;
                if (units[i].Player != b.LocalPlayer && theirs < 0) theirs = i;
            }
            Assert.IsTrue(mine >= 0 && theirs >= 0);
            var u = units[mine];
            var e = units[theirs];
            Assert.AreEqual(GameCursor.Normal, b.CursorAt(u.Position + new Vector3(3f, 0f, 0f), -1, out _));
            Assert.AreEqual(GameCursor.Select, b.CursorAt(e.Position, e.Handle, out _), "nothing selected, so no sword");
            b.Select(new[] { u.Handle }, false);
            Assert.AreEqual(GameCursor.Attack, b.CursorAt(e.Position, e.Handle, out _));
            Assert.AreEqual(GameCursor.Select, b.CursorAt(u.Position, u.Handle, out _));
            b.Arm(CommandKind.Guard);
            Assert.AreEqual(GameCursor.Guard, b.CursorAt(u.Position, -1, out _));
            b.Cancel();
            Assert.AreEqual(GameCursor.Move, GameCursors.For(CommandKind.Move));
            Assert.AreEqual(GameCursor.Place, GameCursors.For(CommandKind.Build));
        }
    }
}
