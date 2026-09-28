// GlbBounds.cs - the box round a .glb's triangles as GlbLoader will stand it,
// from the header alone (each POSITION accessor's min and max through its
// nodes), so the gallery can measure hundreds of files on a worker.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    public static class GlbBounds
    {
        // False with the reason when the file is not a readable glb or has
        // no triangles.
        public static bool Read(string path, out Bounds bounds, out string error)
        {
            bounds = default;
            error = null;
            try
            {
                string json;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var head = new byte[20];
                    if (fs.Read(head, 0, 20) != 20 || BitConverter.ToUInt32(head, 0) != 0x46546C67) { error = "not a glb file"; return false; }
                    int len = BitConverter.ToInt32(head, 12);
                    if (BitConverter.ToUInt32(head, 16) != 0x4E4F534A || len <= 0 || len > fs.Length) { error = "no JSON in the file"; return false; }
                    var text = new byte[len];
                    int got = 0;
                    while (got < len) { int n = fs.Read(text, got, len - got); if (n <= 0) break; got += n; }
                    json = Encoding.UTF8.GetString(text, 0, got);
                }
                var root = MiniJson.Parse(json);
                if (Measure(root, null, out bounds)) return true;
                // Some exporter left out the min and max, so the numbers are read.
                var all = File.ReadAllBytes(path);
                int jsonLen = BitConverter.ToInt32(all, 12);
                int binAt = 20 + jsonLen;
                byte[] bin = null;
                if (binAt + 8 <= all.Length)
                {
                    int binLen = BitConverter.ToInt32(all, binAt);
                    bin = new byte[Math.Max(0, Math.Min(binLen, all.Length - binAt - 8))];
                    Buffer.BlockCopy(all, binAt + 8, bin, 0, bin.Length);
                }
                if (Measure(root, bin ?? Array.Empty<byte>(), out bounds)) return true;
                error = "it has no triangles";
                return false;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        // With bin null, only accessors with min and max count; returns false
        // when one lacks them or nothing was found.
        static bool Measure(object json, byte[] bin, out Bounds bounds)
        {
            bounds = default;
            var nodes = MiniJson.Arr(json, "nodes") ?? new List<object>();
            var scenes = MiniJson.Arr(json, "scenes");
            int sceneIndex = Math.Max(0, MiniJson.Int(json, "scene", 0));
            var roots = scenes != null && sceneIndex < scenes.Count ? MiniJson.Arr(scenes[sceneIndex], "nodes") : null;
            if (roots == null)
            {
                roots = new List<object>();
                var child = new HashSet<int>();
                foreach (var n in nodes) foreach (var c in MiniJson.Arr(n, "children") ?? new List<object>()) child.Add((int)(double)c);
                for (int i = 0; i < nodes.Count; i++) if (!child.Contains(i)) roots.Add((double)i);
            }
            var ctx = new Ctx { Json = json, Nodes = nodes, Bin = bin };
            foreach (var r in roots)
                if (!Walk(ctx, (int)(double)r, Matrix4x4.identity, 0)) return false;
            if (!ctx.Any) return false;
            // glTF's +z is the model's south, Unity's -z.
            var b = ctx.Box;
            bounds = new Bounds(new Vector3(b.center.x, b.center.y, -b.center.z), b.size);
            return true;
        }

        sealed class Ctx
        {
            public object Json;
            public List<object> Nodes;
            public byte[] Bin;
            public Bounds Box;
            public bool Any;

            public void Add(Vector3 p)
            {
                if (!Any) { Box = new Bounds(p, Vector3.zero); Any = true; }
                else Box.Encapsulate(p);
            }
        }

        static bool Walk(Ctx ctx, int index, Matrix4x4 parent, int depth)
        {
            if (index < 0 || index >= ctx.Nodes.Count || depth > 64) return true;
            var n = ctx.Nodes[index];
            var world = parent * Local(n);
            int mesh = MiniJson.Int(n, "mesh");
            var meshes = MiniJson.Arr(ctx.Json, "meshes");
            if (mesh >= 0 && meshes != null && mesh < meshes.Count)
                foreach (var p in MiniJson.Arr(meshes[mesh], "primitives") ?? new List<object>())
                {
                    if (MiniJson.Int(p, "mode", 4) != 4) continue;
                    int pos = MiniJson.Int(MiniJson.Obj(p, "attributes"), "POSITION");
                    if (pos < 0) continue;
                    if (!Positions(ctx, pos, world)) return false;
                }
            foreach (var c in MiniJson.Arr(n, "children") ?? new List<object>())
                if (!Walk(ctx, (int)(double)c, world, depth + 1)) return false;
            return true;
        }

        // A node's own transform in glTF's axes.
        static Matrix4x4 Local(object n)
        {
            var m = MiniJson.Arr(n, "matrix");
            if (m != null && m.Count == 16)
            {
                var mat = new Matrix4x4();
                for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) mat[r, c] = F(m[c * 4 + r]);
                return mat;
            }
            var t = MiniJson.Arr(n, "translation");
            var q = MiniJson.Arr(n, "rotation");
            var s = MiniJson.Arr(n, "scale");
            var tm = t != null && t.Count == 3 ? Matrix4x4.Translate(new Vector3(F(t[0]), F(t[1]), F(t[2]))) : Matrix4x4.identity;
            var rm = q != null && q.Count == 4 ? Matrix4x4.Rotate(new Quaternion(F(q[0]), F(q[1]), F(q[2]), F(q[3]))) : Matrix4x4.identity;
            var sm = s != null && s.Count == 3 ? Matrix4x4.Scale(new Vector3(F(s[0]), F(s[1]), F(s[2]))) : Matrix4x4.identity;
            return tm * rm * sm;
        }

        static float F(object o) => o is double d ? (float)d : 0f;

        static bool Positions(Ctx ctx, int accessor, Matrix4x4 world)
        {
            var accs = MiniJson.Arr(ctx.Json, "accessors");
            if (accs == null || accessor >= accs.Count) return true;
            var a = accs[accessor];
            var min = MiniJson.Arr(a, "min");
            var max = MiniJson.Arr(a, "max");
            if (min != null && max != null && min.Count >= 3 && max.Count >= 3)
            {
                for (int i = 0; i < 8; i++)
                    ctx.Add(world.MultiplyPoint3x4(new Vector3(F((i & 1) == 0 ? min[0] : max[0]), F((i & 2) == 0 ? min[1] : max[1]), F((i & 4) == 0 ? min[2] : max[2]))));
                return true;
            }
            if (ctx.Bin == null) return false;
            // Float VEC3 only, which is what POSITION must be.
            if (MiniJson.Int(a, "componentType") != 5126) return true;
            int view = MiniJson.Int(a, "bufferView"), count = MiniJson.Int(a, "count", 0);
            var views = MiniJson.Arr(ctx.Json, "bufferViews");
            if (view < 0 || views == null || view >= views.Count) return true;
            var v = views[view];
            int at = MiniJson.Int(v, "byteOffset", 0) + MiniJson.Int(a, "byteOffset", 0);
            int stride = Math.Max(12, MiniJson.Int(v, "byteStride", 12));
            for (int i = 0; i < count; i++)
            {
                int o = at + i * stride;
                if (o + 12 > ctx.Bin.Length) break;
                ctx.Add(world.MultiplyPoint3x4(new Vector3(BitConverter.ToSingle(ctx.Bin, o), BitConverter.ToSingle(ctx.Bin, o + 4), BitConverter.ToSingle(ctx.Bin, o + 8))));
            }
            return true;
        }
    }
}
