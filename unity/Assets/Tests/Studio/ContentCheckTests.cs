// ContentCheckTests.cs - the build's content check: which models may ship,
// that carved folders and the original's own files fail it, and that every
// hand-built model in the project passes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OpenKingdomsUnity.Studio;
using UnityEngine;

namespace OpenKingdomsUnity.Studio.Tests
{
    public class ContentCheckTests
    {
        string temp;

        [SetUp]
        public void Before()
        {
            temp = Path.Combine(Path.GetTempPath(), "oku-content-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(temp);
        }

        [TearDown]
        public void After()
        {
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }

        // A glb holding only this JSON.
        static byte[] Glb(string json)
        {
            var body = Encoding.UTF8.GetBytes(json);
            int pad = (4 - body.Length % 4) % 4;
            var chunk = body.Concat(Enumerable.Repeat((byte)' ', pad)).ToArray();
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(0x46546C67u);
            w.Write(2u);
            w.Write((uint)(12 + 8 + chunk.Length));
            w.Write((uint)chunk.Length);
            w.Write(0x4E4F534Au);
            w.Write(chunk);
            return ms.ToArray();
        }

        const string Generated = "{\"materials\":[{\"name\":\"bark\",\"extras\":{\"okGenerated\":true},\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":0}}}]," +
            "\"textures\":[{\"source\":0}],\"images\":[{\"name\":\"bark\"}]}";
        const string Painted = "{\"materials\":[{\"name\":\"roof\",\"extras\":{\"okPaint\":{\"kind\":\"feature\",\"name\":\"AraHut01\"}}}]}";
        const string Carved = "{\"materials\":[{\"name\":\"sprite\",\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":0}}}]," +
            "\"textures\":[{\"source\":0}],\"images\":[{\"name\":\"AraHut01 frame 0\"}]}";
        const string PaintedWithPicture = "{\"materials\":[{\"name\":\"roof\",\"extras\":{\"okPaint\":{\"kind\":\"feature\"}},\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":0}}}]," +
            "\"textures\":[{\"source\":0}],\"images\":[{\"name\":\"x\"}]}";
        const string Review = "{\"nodes\":[{\"name\":\"root\",\"extras\":{\"okFromPlayersFiles\":true}}]}";

        [Test]
        public void GeneratedTexturesMayShip()
        {
            var why = new List<string>();
            Assert.AreEqual(ContentCheck.Kind.Generated, ContentCheck.Glb(Glb(Generated), why));
            CollectionAssert.IsEmpty(why);
        }

        [Test]
        public void PaintedAtLoadMayShip()
        {
            var why = new List<string>();
            Assert.AreEqual(ContentCheck.Kind.PaintedAtLoad, ContentCheck.Glb(Glb(Painted), why));
            Assert.AreEqual(ContentCheck.Kind.Plain, ContentCheck.Glb(Glb("{\"materials\":[{\"name\":\"stone\"}]}"), why));
            CollectionAssert.IsEmpty(why);
        }

        [Test]
        public void APictureNotMarkedGeneratedFails()
        {
            var why = new List<string>();
            Assert.AreEqual(ContentCheck.Kind.Problem, ContentCheck.Glb(Glb(Carved), why));
            StringAssert.Contains("not from a material marked okGenerated", why[0]);
        }

        [Test]
        public void APaintedMaterialThatKeptItsPictureFails()
        {
            var why = new List<string>();
            Assert.AreEqual(ContentCheck.Kind.Problem, ContentCheck.Glb(Glb(PaintedWithPicture), why));
            Assert.IsTrue(why.Any(w => w.Contains("still holds a picture")));
        }

        [Test]
        public void AMaskOrDelitRecipeMayShipButTexelsInARecipeFail()
        {
            var why = new List<string>();
            const string masked = "{\"materials\":[{\"name\":\"skirt\",\"extras\":{\"okPaint\":{\"kind\":\"feature\",\"name\":\"ZonRuin01\",\"alpha\":\"mask\"," +
                "\"mask\":{\"cover\":\"others\",\"hotspot\":[20,30]}}}},{\"name\":\"stone\",\"extras\":{\"okPaint\":{\"kind\":\"feature\",\"name\":\"AraHenge01\"," +
                "\"delit\":{\"hotspot\":[34,43],\"light\":[-0.5,-0.1,0.8],\"ambient\":0.33,\"direct\":0.64,\"stones\":[{\"grey\":[0.15,0.15,0.13],\"dark\":0.15}]}}}}]}";
            Assert.AreEqual(ContentCheck.Kind.PaintedAtLoad, ContentCheck.Glb(Glb(masked), why));
            CollectionAssert.IsEmpty(why);
            string rows = "{\"materials\":[{\"name\":\"skirt\",\"extras\":{\"okPaint\":{\"kind\":\"feature\",\"mask\":{\"cover\":\"others\",\"hotspot\":[" +
                string.Join(",", Enumerable.Range(0, 300)) + "]}}}},{\"name\":\"wall\",\"extras\":{\"okPaint\":{\"kind\":\"feature\",\"texels\":\"" + new string('A', 200) + "\"}}}]}";
            Assert.AreEqual(ContentCheck.Kind.Problem, ContentCheck.Glb(Glb(rows), why));
            Assert.IsTrue(why.Any(w => w.Contains("300 numbers")), string.Join("; ", why));
            Assert.IsTrue(why.Any(w => w.Contains("okPaint key texels")), string.Join("; ", why));
            Assert.IsTrue(why.Any(w => w.Contains("text 200 long")), string.Join("; ", why));
        }

        [Test]
        public void AReviewCopyFails()
        {
            var why = new List<string>();
            Assert.AreEqual(ContentCheck.Kind.Problem, ContentCheck.Glb(Glb(Review), why));
            Assert.AreEqual(ContentCheck.Kind.Problem, ContentCheck.Glb(new byte[] { 1, 2, 3 }, why));
        }

        string Put(string rel, byte[] bytes)
        {
            string path = Path.Combine(temp, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, bytes);
            return path;
        }

        [Test]
        public void AGoodPlayerPasses()
        {
            Put("Game_Data/StreamingAssets/Assets/Overrides/Features/Hut.glb", Glb(Generated));
            Put("Game_Data/StreamingAssets/Assets/Overrides/Features/Tree.glb", Glb(Painted));
            Put("Game_Data/StreamingAssets/Assets/Overrides/Units/flight.json", Encoding.UTF8.GetBytes("{}"));
            Put("Game_Data/Plugins/x86_64/okengine.dll", new byte[] { 0 });
            Put("Game_Data/StreamingAssets/" + ContentCheck.UnityServicesFile, Encoding.UTF8.GetBytes("{}"));
            var packed = new[]
            {
                new ContentCheck.Packed { Type = "Texture2D", Source = "Packages/com.unity.render-pipelines.universal/Textures/BlueNoise.png", Bytes = 10 },
                new ContentCheck.Packed { Type = "Font", Source = "Assets/Game/Resources/Fonts/Cinzel.ttf", Bytes = 10 },
                new ContentCheck.Packed { Type = "Shader", Source = "Assets/Somewhere/Else.shader", Bytes = 10 },
            };
            var r = ContentCheck.Scan(temp, packed);
            CollectionAssert.IsEmpty(r.Problems);
            Assert.AreEqual(4, r.Streaming.Count);
            Assert.AreEqual(2, r.Assets.Count);
            StringAssert.Contains("RESULT: PASS", ContentCheck.Describe(r, "test"));
        }

        [Test]
        public void ACarvedFolderFailsTheBuild()
        {
            Put("Game_Data/StreamingAssets/Assets/Overrides/Generated/AraFence01.glb", Glb(Generated));
            var r = ContentCheck.Scan(temp, null);
            Assert.IsTrue(r.Problems.Any(p => p.Contains("carved")));
            Assert.IsTrue(ContentCheck.IsCarved("D:/OKReplace/out/x.glb"));
            Assert.IsTrue(ContentCheck.IsCarved("Assets\\Overrides\\Drop\\x.glb"));
            Assert.IsFalse(ContentCheck.IsCarved("Assets/Overrides/Features/x.glb"));
        }

        [Test]
        public void TheOriginalsFilesAndStrangersFail()
        {
            Put("Game_Data/StreamingAssets/data.hpi", new byte[] { 0 });
            Put("Game_Data/StreamingAssets/Assets/Overrides/Features/Hut.png", new byte[] { 0 });
            Put("Game_Data/Resources/units.gaf", new byte[] { 0 });
            Put("Game_Data/StreamingAssets/Assets/" + ContentCheck.UnityServicesFile, new byte[] { 0 });
            var packed = new[]
            {
                new ContentCheck.Packed { Type = "Texture2D", Source = "Assets/Art/Stolen.png", Bytes = 10 },
                new ContentCheck.Packed { Type = "Mesh", Source = "Assets/Overrides/Features/Hut.fbx", Bytes = 10 },
            };
            var r = ContentCheck.Scan(temp, packed);
            Assert.IsTrue(r.Problems.Any(p => p.Contains("data.hpi") && p.Contains("original game's own files")));
            Assert.IsTrue(r.Problems.Any(p => p.Contains("units.gaf")));
            Assert.IsTrue(r.Problems.Any(p => p.Contains("Hut.png") && p.Contains("not a model or data file")));
            Assert.IsTrue(r.Problems.Any(p => p.Contains("Assets/" + ContentCheck.UnityServicesFile)), "only at the top of StreamingAssets");
            Assert.IsTrue(r.Problems.Any(p => p.Contains("Stolen.png")));
            Assert.IsTrue(r.Problems.Any(p => p.Contains("Hut.fbx")));
            StringAssert.Contains("RESULT: FAIL", ContentCheck.Describe(r, "test"));
        }

        // Every model the build ships from the project passes, so the build
        // never trips on its own models.
        [Test]
        public void EveryHandBuiltModelInTheProjectMayShip()
        {
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Assets", "Overrides", "Features");
            var files = Directory.GetFiles(dir, "*.glb");
            Assert.Greater(files.Length, 0);
            var bad = new List<string>();
            foreach (var f in files)
            {
                var why = new List<string>();
                if (ContentCheck.Glb(File.ReadAllBytes(f), why) == ContentCheck.Kind.Problem) bad.Add(Path.GetFileName(f) + ": " + string.Join("; ", why));
            }
            CollectionAssert.IsEmpty(bad);
        }
    }
}
