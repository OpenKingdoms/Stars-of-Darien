// GalleryTests.cs - the Studio Gallery on the stand-in world: the grid by
// footprint with no overlaps, the filter, the header-only measure, a folder
// of models on their plinths with the monarch, stepping and picking, hot
// reload, the originals beside each, the memory budget, 800 models without
// a stall, and F9 captures of the gallery and the Studio View.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.Capture;
using OpenKingdomsUnity.Studio;
using UnityEditor;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class GalleryLayoutTests
    {
        static bool Overlap(Rect a, Rect b, float gap) =>
            a.xMin < b.xMax + gap - 1e-4f && b.xMin < a.xMax + gap - 1e-4f && a.yMin < b.yMax + gap - 1e-4f && b.yMin < a.yMax + gap - 1e-4f;

        [Test]
        public void NothingOverlapsWhateverTheSizes()
        {
            var rng = new System.Random(5);
            var sizes = Enumerable.Range(0, 300).Select(_ => new Vector2(0.5f + (float)rng.NextDouble() * (rng.Next(20) == 0 ? 14f : 3f), 0.5f + (float)rng.NextDouble() * 4f)).ToList();
            var names = sizes.Select((_, i) => "m" + i).ToList();
            var r = GalleryLayout.Arrange(names, sizes, "");
            Assert.AreEqual(300, r.Slots.Count);
            for (int i = 0; i < r.Slots.Count; i++)
            {
                Assert.AreEqual(sizes[r.Slots[i].Item], r.Slots[i].Size, "a plinth is its own footprint");
                Assert.IsTrue(r.Bounds.Contains(r.Slots[i].Rect.min) && r.Bounds.Contains(r.Slots[i].Rect.max - Vector2.one * 1e-4f), "inside the grid's bounds");
                for (int j = i + 1; j < r.Slots.Count; j++)
                    Assert.IsFalse(Overlap(r.Slots[i].Rect, r.Slots[j].Rect, GalleryLayout.Gap), $"{i} and {j} are a gap apart");
            }
        }

        [Test]
        public void ModelsAreSpacedByTheirFootprint()
        {
            var names = Enumerable.Range(0, 9).Select(i => "m" + i).ToList();
            var even = GalleryLayout.Arrange(names, names.Select(_ => new Vector2(2f, 2f)).ToList(), "");
            Assert.AreEqual(3, even.Cols);
            Assert.AreEqual(2f + GalleryLayout.Gap, even.Slots[1].Centre.x - even.Slots[0].Centre.x, 1e-4f, "footprint and gap apart");
            Assert.AreEqual(-(2f + GalleryLayout.Gap), even.Slots[3].Centre.y - even.Slots[0].Centre.y, 1e-4f, "the next row to the south");

            // One castle among soldiers widens its own column and row only.
            var sizes = names.Select(_ => new Vector2(1f, 1f)).ToList();
            sizes[4] = new Vector2(10f, 6f);
            var r = GalleryLayout.Arrange(names, sizes, "");
            float col0 = r.Slots[1].Centre.x - r.Slots[0].Centre.x, col1 = r.Slots[2].Centre.x - r.Slots[1].Centre.x;
            Assert.AreEqual(0.5f + GalleryLayout.Gap + 5f, col0, 1e-4f, "to the castle's column");
            Assert.AreEqual(5f + GalleryLayout.Gap + 0.5f, col1, 1e-4f);
            float row0 = r.Slots[0].Centre.y - r.Slots[3].Centre.y, row1 = r.Slots[3].Centre.y - r.Slots[6].Centre.y;
            Assert.AreEqual(0.5f + GalleryLayout.Gap + 3f, row0, 1e-4f, "to the castle's row");
            Assert.AreEqual(3f + GalleryLayout.Gap + 0.5f, row1, 1e-4f);
            Assert.AreEqual(r.Slots[0].Centre.x, r.Slots[3].Centre.x, 1e-4f, "columns line up");
            Assert.AreEqual(r.Slots[3].Centre.y, r.Slots[5].Centre.y, 1e-4f, "rows line up");
        }

        [Test]
        public void TheFilterKeepsMatchesInOrderInASmallerGrid()
        {
            var names = new List<string> { "AraTree01", "AraFence2", "aratree02", "ZonHut", "AraFence10", "TarTree" };
            var sizes = names.Select(_ => new Vector2(2f, 2f)).ToList();
            var all = GalleryLayout.Arrange(names, sizes, "");
            var trees = GalleryLayout.Arrange(names, sizes, "  TREE ");
            CollectionAssert.AreEqual(new[] { 0, 2, 5 }, trees.Slots.Select(s => s.Item).ToArray(), "any case, in the order given");
            Assert.AreEqual(2, trees.Cols);
            Assert.Less(trees.Bounds.width * trees.Bounds.height, all.Bounds.width * all.Bounds.height);
            Assert.AreEqual(0, GalleryLayout.Arrange(names, sizes, "castle").Slots.Count);
            Assert.AreEqual(6, GalleryLayout.Arrange(names, sizes, null).Slots.Count);

            var sorted = new List<string>(names);
            sorted.Sort(GalleryLayout.NaturalCompare);
            CollectionAssert.AreEqual(new[] { "AraFence2", "AraFence10", "AraTree01", "aratree02", "TarTree", "ZonHut" }, sorted, "Fence2 before Fence10");
        }
    }

    public class GalleryTests : StudioFixture
    {
        string models, shots, mapChoice;
        WeatherChoice weather;
        TimeOfDay time;
        StudioGallery gallery;
        long budget;

        [SetUp]
        public void Folders()
        {
            models = Path.Combine(temp, "models");
            shots = Path.Combine(temp, "shots");
            Directory.CreateDirectory(models);
            StudioCapture.DirOverride = shots;
            budget = StudioGallery.MemoryBudget;
            StudioSession.Reset();
            mapChoice = StudioSession.MapChoice;
            weather = StudioSession.Weather;
            time = StudioSession.Time;
            StudioSession.MapChoice = "";
            StudioSession.Weather = WeatherChoice.Off;
            StudioSession.Time = TimeOfDay.Game;
        }

        [TearDown]
        public void Closing()
        {
            StudioCapture.Flush();
            StudioCapture.DirOverride = null;
            StudioGallery.MemoryBudget = budget;
            gallery?.Dispose();
            gallery = null;
            StudioMode.Close(false);
            StudioSession.Reset();
            StudioSession.MapChoice = mapChoice;
            StudioSession.Weather = weather;
            StudioSession.Time = time;
        }

        // The sample well, scaled and turned by a wrapping node.
        static byte[] Well(float scale = 1f, int turns = 0, Vector3 offset = default)
        {
            var f = GlbFile.Read(File.ReadAllBytes(Sample), out _);
            f.Wrap(new StudioFix { Scale = scale, QuarterTurns = turns, Offset = offset });
            return f.Write();
        }

        string Put(string name, byte[] glb)
        {
            string path = Path.Combine(models, name + ".glb");
            File.WriteAllBytes(path, glb);
            // As if written a while ago, so it has settled.
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-5));
            return path;
        }

        StudioGallery Open(double seconds = 60)
        {
            Assert.IsTrue(StudioMode.Open(false), StudioSession.Status);
            gallery = new StudioGallery();
            gallery.Open(models);
            Assert.IsTrue(gallery.Pump(seconds), "the gallery settles: " + gallery.Status + " " + gallery.Progress?.What);
            return gallery;
        }

        static Rect WorldRect(StudioGallery.Entry e)
        {
            var p = e.Node.transform.position - StudioGallery.Origin;
            var s = e.Plinth.transform.localScale;
            return new Rect(p.x - s.x / 2, p.z - s.z / 2, s.x, s.z);
        }

        static void AssertNoOverlaps(StudioGallery g)
        {
            var rects = g.Shown.Select(WorldRect).ToList();
            rects.Add(g.MonarchRect);
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                    Assert.IsFalse(rects[i].Overlaps(rects[j]), $"plinths {i} and {j} overlap: {rects[i]} {rects[j]}");
        }

        [Test]
        public void TheHeaderGivesTheBoundsTheLoaderWillHave()
        {
            foreach (var (scale, turns, offset) in new[] { (1f, 0, Vector3.zero), (2.5f, 1, new Vector3(1f, 0.5f, -2f)), (0.5f, 3, new Vector3(-3f, 0f, 4f)) })
            {
                string path = Put("w", Well(scale, turns, offset));
                Assert.IsTrue(GlbBounds.Read(path, out var header, out var error), error);
                var m = StudioModel.Load(path, out error);
                Assert.IsNull(error);
                try
                {
                    Assert.AreEqual(m.Facts.Bounds.min.x, header.min.x, 1e-3f, $"x{scale} {turns}");
                    Assert.AreEqual(m.Facts.Bounds.max.x, header.max.x, 1e-3f);
                    Assert.AreEqual(m.Facts.Bounds.min.y, header.min.y, 1e-3f);
                    Assert.AreEqual(m.Facts.Bounds.max.y, header.max.y, 1e-3f);
                    Assert.AreEqual(m.Facts.Bounds.min.z, header.min.z, 1e-3f, "south is -z");
                    Assert.AreEqual(m.Facts.Bounds.max.z, header.max.z, 1e-3f);
                }
                finally { m.Dispose(); }
            }
            File.WriteAllText(Path.Combine(models, "junk.glb"), "not a model");
            Assert.IsFalse(GlbBounds.Read(Path.Combine(models, "junk.glb"), out _, out var why));
            Assert.IsNotNull(why);
        }

        [Test]
        public void EveryModelStandsOnItsOwnPlinthWithItsNameAndTheMonarch()
        {
            for (int i = 1; i <= 12; i++) Put($"Well{i:00}", Well(i == 5 ? 4f : 0.6f + i * 0.1f, i % 4));
            File.WriteAllText(Path.Combine(models, "Broken.glb"), "not a model");
            File.WriteAllText(Path.Combine(models, "notes.txt"), "not a glb either");
            var g = Open();
            Assert.AreEqual(13, g.Entries.Count, "every .glb, and only those");
            CollectionAssert.AreEqual(new[] { "Broken" }.Concat(Enumerable.Range(1, 12).Select(i => $"Well{i:00}")).ToArray(), g.Shown.Select(e => e.Name).ToArray(), "by name");
            Assert.IsTrue(g.Entries.Where(e => e.Name != "Broken").All(e => e.Loaded), "all loaded");
            var broken = g.Entries.Single(e => e.Name == "Broken");
            Assert.IsTrue(broken.Failed);
            Assert.AreEqual("Broken (did not load)", broken.Label.GetComponent<TextMesh>().text);
            Assert.AreEqual("Well07", g.Entries.Single(e => e.Name == "Well07").Label.GetComponent<TextMesh>().text);
            Assert.IsNull(g.Progress, "nothing left to load");
            AssertNoOverlaps(g);

            // Game scale: the model's own size, standing on the plinth.
            var big = g.Entries.Single(e => e.Name == "Well05");
            var small = g.Entries.Single(e => e.Name == "Well01");
            Assert.AreEqual(4f * 2f, big.Bounds.size.y, 0.01f, "the well is two cells tall, times four");
            Assert.Greater(WorldRect(big).width, WorldRect(small).width * 2.5f, "a plinth as big as its model's footprint");
            var view = new RenderTexture(640, 360, 24);
            Assert.IsTrue(g.Render(view));
            var rs = big.Model.GetComponentsInChildren<Renderer>().Select(r => r.bounds).ToList();
            float bottom = rs.Min(b => b.min.y);
            Assert.AreEqual(StudioGallery.PlinthHeight, bottom, 0.02f, "on the plinth, not in it");
            Assert.Greater(big.Label.transform.position.y, rs.Max(b => b.max.y), "the name floats over the model");
            var label = big.Label.GetComponent<Renderer>().localBounds;
            Assert.AreEqual(StudioGallery.LabelSize, label.size.y, StudioGallery.LabelSize * 0.6f, "a readable size in cells");
            view.Release();
            UnityEngine.Object.DestroyImmediate(view);

            // The monarch for scale, west of the first row.
            Assert.IsNotNull(g.Monarch);
            Assert.IsTrue(g.MonarchIsStandIn, "the stand-in world's 4-cell monarch");
            Assert.Less(g.MonarchRect.xMax, WorldRect(g.Shown[0]).xMin, "west of the first plinth");
            Assert.AreEqual(WorldRect(g.Shown[0]).center.y, g.MonarchRect.center.y, 0.01f, "in the first row");

            // Next and previous step in the grid's order and wrap round.
            g.Step(1);
            Assert.AreEqual("Broken", g.Selected.Name);
            g.Step(1);
            Assert.AreEqual("Well01", g.Selected.Name);
            g.Step(-2);
            Assert.AreEqual("Well12", g.Selected.Name, "wraps to the end");
            g.Pump(2);
            Assert.AreEqual(g.Selected.Centre.x, g.View.Focus.x, 0.01f, "flew to it");

            // A click on a model picks it, in either view.
            foreach (bool classic in new[] { true, false })
            {
                g.SetClassic(classic);
                // Alone in the last row, so nothing stands between it and the camera.
                var want = g.Entries.Single(e => e.Name == "Well12");
                g.Select(want);
                g.Pump(2);
                var size = new Vector2Int(640, 360);
                var rt = new RenderTexture(size.x, size.y, 24);
                Assert.IsTrue(g.Render(rt));
                var vp = g.Camera.WorldToViewportPoint(want.Centre + Vector3.up * 0.6f);
                Assert.AreSame(want, g.Pick(g.RayAt(new Vector2(vp.x * size.x, (1f - vp.y) * size.y), size)), classic ? "classic" : "free");
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }

            // The search box narrows the grid, and the rest stand aside.
            g.SetFilter("well1");
            CollectionAssert.AreEqual(new[] { "Well10", "Well11", "Well12" }, g.Shown.Select(e => e.Name).ToArray());
            Assert.IsFalse(g.Entries.Single(e => e.Name == "Well05").Node.activeSelf);
            AssertNoOverlaps(g);
            g.SetFilter("");
            Assert.AreEqual(13, g.Shown.Count);
            g.Overview();
            g.Pump(2);
            Assert.IsNull(g.Selected);
        }

        [Test]
        public void AModelChangedOnDiskReloadsWhereItStands()
        {
            for (int i = 1; i <= 6; i++) Put($"Well{i:00}", Well());
            var g = Open();
            var three = g.Entries.Single(e => e.Name == "Well03");
            var before = three.Model;
            float height = three.Bounds.size.y;

            Put("Well03", Well(3f));
            Put("Well07", Well(0.5f));
            File.Delete(Path.Combine(models, "Well01.glb"));
            Assert.IsTrue(g.CheckFolder());
            Assert.IsTrue(g.Pump(30));

            Assert.AreSame(three, g.Entries.Single(e => e.Name == "Well03"), "the same place in the gallery");
            Assert.IsTrue(three.Loaded);
            Assert.AreNotSame(before, three.Model, "loaded again");
            Assert.IsTrue(before == null, "the old one is gone");
            Assert.AreEqual(height * 3f, three.Bounds.size.y, 0.01f, "measured again");
            Assert.IsTrue(g.Entries.Any(e => e.Name == "Well07" && e.Loaded), "a new file comes in");
            Assert.IsFalse(g.Entries.Any(e => e.Name == "Well01"), "a deleted one goes");
            CollectionAssert.AreEqual(new[] { "Well02", "Well03", "Well04", "Well05", "Well06", "Well07" }, g.Shown.Select(e => e.Name).ToArray());
            AssertNoOverlaps(g);

            // A file still being written waits until it settles.
            File.WriteAllBytes(Path.Combine(models, "Well08.glb"), Well());
            g.CheckFolder();
            Assert.IsFalse(g.Entries.Any(e => e.Name == "Well08"));
        }

        [Test]
        public void TheOriginalsStandBesideTheirModels()
        {
            Put("mock_rock", Well());
            Put("aramon_knight", Well(0.8f));
            Put("zz_not_in_the_game", Well());
            var g = Open();
            var widths = g.Shown.ToDictionary(e => e.Name, e => e.Slot.Size.x);
            g.SetCompare(true);
            Assert.IsTrue(g.Pump(30), g.Progress?.What);
            var rock = g.Entries.Single(e => e.Name == "mock_rock");
            var knight = g.Entries.Single(e => e.Name == "aramon_knight");
            var none = g.Entries.Single(e => e.Name == "zz_not_in_the_game");
            Assert.IsNotNull(rock.Original, "the rock's picture as the game draws it");
            Assert.IsNotNull(knight.Original, "the knight's own model");
            Assert.IsTrue(rock.Original.activeInHierarchy && knight.Original.activeInHierarchy);
            Assert.IsNull(none.Original);
            Assert.AreEqual("zz_not_in_the_game (no original)", none.Label.GetComponent<TextMesh>().text);
            Assert.Greater(rock.Slot.Size.x, widths["mock_rock"], "the plinth takes both");
            Assert.AreEqual(widths["zz_not_in_the_game"], none.Slot.Size.x, 1e-4f);
            foreach (var e in new[] { rock, knight })
                Assert.Greater(e.Original.transform.position.x + e.OriginalCentre.x, e.Spin.transform.position.x + e.Bounds.center.x, e.Name + ": the original to the east");
            AssertNoOverlaps(g);

            g.SetCompare(false);
            Assert.IsFalse(knight.Original.activeInHierarchy);
            Assert.AreEqual(widths["aramon_knight"], knight.Slot.Size.x, 1e-4f);
        }

        [Test]
        public void TheNearestLoadFirstWithinTheMemoryBudget()
        {
            for (int i = 1; i <= 36; i++) Put($"Well{i:00}", Well());
            Assert.IsTrue(StudioMode.Open(false), StudioSession.Status);
            gallery = new StudioGallery();
            StudioGallery.MemoryBudget = 250_000;
            gallery.Open(models);
            gallery.Pump(3);
            var g = gallery;
            int loaded = g.LoadedCount;
            Assert.That(loaded, Is.InRange(1, 12), "only what fits");
            Assert.LessOrEqual(g.LoadedBytes, StudioGallery.MemoryBudget);
            var focus = g.View.Focus;
            float farthestLoaded = g.Entries.Where(e => e.Loaded).Max(e => (e.Centre - focus).sqrMagnitude);
            float nearestWaiting = g.Entries.Where(e => !e.Loaded).Min(e => (e.Centre - focus).sqrMagnitude);
            Assert.LessOrEqual(farthestLoaded, nearestWaiting + 1e-3f, "nearest first");
            Assert.IsTrue(g.Progress.HasValue, "the bar says the rest wait");

            // Flying to the far corner loads it and lets the far side go.
            var corner = g.Shown.Last();
            var first = g.Entries.Where(e => e.Loaded).OrderBy(e => (e.Centre - focus).sqrMagnitude).First();
            g.Select(corner);
            g.Pump(3);
            Assert.IsTrue(corner.Loaded);
            Assert.IsFalse(first.Loaded || (first.Centre - corner.Centre).sqrMagnitude < 30f, "the far side was let go");
            Assert.LessOrEqual(g.LoadedBytes, StudioGallery.MemoryBudget);
        }

        [Test]
        public void EightHundredModelsLoadWithoutAStall()
        {
            var glb = Well(0.7f);
            for (int i = 1; i <= 800; i++) Put($"Model{i:000}", glb);
            Assert.IsTrue(StudioMode.Open(false), StudioSession.Status);
            gallery = new StudioGallery();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            gallery.Open(models);
            double open = clock.Elapsed.TotalSeconds;
            var seen = new HashSet<string>();
            gallery.Changed += () => { if (gallery.Progress.HasValue) seen.Add(gallery.Progress.Value.What); };
            Assert.IsTrue(gallery.Pump(600), "settled: " + gallery.Progress?.What);
            Debug.Log($"Gallery: 800 models in {clock.Elapsed.TotalSeconds:0.0} s, opening {open * 1000:0} ms, longest frame {gallery.LongestTick * 1000:0} ms, {gallery.LoadedBytes / 1e6:0.0} MB");
            Assert.AreEqual(800, gallery.Shown.Count);
            Assert.AreEqual(800, gallery.LoadedCount);
            Assert.Less(open, 1.0, "opening hands the reading to a worker");
            Assert.Less(gallery.LongestTick, 1.0, "no editor frame over a second");
            Assert.IsTrue(seen.Contains("Setting out the plinths") || seen.Contains("Reading the files"), string.Join(", ", seen));
            Assert.IsTrue(seen.Any(s => s.StartsWith("Loading the models")), string.Join(", ", seen));
            AssertNoOverlaps(gallery);
        }

        // The gallery on a real folder, such as the player's own generated
        // models, read and never written. Runs only when OKU_GALLERY_DIR names
        // one, and saves its pictures in OKU_GALLERY_SHOTS when that is set.
        [Test]
        public void ARealFolderLoadsWithoutAStall()
        {
            string dir = Environment.GetEnvironmentVariable("OKU_GALLERY_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("set OKU_GALLERY_DIR to time the gallery on a real folder");
            string pictures = Environment.GetEnvironmentVariable("OKU_GALLERY_SHOTS");
            Assert.IsTrue(StudioMode.Open(false), StudioSession.Status);
            gallery = new StudioGallery();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            gallery.Open(dir);
            bool idle = gallery.Pump(900);
            Debug.Log($"Gallery on {dir}: {gallery.Entries.Count} models, {gallery.LoadedCount} loaded, {gallery.Entries.Count(e => e.Failed)} failed, idle {idle}, " +
                $"{clock.Elapsed.TotalSeconds:0.0} s, longest frame {gallery.LongestTick * 1000:0} ms, {gallery.LoadedBytes / 1e6:0} MB");
            Assert.Less(gallery.LongestTick, 1.0, "no editor frame over a second");
            AssertNoOverlaps(gallery);
            if (string.IsNullOrEmpty(pictures)) return;
            StudioCapture.DirOverride = pictures;
            var size = new Vector2Int(1600, 900);
            StudioCapture.Shot(null, gallery.Render, size);
            gallery.Step(1);
            gallery.Pump(3);
            StudioCapture.Shot(null, gallery.Render, size);
            gallery.SetClassic(false);
            gallery.Step(10);
            gallery.Pump(3);
            StudioCapture.Shot(null, gallery.Render, size);
            gallery.SetCompare(true);
            gallery.Pump(120);
            gallery.Select(gallery.Selected);
            gallery.Pump(3);
            StudioCapture.Shot(null, gallery.Render, size);
            Assert.IsTrue(StudioCapture.Flush());
        }

        // Pictures of the gallery on copies of the sample, for looking at.
        // Runs only when OKU_GALLERY_SHOTS names a folder.
        [Test]
        public void CapturesOfTheGallery()
        {
            string pictures = Environment.GetEnvironmentVariable("OKU_GALLERY_SHOTS");
            if (string.IsNullOrEmpty(pictures)) Assert.Ignore("set OKU_GALLERY_SHOTS to capture the gallery");
            for (int i = 1; i <= 30; i++) Put($"Well{i:00}", Well(i % 7 == 0 ? 2.5f : 0.5f + i % 5 * 0.2f, i % 4));
            Put("mock_rock", Well());
            Put("aramon_knight", Well(0.8f));
            var g = Open();
            StudioCapture.DirOverride = pictures;
            var size = new Vector2Int(1600, 900);
            StudioCapture.Shot(null, g.Render, size);
            g.Select(g.Entries.Single(e => e.Name == "Well07"));
            g.Pump(3);
            StudioCapture.Shot(null, g.Render, size);
            g.SetClassic(false);
            g.Pump(3);
            StudioCapture.Shot(null, g.Render, size);
            g.SetCompare(true);
            g.Pump(30);
            g.Select(g.Entries.Single(e => e.Name == "aramon_knight"));
            g.Pump(3);
            StudioCapture.Shot(null, g.Render, size);
            g.Overview();
            g.Pump(3);
            StudioCapture.Shot(null, g.Render, size);
            Assert.IsTrue(StudioCapture.Flush());
        }

        // The overview in the free view from low down: sky over ground.
        [Test]
        public void F9SavesTheGalleryAsDrawnTheRightWayUp()
        {
            for (int i = 1; i <= 4; i++) Put($"Well{i:00}", Well());
            var g = Open();
            g.SetClassic(false);
            var v = g.View;
            v.Focus = g.Shown[0].Centre + Vector3.up;
            v.Pitch = 4f;
            v.Yaw = 0f;
            v.Distance = 12f;
            g.View = v;
            string path = StudioCapture.Shot(null, g.Render, new Vector2Int(480, 270));
            Assert.IsNotNull(path);
            Assert.IsTrue(StudioCapture.Flush());
            Assert.AreEqual(path, StudioCapture.LastPath);
            StringAssert.StartsWith(Path.Combine(shots, "shot-"), path);
            var t = new Texture2D(2, 2);
            Assert.IsTrue(t.LoadImage(File.ReadAllBytes(path)));
            Assert.AreEqual(480, t.width);
            Assert.AreEqual(270, t.height);
            float Blueness(int y0, int y1)
            {
                float sum = 0;
                int n = 0;
                for (int y = y0; y < y1; y++)
                    for (int x = 0; x < t.width; x += 8) { var c = t.GetPixel(x, y); sum += c.b - c.g; n++; }
                return sum / n;
            }
            Assert.Greater(Blueness(t.height * 3 / 4, t.height), Blueness(0, t.height / 4) + 0.02f, "sky at the top, grass at the bottom");
            UnityEngine.Object.DestroyImmediate(t);
            Assert.AreEqual(path, File.ReadAllText(Path.Combine(shots, CaptureFiles.LatestFile)).Trim());
        }

        [Test]
        public void TheStudioViewSavesAShotAndAClipWithItsSheet()
        {
            Assert.IsTrue(StudioMode.Open(false), StudioSession.Status);
            StudioSession.LoadModel(Sample);
            bool Draw(RenderTexture rt) { StudioSession.Stage.Render(rt, StudioView.ClassicDefault); return true; }
            string shot = StudioCapture.Shot(null, Draw, new Vector2Int(400, 225));
            Assert.IsTrue(StudioCapture.Flush());
            Assert.IsTrue(File.Exists(shot));
            var t = new Texture2D(2, 2);
            t.LoadImage(File.ReadAllBytes(shot));
            Assert.AreEqual(new Vector2Int(400, 225), new Vector2Int(t.width, t.height));
            UnityEngine.Object.DestroyImmediate(t);

            Assert.IsTrue(StudioCapture.StartClip(null, Draw, new Vector2Int(320, 180)));
            Assert.IsTrue(StudioCapture.Recording);
            double t0 = EditorApplication.timeSinceStartup;
            for (int i = 0; i < 80 && StudioCapture.Recording; i++) StudioCapture.TickAt(t0 + i / 12.0 + 0.001);
            Assert.IsFalse(StudioCapture.Recording, "sixty frames taken");
            Assert.IsTrue(StudioCapture.Flush());
            string sheet = StudioCapture.LastPath;
            StringAssert.EndsWith(".png", sheet);
            string folder = sheet.Substring(0, sheet.Length - 4);
            Assert.AreEqual(OwnerCapture.ClipFrames, Directory.GetFiles(folder, "frame-*.png").Length);
            Assert.IsTrue(File.Exists(Path.Combine(folder, "frame-060.png")));
            var s = new Texture2D(2, 2);
            s.LoadImage(File.ReadAllBytes(sheet));
            var plan = ContactSheet.PlanFor(OwnerCapture.ClipFrames, 320, 180);
            Assert.AreEqual(plan.Width, s.width);
            Assert.LessOrEqual(s.width, 2048);
            UnityEngine.Object.DestroyImmediate(s);
            Assert.AreEqual(sheet, File.ReadAllText(Path.Combine(shots, CaptureFiles.LatestFile)).Trim());
        }
    }
}
