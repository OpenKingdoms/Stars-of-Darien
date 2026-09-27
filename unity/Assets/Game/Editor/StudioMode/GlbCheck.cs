// GlbCheck.cs - what the game's glb reader cannot read, found before it
// tries, in plain words, and the stamp the sprite tools put on models made
// from the player's own files.
using System;
using System.Collections.Generic;
using System.Linq;
using OpenKingdomsUnity.Game.World;

namespace OpenKingdomsUnity.Studio
{
    public static class GlbCheck
    {
        public const string PlayersFilesKey = "okFromPlayersFiles";

        // Required extensions the reader copes with, since leaving them out
        // only changes the look.
        static readonly string[] Harmless = { "KHR_mesh_quantization", "KHR_texture_transform", "KHR_materials_emissive_strength", "KHR_materials_unlit" };

        // Why the game cannot read the file, or null when it can.
        public static string Refuse(GlbFile f)
        {
            if (f?.Json == null) return "The file is damaged or is not a glTF file.";
            var json = f.Json;
            foreach (var o in MiniJson.Arr(json, "extensionsRequired") ?? new List<object>())
            {
                string ext = o as string ?? "";
                if (ext == "KHR_draco_mesh_compression" || ext == "EXT_meshopt_compression")
                    return "The model is compressed, which the game can't read. In Blender's glTF export, open Data, then Compression, untick it and export again.";
                if (ext == "KHR_texture_basisu" || ext == "EXT_texture_webp" || ext == "EXT_texture_avif")
                    return "Its pictures are stored in a compressed format the game can't read. Export them as PNG or JPEG.";
                if (!Harmless.Contains(ext))
                    return $"The file needs the glTF extension {ext}, which the game can't read. Export without it.";
            }
            var buffers = MiniJson.Arr(json, "buffers") ?? new List<object>();
            if (buffers.Count > 1 || buffers.Any(b => MiniJson.Text(b, "uri") != null))
                return "The model keeps its geometry in a separate .bin file. Export as glTF Binary (.glb), or keep the .bin beside the .gltf.";
            var views = MiniJson.Arr(json, "bufferViews") ?? new List<object>();
            foreach (var v in views)
            {
                int off = MiniJson.Int(v, "byteOffset", 0), len = MiniJson.Int(v, "byteLength", 0);
                if (MiniJson.Int(v, "buffer", 0) != 0 || off < 0 || len < 0 || (long)off + len > f.Bin.Length) return Damaged;
            }
            var accessors = MiniJson.Arr(json, "accessors") ?? new List<object>();
            var materials = MiniJson.Arr(json, "materials") ?? new List<object>();
            var meshes = MiniJson.Arr(json, "meshes") ?? new List<object>();
            var nodes = MiniJson.Arr(json, "nodes") ?? new List<object>();
            bool In(int i, int count) => i >= 0 && i < count;
            string Accessor(int a, bool needed)
            {
                if (a < 0 && !needed) return null;
                if (!In(a, accessors.Count)) return Damaged;
                var acc = accessors[a];
                int view = MiniJson.Int(acc, "bufferView");
                if (view < 0) return "The model's geometry is stored in a way the game can't read, usually compression. In Blender's glTF export, untick Compression and export again.";
                if (!In(view, views.Count)) return Damaged;
                int count = MiniJson.Int(acc, "count", 0), type = MiniJson.Int(acc, "componentType", 5126);
                string shape = MiniJson.Text(acc, "type") ?? "SCALAR";
                int comps = shape == "VEC2" ? 2 : shape == "VEC3" ? 3 : shape == "VEC4" ? 4 : shape == "MAT4" ? 16 : 1;
                int size = type == 5126 || type == 5125 ? 4 : type == 5123 || type == 5122 ? 2 : 1;
                int stride = MiniJson.Int(views[view], "byteStride", 0);
                if (stride <= 0) stride = size * comps;
                long need = count <= 0 ? 0 : (long)MiniJson.Int(acc, "byteOffset", 0) + (long)(count - 1) * stride + size * comps;
                return need > MiniJson.Int(views[view], "byteLength", 0) ? Damaged : null;
            }
            foreach (var mesh in meshes)
                foreach (var p in MiniJson.Arr(mesh, "primitives") ?? new List<object>())
                {
                    if (MiniJson.Int(p, "mode", 4) != 4) continue;
                    var attrs = MiniJson.Obj(p, "attributes");
                    string why = Accessor(MiniJson.Int(attrs, "POSITION"), true)
                        ?? Accessor(MiniJson.Int(p, "indices"), false)
                        ?? Accessor(MiniJson.Int(attrs, "NORMAL"), false)
                        ?? Accessor(MiniJson.Int(attrs, "TEXCOORD_0"), false)
                        ?? Accessor(MiniJson.Int(attrs, "COLOR_0"), false);
                    if (why != null) return why;
                    int mat = MiniJson.Int(p, "material");
                    if (mat >= 0 && !In(mat, materials.Count)) return Damaged;
                }
            foreach (var n in nodes)
            {
                int mesh = MiniJson.Int(n, "mesh");
                if (mesh >= 0 && !In(mesh, meshes.Count)) return Damaged;
                foreach (var c in MiniJson.Arr(n, "children") ?? new List<object>())
                    if (!(c is double d) || !In((int)d, nodes.Count)) return Damaged;
            }
            foreach (var s in MiniJson.Arr(json, "scenes") ?? new List<object>())
                foreach (var c in MiniJson.Arr(s, "nodes") ?? new List<object>())
                    if (!(c is double d) || !In((int)d, nodes.Count)) return Damaged;
            var images = MiniJson.Arr(json, "images") ?? new List<object>();
            foreach (var t in MiniJson.Arr(json, "textures") ?? new List<object>())
            {
                int src = MiniJson.Int(t, "source");
                if (src >= 0 && !In(src, images.Count)) return Damaged;
            }
            foreach (var img in images)
            {
                int view = MiniJson.Int(img, "bufferView");
                if (view >= 0 && !In(view, views.Count)) return Damaged;
            }
            return null;
        }

        const string Damaged = "The file is damaged or cut short. Export it again.";

        // True when the sprite tools stamped the file as made from the
        // player's own game files, on the asset, a scene or any node.
        public static bool FromPlayersFiles(GlbFile f)
        {
            if (f?.Json == null) return false;
            bool Stamped(object o)
            {
                var x = MiniJson.Obj(o, "extras");
                if (x == null || !x.TryGetValue(PlayersFilesKey, out var v)) return false;
                return v is bool b ? b : v is double d ? d != 0 : v is string s && (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase));
            }
            if (Stamped(MiniJson.Obj(f.Json, "asset"))) return true;
            foreach (var s in MiniJson.Arr(f.Json, "scenes") ?? new List<object>()) if (Stamped(s)) return true;
            foreach (var n in MiniJson.Arr(f.Json, "nodes") ?? new List<object>()) if (Stamped(n)) return true;
            return false;
        }
    }
}
