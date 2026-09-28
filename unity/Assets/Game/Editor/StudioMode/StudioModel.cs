// StudioModel.cs - one dropped model as the glb the game will read, whatever
// it came as (.glb, .gltf, or .fbx and .obj through Unity's importer), with
// its facts for the checks and whether its file changed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenKingdomsUnity.Game.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace OpenKingdomsUnity.Studio
{
    public sealed class StudioModel : IDisposable
    {
        public const string DropFolder = "Assets/Overrides/Drop";
        public static readonly string[] Native = { ".glb", ".gltf" };
        public static readonly string[] Imported = { ".fbx", ".obj", ".dae" };

        public string SourcePath;       // the file the artist works on
        public string Name;
        public byte[] Glb;
        public GlbFile File;
        public GameObject Template;     // as GlbLoader reads it, hidden
        public ModelFacts Facts;
        public readonly List<string> Notes = new List<string>();
        DateTime stamp;
        long size;

        public static bool CanLoad(string path)
        {
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            return Native.Contains(ext) || Imported.Contains(ext);
        }

        public static string ProjectDir => Path.GetDirectoryName(Application.dataPath);

        public static string Absolute(string path) =>
            Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(ProjectDir, path));

        // Loads a file, or returns null with the reason.
        public static StudioModel Load(string path, out string error)
        {
            error = null;
            path = Absolute(path);
            if (!System.IO.File.Exists(path)) { error = "There is no file at " + path; return null; }
            if (!CanLoad(path)) { error = "The studio reads .glb, .gltf, .fbx and .obj files."; return null; }
            var m = new StudioModel { SourcePath = path, Name = Path.GetFileNameWithoutExtension(path) };
            m.Remember();
            string ext = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                if (ext == ".glb")
                {
                    m.Glb = System.IO.File.ReadAllBytes(path);
                    m.File = GlbFile.Read(m.Glb, out error);
                }
                else if (ext == ".gltf")
                {
                    m.File = GlbFile.PackGltf(path, out error);
                    if (m.File != null) m.Glb = m.File.Write();
                }
                else
                {
                    m.Glb = FromUnity(path, m.Notes, out error);
                    if (m.Glb != null) m.File = GlbFile.Read(m.Glb, out error);
                }
            }
            catch (Exception e) { error = e.Message; }
            if (m.File == null) { error ??= "The file did not read."; return null; }
            m.Template = GlbLoader.Load(m.Glb, m.Name, out error);
            if (m.Template == null) return null;
            m.Facts = FactsOf(m.File, m.Template);
            return m;
        }

        void Remember()
        {
            var info = new FileInfo(SourcePath);
            stamp = info.LastWriteTimeUtc;
            size = info.Length;
        }

        // True once the file on disk differs from what was loaded and has
        // stopped changing, so a half-written export is not read.
        public bool ChangedOnDisk()
        {
            try
            {
                var info = new FileInfo(SourcePath);
                if (!info.Exists) return false;
                if (info.LastWriteTimeUtc == stamp && info.Length == size) return false;
                return (DateTime.UtcNow - info.LastWriteTimeUtc).TotalSeconds > 0.3;
            }
            catch (IOException) { return false; }
        }

        // An .fbx or .obj through Unity's importer, from the drop folder.
        static byte[] FromUnity(string path, List<string> notes, out string error)
        {
            error = null;
            string asset = ToAsset(path);
            if (asset == null || !asset.StartsWith(DropFolder + "/", StringComparison.OrdinalIgnoreCase))
                asset = CopyToDrop(path);
            AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
            if (prefab == null) { error = "Unity did not import " + Path.GetFileName(path) + " as a model."; return null; }
            var go = UnityEngine.Object.Instantiate(prefab);
            go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                go.transform.position = Vector3.zero;
                return GlbWriter.Write(go, notes);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // Made through the asset database, so files put in it import.
        public static void EnsureDropFolder()
        {
            if (AssetDatabase.IsValidFolder(DropFolder)) return;
            if (!AssetDatabase.IsValidFolder(OpenKingdomsUnity.Game.OverrideIndex.Root)) AssetDatabase.CreateFolder("Assets", "Overrides");
            AssetDatabase.CreateFolder(OpenKingdomsUnity.Game.OverrideIndex.Root, "Drop");
        }

        // Models the sprite tools made from the player's own game files, which
        // must never go into the shared folders.
        public static bool FromPlayersFiles(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string full = Path.GetFullPath(Absolute(path)).Replace(Path.DirectorySeparatorChar, '/');
            string gen = Path.GetFullPath(Path.Combine(ProjectDir, OpenKingdomsUnity.Game.OverrideIndex.GeneratedFolder)).Replace(Path.DirectorySeparatorChar, '/');
            return full.StartsWith(gen + "/", StringComparison.OrdinalIgnoreCase);
        }

        public static string ToAsset(string path)
        {
            string full = Path.GetFullPath(path).Replace('\\', '/');
            string data = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            return full.StartsWith(data + "/", StringComparison.OrdinalIgnoreCase) ? "Assets" + full.Substring(data.Length) : null;
        }

        // Copies a model into the drop folder so Unity imports it, with an
        // .obj's material file and the pictures that names.
        public static string CopyToDrop(string path)
        {
            string dir = Path.Combine(ProjectDir, DropFolder);
            EnsureDropFolder();
            string dest = Path.Combine(dir, Path.GetFileName(path));
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                System.IO.File.Copy(path, dest, true);
            string mtl = Path.ChangeExtension(path, ".mtl");
            if (System.IO.File.Exists(mtl))
            {
                System.IO.File.Copy(mtl, Path.Combine(dir, Path.GetFileName(mtl)), true);
                string text = System.IO.File.ReadAllText(mtl);
                foreach (var f in Directory.GetFiles(Path.GetDirectoryName(path)))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if ((ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga") && text.IndexOf(Path.GetFileName(f), StringComparison.OrdinalIgnoreCase) >= 0)
                        System.IO.File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);
                }
            }
            return DropFolder + "/" + Path.GetFileName(path);
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
            if (Template == null) return;
            // What GlbLoader made for this model only.
            foreach (var r in Template.GetComponentsInChildren<Renderer>(true))
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
            UnityEngine.Object.DestroyImmediate(Template);
            Template = null;
        }
    }

    // Models dropped for the studio import with their meshes readable, so
    // the studio can write them out as glb, and with Blender's axes turned
    // into the meshes rather than left on the nodes.
    public sealed class StudioDropImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(StudioModel.DropFolder + "/", StringComparison.OrdinalIgnoreCase) || !(assetImporter is ModelImporter mi)) return;
            mi.isReadable = true;
            mi.bakeAxisConversion = true;
        }
    }
}
