// Overrides.cs - drop-in models that replace the originals at load. A file
// in Assets/Overrides/Units or Assets/Overrides/Features named after the
// thing it replaces, in any case, wins over the original: <unitname>.glb,
// <feature>.glb, .gltf, .fbx or .prefab. A prefab beats a glb, which
// beats a gltf, which beats an fbx. Models are in map cells (one unit is
// one cell of 16 engine pixels), Y up, origin on the ground at the anchor.
// See docs/STUDIO.md.
using System;
using System.Collections.Generic;
using System.IO;

namespace OpenKingdomsUnity.Game
{
    public enum OverrideKind { Unit, Feature }

    public sealed class OverrideIndex
    {
        public const string Root = "Assets/Overrides";
        public static readonly string[] Extensions = { ".prefab", ".glb", ".gltf", ".fbx" };

        readonly Dictionary<string, string> best = new Dictionary<string, string>();

        public int Count => best.Count;

        public static string Folder(OverrideKind kind) => Root + (kind == OverrideKind.Unit ? "/Units" : "/Features");

        public static string Key(OverrideKind kind, string name) => (kind == OverrideKind.Unit ? "u:" : "f:") + name.Trim().ToLowerInvariant();

        static int Rank(string ext) => Array.IndexOf(Extensions, ext.ToLowerInvariant());

        // Paths as Unity names them, "Assets/Overrides/Units/araking.glb".
        // Files outside the two folders or of other types are ignored.
        public static OverrideIndex Build(IEnumerable<string> paths)
        {
            var index = new OverrideIndex();
            foreach (var raw in paths)
            {
                var path = raw.Replace('\\', '/');
                string ext = Path.GetExtension(path);
                if (Rank(ext) < 0) continue;
                string dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
                OverrideKind kind;
                if (string.Equals(dir, Folder(OverrideKind.Unit), StringComparison.OrdinalIgnoreCase)) kind = OverrideKind.Unit;
                else if (string.Equals(dir, Folder(OverrideKind.Feature), StringComparison.OrdinalIgnoreCase)) kind = OverrideKind.Feature;
                else continue;
                string key = Key(kind, Path.GetFileNameWithoutExtension(path));
                if (!index.best.TryGetValue(key, out var had) || Rank(ext) < Rank(Path.GetExtension(had)))
                    index.best[key] = path;
            }
            return index;
        }

        // The first of the names that has an override, or null. A unit is
        // tried by unit name then model name, a feature by its name, then
        // its sprite sequence, then its model name.
        public string Find(OverrideKind kind, params string[] names)
        {
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                if (best.TryGetValue(Key(kind, n), out var path)) return path;
            }
            return null;
        }

        // Scans the project folder on disk, for the editor and tests.
        public static OverrideIndex Scan(string projectDir)
        {
            var list = new List<string>();
            foreach (var kind in new[] { OverrideKind.Unit, OverrideKind.Feature })
            {
                string dir = Path.Combine(projectDir, Folder(kind));
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir))
                    list.Add(Folder(kind) + "/" + Path.GetFileName(f));
            }
            return Build(list);
        }
    }
}
