// ContentCheck.cs - what a built player carries, and whether any of it could
// hold the original game's pixels. It lists every texture, mesh, font and
// sound Unity packed by the asset it came from, and every file under
// StreamingAssets. A model there must be hand-built: its pictures made by
// our own generators (okGenerated) or painted at load from the player's
// files (okPaint), by the rules of tools/sprite-replace/okpaint.py. Carved
// models keep the original's pixels and live in folders that never ship.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenKingdomsUnity.Game.World;

namespace OpenKingdomsUnity.Studio
{
    public static class ContentCheck
    {
        // Where models with the original's pixels baked in are kept. A build
        // that carries anything from these fails.
        public static readonly string[] CarvedFolders = { "Overrides/Generated", "Overrides/Drop", "OKReplace", "owner-pending", "sprite-replace" };

        // The original game's own kinds of file, which never ship.
        public static readonly string[] GameFileKinds = { ".hpi", ".ufo", ".ccx", ".gp3", ".gaf", ".3do", ".tnt", ".pcx", ".cob", ".fbi", ".gui", ".ota", ".kmp", ".tdf" };

        // Where Unity's packed textures, meshes, fonts and sounds may come
        // from: Unity and its render pipeline, our shaders and settings, and
        // the three open fonts.
        public static readonly string[] PackedSources =
        {
            "Packages/", "Resources/unity_builtin_extra", "Library/unity default resources", "Built-in",
            "Assets/Game/Resources/Fonts/", "Assets/Game/Resources/Shaders/", "Assets/Engine/Resources/Shaders/",
            "Assets/Game/Rendering/", "Assets/Game/Shaders/", "Assets/DefaultVolumeProfile.asset", "Assets/UniversalRenderPipelineGlobalSettings.asset",
        };

        // The kinds of packed object the report lists.
        public static readonly string[] PackedKinds = { "Texture2D", "Texture3D", "Texture2DArray", "Cubemap", "CubemapArray", "Sprite", "Mesh", "Font", "AudioClip", "VideoClip" };

        public enum Kind { Generated, PaintedAtLoad, Plain, Data, Problem }

        // Unity writes this into StreamingAssets for its service packages.
        public const string UnityServicesFile = "UnityServicesProjectConfiguration.json";

        public sealed class Entry
        {
            public string Path;
            public Kind Kind;
            public long Bytes;
            public string Note;
        }

        public struct Packed
        {
            public string Type, Source;
            public long Bytes;
        }

        public sealed class Report
        {
            public readonly List<Entry> Streaming = new List<Entry>();
            public readonly List<Packed> Assets = new List<Packed>();
            public readonly List<string> Problems = new List<string>();
            public readonly List<(string path, long bytes)> Files = new List<(string, long)>();
            public bool Passed => Problems.Count == 0;
        }

