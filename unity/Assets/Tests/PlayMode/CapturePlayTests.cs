// CapturePlayTests.cs - the owner's captures in Play: F9's picture is the
// whole screen at its size the right way up without the note, Shift+F9's
// clip is sixty frames and a contact sheet, LATEST.txt names the newest,
// and F9 is free in the game. In batch mode the cameras are drawn again,
// and run with a window the screen itself is read, which is how the owner
// takes them. A picture in the screen's own format, which Metal can't read
// back, still reads back in its true colours.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.Capture;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Tests
{
    public class CapturePlayTests
    {
        string dir;
        GameObject scene;

        [SetUp]
        public void Before()
        {
            dir = Path.Combine(Path.GetTempPath(), "oku-capture-play-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            OwnerCapture.DirOverride = dir;
        }

        [TearDown]
        public void After()
        {
            CaptureWriter.WaitIdle(20000);
            OwnerCapture.DirOverride = null;
            if (scene != null) UnityEngine.Object.Destroy(scene);
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }

        // A blue sky from a camera and a red band over the top of the screen
        // on an overlay canvas, as the game's menus draw.
        void Scene()
        {
            scene = new GameObject("Capture test");
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.transform.SetParent(scene.transform, false);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 1f);
            cam.depth = 100;
            var canvas = new GameObject("Canvas").AddComponent<Canvas>();
            canvas.transform.SetParent(scene.transform, false);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var band = new GameObject("Band").AddComponent<Image>();
            band.transform.SetParent(canvas.transform, false);
            band.color = Color.red;
            var r = band.rectTransform;
            r.anchorMin = new Vector2(0f, 0.75f);
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        static IEnumerator Until(Func<bool> done, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < end) yield return null;
        }

        // Where the note's dark box is, clear of its words: x across, y down
        // from the top of the screen.
        const int NoteX = 40, NoteY = 15;

        static Color NoteSpot(Texture2D t) => t.GetPixel(NoteX, t.height - 1 - NoteY);

        static void AssertRoute(OwnerCapture cap)
        {
            Debug.Log($"Capture test: route {cap.LastRoute}, screen {Screen.width}x{Screen.height} on {SystemInfo.graphicsDeviceType}, batch {Application.isBatchMode}, " +
                $"upside down {OwnerCapture.FoundFlipped}, uv starts at top {SystemInfo.graphicsUVStartsAtTop}, " +
                $"default format {SystemInfo.GetGraphicsFormat(DefaultFormat.LDR)}, read back as {OwnerCapture.ReadableFormat}");
            Assert.AreEqual(Application.isBatchMode ? OwnerCapture.CamerasRoute : OwnerCapture.ScreenRoute, cap.LastRoute, "the screen is read whenever there is one");
        }

        [UnityTest]
        public IEnumerator F9SavesTheWholeScreenTheRightWayUp()
        {
            Scene();
            var cap = OwnerCapture.Ensure();
            Assert.AreSame(cap, OwnerCapture.Instance, "one, added at startup");
            yield return null;
            yield return null;
            int w = OwnerCapture.CaptureSize.x, h = OwnerCapture.CaptureSize.y;
            if (Screen.width > 0) Assert.AreEqual(new Vector2Int(Screen.width, Screen.height), OwnerCapture.CaptureSize, "the screen's own size");
            string before = cap.LastPath;
            cap.Shot();
            yield return Until(() => cap.LastPath != before, 20f);
            string path = cap.LastPath;
            Debug.Log($"Capture test: screen {Screen.width}x{Screen.height} on {SystemInfo.graphicsDeviceType}, batch {Application.isBatchMode}, error {cap.LastError}");
            Assert.IsNotNull(path, "a picture was saved: " + cap.LastError);
            AssertRoute(cap);
            if (!Application.isBatchMode) Assert.GreaterOrEqual(OwnerCapture.FoundFlipped, 0, "the first picture found which way up the screen comes back");
            StringAssert.StartsWith(Path.Combine(dir, "shot-"), path);
            StringAssert.EndsWith(".png", path);
            var t = new Texture2D(2, 2);
            Assert.IsTrue(t.LoadImage(File.ReadAllBytes(path)));
            Assert.AreEqual(w, t.width, "the screen's width");
            Assert.AreEqual(h, t.height, "the screen's height");
            var top = t.GetPixel(w / 2, h - 3);
            var bottom = t.GetPixel(w / 2, 3);
            Assert.Greater(top.r, 0.8f, $"the red band at the top, {top}");
            Assert.Less(top.b, 0.2f, $"{top}");
            Assert.Greater(bottom.b, 0.8f, $"the blue sky at the bottom, {bottom}");
            Assert.Less(bottom.r, 0.2f, $"{bottom}");
            UnityEngine.Object.Destroy(t);
            Assert.AreEqual(path, File.ReadAllText(Path.Combine(dir, CaptureFiles.LatestFile)).Trim());
            StringAssert.Contains(path, cap.Toast ?? "", "the note says where it went");

            // A second picture while that note is up leaves the note out.
            yield return null;
            if (!Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                var seen = ScreenCapture.CaptureScreenshotAsTexture();
                var dark = NoteSpot(seen);
                UnityEngine.Object.Destroy(seen);
                Assert.Less(dark.r, 0.6f, $"the note is on the screen itself, {dark}");
            }
            Assert.IsNotNull(cap.Toast);
            yield return new WaitForSecondsRealtime(1.1f);
            string first = path;
            cap.Shot();
            yield return Until(() => cap.LastPath != first, 20f);
            Assert.AreNotEqual(first, cap.LastPath, cap.LastError);
            var t2 = new Texture2D(2, 2);
            Assert.IsTrue(t2.LoadImage(File.ReadAllBytes(cap.LastPath)));
            Assert.AreEqual(new Vector2Int(w, h), new Vector2Int(t2.width, t2.height));
            var spot = NoteSpot(t2);
            UnityEngine.Object.Destroy(t2);
            Assert.Greater(spot.r, 0.8f, $"the red band where the note was, {spot}");
            Assert.Less(spot.g, 0.2f, $"{spot}");
            AssertRoute(cap);
        }

        [UnityTest]
        public IEnumerator ShiftF9RecordsSixtyFramesAndAContactSheet()
        {
            Scene();
            var cap = OwnerCapture.Ensure();
            yield return null;
            int w = OwnerCapture.CaptureSize.x, h = OwnerCapture.CaptureSize.y;
            string before = cap.LastPath;
            float start = Time.realtimeSinceStartup;
            cap.StartClip();
            Assert.IsTrue(cap.Recording);
            yield return Until(() => !cap.Recording, 30f);
            Assert.IsFalse(cap.Recording, "the clip ended");
            Assert.That(Time.realtimeSinceStartup - start, Is.InRange(4.5f, 7.5f), "about five seconds");
            yield return Until(() => cap.LastPath != before && cap.LastPath != null && cap.LastPath.Contains("clip-"), 60f);
            string sheet = cap.LastPath;
            Assert.IsNotNull(sheet, "the sheet was saved: " + cap.LastError);
            StringAssert.Contains(Path.Combine(dir, "clip-"), sheet);
            string folder = sheet.Substring(0, sheet.Length - ".png".Length);
            var frames = Directory.GetFiles(folder, "frame-*.png").Select(Path.GetFileName).OrderBy(n => n).ToArray();
            Assert.AreEqual(60, frames.Length, "twelve a second for five seconds");
            Assert.AreEqual("frame-001.png", frames[0]);
            Assert.AreEqual("frame-060.png", frames[59]);
            AssertRoute(cap);
            var f = new Texture2D(2, 2);
            foreach (int i in new[] { 0, 30 })
            {
                Assert.IsTrue(f.LoadImage(File.ReadAllBytes(Path.Combine(folder, frames[i]))));
                Assert.AreEqual(new Vector2Int(w, h), new Vector2Int(f.width, f.height));
                var spot = NoteSpot(f);
                Assert.Greater(spot.r, 0.8f, $"{frames[i]} has no note in it, {spot}");
                Assert.Greater(f.GetPixel(w / 2, 3).b, 0.8f, $"{frames[i]} the right way up");
            }
            UnityEngine.Object.Destroy(f);
            var s = new Texture2D(2, 2);
            Assert.IsTrue(s.LoadImage(File.ReadAllBytes(sheet)));
            var plan = ContactSheet.PlanFor(60, w, h);
            Assert.AreEqual(plan.Width, s.width);
            Assert.AreEqual(plan.Height, s.height);
            Assert.LessOrEqual(s.width, 2048);
            // The first frame, top left, is red over blue like the screen.
            var t0 = plan.Thumb(0);
            var upper = s.GetPixel(t0.x + t0.width / 2, s.height - 1 - (t0.y + 2));
            var lower = s.GetPixel(t0.x + t0.width / 2, s.height - 1 - (t0.yMax - 3));
            Assert.Greater(upper.r, 0.7f, $"{upper}");
            Assert.Greater(lower.b, 0.7f, $"{lower}");
            UnityEngine.Object.Destroy(s);
            Assert.AreEqual(sheet, File.ReadAllText(Path.Combine(dir, CaptureFiles.LatestFile)).Trim(), "LATEST.txt names the sheet");
        }

        [UnityTest]
        public IEnumerator LatestFollowsTheNewestCapture()
        {
            Scene();
            var cap = OwnerCapture.Ensure();
            yield return null;
            cap.Shot();
            yield return Until(() => CaptureFiles.ReadLatest(dir) != null, 20f);
            string first = CaptureFiles.ReadLatest(dir);
            Assert.IsNotNull(first, cap.LastError);
            // A second later, so the names differ by their time. Read all the
            // while, as someone waiting for the next picture would.
            yield return new WaitForSecondsRealtime(1.1f);
            cap.Shot();
            yield return Until(() => CaptureFiles.ReadLatest(dir) is string now && now != first, 20f);
            string second = CaptureFiles.ReadLatest(dir);
            Assert.AreNotEqual(first, second);
            Assert.IsTrue(File.Exists(first) && File.Exists(second));
            // The game hears of it on its next frame.
            yield return Until(() => cap.LastPath == second, 5f);
            Assert.AreEqual(second, cap.LastPath);
        }

        // The keys as Update hears them: F9 alone, Shift+F9, and a second
        // Shift+F9 while one is recording.
        [UnityTest]
        public IEnumerator F9TakesAPictureAndShiftF9AClip()
        {
            Scene();
            var cap = OwnerCapture.Ensure();
            yield return null;
            Assert.IsNull(cap.Handle(false, false));
            Assert.IsNull(cap.Handle(false, true));
            Assert.IsFalse(cap.Recording);
            string before = cap.LastPath;
            Assert.AreEqual("shot", cap.Handle(true, false));
            yield return Until(() => cap.LastPath != before, 20f);
            StringAssert.StartsWith(Path.Combine(dir, "shot-"), cap.LastPath, cap.LastError);
            Assert.AreEqual("clip", cap.Handle(true, true));
            Assert.IsTrue(cap.Recording);
            yield return null;
            cap.Handle(true, true);
            Assert.AreEqual("Already recording a clip.", cap.Toast);
            yield return Until(() => !cap.Recording, 30f);
            yield return Until(() => cap.LastPath != null && cap.LastPath.Contains("clip-"), 60f);
            StringAssert.Contains(Path.Combine(dir, "clip-"), cap.LastPath, cap.LastError);
            Assert.AreEqual(1, Directory.GetDirectories(dir, "clip-*").Length, "one clip");
        }

        // Leaving Play mid-clip still writes the sheet from the frames taken.
        [UnityTest]
        public IEnumerator AClipCutShortStillGetsItsSheet()
        {
            Scene();
            var cap = OwnerCapture.Ensure();
            yield return null;
            // A picture first, so the clip's second isn't spent on what the
            // first capture of a run sets up.
            string before = cap.LastPath;
            cap.Shot();
            yield return Until(() => cap.LastPath != before, 20f);
            cap.StartClip();
            yield return new WaitForSecondsRealtime(1f);
            Assert.IsTrue(cap.Recording);
            UnityEngine.Object.Destroy(cap.gameObject);
            yield return null;
            Assert.IsTrue(OwnerCapture.Instance == null);
            Assert.IsTrue(CaptureWriter.WaitIdle(20000));
            string sheet = CaptureFiles.ReadLatest(dir);
            Assert.IsNotNull(sheet, "LATEST.txt names the sheet");
            StringAssert.Contains(Path.Combine(dir, "clip-"), sheet);
            Assert.IsTrue(File.Exists(sheet));
            string folder = sheet.Substring(0, sheet.Length - ".png".Length);
            int n = Directory.GetFiles(folder, "frame-*.png").Length;
            Assert.That(n, Is.InRange(5, 20), "about a second of frames");
            OwnerCapture.Ensure();
        }

        // A picture in the default format, which is the screen's own and on
        // Metal can't be read back, reads back in its own colours.
        [UnityTest]
        public IEnumerator APictureInTheDefaultFormatReadsBackInItsColours()
        {
            var colour = new Color32(200, 40, 10, 255);
            var src = new Texture2D(16, 8, TextureFormat.RGBA32, false);
            src.SetPixels32(Enumerable.Repeat(colour, 16 * 8).ToArray());
            src.Apply();
            var screen = new RenderTexture(16, 8, 0);
            var format = screen.graphicsFormat;
            Graphics.Blit(src, screen);
            RenderTexture.active = null;
            var rt = OwnerCapture.Readable(screen);
            Debug.Log($"Capture test: {format} read back as {rt.graphicsFormat}, copied {rt != screen}");
            Assert.IsTrue(SystemInfo.IsFormatSupported(rt.graphicsFormat, GraphicsFormatUsage.ReadPixels), $"{rt.graphicsFormat} can be read back");
            var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
            yield return Until(() => req.done, 10f);
            Assert.IsTrue(req.done && !req.hasError, "read back");
            var px = req.GetData<byte>().ToArray();
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(src);
            for (int i = 0; i < px.Length; i += 4)
            {
                var got = new Color32(px[i], px[i + 1], px[i + 2], px[i + 3]);
                Assert.That(Mathf.Abs(got.r - colour.r) <= 2 && Mathf.Abs(got.g - colour.g) <= 2 && Mathf.Abs(got.b - colour.b) <= 2, $"texel {i / 4} is {got}, not {colour}");
            }
        }

        // The screen read back top first or bottom first, told apart against
        // a picture kept bottom first, and a picture the same both ways left
        // undecided.
        [Test]
        public void WhichWayUpTheScreenComesBackIsFoundOnce()
        {
            const int w = 8, h = 8;
            var check = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var rows = new Color32[w * h];
            for (int i = 0; i < rows.Length; i++) rows[i] = i / w < h / 2 ? new Color32(255, 0, 0, 255) : new Color32(0, 0, 255, 255);
            check.SetPixels32(rows);
            check.Apply();
            var bottomFirst = new byte[w * h * 4];
            for (int i = 0; i < rows.Length; i++) { bottomFirst[i * 4] = rows[i].r; bottomFirst[i * 4 + 2] = rows[i].b; bottomFirst[i * 4 + 3] = 255; }
            var topFirst = (byte[])bottomFirst.Clone();
            ContactSheet.FlipRows(topFirst, w, h);
            Assert.AreEqual(0, OwnerCapture.Flipped(bottomFirst, w, h, check));
            Assert.AreEqual(1, OwnerCapture.Flipped(topFirst, w, h, check));
            var plain = new byte[w * h * 4];
            var grey = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var greys = new Color32[w * h];
            for (int i = 0; i < greys.Length; i++) greys[i] = new Color32(128, 128, 128, 255);
            grey.SetPixels32(greys);
            grey.Apply();
            Assert.AreEqual(-1, OwnerCapture.Flipped(plain, w, h, grey), "a plain screen can't tell");
            UnityEngine.Object.Destroy(check);
            UnityEngine.Object.Destroy(grey);
        }

        // Keys.tdf gives F12 to ClearChat and leaves F9 for screenshots, and
        // the remaster's battle keys leave it alone too.
        [Test]
        public void F9IsFreeInTheGame()
        {
            Assert.AreEqual(KeyCode.F9, OwnerCapture.Key);
            var battle = (KeyCode[])typeof(GameRoot).GetField("BattleKeys", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            CollectionAssert.DoesNotContain(battle, OwnerCapture.Key);
        }
    }
}
