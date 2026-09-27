// GlbFile.cs - a binary glTF as JSON plus one binary chunk: packs a .gltf
// into a glb, wraps the scene in a node carrying the studio's fix, and bakes
// material tweaks, without touching the geometry.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    public sealed class GlbFile
    {
        public Dictionary<string, object> Json;
        public byte[] Bin = Array.Empty<byte>();
        // Pictures a .gltf named that were not found, for the checks, and
        // the files it was packed from, for the hot reload.
        public readonly List<string> Missing = new List<string>();
        public readonly List<string> Sources = new List<string>();

        public const string WrapperName = "studio";

        public static GlbFile Read(byte[] glb, out string error)
        {
            error = null;
            if (glb == null || glb.Length < 20 || BitConverter.ToUInt32(glb, 0) != 0x46546C67) { error = "not a glb file"; return null; }
            int jsonLen = BitConverter.ToInt32(glb, 12);
            if (BitConverter.ToUInt32(glb, 16) != 0x4E4F534A || 20 + jsonLen > glb.Length) { error = "the glb has no JSON chunk"; return null; }
            var f = new GlbFile();
            try { f.Json = MiniJson.Parse(Encoding.UTF8.GetString(glb, 20, jsonLen)) as Dictionary<string, object>; }
            catch (Exception e) { error = "the glb's JSON does not read: " + e.Message; return null; }
            if (f.Json == null) { error = "the glb's JSON is not an object"; return null; }
            int at = 20 + jsonLen;
            if (at + 8 <= glb.Length && BitConverter.ToUInt32(glb, at + 4) == 0x004E4942)
            {
                int len = Math.Min(BitConverter.ToInt32(glb, at), glb.Length - at - 8);
                f.Bin = new byte[len];
                Buffer.BlockCopy(glb, at + 8, f.Bin, 0, len);
            }
            return f;
        }

        public byte[] Write()
        {
            if (Bin.Length > 0)
            {
                var buffers = List(Json, "buffers");
                if (buffers.Count == 0) buffers.Add(new Dictionary<string, object>());
                if (buffers[0] is Dictionary<string, object> b0) { b0.Remove("uri"); b0["byteLength"] = (double)Bin.Length; }
            }
            var json = Encoding.UTF8.GetBytes(JsonText.Write(Json));
            int jsonPad = (4 - json.Length % 4) % 4, binPad = (4 - Bin.Length % 4) % 4;
            int total = 12 + 8 + json.Length + jsonPad + (Bin.Length > 0 ? 8 + Bin.Length + binPad : 0);
            var o = new MemoryStream(total);
            void U32(uint v) => o.Write(BitConverter.GetBytes(v), 0, 4);
            U32(0x46546C67); U32(2); U32((uint)total);
            U32((uint)(json.Length + jsonPad)); U32(0x4E4F534A);
            o.Write(json, 0, json.Length);
            for (int i = 0; i < jsonPad; i++) o.WriteByte(0x20);
            if (Bin.Length > 0)
            {
                U32((uint)(Bin.Length + binPad)); U32(0x004E4942);
                o.Write(Bin, 0, Bin.Length);
                for (int i = 0; i < binPad; i++) o.WriteByte(0);
            }
            return o.ToArray();
        }

        public static List<object> List(Dictionary<string, object> o, string key)
        {
            if (o.TryGetValue(key, out var v) && v is List<object> l) return l;
            var n = new List<object>();
            o[key] = n;
            return n;
        }

        // ---- .gltf ----

        // A .gltf with its buffers and pictures, from files beside it or
        // data URIs, as one self-contained glb.
        public static GlbFile PackGltf(string path, out string error)
        {
            error = null;
            Dictionary<string, object> json;
            try { json = MiniJson.Parse(File.ReadAllText(path)) as Dictionary<string, object>; }
            catch (Exception e) { error = "the .gltf does not read: " + e.Message; return null; }
            if (json == null) { error = "the .gltf is not a JSON object"; return null; }
            string dir = Path.GetDirectoryName(path);
            var f = new GlbFile { Json = json };
            var bin = new List<byte>();
            var buffers = List(json, "buffers");
            var bases = new int[buffers.Count];
            for (int i = 0; i < buffers.Count; i++)
            {
                string uri = MiniJson.Text(buffers[i], "uri");
                var bytes = uri != null ? LoadUri(uri, dir, f.Sources) : null;
                if (bytes == null) { error = "the buffer " + (uri ?? "#" + i) + " is missing"; return null; }
                Align(bin);
                bases[i] = bin.Count;
                bin.AddRange(bytes);
            }
            var views = List(json, "bufferViews");
            foreach (var v in views)
            {
                if (!(v is Dictionary<string, object> view)) continue;
                int b = MiniJson.Int(view, "buffer", 0);
                view["byteOffset"] = (double)((b >= 0 && b < bases.Length ? bases[b] : 0) + MiniJson.Int(view, "byteOffset", 0));
                view["buffer"] = 0.0;
            }
            foreach (var img in List(json, "images"))
            {
                if (!(img is Dictionary<string, object> image)) continue;
                string uri = MiniJson.Text(image, "uri");
                if (uri == null) continue;
                image.Remove("uri");
                var bytes = LoadUri(uri, dir, f.Sources);
                if (bytes == null) { f.Missing.Add(Uri.UnescapeDataString(uri)); continue; }
                Align(bin);
                views.Add(new Dictionary<string, object> { ["buffer"] = 0.0, ["byteOffset"] = (double)bin.Count, ["byteLength"] = (double)bytes.Length });
                bin.AddRange(bytes);
                image["bufferView"] = (double)(views.Count - 1);
                if (!image.ContainsKey("mimeType")) image["mimeType"] = uri.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || uri.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : "image/png";
            }
            json["buffers"] = new List<object> { new Dictionary<string, object> { ["byteLength"] = (double)bin.Count } };
            f.Bin = bin.ToArray();
            return f;
        }

        static void Align(List<byte> bin) { while (bin.Count % 4 != 0) bin.Add(0); }

        static byte[] LoadUri(string uri, string dir, List<string> sources)
        {
            if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = uri.IndexOf(',');
                return comma > 0 && uri.Substring(0, comma).EndsWith(";base64") ? Convert.FromBase64String(uri.Substring(comma + 1)) : null;
            }
            string p = Path.Combine(dir ?? "", Uri.UnescapeDataString(uri));
            sources.Add(Path.GetFullPath(p));
            return File.Exists(p) ? File.ReadAllBytes(p) : null;
        }

        // ---- The studio's changes ----

        // Puts the scene's root nodes under one new node holding the fix, so
        // the geometry itself is untouched. A fix of nothing changes nothing.
        public void Wrap(StudioFix fix)
        {
            if (fix.IsIdentity) return;
            var nodes = List(Json, "nodes");
            var scenes = List(Json, "scenes");
            int sceneIndex = Math.Max(0, MiniJson.Int(Json, "scene", 0));
            List<object> roots;
            if (sceneIndex < scenes.Count && scenes[sceneIndex] is Dictionary<string, object> s) roots = List(s, "nodes");
            else
            {
                roots = new List<object>();
                var child = new HashSet<int>();
                foreach (var n in nodes) foreach (var c in MiniJson.Arr(n, "children") ?? new List<object>()) child.Add((int)(double)c);
                for (int i = 0; i < nodes.Count; i++) if (!child.Contains(i)) roots.Add((double)i);
                var scene = new Dictionary<string, object> { ["nodes"] = roots };
                scenes.Clear();
                scenes.Add(scene);
                sceneIndex = 0;
                Json["scene"] = 0.0;
            }
            // glTF's z is the map's south, so z and the turn flip.
            var q = Quaternion.Euler(0, fix.QuarterTurns * 90f, 0);
            var wrapper = new Dictionary<string, object>
            {
                ["name"] = WrapperName,
                ["children"] = new List<object>(roots),
                ["translation"] = new List<object> { (double)fix.Offset.x, (double)fix.Offset.y, (double)-fix.Offset.z },
                ["rotation"] = new List<object> { (double)-q.x, (double)-q.y, (double)q.z, (double)q.w },
                ["scale"] = new List<object> { (double)fix.Scale, (double)fix.Scale, (double)fix.Scale },
            };
            nodes.Add(wrapper);
            roots.Clear();
            roots.Add((double)(nodes.Count - 1));
        }

        // Bakes tint, brightness, roughness and self light into every
        // material, as the game's loader reads them back.
        public void Bake(MaterialTweaks t)
        {
            if (t == null || t.IsDefault) return;
            var mats = List(Json, "materials");
            int plain = mats.Count;
            mats.Add(new Dictionary<string, object> { ["name"] = "studio" });
            foreach (var m in mats)
            {
                if (!(m is Dictionary<string, object> mat)) continue;
                if (!(mat.TryGetValue("pbrMetallicRoughness", out var p) && p is Dictionary<string, object> pbr))
                    mat["pbrMetallicRoughness"] = pbr = new Dictionary<string, object>();
                var f = MiniJson.Arr(pbr, "baseColorFactor");
                var c = f != null && f.Count == 4 ? new Color((float)(double)f[0], (float)(double)f[1], (float)(double)f[2], (float)(double)f[3]) : Color.white;
                c = t.Apply(c);
                pbr["baseColorFactor"] = new List<object> { (double)c.r, (double)c.g, (double)c.b, (double)c.a };
                if (t.Roughness >= 0) pbr["roughnessFactor"] = (double)t.Roughness;
                if (t.Emission > 0)
                {
                    float e = Mathf.Min(1f, t.Emission);
                    mat["emissiveFactor"] = new List<object> { (double)e, (double)e, (double)e };
                    if (t.Emission > 1f)
                    {
                        if (!(mat.TryGetValue("extensions", out var x) && x is Dictionary<string, object> ext)) mat["extensions"] = ext = new Dictionary<string, object>();
                        ext["KHR_materials_emissive_strength"] = new Dictionary<string, object> { ["emissiveStrength"] = (double)t.Emission };
                        var used = List(Json, "extensionsUsed");
                        if (!used.Contains("KHR_materials_emissive_strength")) used.Add("KHR_materials_emissive_strength");
                    }
                }
            }
            // Primitives without a material get a tweaked plain one.
            bool usedPlain = false;
            foreach (var mesh in List(Json, "meshes"))
                foreach (var prim in MiniJson.Arr(mesh, "primitives") ?? new List<object>())
                    if (prim is Dictionary<string, object> pd && !pd.ContainsKey("material")) { pd["material"] = (double)plain; usedPlain = true; }
            if (!usedPlain) mats.RemoveAt(plain);
        }

        // Every node name, for the piece check.
        public List<string> NodeNames()
        {
            var list = new List<string>();
            foreach (var n in List(Json, "nodes")) { var name = MiniJson.Text(n, "name"); if (!string.IsNullOrEmpty(name)) list.Add(name); }
            return list;
        }
    }
}
