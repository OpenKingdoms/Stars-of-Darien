// GlbLoaderTests.cs - a tiny glb made in memory loads with its node
// transform, its z flipped to the map's north, and triangles still facing
// the way they did.
using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class GlbLoaderTests
    {
        // One triangle in the glTF xz plane facing +y, at a node moved 2
        // along glTF +z (toward the viewer, the map's south).
        static byte[] Triangle()
        {
            var bin = new List<byte>();
            foreach (var f in new float[] { 0, 0, 0, 0, 0, 1, 1, 0, 0 }) bin.AddRange(BitConverter.GetBytes(f));
            foreach (var i in new ushort[] { 0, 1, 2, 0 }) bin.AddRange(BitConverter.GetBytes(i));
            string json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}]," +
                "\"nodes\":[{\"name\":\"tri\",\"mesh\":0,\"translation\":[0,0,2]}]," +
                "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"indices\":1}]}]," +
                "\"buffers\":[{\"byteLength\":44}]," +
                "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":36,\"byteLength\":6}]," +
                "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"},{\"bufferView\":1,\"componentType\":5123,\"count\":3,\"type\":\"SCALAR\"}]}";
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

        [Test]
        public void ATriangleLoadsWithZFlipped()
        {
            var root = GlbLoader.Load(Triangle(), "tri", out var error);
            Assert.IsNull(error);
            try
            {
                var node = root.transform.Find("tri");
                Assert.IsNotNull(node);
                Assert.AreEqual(new Vector3(0, 0, -2), node.localPosition, "glTF +z, the south, is Unity -z");
                var mesh = node.GetComponent<MeshFilter>().sharedMesh;
                Assert.AreEqual(3, mesh.vertexCount);
                Assert.AreEqual(new Vector3(0, 0, -1), mesh.vertices[1]);
                var t = mesh.triangles;
                var a = mesh.vertices[t[0]]; var b = mesh.vertices[t[1]]; var c = mesh.vertices[t[2]];
                Assert.Greater(Vector3.Cross(b - a, c - a).y, 0f, "still faces up");
                Assert.IsNotNull(node.GetComponent<MeshRenderer>().sharedMaterial);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // A triangle with a vertex colour and a material using a picture
        // whose texels are all clear, in the given alpha mode.
        static byte[] Painted(string alphaMode, string extra = "")
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.SetPixels32(new[] { new Color32(200, 100, 50, 0), new Color32(200, 100, 50, 0), new Color32(200, 100, 50, 0), new Color32(200, 100, 50, 0) });
            var png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            var bin = new List<byte>();
            foreach (var f in new float[] { 0, 0, 0, 0, 0, 1, 1, 0, 0 }) bin.AddRange(BitConverter.GetBytes(f));
            foreach (var f in new float[] { 0.5f, 0.5f, 0.5f, 1, 1, 1, 1, 1, 1 }) bin.AddRange(BitConverter.GetBytes(f));
            foreach (var f in new float[] { 0, 0, 1, 0, 0, 1 }) bin.AddRange(BitConverter.GetBytes(f));
            int imageAt = bin.Count;
            bin.AddRange(png);
            while (bin.Count % 4 != 0) bin.Add(0);
            string mat = alphaMode == "MASK" ? "\"alphaMode\":\"MASK\",\"alphaCutoff\":0.3," : $"\"alphaMode\":\"{alphaMode}\",";
            string json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}]," +
                "\"nodes\":[{\"name\":\"tri\",\"mesh\":0}]," +
                "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"COLOR_0\":1,\"TEXCOORD_0\":2},\"material\":0}]}]," +
                "\"materials\":[{" + mat + extra + "\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":0}}}]," +
                "\"textures\":[{\"source\":0}],\"images\":[{\"bufferView\":3,\"mimeType\":\"image/png\"}]," +
                $"\"buffers\":[{{\"byteLength\":{bin.Count}}}]," +
                "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":36,\"byteLength\":36}," +
                $"{{\"buffer\":0,\"byteOffset\":72,\"byteLength\":24}},{{\"buffer\":0,\"byteOffset\":{imageAt},\"byteLength\":{png.Length}}}]," +
                "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"},{\"bufferView\":1,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"}," +
                "{\"bufferView\":2,\"componentType\":5126,\"count\":3,\"type\":\"VEC2\"}]}";
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

        [Test]
        public void AnOpaqueModelCutsNothingOutWhateverItsTextureSays()
        {
            var root = GlbLoader.Load(Painted("OPAQUE"), "painted", out var error);
            Assert.IsNull(error);
            try
            {
                var r = root.GetComponentInChildren<MeshRenderer>(true);
                var mat = r.sharedMaterial;
                Assert.LessOrEqual(mat.GetFloat("_Cutoff"), 0f, "no alpha test on an opaque material");
                Assert.AreEqual(1f, mat.color.a, "alpha is not used");
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                Assert.AreEqual(0.5f, mesh.colors[0].r, 0.01f, "the vertex colour is read");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void AMaskedModelCutsAtItsOwnCutoff()
        {
            var root = GlbLoader.Load(Painted("MASK"), "painted", out var error);
            Assert.IsNull(error);
            try
            {
                var mat = root.GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
                Assert.AreEqual(0.3f, mat.GetFloat("_Cutoff"), 1e-4f);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void AnEmissiveMaterialGlowsAtItsStrength()
        {
            string extra = "\"emissiveFactor\":[1,0.5,0.25],\"emissiveTexture\":{\"index\":0}," +
                "\"extensions\":{\"KHR_materials_emissive_strength\":{\"emissiveStrength\":4}},";
            var root = GlbLoader.Load(Painted("OPAQUE", extra), "glowing", out var error);
            Assert.IsNull(error);
            try
            {
                var mat = root.GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
                Assert.IsTrue(mat.IsKeywordEnabled("_EMISSION"), "emission is on");
                var c = mat.GetVector("_EmissionColor");
                Assert.AreEqual(4f, c.x, 1e-4f, "factor times strength, bright enough to bloom");
                Assert.AreEqual(2f, c.y, 1e-4f);
                Assert.AreEqual(1f, c.z, 1e-4f);
                Assert.IsNotNull(mat.GetTexture("_EmissionMap"), "with its map");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void ABlendedMaterialIsTransparent()
        {
            var root = GlbLoader.Load(Painted("BLEND"), "halo", out var error);
            Assert.IsNull(error);
            try
            {
                var mat = root.GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
                Assert.GreaterOrEqual(mat.renderQueue, (int)UnityEngine.Rendering.RenderQueue.Transparent);
                Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.SrcAlpha, mat.GetFloat("_SrcBlend"));
                Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha, mat.GetFloat("_DstBlend"));
                Assert.AreEqual(0f, mat.GetFloat("_ZWrite"), "writes no depth");
                Assert.IsFalse(mat.GetShaderPassEnabled("ShadowCaster"), "casts no shadow");
                var opaque = GlbLoader.Load(Painted("OPAQUE"), "solid", out _);
                var om = opaque.GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
                Assert.Less(om.renderQueue, (int)UnityEngine.Rendering.RenderQueue.Transparent, "an opaque one stays solid");
                Assert.AreEqual(1f, om.GetFloat("_ZWrite"));
                UnityEngine.Object.DestroyImmediate(opaque);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void MetalRoughnessAndClearCoatAreLitPhysically()
        {
            string extra = "\"extensions\":{\"KHR_materials_clearcoat\":{\"clearcoatFactor\":0.7,\"clearcoatRoughnessFactor\":0.1}},";
            var root = GlbLoader.Load(Painted("OPAQUE", extra), "gilt", out var error);
            Assert.IsNull(error);
            try
            {
                var mat = root.GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
                Assert.IsTrue(mat.IsKeywordEnabled("_OKU_PBR"), "lit as URP's Lit is");
                Assert.AreEqual(1f, mat.GetFloat("_Metallic"), 1e-4f, "glTF's default is fully metal");
                Assert.AreEqual(0f, mat.GetFloat("_Smoothness"), 1e-4f, "and fully rough");
                Assert.IsTrue(mat.IsKeywordEnabled("_CLEARCOAT"));
                Assert.AreEqual(0.7f, mat.GetFloat("_ClearCoat"), 1e-4f);
                Assert.AreEqual(0.9f, mat.GetFloat("_ClearCoatSmoothness"), 1e-4f);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void NotAGlbIsRefused()
        {
            Assert.IsNull(GlbLoader.Load(new byte[40], "x", out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void MiniJsonReadsGltfShapes()
        {
            var o = MiniJson.Parse("{\"a\":[1,2.5,-3e2],\"b\":{\"c\":\"x\\\"y\"},\"d\":true,\"e\":null}");
            Assert.AreEqual(3, MiniJson.Arr(o, "a").Count);
            Assert.AreEqual(-300.0, (double)MiniJson.Arr(o, "a")[2]);
            Assert.AreEqual("x\"y", MiniJson.Text(MiniJson.Obj(o, "b"), "c"));
            Assert.AreEqual(-1, MiniJson.Int(o, "missing"));
        }
    }
}
