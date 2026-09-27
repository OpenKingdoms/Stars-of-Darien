// AnimOverride.cs - nudges to a model's animation, made in the animation
// editor and kept beside the model as Overrides/Units/<object>.anim.json,
// never in the game files. Each piece can be moved (world units) and
// turned (degrees) in its own frame, for every animation ("all") or for
// one script function. A nudge is applied after the script's pose, and
// the piece's children follow it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class AnimOverride
    {
        public const string All = "all";

        public struct Nudge
        {
            public Vector3 Move, Turn;
            public bool IsZero => Move == Vector3.zero && Turn == Vector3.zero;
            public Matrix4x4 Matrix => Matrix4x4.TRS(Move, Quaternion.Euler(Turn), Vector3.one);
        }

        public string Model = "";
        // piece name, then animation name ("all" or a script function).
        public readonly Dictionary<string, Dictionary<string, Nudge>> Pieces =
            new Dictionary<string, Dictionary<string, Nudge>>(StringComparer.OrdinalIgnoreCase);

        public bool IsEmpty
        {
            get
            {
                foreach (var p in Pieces.Values) foreach (var n in p.Values) if (!n.IsZero) return false;
                return true;
            }
        }

        public Nudge Get(string piece, string animation)
        {
            if (piece != null && Pieces.TryGetValue(piece, out var byAnim) && byAnim.TryGetValue(animation ?? All, out var n)) return n;
            return default;
        }

        public void Set(string piece, string animation, Nudge n)
        {
            if (!Pieces.TryGetValue(piece, out var byAnim)) Pieces[piece] = byAnim = new Dictionary<string, Nudge>(StringComparer.OrdinalIgnoreCase);
            if (n.IsZero) byAnim.Remove(animation ?? All);
            else byAnim[animation ?? All] = n;
            if (byAnim.Count == 0) Pieces.Remove(piece);
        }

        // The nudge for a piece in an animation: the "all" one, then the
        // animation's own on top.
        public Matrix4x4 For(string piece, string animation)
        {
            if (piece == null || !Pieces.TryGetValue(piece, out var byAnim)) return Matrix4x4.identity;
            var m = byAnim.TryGetValue(All, out var a) ? a.Matrix : Matrix4x4.identity;
            if (!string.IsNullOrEmpty(animation) && !string.Equals(animation, All, StringComparison.OrdinalIgnoreCase) &&
                byAnim.TryGetValue(animation, out var b)) m = m * b.Matrix;
            return m;
        }

        // Applies the nudges to piece matrices already in world (or model)
        // space with no scale left in them, parents before children.
        public void Apply(PieceInfo[] pieces, Matrix4x4[] matrices, int count, string animation)
        {
            var correction = new Matrix4x4[count];
            for (int p = 0; p < count && p < pieces.Length; p++)
            {
                int parent = pieces[p].Parent;
                var m = parent >= 0 && parent < p ? correction[parent] * matrices[p] : matrices[p];
                var nudged = m * For(pieces[p].Name, animation);
                correction[p] = nudged * matrices[p].inverse;
                matrices[p] = nudged;
            }
        }

        // ---- Files ----

        public static string PathFor(string projectDir, string objectName) =>
            Path.Combine(projectDir, OverrideIndex.Folder(OverrideKind.Unit), objectName.ToLowerInvariant() + ".anim.json");

        public string ToJson()
        {
            var ci = CultureInfo.InvariantCulture;
            string V(Vector3 v) => string.Format(ci, "[{0:0.####}, {1:0.####}, {2:0.####}]", v.x, v.y, v.z);
            var sb = new StringBuilder();
            sb.Append("{\n  \"model\": \"").Append(Model).Append("\",\n  \"pieces\": {");
            bool firstPiece = true;
            foreach (var p in Pieces)
            {
                sb.Append(firstPiece ? "\n" : ",\n").Append("    \"").Append(p.Key).Append("\": {");
                firstPiece = false;
                bool firstAnim = true;
                foreach (var a in p.Value)
                {
                    sb.Append(firstAnim ? " " : ", ").Append('"').Append(a.Key).Append("\": { \"move\": ").Append(V(a.Value.Move))
                      .Append(", \"turn\": ").Append(V(a.Value.Turn)).Append(" }");
                    firstAnim = false;
                }
                sb.Append(" }");
            }
            sb.Append(firstPiece ? "}\n}\n" : "\n  }\n}\n");
            return sb.ToString();
        }

        public static AnimOverride FromJson(string json)
        {
            var o = new AnimOverride();
            var root = MiniJson.Parse(json);
            o.Model = MiniJson.Text(root, "model", "");
            var pieces = MiniJson.Obj(root, "pieces");
            if (pieces == null) return o;
            foreach (var p in pieces)
            {
                if (!(p.Value is Dictionary<string, object> anims)) continue;
                foreach (var a in anims)
                {
                    var n = new Nudge { Move = Vec(MiniJson.Arr(a.Value, "move")), Turn = Vec(MiniJson.Arr(a.Value, "turn")) };
                    o.Set(p.Key, a.Key, n);
                }
            }
            return o;
        }

        static Vector3 Vec(List<object> a) =>
            a != null && a.Count == 3 ? new Vector3((float)(double)a[0], (float)(double)a[1], (float)(double)a[2]) : Vector3.zero;

        // Loaded once per object name for the session, or null.
        static readonly Dictionary<string, AnimOverride> cache = new Dictionary<string, AnimOverride>(StringComparer.OrdinalIgnoreCase);

        public static AnimOverride Load(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return null;
            if (cache.TryGetValue(objectName, out var o)) return o;
            string path = PathFor(OverrideLoader.ProjectDir, objectName);
            try { o = File.Exists(path) ? FromJson(File.ReadAllText(path)) : null; }
            catch (Exception e) { Debug.LogWarning($"Animation override {path} was not read: {e.Message}"); o = null; }
            cache[objectName] = o;
            return o;
        }

        public static void Forget(string objectName = null)
        {
            if (objectName == null) cache.Clear(); else cache.Remove(objectName);
        }

        public void Save(string objectName)
        {
            string path = PathFor(OverrideLoader.ProjectDir, objectName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Model = objectName;
            if (IsEmpty) { if (File.Exists(path)) File.Delete(path); }
            else File.WriteAllText(path, ToJson());
            Forget(objectName);
        }
    }
}
