// OverrideModel.cs - a drop-in model ready to draw: every mesh in it with
// its material and its place relative to the model's root, and the piece
// of the original it follows when a node is named like one.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public static class OverrideLoader
    {
        // Which overrides exist. Scanned from the project folder the first
        // time it is needed, and again after Reset.
        public static OverrideIndex Index;
        // Loads what the runtime cannot, .fbx and .prefab, set by the editor.
        public static Func<string, GameObject> EditorLoad;
        public static int Loaded => templates.Count;

        static readonly Dictionary<string, OverrideModel> plain = new Dictionary<string, OverrideModel>();
        static readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();

        // The folder that Assets/Overrides paths start from: the project in
        // the editor, and in a built player its StreamingAssets, where the
        // build copies the hand-built models.
        public static string ProjectDir => Application.isEditor ? System.IO.Path.GetDirectoryName(Application.dataPath) : Application.streamingAssetsPath;

        public static void Reset(OverrideIndex index = null)
        {
            Index = index;
            plain.Clear();
            foreach (var t in templates.Values) if (t != null) Looks.Release(t);
            templates.Clear();
        }

        public static OverrideIndex EnsureIndex()
        {
            if (Index == null) Index = OverrideIndex.Scan(ProjectDir);
            return Index;
        }

        // The model for a path in the index: a glb is read here, anything
        // else goes to the editor. Kept for the session.
        public static GameObject Template(string path)
        {
            if (templates.TryGetValue(path, out var go)) return go;
            if (path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            {
                go = GlbLoader.Load(System.IO.Path.Combine(ProjectDir, path), out var error);
                if (go == null) Debug.LogWarning($"Override {path} was not read: {error}");
            }
            else if (EditorLoad != null) go = EditorLoad(path);
            templates[path] = go;
            return go;
        }

        // Pieceless lookups (sprite features) are cached by path.
        public static OverrideModel Find(OverrideKind kind, PieceInfo[] pieces, params string[] names)
        {
            string path = EnsureIndex().Find(kind, names);
            if (path == null) return null;
            if (pieces == null && plain.TryGetValue(path, out var cached)) return cached;
            var go = Template(path);
            var o = go != null ? OverrideModel.From(go, pieces, path) : null;
            if (pieces == null) plain[path] = o;
            return o;
        }
    }

    public sealed class OverrideModel
    {
        public struct Part
        {
            public Mesh Mesh;
            public int Submesh;
            public Material Material;
            public Matrix4x4 NodeToRoot;
            public int Piece;           // -1 to ride the model root
            public bool Flat;           // a decal on the ground, which casts no shadow
        }

        public string Path;
        // The top of a lodestone site's plinth, in cells, from the glTF
        // root node's extras ("standTop"); 0 when it has none.
        public float StandTop;
        public readonly List<Part> Parts = new List<Part>();

        public static OverrideModel From(GameObject template, PieceInfo[] pieces, string path)
        {
            var o = new OverrideModel { Path = path };
            var extras = template.GetComponentInChildren<GlbExtras>(true);
            if (extras != null) o.StandTop = extras.StandTop;
            var toRoot = template.transform.worldToLocalMatrix;
            var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (pieces != null)
                for (int i = 0; i < pieces.Length; i++)
                    if (!string.IsNullOrEmpty(pieces[i].Name) && !names.ContainsKey(pieces[i].Name)) names[pieces[i].Name] = i;

            foreach (var r in template.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                if (r is MeshRenderer) { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
                else if (r is SkinnedMeshRenderer sk) mesh = sk.sharedMesh;
                if (mesh == null) continue;
                int piece = -1;
                for (var t = r.transform; t != null && t != template.transform.parent; t = t.parent)
                    if (names.TryGetValue(t.name, out piece)) break; else piece = -1;
                var mats = r.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var mat = s < mats.Length ? mats[s] : mats.Length > 0 ? mats[mats.Length - 1] : null;
                    if (mat == null) continue;
                    var toModel = toRoot * r.transform.localToWorldMatrix;
                    o.Parts.Add(new Part { Mesh = mesh, Submesh = s, Material = mat, NodeToRoot = toModel, Piece = piece, Flat = Height(mesh.bounds, toModel) < 0.1f });
                }
            }
            return o;
        }

        static float Height(Bounds b, Matrix4x4 m)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                float y = m.MultiplyPoint3x4(c).y;
                lo = Mathf.Min(lo, y);
                hi = Mathf.Max(hi, y);
            }
            return hi - lo;
        }

        // Inverse rest transform of each piece, offsets scaled to world units.
        public static Matrix4x4[] RestInverse(PieceInfo[] pieces, float scale)
        {
            var rest = new Matrix4x4[pieces.Length];
            var inv = new Matrix4x4[pieces.Length];
            for (int i = 0; i < pieces.Length; i++)
            {
                var m = Matrix4x4.Translate(pieces[i].Offset * scale);
                int p = pieces[i].Parent;
                rest[i] = p >= 0 && p < i ? rest[p] * m : m;
                inv[i] = rest[i].inverse;
            }
            return inv;
        }
    }
}
