// StudioModel.cs - one dropped model as the glb the game will read, whatever
// it came as (.glb, .gltf, or .fbx and .obj through Unity's importer), with
// its facts for the checks and whether its files changed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenKingdomsUnity.Game.World;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace OpenKingdomsUnity.Studio
{
    public sealed class StudioModel : IDisposable
    {
        public const string DropFolder = "Assets/Overrides/Drop";
        // Copies of models from outside the project, for Unity's importer.
        public const string ImportedFolder = DropFolder + "/Imported";
        public static readonly string[] Native = { ".glb", ".gltf" };
        public static readonly string[] Imported = { ".fbx", ".obj", ".dae" };
        static readonly string[] Pictures = { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".bmp" };

        public string SourcePath;       // the file the artist works on
        public string Name;
        public byte[] Glb;
        public GlbFile File;
        public GameObject Template;     // as GlbLoader reads it, hidden
        public ModelFacts Facts;
        public readonly List<string> Notes = new List<string>();
        // The files it was read from, each with the time and size read.
        readonly List<string> watched = new List<string>();
        readonly Dictionary<string, (DateTime time, long size)> stamps = new Dictionary<string, (DateTime, long)>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> importMissing = new List<string>();

        public static bool CanLoad(string path)
        {
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            return Native.Contains(ext) || Imported.Contains(ext);
        }

        public static string ProjectDir => Path.GetDirectoryName(Application.dataPath);

        public static string Absolute(string path) =>
            Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(ProjectDir, path));

        // Loads a file, or returns null with the reason in plain words.
        public static StudioModel Load(string path, out string error)
        {
            error = null;
            path = Absolute(path);
            if (!System.IO.File.Exists(path)) { error = "There is no file at " + path; return null; }
            if (!CanLoad(path)) { error = "The studio reads .glb, .gltf, .fbx and .obj files."; return null; }
            var m = new StudioModel { SourcePath = path, Name = Path.GetFileNameWithoutExtension(path) };
            m.watched.Add(path);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                if (ext == ".glb")
                {
                    m.MarkRead();
                    m.Glb = System.IO.File.ReadAllBytes(path);
                    m.File = GlbFile.Read(m.Glb, out error);
                    if (m.File == null) error = "It isn't a glTF Binary (.glb) file, or it is damaged. Export it again.";
                }
                else if (ext == ".gltf")
                {
                    m.File = GlbFile.PackGltf(path, out error);
                    if (m.File != null)
                    {
                        m.watched.AddRange(m.File.Sources);
                        m.Glb = m.File.Write();
                    }
                    m.MarkRead();
                }
                else
                {
                    m.Glb = FromUnity(path, m, out error);
                    m.MarkRead();
                    if (m.Glb != null) m.File = GlbFile.Read(m.Glb, out error);
                }
                if (m.File == null) { error ??= "The file did not read."; m.Dispose(); return null; }
                error = GlbCheck.Refuse(m.File);
                if (error != null) { m.Dispose(); return null; }
                m.Template = LoadTemplate(m.Glb, m.Name, out error);
                if (m.Template == null) { error ??= "The file did not read."; m.Dispose(); return null; }
                m.Facts = FactsOf(m.File, m.Template);
                foreach (var miss in m.importMissing) if (!m.Facts.MissingTextures.Contains(miss)) m.Facts.MissingTextures.Add(miss);
                return m;
            }
            catch (Exception e)
            {
                m.Dispose();
                error = "The file did not read: " + e.Message;
                return null;
            }
        }

        // GlbLoader under a name of its own, so a half-built model left by
        // a throw can be found and taken away.
        static GameObject LoadTemplate(byte[] glb, string name, out string error)
        {
            string temp = "studio-load-" + Guid.NewGuid().ToString("N");
            try
            {
                var go = GlbLoader.Load(glb, temp, out error);
                if (go != null) go.name = name;
                return go;
            }
            catch (Exception e)
            {
                foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                    if (go != null && go.name == temp && go.transform.parent == null) DisposeTemplate(go);
                error = "The file did not read: " + e.Message;
                return null;
            }
        }

        // Notes the time and size of every file it was read from, so only a
        // later change reloads it, even when this read failed.
        public void MarkRead()
        {
            foreach (var p in watched.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(p);
                stamps[p] = info.Exists ? (info.LastWriteTimeUtc, info.Length) : (DateTime.MinValue, -1L);
            }
        }

        // True once a file it was read from differs from what was read and
        // has stopped changing, so a half-written export is not read.
        public bool ChangedOnDisk()
        {
            try
            {
                bool changed = false;
                DateTime newest = DateTime.MinValue;
                foreach (var kv in stamps)
                {
                    var info = new FileInfo(kv.Key);
                    if (!info.Exists)
                    {
                        if (kv.Value.size >= 0 && string.Equals(kv.Key, SourcePath, StringComparison.OrdinalIgnoreCase)) return false;
                        continue;
                    }
                    if (info.LastWriteTimeUtc == kv.Value.time && info.Length == kv.Value.size) continue;
                    changed = true;
                    if (info.LastWriteTimeUtc > newest) newest = info.LastWriteTimeUtc;
                }
                return changed && (DateTime.UtcNow - newest).TotalSeconds > 0.3;
            }
            catch (IOException) { return false; }
        }

        // An .fbx or .obj through Unity's importer. One from outside the
        // project is copied in first, with the pictures it names.
        static byte[] FromUnity(string path, StudioModel m, out string error)
        {
            error = null;
            string asset = ToAsset(path);
            if (asset == null || !asset.StartsWith(DropFolder + "/", StringComparison.OrdinalIgnoreCase))
                asset = CopyToDrop(path, m.watched);
            else m.watched.AddRange(Beside(path));
            m.importMissing.AddRange(MissingFromMtl(path));
            StudioDropImport.Missing.Remove(asset);
            AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            if (StudioDropImport.Missing.TryGetValue(asset, out var missing))
                foreach (var p in missing) if (!m.importMissing.Contains(p, StringComparer.OrdinalIgnoreCase)) m.importMissing.Add(p);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
            if (prefab == null) { error = "Unity did not import " + Path.GetFileName(path) + " as a model."; return null; }
            var go = UnityEngine.Object.Instantiate(prefab);
            go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                go.transform.position = Vector3.zero;
                return GlbWriter.Write(go, m.Notes);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // Made through the asset database, so files put in it import.
        public static void EnsureDropFolder() => EnsureFolder(DropFolder);

        static void EnsureFolder(string asset)
        {
            if (AssetDatabase.IsValidFolder(asset)) return;
            string parent = Path.GetDirectoryName(asset).Replace('\\', '/');
            EnsureFolder(parent);
            if (Directory.Exists(Path.Combine(ProjectDir, asset))) AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport);
            else AssetDatabase.CreateFolder(parent, Path.GetFileName(asset));
        }

        // Models the sprite tools made from the player's own game files, which
        // must never go into the shared folders: anything under Generated or
        // the sprite catalog's folder.
        public static bool FromPlayersFiles(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string full = Norm(Absolute(path));
            if (full.StartsWith(Norm(Path.Combine(ProjectDir, OpenKingdomsUnity.Game.OverrideIndex.GeneratedFolder)) + "/", StringComparison.OrdinalIgnoreCase)) return true;
            string catalog = StudioTargets.CatalogDir;
            return !string.IsNullOrEmpty(catalog) && Directory.Exists(catalog) && full.StartsWith(Norm(catalog) + "/", StringComparison.OrdinalIgnoreCase);
        }

        static string Norm(string p) => Path.GetFullPath(p).Replace('\\', '/').TrimEnd('/');

        public static string ToAsset(string path)
        {
            string full = Path.GetFullPath(path).Replace('\\', '/');
            string data = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            return full.StartsWith(data + "/", StringComparison.OrdinalIgnoreCase) ? "Assets" + full.Substring(data.Length) : null;
        }

        // Copies a model into a folder of its own under Drop/Imported, so it
        // never clashes with another of the same name, with an .obj's material
        // file and the pictures beside it or in a textures folder that it
        // names. Returns the copy's asset path.
        public static string CopyToDrop(string path, List<string> sources = null)
        {
            string src = Path.GetFullPath(path);
            string key;
            using (var sha = SHA1.Create())
                key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetDirectoryName(src).ToLowerInvariant()))).Replace("-", "").Substring(0, 8);
            string folder = ImportedFolder + "/" + Path.GetFileNameWithoutExtension(src) + "-" + key.ToLowerInvariant();
            EnsureFolder(folder);
            string dir = Path.Combine(ProjectDir, folder);
            string dest = Path.Combine(dir, Path.GetFileName(src));
            System.IO.File.Copy(src, dest, true);
            foreach (var extra in Beside(src))
            {
                string rel = extra.Substring(Path.GetDirectoryName(src).Length).Replace('\\', '/').TrimStart('/');
                string assetDir = (folder + "/" + Path.GetDirectoryName(rel)).Replace('\\', '/').TrimEnd('/');
                EnsureFolder(assetDir);
                System.IO.File.Copy(extra, Path.Combine(ProjectDir, assetDir, Path.GetFileName(rel)), true);
                sources?.Add(extra);
                // Pictures import first, so the model's importer finds them.
                if (Pictures.Contains(Path.GetExtension(extra).ToLowerInvariant()))
                    AssetDatabase.ImportAsset(assetDir + "/" + Path.GetFileName(rel), ImportAssetOptions.ForceSynchronousImport);
            }
            return folder + "/" + Path.GetFileName(src);
        }

        // The files a model names that sit beside it or in a textures folder:
        // an .obj's .mtl and its pictures, or the pictures an .fbx names.
        static List<string> Beside(string path)
        {
            var list = new List<string>();
            string dir = Path.GetDirectoryName(path);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string names;
            if (ext == ".obj")
            {
                string mtl = Path.ChangeExtension(path, ".mtl");
                if (!System.IO.File.Exists(mtl)) return list;
                list.Add(mtl);
                names = System.IO.File.ReadAllText(mtl);
            }
            else if (ext == ".fbx" || ext == ".dae")
            {
                names = Encoding.ASCII.GetString(System.IO.File.ReadAllBytes(path));
            }
            else return list;
            foreach (var folder in new[] { dir, Path.Combine(dir, "textures"), Path.Combine(dir, "Textures") }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var f in Directory.GetFiles(folder))
                    if (Pictures.Contains(Path.GetExtension(f).ToLowerInvariant()) &&
                        names.IndexOf(Path.GetFileName(f), StringComparison.OrdinalIgnoreCase) >= 0 && !list.Contains(f, StringComparer.OrdinalIgnoreCase))
                        list.Add(f);
            }
            return list;
        }

        // Pictures an .obj's material file names that are not beside it.
        static List<string> MissingFromMtl(string path)
        {
            var list = new List<string>();
            string mtl = Path.ChangeExtension(path, ".mtl");
            if (!string.Equals(Path.GetExtension(path), ".obj", StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(mtl)) return list;
            string dir = Path.GetDirectoryName(path);
            foreach (var raw in System.IO.File.ReadAllLines(mtl))
            {
                string line = raw.Trim();
                if (!line.StartsWith("map_", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("bump", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                string file = parts[parts.Length - 1];
                string name = Path.GetFileName(file.Replace('\\', '/'));
                bool found = new[] { Path.Combine(dir, file), Path.Combine(dir, name), Path.Combine(dir, "textures", name) }.Any(System.IO.File.Exists);
                if (!found && !list.Contains(name)) list.Add(name);
            }
            return list;
        }

        // ---- Facts for the checks ----

        public static ModelFacts FactsOf(GlbFile f, GameObject template)
        {
            var facts = new ModelFacts();
            var o = OverrideModel.From(template, null, "");
            bool first = true;
            foreach (var part in o.Parts)
            {
                var b = part.Mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = part.NodeToRoot.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                    if (first) { facts.Bounds = new Bounds(c, Vector3.zero); first = false; }
                    else facts.Bounds.Encapsulate(c);
                }
            }
            var json = f.Json;
            var meshes = MiniJson.Arr(json, "meshes") ?? new List<object>();
            var accessors = MiniJson.Arr(json, "accessors") ?? new List<object>();
            int Count(int a) => a >= 0 && a < accessors.Count ? MiniJson.Int(accessors[a], "count", 0) : 0;
            foreach (var node in MiniJson.Arr(json, "nodes") ?? new List<object>())
            {
                int mi = MiniJson.Int(node, "mesh");
                if (mi < 0 || mi >= meshes.Count) continue;
                facts.Meshes++;
                foreach (var p in MiniJson.Arr(meshes[mi], "primitives") ?? new List<object>())
                {
                    if (MiniJson.Int(p, "mode", 4) != 4) continue;
                    int verts = Count(MiniJson.Int(MiniJson.Obj(p, "attributes"), "POSITION"));
                    int ind = MiniJson.Int(p, "indices");
                    facts.Vertices += verts;
                    facts.Triangles += (ind >= 0 ? Count(ind) : verts) / 3;
                }
            }
            facts.HasGeometry = facts.Triangles > 0 && !first;
            facts.NodeNames.AddRange(f.NodeNames());
            facts.FromPlayersFiles = GlbCheck.FromPlayersFiles(f);
            foreach (var m in MiniJson.Arr(json, "materials") ?? new List<object>()) facts.MaterialNames.Add(MiniJson.Text(m, "name", "material"));

            var images = MiniJson.Arr(json, "images") ?? new List<object>();
            var views = MiniJson.Arr(json, "bufferViews") ?? new List<object>();
            var clear = new bool[images.Count];
            for (int i = 0; i < images.Count; i++)
            {
                var img = images[i];
                string name = MiniJson.Text(img, "name") ?? MiniJson.Text(img, "uri") ?? "picture " + (i + 1);
                int view = MiniJson.Int(img, "bufferView");
                if (view < 0 || view >= views.Count)
                {
                    string uri = MiniJson.Text(img, "uri");
                    if (uri != null && !f.Missing.Contains(uri)) facts.MissingTextures.Add(uri);
                    continue;
                }
                int off = MiniJson.Int(views[view], "byteOffset", 0), len = MiniJson.Int(views[view], "byteLength", 0);
                var fact = new TextureFact { Name = name };
                if (off >= 0 && len > 0 && off + len <= f.Bin.Length)
                {
                    var bytes = new byte[len];
                    Buffer.BlockCopy(f.Bin, off, bytes, 0, len);
                    var t = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                    fact.Readable = t.LoadImage(bytes);
                    fact.Width = t.width;
                    fact.Height = t.height;
                    clear[i] = fact.Readable && HasClearPixels(t);
                    UnityEngine.Object.DestroyImmediate(t);
                }
                else fact.Readable = false;
                facts.Textures.Add(fact);
            }
            facts.MissingTextures.AddRange(f.Missing);

            var textures = MiniJson.Arr(json, "textures") ?? new List<object>();
            foreach (var m in MiniJson.Arr(json, "materials") ?? new List<object>())
            {
                if (MiniJson.Text(m, "alphaMode", "OPAQUE") != "OPAQUE") continue;
                int tex = MiniJson.Int(MiniJson.Obj(MiniJson.Obj(m, "pbrMetallicRoughness"), "baseColorTexture"), "index");
                int img = tex >= 0 && tex < textures.Count ? MiniJson.Int(textures[tex], "source") : -1;
                if (img >= 0 && img < clear.Length && clear[img]) facts.SolidSeeThrough.Add(MiniJson.Text(m, "name", "material"));
            }
            return facts;
        }

        static bool HasClearPixels(Texture2D t)
        {
            if (!GraphicsFormatUtility.HasAlphaChannel(t.graphicsFormat)) return false;
            foreach (var p in t.GetPixels32()) if (p.a < 128) return true;
            return false;
        }

        public void Dispose()
        {
            DisposeTemplate(Template);
            Template = null;
        }

        // What GlbLoader made for one model only.
        internal static void DisposeTemplate(GameObject template)
        {
            if (template == null) return;
            foreach (var r in template.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    if (m.mainTexture != null) UnityEngine.Object.DestroyImmediate(m.mainTexture);
                    UnityEngine.Object.DestroyImmediate(m);
                }
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) UnityEngine.Object.DestroyImmediate(mf.sharedMesh);
            }
            UnityEngine.Object.DestroyImmediate(template);
        }
    }

    // Models dropped for the studio import with their meshes readable, so
    // the studio can write them out as glb, and with Blender's axes turned
    // into the meshes rather than left on the nodes. Pictures a material
    // names that the importer could not find are noted for the checks.
    public sealed class StudioDropImport : AssetPostprocessor
    {
        public static readonly Dictionary<string, List<string>> Missing = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        static bool InDrop(string path) => path.StartsWith(StudioModel.DropFolder + "/", StringComparison.OrdinalIgnoreCase);

        void OnPreprocessModel()
        {
            if (!InDrop(assetPath) || !(assetImporter is ModelImporter mi)) return;
            mi.isReadable = true;
            mi.bakeAxisConversion = true;
        }

        void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] animations)
        {
            if (!InDrop(assetPath)) return;
            var names = new List<string>();
            description.GetTexturePropertyNames(names);
            foreach (var n in names)
            {
                if (!description.TryGetProperty(n, out TexturePropertyDescription t) || t.texture != null) continue;
                string file = Path.GetFileName((string.IsNullOrEmpty(t.relativePath) ? t.path : t.relativePath) ?? "");
                if (file.Length == 0) continue;
                if (!Missing.TryGetValue(assetPath, out var list)) Missing[assetPath] = list = new List<string>();
                if (!list.Contains(file, StringComparer.OrdinalIgnoreCase)) list.Add(file);
            }
        }
    }
}
