// CaptureTests.cs - the owner's captures: which folder, what the files are
// called, LATEST.txt, the PNG writer's orientation, and the contact sheet's
// grid, labels and width.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game.Capture;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class CaptureTests
    {
        string temp;

        [SetUp]
        public void Before()
        {
            temp = Path.Combine(Path.GetTempPath(), "oku-capture-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(temp);
        }

        [TearDown]
        public void After()
        {
            CaptureWriter.WaitIdle(10000);
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }

        // ---- Where and what ----

        [Test]
        public void TheFolderIsTheEnvironmentThenTheSettingThenTheOwnersThenTheRepositorys()
        {
            string owner = Path.Combine(temp, "OKBuild", "owner-shots");
            string repo = Path.Combine(temp, "repo");
            Assert.AreEqual("E:/env", CaptureFiles.Resolve("E:/env", "S:/set", owner, repo));
            Assert.AreEqual("S:/set", CaptureFiles.Resolve(null, "S:/set", owner, repo));
            Assert.AreEqual(Path.Combine(repo, "Captures"), CaptureFiles.Resolve(null, "", owner, repo), "no OKBuild folder on this machine");
            Directory.CreateDirectory(Path.GetDirectoryName(owner));
            Assert.AreEqual(owner, CaptureFiles.Resolve(null, "  ", owner, repo), "OKBuild is there, so owner-shots is made in it");
            Assert.AreEqual(Path.Combine(Application.persistentDataPath, "Captures"), CaptureFiles.Resolve(null, null, Path.Combine(temp, "nowhere", "at-all"), null), "a built player without OKBuild");
        }

        [Test]
        public void FilesAreNamedByTheSecond()
        {
            var at = new DateTime(2026, 9, 28, 7, 5, 9);
            Assert.AreEqual("shot-20260928-070509.png", CaptureFiles.ShotName(at));
            Assert.AreEqual("clip-20260928-070509", CaptureFiles.ClipName(at));
            Assert.AreEqual("frame-001.png", CaptureFiles.FrameName(1));
            Assert.AreEqual("frame-060.png", CaptureFiles.FrameName(60));

            string first = CaptureFiles.NewShot(temp, at);
            Assert.AreEqual(Path.Combine(temp, "shot-20260928-070509.png"), first);
            File.WriteAllText(first, "");
            Assert.AreEqual(Path.Combine(temp, "shot-20260928-070509-2.png"), CaptureFiles.NewShot(temp, at), "two in one second");

            var (folder, sheet) = CaptureFiles.NewClip(temp, at);
            Assert.AreEqual(Path.Combine(temp, "clip-20260928-070509"), folder);
            Assert.AreEqual(folder + ".png", sheet, "the sheet sits beside its frames' folder");
            Directory.CreateDirectory(folder);
            Assert.AreEqual(Path.Combine(temp, "clip-20260928-070509-2"), CaptureFiles.NewClip(temp, at).folder);
        }

        [Test]
        public void LatestNamesTheNewestCapture()
        {
            string a = Path.Combine(temp, "shot-a.png"), b = Path.Combine(temp, "shot-b.png");
            CaptureFiles.WriteLatest(temp, a);
            Assert.AreEqual(a, File.ReadAllText(Path.Combine(temp, CaptureFiles.LatestFile)).Trim());
            CaptureFiles.WriteLatest(temp, b);
            Assert.AreEqual(b, File.ReadAllText(Path.Combine(temp, CaptureFiles.LatestFile)).Trim());
            Assert.IsFalse(File.Exists(Path.Combine(temp, CaptureFiles.LatestFile + ".tmp")));
        }

        // ---- Writing ----

        // Rows from the bottom up go in, and the PNG shows them the right way
        // up: red at the bottom, blue at the top.
        [Test]
        public void AShotIsWrittenTheRightWayUpOnAWorker()
        {
            const int w = 8, h = 6;
            var px = Solid(w, h, new Color32(255, 0, 0, 255));
            for (int y = h / 2; y < h; y++) Fill(px, w, y, new Color32(0, 0, 255, 255));
            string path = Path.Combine(temp, "shot.png");
            string written = null;
            CaptureWriter.Shot(px, w, h, false, path, temp, (p, e) => { Assert.IsNull(e); written = p; });
            Assert.IsTrue(CaptureWriter.WaitIdle(10000));
            Assert.AreEqual(path, written);
            var t = Load(path);
            Assert.AreEqual(w, t.width);
            Assert.AreEqual(h, t.height);
            Assert.AreEqual(Color.red, (Color)t.GetPixel(0, 0), "bottom");
            Assert.AreEqual(Color.blue, (Color)t.GetPixel(0, h - 1), "top");
            UnityEngine.Object.DestroyImmediate(t);
            Assert.AreEqual(path, File.ReadAllText(Path.Combine(temp, CaptureFiles.LatestFile)).Trim());

            // A picture read from the top down is turned over first.
            var down = Solid(w, h, new Color32(0, 0, 255, 255));
            for (int y = h / 2; y < h; y++) Fill(down, w, y, new Color32(255, 0, 0, 255));
            CaptureWriter.Shot(down, w, h, true, path, null, null);
            Assert.IsTrue(CaptureWriter.WaitIdle(10000));
            t = Load(path);
            Assert.AreEqual(Color.red, (Color)t.GetPixel(0, 0));
            UnityEngine.Object.DestroyImmediate(t);
        }

        [Test]
        public void AClipWritesEveryFrameThenItsSheet()
        {
            var (folder, sheet) = CaptureFiles.NewClip(temp, DateTime.Now);
            string done = null;
            var clip = new ClipWriter(folder, sheet, 5, temp, (p, e) => { Assert.IsNull(e); done = p; });
            // Read-backs can arrive out of order.
            foreach (int i in new[] { 1, 0, 4, 2, 3 })
                clip.Add(i, Solid(40, 30, Palette(i)), 40, 30, false, i / 12f);
            Assert.IsTrue(CaptureWriter.WaitIdle(20000));
            Assert.AreEqual(sheet, done);
            CollectionAssert.AreEqual(Enumerable.Range(1, 5).Select(CaptureFiles.FrameName).ToArray(),
                Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n).ToArray());
            var t = Load(sheet);
            var plan = ContactSheet.PlanFor(5, 40, 30);
            Assert.AreEqual(plan.Width, t.width);
            Assert.AreEqual(plan.Height, t.height);
            for (int i = 0; i < 5; i++)
            {
                var r = plan.Thumb(i);
                Assert.AreEqual((Color)Palette(i), t.GetPixel(r.x + r.width / 2, t.height - 1 - (r.y + r.height / 2)), "frame " + (i + 1) + " in its cell");
            }
            UnityEngine.Object.DestroyImmediate(t);
            Assert.AreEqual(sheet, File.ReadAllText(Path.Combine(temp, CaptureFiles.LatestFile)).Trim());
        }

        // ---- The contact sheet ----

        [Test]
        public void ASheetOfSixtyFramesFitsInto2048PixelsWithoutOverlap()
        {
            foreach (var (w, h) in new[] { (1920, 1080), (1280, 720), (3840, 2160), (800, 1200), (320, 180) })
            {
                var p = ContactSheet.PlanFor(60, w, h);
                Assert.LessOrEqual(p.Width, ContactSheet.MaxWidth, $"{w}x{h}");
                Assert.GreaterOrEqual(p.Cols * p.Rows, 60);
                Assert.Less((p.Rows - 1) * p.Cols, 60, "no empty row");
                Assert.LessOrEqual(p.ThumbW, w, "never larger than the frame");
                Assert.AreEqual(w / (float)h, p.ThumbW / (float)p.ThumbH, 0.05f * w / h, "the frame's shape");
                var cells = Enumerable.Range(0, 60).Select(i => (p.Thumb(i), p.Label(i))).ToList();
                var sheet = new RectInt(0, 0, p.Width, p.Height);
                foreach (var (t, l) in cells)
                {
                    Assert.IsTrue(Inside(sheet, t) && Inside(sheet, l), $"{w}x{h}: {t} inside");
                    Assert.AreEqual(t.yMax, l.y, "the label under its frame");
                }
                for (int i = 0; i < cells.Count; i++)
                    for (int j = i + 1; j < cells.Count; j++)
                        Assert.IsFalse(Overlap(cells[i].Item1, cells[j].Item1) || Overlap(cells[i].Item2, cells[j].Item1), $"{w}x{h}: {i} and {j}");
            }
        }

        [Test]
        public void FramesGoLeftToRightFromTheTopWithTheirNumberAndTime()
        {
            var p = ContactSheet.PlanFor(7, 64, 36);
            Assert.AreEqual(2, p.Cols, "seven wide frames, two across");
            Assert.AreEqual(4, p.Rows);
            Assert.AreEqual(p.Thumb(0).y, p.Thumb(1).y, "one row");
            Assert.Greater(p.Thumb(2).y, p.Thumb(0).y, "the second row lower down");
            Assert.Greater(p.Thumb(1).x, p.Thumb(0).x);
            Assert.AreEqual("#01 0.00s", ContactSheet.LabelFor(0, 0f));
            Assert.AreEqual("#12 0.92s", ContactSheet.LabelFor(11, 11 / 12f));

            var thumbs = Enumerable.Range(0, 7).Select(i => Solid(p.ThumbW, p.ThumbH, Palette(i))).ToList();
            var px = ContactSheet.Compose(p, thumbs, Enumerable.Range(0, 7).Select(i => i / 12f).ToList());
            Assert.AreEqual(p.Width * p.Height * 4, px.Length);
            for (int i = 0; i < 7; i++)
            {
                var t = p.Thumb(i);
                Assert.AreEqual(Palette(i), At(px, p, t.x + t.width / 2, t.y + t.height / 2), "frame " + i);
                var l = p.Label(i);
                int ink = 0;
                for (int y = l.y; y < l.yMax; y++)
                    for (int x = l.x; x < l.xMax; x++)
                        if (At(px, p, x, y).Equals(ContactSheet.Ink)) ink++;
                Assert.Greater(ink, 40, "frame " + i + " has its label");
            }
            var last = p.Thumb(7);
            Assert.AreEqual(ContactSheet.Background, At(px, p, last.x + last.width / 2, last.y + last.height / 2), "the eighth cell is empty");
        }

        [Test]
        public void ShrinkingAveragesAndFlippingTurnsOver()
        {
            var px = new byte[4 * 2 * 4];
            // The first column red, the rest black.
            for (int i = 0; i < 8; i++) px[i * 4] = (byte)(i % 4 == 0 ? 200 : 0);
            var small = ContactSheet.Shrink(px, 4, 2, 2, 1);
            Assert.AreEqual(100, small[0], "the left half averages the red column and a black one");
            Assert.AreEqual(0, small[4]);

            var rows = new byte[] { 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3 };
            ContactSheet.FlipRows(rows, 1, 3);
            CollectionAssert.AreEqual(new byte[] { 3, 3, 3, 3, 2, 2, 2, 2, 1, 1, 1, 1 }, rows);
        }

        // ---- Helpers ----

        static Color32 Palette(int i) => new[] { new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255), new Color32(0, 0, 255, 255), new Color32(255, 255, 0, 255), new Color32(0, 255, 255, 255), new Color32(255, 0, 255, 255), new Color32(255, 255, 255, 255) }[i % 7];

        static byte[] Solid(int w, int h, Color32 c)
        {
            var px = new byte[w * h * 4];
            for (int y = 0; y < h; y++) Fill(px, w, y, c);
            return px;
        }

        static void Fill(byte[] px, int w, int row, Color32 c)
        {
            for (int x = 0; x < w; x++) { int o = (row * w + x) * 4; px[o] = c.r; px[o + 1] = c.g; px[o + 2] = c.b; px[o + 3] = c.a; }
        }

        // A pixel of the sheet by picture coordinates from the top left.
        static Color32 At(byte[] px, ContactSheet.Plan p, int x, int y)
        {
            int o = ((p.Height - 1 - y) * p.Width + x) * 4;
            return new Color32(px[o], px[o + 1], px[o + 2], px[o + 3]);
        }

        static bool Inside(RectInt outer, RectInt r) => r.xMin >= outer.xMin && r.yMin >= outer.yMin && r.xMax <= outer.xMax && r.yMax <= outer.yMax;
        static bool Overlap(RectInt a, RectInt b) => a.xMin < b.xMax && b.xMin < a.xMax && a.yMin < b.yMax && b.yMin < a.yMax;

        static Texture2D Load(string path)
        {
            var t = new Texture2D(2, 2);
            Assert.IsTrue(t.LoadImage(File.ReadAllBytes(path)), path);
            return t;
        }
    }
}
