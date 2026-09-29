// PaintedModelEngineTests.cs - on the real engine, a model painted at load
// takes exactly the pixels the carve pipeline baked into its review copy,
// for a feature's sprite and for a 3DO texture. The pipeline's pictures come
// from tools/sprite-replace/paintref.py, in OKU_PAINT_REF or
// D:/OKReplace/paintref. Needs okengine, the game files and those
// pictures, and is ignored without them.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class PaintedModelEngineTests
    {
        EngineBackend backend;
        string refDir;
        List<object> refs;

        [OneTimeSetUp]
        public void Boot()
        {
            if (!EngineSettings.EngineAvailable) Assert.Ignore("okengine or the game files are missing");
            refDir = Environment.GetEnvironmentVariable("OKU_PAINT_REF");
            if (string.IsNullOrEmpty(refDir)) refDir = "D:/OKReplace/paintref";
            string list = Path.Combine(refDir, "refs.json");
            if (!File.Exists(list)) Assert.Ignore("no pictures from paintref.py in " + refDir);
            refs = MiniJson.Parse(File.ReadAllText(list)) as List<object>;
            backend = new EngineBackend();
            GlbLoader.SetPainter(backend);
        }

        [OneTimeTearDown]
        public void End()
        {
            GlbLoader.SetPainter(null);
            backend?.Dispose();
        }

        [Test]
        public void AFeaturesSpriteIsPaintedAsThePipelineBakedIt() => Matches("feature");

        [Test]
        public void A3doTextureIsPaintedAsThePipelineBakedIt() => Matches("texture");

        void Matches(string kind)
        {
            var r = refs?.FirstOrDefault(o => MiniJson.Text(o, "kind") == kind);
            if (r == null) Assert.Ignore("paintref.py made no " + kind);
            string name = MiniJson.Text(r, "name"), world = MiniJson.Text(r, "world", "");
            var size = MiniJson.Arr(r, "size");
            string paint = $"{{\"kind\":\"{kind}\",\"name\":\"{name}\",\"world\":\"{world}\",\"gain\":{MiniJson.Num(r, "gain", 1).ToString("R", CultureInfo.InvariantCulture)}," +
                           $"\"bleed\":true,\"alpha\":\"opaque\",\"size\":[{size[0]},{size[1]}]}}";
            var root = GlbLoader.Load(PaintAtLoadTests.Glb("{\"name\":\"painted\",\"extras\":{\"okPaint\":" + paint + "}}"), name, out var error);
            Assert.IsNull(error);
            var want = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                var got = root.GetComponentInChildren<MeshRenderer>(true).sharedMaterial.mainTexture as Texture2D;
                Assert.IsNotNull(got, $"the engine gave no picture for {kind} {name}");
                Assert.IsTrue(want.LoadImage(File.ReadAllBytes(Path.Combine(refDir, MiniJson.Text(r, "file")))));
                Assert.AreEqual(want.width, got.width);
                Assert.AreEqual(want.height, got.height);
                var a = want.GetPixels32();
                var b = got.GetPixels32();
                int off = 0, first = -1;
                for (int i = 0; i < a.Length; i++)
                    if (!a[i].Equals(b[i])) { off++; if (first < 0) first = i; }
                Assert.AreEqual(0, off, first < 0 ? "" : $"{off} texels differ, the first at {first % want.width},{first / want.width}: {a[first]} baked, {b[first]} painted");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(want);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
