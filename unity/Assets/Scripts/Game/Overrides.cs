// Overrides.cs - drop-in models that replace the originals at load. A file
// in Assets/Overrides/Units or Assets/Overrides/Features named after the
// thing it replaces, in any case, wins over the original: <unitname>.glb,
// <feature>.glb, .gltf, .fbx or .prefab. A prefab beats a glb, which
// beats a gltf, which beats an fbx. Assets/Overrides/Generated holds
// feature models made on the player's machine from their own sprites,
// never committed, and a hand-made one in Features beats them. Models are
// in map cells (one unit is one cell of 16 engine pixels), Y up, origin on
// the ground at the anchor. See docs/STUDIO.md.
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
        public const string GeneratedFolder = Root + "/Generated";

        public static string Key(OverrideKind kind, string name) => (kind == OverrideKind.Unit ? "u:" : "f:") + name.Trim().ToLowerInvariant();

        static int Rank(string ext) => Array.IndexOf(Extensions, ext.ToLowerInvariant());

        // Lower is better: hand-made before generated, then by file type.
        static int Rank(string path, bool generated) => (generated ? 100 : 0) + Rank(Path.GetExtension(path));
        readonly HashSet<string> generatedPaths = new HashSet<string>();

        public bool IsGenerated(string path) => path != null && generatedPaths.Contains(path);

        // Paths as Unity names them, "Assets/Overrides/Units/araking.glb".
        // Files outside the two folders or of other types are ignored.
        public static OverrideIndex Build(IEnumerable<string> paths)
        {
            var index = new OverrideIndex();
            // A unit model with a .json of the same name is a card (CardOverride).
            var all = new List<string>();
            var cards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in paths)
            {
                var path = raw.Replace('\\', '/');
                all.Add(path);
                if (string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Path.GetDirectoryName(path)?.Replace('\\', '/'), Folder(OverrideKind.Unit), StringComparison.OrdinalIgnoreCase))
                    cards.Add(Path.GetFileNameWithoutExtension(path));
            }
            foreach (var path in all)
            {
                string ext = Path.GetExtension(path);
                if (Rank(ext) < 0) continue;
                string dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
                OverrideKind kind;
                bool generated = false;
                if (string.Equals(dir, Folder(OverrideKind.Unit), StringComparison.OrdinalIgnoreCase)) kind = OverrideKind.Unit;
                else if (string.Equals(dir, Folder(OverrideKind.Feature), StringComparison.OrdinalIgnoreCase)) kind = OverrideKind.Feature;
                else if (string.Equals(dir, GeneratedFolder, StringComparison.OrdinalIgnoreCase)) { kind = OverrideKind.Feature; generated = true; }
                else continue;
                if (kind == OverrideKind.Unit && cards.Contains(Path.GetFileNameWithoutExtension(path))) continue;
                if (generated) index.generatedPaths.Add(path);
                string key = Key(kind, Path.GetFileNameWithoutExtension(path));
                if (!index.best.TryGetValue(key, out var had) || Rank(path, generated) < Rank(had, index.generatedPaths.Contains(had)))
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
            foreach (var folder in new[] { Folder(OverrideKind.Unit), Folder(OverrideKind.Feature), GeneratedFolder })
            {
                string dir = Path.Combine(projectDir, folder);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir))
                    list.Add(folder + "/" + Path.GetFileName(f));
            }
            return Build(list);
        }
    }
}
