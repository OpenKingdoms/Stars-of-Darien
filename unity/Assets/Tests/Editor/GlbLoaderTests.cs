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