        public static bool IsCarved(string path)
        {
            string p = (path ?? "").Replace('\\', '/');
            foreach (var f in CarvedFolders)
                if (p.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static bool IsGameFile(string path) =>
            Array.IndexOf(GameFileKinds, System.IO.Path.GetExtension(path ?? "").ToLowerInvariant()) >= 0;

        // ---- One model ----

        // What a .glb is, and why it may not ship when it may not.
        public static Kind Glb(byte[] glb, List<string> problems)
        {
            if (glb == null || glb.Length < 20 || BitConverter.ToUInt32(glb, 0) != 0x46546C67 || BitConverter.ToUInt32(glb, 16) != 0x4E4F534A)
            {
                problems.Add("not a glb file");
                return Kind.Problem;
            }
            int n = BitConverter.ToInt32(glb, 12);
            if (n <= 0 || 20 + n > glb.Length)
            {
                problems.Add("its JSON chunk is cut short");
                return Kind.Problem;
            }
            var root = MiniJson.Parse(Encoding.UTF8.GetString(glb, 20, n)) as Dictionary<string, object>;
            if (root == null)
            {
                problems.Add("its JSON does not read");
                return Kind.Problem;
            }
            var textures = MiniJson.Arr(root, "textures") ?? new List<object>();
            var images = MiniJson.Arr(root, "images") ?? new List<object>();
            var allowed = new HashSet<int>();
            bool painted = false;
            int before = problems.Count;
            foreach (var mo in MiniJson.Arr(root, "materials") ?? new List<object>())
            {
                var m = mo as Dictionary<string, object>;
                if (m == null) continue;
                var extras = MiniJson.Obj(m, "extras") ?? new Dictionary<string, object>();
                var used = TexturesOf(m).Where(t => t >= 0 && t < textures.Count).ToList();
                var sources = used.Select(t => MiniJson.Int(textures[t], "source", -1)).ToList();
                if (extras.ContainsKey("okPaint"))
                {
                    painted = true;
                    if (used.Count > 0) problems.Add($"painted material {MiniJson.Text(m, "name", "?")} still holds a picture");
                }
                else if (extras.TryGetValue("okGenerated", out var g) && (g is bool b && b || g is double d && d != 0))
                    foreach (var s in sources) allowed.Add(s);
            }
            for (int k = 0; k < images.Count; k++)
                if (!allowed.Contains(k)) problems.Add($"picture {MiniJson.Text(images[k], "name", k.ToString())} is not from a material marked okGenerated");
            foreach (var list in new[] { "nodes", "meshes", "scenes" })
                foreach (var o in MiniJson.Arr(root, list) ?? new List<object>())
                    if ((MiniJson.Obj(o, "extras") ?? new Dictionary<string, object>()).ContainsKey("okFromPlayersFiles"))
                        problems.Add("stamped okFromPlayersFiles, a review copy with the player's pixels");
            if (problems.Count > before) return Kind.Problem;
            if (images.Count > 0) return Kind.Generated;
            return painted ? Kind.PaintedAtLoad : Kind.Plain;
        }

        static IEnumerable<int> TexturesOf(Dictionary<string, object> m)
        {
            var pbr = MiniJson.Obj(m, "pbrMetallicRoughness");
            foreach (var k in new[] { "baseColorTexture", "metallicRoughnessTexture" })
                if (pbr != null && MiniJson.Obj(pbr, k) is Dictionary<string, object> r && r.ContainsKey("index")) yield return MiniJson.Int(r, "index");
            foreach (var k in new[] { "normalTexture", "occlusionTexture", "emissiveTexture" })
                if (MiniJson.Obj(m, k) is Dictionary<string, object> r && r.ContainsKey("index")) yield return MiniJson.Int(r, "index");
        }

        // ---- A whole player ----

        // Looks through a built player folder, with the assets Unity says it
        // packed into it.
        public static Report Scan(string playerDir, IEnumerable<Packed> packed)
        {
            var r = new Report();
            playerDir = Path.GetFullPath(playerDir);
            foreach (var f in Directory.GetFiles(playerDir, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                string rel = f.Substring(playerDir.Length).TrimStart('\\', '/').Replace('\\', '/');
                long bytes = new FileInfo(f).Length;
                r.Files.Add((rel, bytes));
                if (IsCarved(rel)) r.Problems.Add($"{rel} is in a folder of carved models, which hold the original's pixels");
                if (IsGameFile(rel)) r.Problems.Add($"{rel} is one of the original game's own files");
                int sa = rel.IndexOf("/StreamingAssets/", StringComparison.OrdinalIgnoreCase);
                if (sa >= 0) r.Streaming.Add(Look(f, rel.Substring(sa + "/StreamingAssets/".Length), bytes, r.Problems));
            }
            foreach (var p in packed ?? Enumerable.Empty<Packed>())
            {
                if (Array.IndexOf(PackedKinds, p.Type) < 0) continue;
                r.Assets.Add(p);
                string src = p.Source ?? "";
                if (IsCarved(src)) r.Problems.Add($"{p.Type} from {src}, a folder of carved models");
                else if (src.StartsWith("Assets/Overrides", StringComparison.OrdinalIgnoreCase)) r.Problems.Add($"{p.Type} from {src}: models ship under StreamingAssets, checked, never packed");
                else if (!PackedSources.Any(s => src.StartsWith(s, StringComparison.OrdinalIgnoreCase))) r.Problems.Add($"{p.Type} from {src}, which is not a source this check knows. Look at it, then add it to ContentCheck.PackedSources");
            }
            return r;
        }

        static Entry Look(string file, string rel, long bytes, List<string> problems)
        {
            var e = new Entry { Path = rel, Bytes = bytes };
            string ext = Path.GetExtension(rel).ToLowerInvariant();
            if (ext == ".glb")
            {
                var why = new List<string>();
                e.Kind = Glb(File.ReadAllBytes(file), why);
                e.Note = string.Join("; ", why);
                foreach (var w in why) problems.Add($"StreamingAssets/{rel}: {w}");
            }
            else if (ext == ".json" && rel.Replace('\\', '/').StartsWith("Assets/Overrides/Units/", StringComparison.OrdinalIgnoreCase))
            {
                e.Kind = Kind.Data;
                e.Note = "numbers for the unit models (flight poses or a card's piece), no pictures";
            }
            else if (string.Equals(rel, UnityServicesFile, StringComparison.OrdinalIgnoreCase))
            {
                e.Kind = Kind.Data;
                e.Note = "Unity's own list of its service packages and their versions, written by the build";
            }
            else
            {
                e.Kind = Kind.Problem;
                e.Note = "not a model or data file this check knows";
                problems.Add($"StreamingAssets/{rel} is not a hand-built model");
            }
            return e;
        }

        // ---- The report ----

        public static string Describe(Report r, string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine(r.Passed ? "RESULT: PASS, nothing in the build holds the original game's pixels." : $"RESULT: FAIL, {r.Problems.Count} problems.");
            foreach (var p in r.Problems) sb.AppendLine("  PROBLEM " + p);
            sb.AppendLine();

            sb.AppendLine("Files under StreamingAssets, by kind:");
            foreach (var g in r.Streaming.GroupBy(e => e.Kind).OrderBy(g => g.Key))
                sb.AppendLine($"  {Label(g.Key)}: {g.Count()} files, {g.Sum(e => e.Bytes) / 1048576.0:0.0} MB");
            sb.AppendLine();

            sb.AppendLine("Textures, meshes, fonts and sounds Unity packed, by source:");
            foreach (var g in r.Assets.GroupBy(a => (a.Type, Where(a.Source))).OrderBy(g => g.Key.Item2).ThenBy(g => g.Key.Type))
                sb.AppendLine($"  {g.Key.Type,-10} {g.Count(),5}  {g.Sum(a => a.Bytes) / 1024.0,9:0} KB  {g.Key.Item2}");
            sb.AppendLine();

            long total = r.Files.Sum(f => f.bytes);
            sb.AppendLine($"Every file in the player ({r.Files.Count} files, {total / 1048576.0:0.0} MB), StreamingAssets models counted above:");
            foreach (var f in r.Files)
                if (f.path.IndexOf("/StreamingAssets/Assets/Overrides/", StringComparison.OrdinalIgnoreCase) < 0)
                    sb.AppendLine($"  {f.bytes,12:N0}  {f.path}");
            sb.AppendLine();

            sb.AppendLine("Every file under StreamingAssets:");
            foreach (var e in r.Streaming)
                sb.AppendLine($"  {Label(e.Kind),-34} {e.Bytes,10:N0}  {e.Path}{(string.IsNullOrEmpty(e.Note) ? "" : "  (" + e.Note + ")")}");
            return sb.ToString();
        }

        static string Label(Kind k)
        {
            switch (k)
            {
                case Kind.Generated: return "hand-built, generated textures";
                case Kind.PaintedAtLoad: return "hand-built, painted at load";
                case Kind.Plain: return "hand-built, colours only";
                case Kind.Data: return "data";
                default: return "PROBLEM";
            }
        }

        // A packed asset's source, shortened for the summary: the package or
        // the asset.
        static string Where(string source)
        {
            if (string.IsNullOrEmpty(source)) return "(built in)";
            if (source.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                var parts = source.Split('/');
                return parts.Length > 1 ? "Packages/" + parts[1] : source;
            }
            return source;
        }
    }
}
