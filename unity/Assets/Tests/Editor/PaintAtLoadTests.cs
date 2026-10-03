// PaintAtLoadTests.cs - a glb material with okPaint takes its picture by
// name from the backend, once per name, treated as carve.Sprite treats it;
// with no game files (the mock) it draws a plain colour and the model
// still loads.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class PaintAtLoadTests
    {
        [TearDown]
        public void NoPictures() => GlbLoader.SetPictures(null);

        // One triangle drawn once per material, each an entry of materials.
        internal static byte[] Glb(params string[] materials)
        {
            var bin = new List<byte>();
            foreach (var f in new float[] { 0, 0, 0, 0, 0, 1, 1, 0, 0 }) bin.AddRange(BitConverter.GetBytes(f));
            foreach (var f in new float[] { 0, 0, 0, 1, 1, 0 }) bin.AddRange(BitConverter.GetBytes(f));
            foreach (var i in new ushort[] { 0, 1, 2, 0 }) bin.AddRange(BitConverter.GetBytes(i));
            var prims = string.Join(",", materials.Select((_, i) => "{\"attributes\":{\"POSITION\":0,\"TEXCOORD_0\":1},\"indices\":2,\"material\":" + i + "}"));
            string json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}]," +
                "\"nodes\":[{\"name\":\"model\",\"mesh\":0}]," +
                "\"meshes\":[{\"primitives\":[" + prims + "]}]," +
                "\"materials\":[" + string.Join(",", materials) + "]," +
                "\"buffers\":[{\"byteLength\":68}]," +
                "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":36,\"byteLength\":24},{\"buffer\":0,\"byteOffset\":60,\"byteLength\":6}]," +
                "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"},{\"bufferView\":1,\"componentType\":5126,\"count\":3,\"type\":\"VEC2\"},{\"bufferView\":2,\"componentType\":5123,\"count\":3,\"type\":\"SCALAR\"}]}";
            while (json.Length % 4 != 0) json += " ";
            var j = Encoding.UTF8.GetBytes(json);
            var glb = new List<byte>();
            glb.AddRange(BitConverter.GetBytes(0x46546C67u));
            glb.AddRange(BitConverter.GetBytes(2u));
            glb.AddRange(BitConverter.GetBytes((uint)(12 + 8 + j.Length + 8 + bin.Count)));
            glb.AddRange(BitConverter.GetBytes((uint)j.Length));
            glb.AddRange(BitConverter.GetBytes(0x4E4F534Au));
            glb.AddRange(j);
            glb.AddRange(BitConverter.GetBytes((uint)bin.Count));
            glb.AddRange(BitConverter.GetBytes(0x004E4942u));
            glb.AddRange(bin);
            return glb.ToArray();
        }

        static string Painted(string name, string picture = "Tree", string alpha = "opaque", string more = "", string beside = "") =>
            "{\"name\":\"" + name + "\",\"extras\":{\"okPaint\":{\"kind\":\"feature\",\"name\":\"" + picture +
            "\",\"world\":\"aramon\",\"gain\":1.3,\"bleed\":true,\"alpha\":\"" + alpha + "\",\"size\":[3,3]" + more + "}" + beside + "}}";

        // A 3 by 3 picture, rows top-down, clear but for its middle texel.
        static RgbaImage Dot()
        {
            var img = new RgbaImage(3, 3);
            int m = (1 * 3 + 1) * 4;
            img.Pixels[m] = 200; img.Pixels[m + 1] = 100; img.Pixels[m + 2] = 50; img.Pixels[m + 3] = 255;
            return img;
        }

        static void Near(Color want, Color got) =>
            Assert.Less(Vector4.Distance(want, got), 1e-3f, $"{want} against {got}");

        static Material[] Load(byte[] glb, out GameObject root)
        {
            root = GlbLoader.Load(glb, "painted", out var error);
            Assert.IsNull(error);
            var r = root.GetComponentInChildren<MeshRenderer>(true);
            Assert.IsNotNull(r, "the model draws");
            return r.sharedMaterials;
        }

        [Test]
        public void WithTheMockAPaintedMaterialIsNeutralAndTheModelStillDraws()
        {
            var mock = new MockBackend();
            try
            {
                GlbLoader.SetPainter(mock);
                var mats = Load(Glb(Painted("walls")), out var root);
                try
                {
                    Assert.IsNull(mats[0].mainTexture);
                    Near(GlbLoader.Neutral, mats[0].color);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            finally { GlbLoader.SetPainter(null); mock.Dispose(); }
        }

        [Test]
        public void OkFallbackIsTheColourWithoutGameFiles()
        {
            var mats = Load(Glb(Painted("roof", beside: ",\"okFallback\":[0.2,0.1,0.05]")), out var root);
            try
            {
                Assert.IsNull(mats[0].mainTexture);
                Near(new Color(0.2f, 0.1f, 0.05f, 1f), mats[0].color);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void APictureIsAskedForOnceAndTreatedAsCarveTreatsIt()
        {
            var asked = new List<string>();
            GlbLoader.SetPictures((kind, name, world) => { asked.Add(kind + " " + name + " " + world); return Dot(); });
            var mats = Load(Glb(Painted("walls"), Painted("roof"), "{\"name\":\"stone\"}"), out var root);
            try
            {
                CollectionAssert.AreEqual(new[] { "feature Tree aramon" }, asked, "one texture per name");
                var tex = mats[0].mainTexture as Texture2D;
                Assert.IsNotNull(tex);
                Assert.AreSame(tex, mats[1].mainTexture);
                Assert.IsNull(mats[2].mainTexture, "a plain material asks for nothing");
                Near(Color.white, mats[0].color);
                Assert.AreEqual(TextureWrapMode.Clamp, tex.wrapMode);
                // The fill spreads the one colour everywhere, lifted by 1.3,
                // and opaque makes every texel solid.
                foreach (var c in tex.GetPixels32())
                    Assert.AreEqual(new Color32(255, 130, 65, 255), c);
                Assert.AreEqual(0f, mats[0].GetFloat("_Cutoff"));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void APictureOfAnotherSizeIsLeftOut()
        {
            GlbLoader.SetPictures((kind, name, world) => new RgbaImage(4, 3));
            var mats = Load(Glb(Painted("walls")), out var root);
            try
            {
                Assert.IsNull(mats[0].mainTexture, "the UVs were made against 3 by 3");
                Near(GlbLoader.Neutral, mats[0].color);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void AMaskKeepsThePicturesAlphaAndCutsAtHalf()
        {
            GlbLoader.SetPictures((kind, name, world) => Dot());
            var mats = Load(Glb(Painted("fronds", alpha: "mask")), out var root);
            try
            {
                var px = ((Texture2D)mats[0].mainTexture).GetPixels32();
                Assert.AreEqual(255, px[4].a, "the middle texel is drawn");
                Assert.AreEqual(1, px.Count(p => p.a == 255));
                Assert.AreEqual(0.5f, mats[0].GetFloat("_Cutoff"), 1e-6f);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void ATintMultipliesAndABorderClearsTheOuterRing()
        {
            GlbLoader.SetPictures((kind, name, world) => Dot());
            var img = GlbLoader.Paint(Dot(), 1.3f, true, false, true);
            for (int i = 0; i < 9; i++) Assert.AreEqual(i == 4 ? 255 : 0, img[i * 4 + 3], "texel " + i);
            var mats = Load(Glb(Painted("walls", more: ",\"tint\":0.5")), out var root);
            try { Near(new Color(0.5f, 0.5f, 0.5f, 1f), mats[0].color); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // As tools/sprite-replace/okpaint.py checks a shipped model: a painted
        // material holds no picture, nothing is stamped as the player's, and a
        // carved model's pictures belong to materials marked okGenerated. Any
        // other picture is an artist's own, approved when it was merged.
        [Test]
        public void NoShippedModelHoldsTheOriginalsArt()
        {
            string dir = System.IO.Path.Combine(OverrideLoader.ProjectDir, "Assets", "Overrides", "Features");
            var bad = new List<string>();
            foreach (var path in System.IO.Directory.GetFiles(dir, "*.glb"))
            {
                var glb = System.IO.File.ReadAllBytes(path);
                var json = MiniJson.Parse(Encoding.UTF8.GetString(glb, 20, BitConverter.ToInt32(glb, 12)));
                string name = System.IO.Path.GetFileName(path);
                var textures = MiniJson.Arr(json, "textures") ?? new List<object>();
                var vouched = new HashSet<int>();
                foreach (var m in MiniJson.Arr(json, "materials") ?? new List<object>())
                {
                    var extras = MiniJson.Obj(m, "extras");
                    var pbr = MiniJson.Obj(m, "pbrMetallicRoughness");
                    var refs = new[] { MiniJson.Obj(pbr, "baseColorTexture"), MiniJson.Obj(pbr, "metallicRoughnessTexture"),
                                       MiniJson.Obj(m, "normalTexture"), MiniJson.Obj(m, "occlusionTexture"), MiniJson.Obj(m, "emissiveTexture") }
                        .Where(r => r != null).Select(r => MiniJson.Int(r, "index")).Where(i => i >= 0 && i < textures.Count).ToList();
                    if (MiniJson.Obj(extras, "okPaint") != null && refs.Count > 0) bad.Add(name + ": painted material " + MiniJson.Text(m, "name") + " holds a picture");
                    else if (extras != null && extras.TryGetValue("okGenerated", out var g) && g is bool yes && yes)
                        foreach (var t in refs) vouched.Add(MiniJson.Int(textures[t], "source"));
                }
                int images = (MiniJson.Arr(json, "images") ?? new List<object>()).Count;
                var nodes = MiniJson.Arr(json, "nodes") ?? new List<object>();
                if (nodes.Any(n => MiniJson.Obj(n, "extras")?.ContainsKey("okCarved") == true))
                    for (int i = 0; i < images; i++) if (!vouched.Contains(i)) bad.Add(name + ": picture " + i + " is in a carved model and not from a material marked generated");
                foreach (var n in nodes)
                    if (MiniJson.Obj(n, "extras")?.ContainsKey("okFromPlayersFiles") == true) bad.Add(name + ": stamped as the player's");
            }
            CollectionAssert.IsEmpty(bad);
        }

        [Test]
        public void TheFillTakesTheMeanOfOpaqueNeighboursRingByRing()
        {
            // red, clear, clear, blue in a row: each clear texel takes its
            // one opaque neighbour on the first pass
            var img = new RgbaImage(4, 1);
            img.Pixels[0] = 255; img.Pixels[3] = 255;
            img.Pixels[14] = 255; img.Pixels[15] = 255;
            var px = GlbLoader.Paint(img, 1f, true, true, false);
            CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255, 255, 0, 0, 255, 0, 0, 255, 255, 0, 0, 255, 255 }, px);
            // red, clear, blue: the middle is their mean, rounded half up
            img = new RgbaImage(3, 1);
            img.Pixels[0] = 255; img.Pixels[3] = 255;
            img.Pixels[10] = 255; img.Pixels[11] = 255;
            px = GlbLoader.Paint(img, 1f, true, true, false);
            CollectionAssert.AreEqual(new byte[] { 128, 0, 128, 255 }, px.Skip(4).Take(4).ToArray());
        }
    }
}
