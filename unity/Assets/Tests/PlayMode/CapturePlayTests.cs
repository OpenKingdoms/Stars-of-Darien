// CapturePlayTests.cs - the owner's captures in Play: F9's picture is the
// whole screen at its size the right way up, Shift+F9's clip is sixty
// frames and a contact sheet, LATEST.txt names the newest, and F9 is free
// in the game.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.Capture;
using UnityEngine;
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

        [UnityTest]
        public IEnumerator F9SavesTheWholeScreenTheRightWayUp()
        {
            Scene();
            var cap = OwnerCapture.Ensure();
            Assert.AreSame(cap, OwnerCapture.Instance, "one, added at startup");
            yield return null;
            yield return null;
            int w = Screen.width, h = Screen.height;
            string before = cap.LastPath;
            cap.Shot();
            yield return Until(() => cap.LastPath != before, 20f);
            string path = cap.LastPath;
            Debug.Log($"Capture test: screen {w}x{h} on {SystemInfo.graphicsDeviceType}, batch {Application.isBatchMode}, error {cap.LastError}");
            Assert.IsNotNull(path, "a picture was saved: " + cap.LastError);
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
        }

        [UnityTest]
        public IEnumerator ShiftF9RecordsSixtyFramesAndAContactSheet()
        {
            Scene();
            var cap = OwnerCapture.Ensure();
            yield return null;
            int w = Screen.width, h = Screen.height;
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
            var f = new Texture2D(2, 2);
            Assert.IsTrue(f.LoadImage(File.ReadAllBytes(Path.Combine(folder, frames[30]))));
            Assert.AreEqual(new Vector2Int(w, h), new Vector2Int(f.width, f.height));
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
            string latest = Path.Combine(dir, CaptureFiles.LatestFile);
            cap.Shot();
            yield return Until(() => File.Exists(latest), 20f);
            string first = File.ReadAllText(latest).Trim();
            // A second later, so the names differ by their time.
            yield return new WaitForSecondsRealtime(1.1f);
            cap.Shot();
            yield return Until(() => File.Exists(latest) && File.ReadAllText(latest).Trim() != first, 20f);
            string second = File.ReadAllText(latest).Trim();
            Assert.AreNotEqual(first, second);
            Assert.IsTrue(File.Exists(first) && File.Exists(second));
            Assert.AreEqual(cap.LastPath, second);
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
