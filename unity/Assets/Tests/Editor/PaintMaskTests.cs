// PaintMaskTests.cs - okPaint's mask and delit, which make a painted
// picture from the player's pixels and the model's own faces: a ruin's
// rubble skirt loses the texels its walls cover from the classic camera,
// and a standing stone has its painted light taken out and its shading
// flattened toward each stone's grey.
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
    public class PaintMaskTests
    {
        [TearDown]
        public void NoPictures() => GlbLoader.SetPictures(null);

        // One triangle per primitive: its glTF corners, its material and its
        // stone (TEXCOORD_1 u).
        internal struct Tri
        {
            public Vector3 A, B, C;
            public int Material;
            public float Stone;
        }

        internal static byte[] Glb(string[] materials, params Tri[] tris)
        {
            var bin = new List<byte>();
            var views = new List<string>();
            var accessors = new List<string>();
            var prims = new List<string>();
            void View(IEnumerable<byte> bytes, int count, string type, int component)
            {
                int start = bin.Count;
                bin.AddRange(bytes);
                views.Add($"{{\"buffer\":0,\"byteOffset\":{start},\"byteLength\":{bin.Count - start}}}");
                while (bin.Count % 4 != 0) bin.Add(0);
                accessors.Add($"{{\"bufferView\":{views.Count - 1},\"componentType\":{component},\"count\":{count},\"type\":\"{type}\"}}");
            }
            IEnumerable<byte> Floats(params float[] f) => f.SelectMany(BitConverter.GetBytes);
            foreach (var t in tris)
            {
                int first = accessors.Count;
                View(Floats(t.A.x, t.A.y, t.A.z, t.B.x, t.B.y, t.B.z, t.C.x, t.C.y, t.C.z), 3, "VEC3", 5126);
                View(Floats(0, 0, 0, 1, 1, 0), 3, "VEC2", 5126);
                View(Floats(t.Stone, 0, t.Stone, 0, t.Stone, 0), 3, "VEC2", 5126);
                View(new ushort[] { 0, 1, 2 }.SelectMany(BitConverter.GetBytes), 3, "SCALAR", 5123);
                prims.Add($"{{\"attributes\":{{\"POSITION\":{first},\"TEXCOORD_0\":{first + 1},\"TEXCOORD_1\":{first + 2}}},\"indices\":{first + 3},\"material\":{t.Material}}}");
            }
            string json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}]," +
                "\"nodes\":[{\"name\":\"model\",\"mesh\":0}]," +
                "\"meshes\":[{\"primitives\":[" + string.Join(",", prims) + "]}]," +
                "\"materials\":[" + string.Join(",", materials) + "]," +
                $"\"buffers\":[{{\"byteLength\":{bin.Count}}}]," +
                "\"bufferViews\":[" + string.Join(",", views) + "]," +
                "\"accessors\":[" + string.Join(",", accessors) + "]}";
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

        // glTF's corner for a point of the model in Blender's frame.
        static Vector3 G(float x, float y, float z) => new Vector3(x, z, -y);

        static string Painted(string name, int w, int h, string alpha, string more = "") =>
            "{\"name\":\"" + name + "\",\"extras\":{\"okPaint\":{\"kind\":\"feature\",\"name\":\"Ruin\",\"world\":\"aramon\"," +
            "\"gain\":1,\"bleed\":true,\"alpha\":\"" + alpha + "\",\"size\":[" + w + "," + h + "]" + more + "}}}";

        static RgbaImage Solid(int w, int h, byte v, Func<int, int, bool> clear = null)
        {
            var img = new RgbaImage(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    img.Pixels[i] = img.Pixels[i + 1] = img.Pixels[i + 2] = v;
                    img.Pixels[i + 3] = clear != null && clear(x, y) ? (byte)0 : (byte)255;
                }
            return img;
        }

        static Material[] Load(byte[] glb, out GameObject root)
        {
            root = GlbLoader.Load(glb, "masked", out var error);
            Assert.IsNull(error);
            var r = root.GetComponentInChildren<MeshRenderer>(true);
            Assert.IsNotNull(r, "the model draws");
            return r.sharedMaterials;
        }

        // Texture alpha at picture column x, row y from the top.
        static byte AlphaAt(Texture2D t, int x, int y) => t.GetPixels32()[(t.height - 1 - y) * t.width + x].a;

        [Test]
        public void AMaskClearsWhatTheModelsOtherFacesCoverAndKeepsThePicturesOwnClearTexels()
        {
            GlbLoader.SetPictures((kind, name, world) => Solid(3, 3, 120, (x, y) => x == 2 && y == 2));
            // The skirt lies over the whole picture. The wall's face covers
            // only the top-left texel's centre, from hotspot (0, 0).
            var skirt = new Tri { A = G(0, 0, 0), B = G(0.2f, 0, 0), C = G(0, -0.2f, 0), Material = 0 };
            var wall = new Tri { A = G(0, 0, 0), B = G(1.2f / 16f, 0, 0), C = G(0, -1.2f / 16f, 0), Material = 1 };
            // Fronds off the picture's edge, painted from the same picture.
            var fronds = new Tri { A = G(-1, 0, 0), B = G(-0.8f, 0, 0), C = G(-1, -0.2f, 0), Material = 2 };
            var mats = Load(Glb(new[]
            {
                Painted("skirt", 3, 3, "mask", ",\"mask\":{\"cover\":\"others\",\"hotspot\":[0,0]}"),
                "{\"name\":\"wall\"}",
                Painted("fronds", 3, 3, "mask"),
            }, skirt, wall, fronds), out var root);
            try
            {
                var t = (Texture2D)mats[0].mainTexture;
                Assert.IsNotNull(t);
                Assert.AreEqual(0, AlphaAt(t, 0, 0), "the wall stands on the top-left texel");
                Assert.AreEqual(0, AlphaAt(t, 2, 2), "the picture's own clear texel stays clear");
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 3; x++)
                        if ((x, y) != (0, 0) && (x, y) != (2, 2)) Assert.AreEqual(255, AlphaAt(t, x, y), $"texel {x},{y}");
                Assert.AreEqual(0.5f, mats[0].GetFloat("_Cutoff"), 1e-6f, "a mask cuts out");
                var plain = (Texture2D)mats[2].mainTexture;
                Assert.AreNotSame(t, plain, "the same picture without a mask is its own texture");
                Assert.AreEqual(255, AlphaAt(plain, 0, 0));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void AFaceInTheMaskedMaterialItselfCoversNothing()
        {
            GlbLoader.SetPictures((kind, name, world) => Solid(3, 3, 120));
            var skirt = new Tri { A = G(0, 0, 0), B = G(0.2f, 0, 0), C = G(0, -0.2f, 0), Material = 0 };
            var mats = Load(Glb(new[] { Painted("skirt", 3, 3, "mask", ",\"mask\":{\"cover\":\"others\",\"hotspot\":[0,0]}") }, skirt), out var root);
            try
            {
                var t = (Texture2D)mats[0].mainTexture;
                Assert.IsTrue(t.GetPixels32().All(p => p.a == 255));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void TheClassicCameraSeesTheFaceNearestIt()
        {
            // Two faces over the same texels: the one higher up (greater
            // 2z - y) is seen, whichever comes first.
            float s = 2.5f / 16f;
            var f = new GlbLoader.Faces();
            f.Add(new Vector3(0, 0, 0), new Vector3(s, 0, 0), new Vector3(0, -s, 0), 0, 0);
            f.Add(new Vector3(0, 0, 0.1f), new Vector3(s, 0, 0.1f), new Vector3(0, -s, 0.1f), 1, 1);
            var hit = GlbLoader.ClassicHits(f, new Vector2(0, 0), 4, 4);
            Assert.AreEqual(1, hit[0], "both cover the top-left texel, the lifted one is nearer");
            Assert.AreEqual(0, hit[1], "only the lower one reaches the next texel");
            Assert.AreEqual(-1, hit[3 * 4 + 3], "beyond both");
            var low = GlbLoader.ClassicHits(f, new Vector2(0, 0), 4, 4, t => t == 0);
            Assert.AreEqual(0, low[0]);
        }

        [Test]
        public void DelitTakesEachStoneToItsGreyAndFillsWhatNoFaceCovers()
        {
            // A flat grey picture on two stones, each seen over part of it:
            // with no detail each stone's texels land on its own grey, and
            // texels no face covers fill from their neighbours.
            GlbLoader.SetPictures((kind, name, world) => Solid(8, 8, 128));
            var a = new Tri { A = G(0, 0, 0), B = G(0, -0.5f, 0), C = G(0.5f, 0, 0), Material = 0, Stone = 0.5f };
            var b = new Tri { A = G(0.5f, 0, 0), B = G(0, -0.5f, 0), C = G(0.5f, -0.5f, 0), Material = 0, Stone = 1.5f };
            string delit = ",\"delit\":{\"hotspot\":[0,0],\"light\":[0,0,1],\"ambient\":0.33,\"direct\":0.64," +
                "\"stones\":[{\"grey\":[0.2,0.2,0.2],\"dark\":0.15},{\"grey\":[0.12,0.12,0.12]}]}";
            var mats = Load(Glb(new[] { Painted("stones", 8, 8, "opaque", delit) }, a, b), out var root);
            try
            {
                var t = (Texture2D)mats[0].mainTexture;
                Assert.IsNotNull(t);
                var px = t.GetPixels32();
                Color32 At(int x, int y) => px[(t.height - 1 - y) * t.width + x];
                byte want0 = (byte)Mathf.RoundToInt(Mathf.LinearToGammaSpace(0.2f) * 255f);
                byte want1 = (byte)Mathf.RoundToInt(Mathf.LinearToGammaSpace(0.12f) * 255f);
                Assert.AreEqual(want0, At(1, 1).r, 1, "the first stone's texels take its grey");
                Assert.AreEqual(want1, At(6, 6).r, 1, "the second stone's texels take its grey");
                Assert.AreEqual(At(1, 1).r, At(1, 1).g, "grey stays grey");
                Assert.IsTrue(px.All(p => p.a == 255), "the picture is opaque");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void DelitKeepsCarvingsDarkButNotBlack()
        {
            // A dark groove across a light stone keeps its darkness only
            // down to the stone's dark share of its grey.
            GlbLoader.SetPictures((kind, name, world) =>
            {
                var img = Solid(9, 9, 160);
                for (int y = 0; y < 9; y++) { int i = (y * 9 + 4) * 4; img.Pixels[i] = img.Pixels[i + 1] = img.Pixels[i + 2] = 0; }
                return img;
            });
            var a = new Tri { A = G(-1, 1, 0), B = G(-1, -2, 0), C = G(2, 1, 0), Material = 0 };
            var b = new Tri { A = G(2, 1, 0), B = G(-1, -2, 0), C = G(2, -2, 0), Material = 0 };
            string delit = ",\"delit\":{\"hotspot\":[0,0],\"light\":[0,0,1],\"ambient\":0.33,\"direct\":0.64," +
                "\"stones\":[{\"grey\":[0.3,0.3,0.3],\"dark\":0.25}]}";
            var mats = Load(Glb(new[] { Painted("stone", 9, 9, "opaque", delit) }, a, b), out var root);
            try
            {
                var t = (Texture2D)mats[0].mainTexture;
                var groove = t.GetPixels32()[(t.height - 1 - 4) * t.width + 4];
                var face = t.GetPixels32()[(t.height - 1 - 4) * t.width + 0];
                float g = Mathf.GammaToLinearSpace(groove.r / 255f), l = Mathf.GammaToLinearSpace(face.r / 255f);
                Assert.Less(g, l * 0.6f, "the groove stays darker than the face");
                Assert.Greater(g, 0.3f * 0.25f * 0.8f, "but no darker than dark of the grey, broadly");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
