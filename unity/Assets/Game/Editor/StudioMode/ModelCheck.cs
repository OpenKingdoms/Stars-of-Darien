// ModelCheck.cs - the plain-words checks a model goes through before it goes
// in the game (size, anchor, turn, budgets, pictures), with one-click fixes
// where they are safe.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    public static class ModelCheck
    {
        public enum Level { Good, Note, Warning, Problem }
        public enum FixKind { None, Recentre, MatchSize, FitFootprint, TurnQuarter, Shrink100, StandOnGround }

        public sealed class Issue
        {
            public Level Level;
            public string Text;
            public FixKind Fix;
            public string FixLabel;
            public override string ToString() => $"{Level}: {Text}";
        }

        public const int FeatureTriangles = 3000, UnitTriangles = 6000, TextureSide = 1024, TextureCount = 4;

        // A base may reach below the ground as a foundation, so the model
        // never floats on a slope, down to this share of its whole height.
        // Within OnGround of the ground line a model stands on it. The pull
        // request check, scripts/check-models.py, uses the same numbers.
        public const float FoundationShare = 0.25f, OnGround = 0.15f;

        // How tall the original stands, in cells, or 0 when unknown or not to
        // scale: a unit's model, a feature as the game draws it, and only
        // failing that its definition's height, which is a rough guide.
        public static float ExpectedHeight(StudioTarget t, Bounds? original)
        {
            if (t == null || !t.ToScale) return 0;
            if (t.IsUnit) return original.HasValue ? original.Value.size.y : 0;
            if (t.DrawnHeight > 0) return t.DrawnHeight;
            if (t.Height > 0) return t.Height;
            return original.HasValue ? original.Value.size.y : 0;
        }

        // True when the height to match is only the definition's.
        public static bool Rough(StudioTarget t) => t != null && !t.IsUnit && t.DrawnHeight <= 0 && t.Height > 0;

        static string HeightSource(StudioTarget t)
        {
            if (t.IsUnit) return "the original model";
            if (Rough(t)) return "the feature's definition, which is only a rough guide";
            switch (t.DrawnFrom)
            {
                case "typed": return "the height you typed";
                case "model": return "the original model";
                default: return "the original picture as the game draws it";
            }
        }

        public static List<Issue> Run(ModelFacts f, StudioFix fix, StudioTarget t, Bounds? original = null, IList<string> pieces = null)
        {
            var list = new List<Issue>();
            void Add(Level l, string text, FixKind k = FixKind.None, string label = null) =>
                list.Add(new Issue { Level = l, Text = text, Fix = k, FixLabel = label });

            if (t == null || t.Kind == TargetKind.None)
                Add(Level.Problem, "Pick what this model replaces, under Replaces, so the studio knows its size and where it goes.");
            else if (t.Kind == TargetKind.UnitCard && string.IsNullOrEmpty(t.ReplacesPiece))
                Add(Level.Problem, "Pick the card piece this model stands in for, under Card piece.");
            if (f == null || !f.HasGeometry)
            {
                Add(Level.Problem, "The model has nothing to draw. Check that the export included the meshes.");
                return list;
            }

            var b = fix.Apply(f.Bounds);
            float across = Mathf.Max(b.size.x, b.size.z), tall = b.size.y;
            float expected = ExpectedHeight(t, original);
            bool sized = false;
            if (Mathf.Max(across, tall) > 150f)
            {
                Add(Level.Warning, $"The model is {Mathf.Max(across, tall):0} cells big, bigger than the whole view. It was probably exported in centimetres.",
                    FixKind.Shrink100, "Make it 100 times smaller");
                sized = true;
            }
            else if (t != null && t.Kind != TargetKind.None && !t.ToScale)
                Add(Level.Note, $"It is {across:0.#} cells across and {tall:0.#} tall. The stand-in world's things are not to scale, so the size is not judged. A monarch in the game stands 4 cells tall.");
            else if (expected > 0 && tall > 0)
            {
                float r = tall / expected;
                string from = HeightSource(t);
                if (r < 0.6f || r > 1.6f)
                {
                    Add(Level.Warning, $"It stands {tall:0.#} cells tall, {Describe(r)} the original ({expected:0.#} cells, from {from}).", FixKind.MatchSize, "Match the original's height");
                    sized = true;
                }
                else Add(Level.Good, $"Its height suits the original: {tall:0.#} cells against {expected:0.#}, from {from}.");
            }
            else if (t != null && t.Kind != TargetKind.None && !t.IsUnit)
            {
                float foot = Mathf.Max(t.Footprint.x, t.Footprint.y);
                if (across > foot * 4f + 2f || across < foot * 0.25f)
                {
                    Add(Level.Warning, $"It is {across:0.#} cells across on a footprint of {t.Footprint.x} by {t.Footprint.y} cells.", FixKind.FitFootprint, "Fit it to the footprint");
                    sized = true;
                }
            }
            if (!sized && t != null && t.Kind == TargetKind.None && across > 0) Add(Level.Note, $"It is {across:0.#} cells across and {tall:0.#} tall. One cell is 16 pixels of the original.");

            // The anchor is where the game stands the model: the middle of its
            // base, on the ground line.
            float slack = Mathf.Max(0.3f, 0.2f * across);
            float off = new Vector2(b.center.x, b.center.z).magnitude;
            float below = -b.min.y, deepest = Mathf.Max(OnGround, FoundationShare * tall);
            if (off > slack)
                Add(Level.Warning, $"The game stands the model on its anchor, and its middle is {off:0.#} cells from the anchor.", FixKind.Recentre, "Centre it on the anchor");
            if (b.min.y > OnGround)
                Add(Level.Warning, $"It floats {b.min.y:0.##} cells above the ground.", FixKind.StandOnGround, "Stand it on the ground");
            else if (below > deepest)
                Add(Level.Warning, $"It sinks {below:0.##} cells into the ground, more than a foundation needs ({deepest:0.##} cells, a quarter of its height), so it was probably exported too low.",
                    FixKind.StandOnGround, "Stand it on the ground");
            else if (below > OnGround)
                Add(Level.Good, $"{(off > slack ? "Its" : "It stands on its anchor, and its")} foundation reaches {below:0.##} cells below the ground, so it never floats on a slope.");
            else if (off <= slack) Add(Level.Good, "It stands on its anchor.");

            // A long footprint or original that the model crosses the other way
            // was probably exported a quarter turn off.
            Vector2 want = Vector2.zero;
            if (t != null && !t.ToScale) { }
            else if (t != null && t.IsUnit && original.HasValue) want = new Vector2(original.Value.size.x, original.Value.size.z);
            else if (t != null && t.Kind == TargetKind.Feature) want = new Vector2(t.Footprint.x, t.Footprint.y);
            if (want.x > 0 && want.y > 0 && b.size.x > 0 && b.size.z > 0)
            {
                float a = want.x / want.y, m = b.size.x / b.size.z;
                if ((a >= 1.5f && m <= 1f / 1.3f) || (a <= 1f / 1.5f && m >= 1.3f))
                    Add(Level.Warning, "It lies across the other way from the original, so it may be turned a quarter.", FixKind.TurnQuarter, "Turn it a quarter");
            }
            Add(Level.Note, "The front faces the classic camera, to the south. If you see its back in the classic view, turn it half way.");

            int budget = t != null && t.Kind == TargetKind.Unit ? UnitTriangles : FeatureTriangles;
            string kind = t == null ? "a feature" : t.Kind == TargetKind.Unit ? "a unit" : t.Kind == TargetKind.UnitCard ? "a card model" : "a feature";
            if (f.Triangles > budget)
                Add(Level.Warning, $"It has {N(f.Triangles)} triangles. Keep {kind} under {N(budget)}, since a map can hold hundreds of them.");
            else Add(Level.Good, $"{N(f.Triangles)} triangles, inside the budget of {N(budget)}.");

            foreach (var tex in f.Textures)
            {
                if (!tex.Readable)
                    Add(Level.Warning, $"The picture {tex.Name} is in a format the game cannot read. Use PNG or JPEG.");
                else if (Mathf.Max(tex.Width, tex.Height) > TextureSide)
                    Add(Level.Warning, $"The picture {tex.Name} is {tex.Width} by {tex.Height}. {TextureSide} on a side is plenty at the game's zoom and keeps memory low.");
            }
            if (f.Textures.Count > TextureCount)
                Add(Level.Note, $"It uses {f.Textures.Count} pictures. Fewer, shared ones draw faster.");
            foreach (var m in f.SolidSeeThrough)
                Add(Level.Warning, $"The picture on {m} has see-through parts, but {m} is set to opaque, so they draw solid. " +
                    "In Blender, plug the picture's Alpha into the shader's Alpha to cut them out.");
            foreach (var m in f.MissingTextures)
                Add(Level.Warning, $"The picture {m} was not found, so that part draws plain. Export as glTF Binary (.glb) so the pictures travel inside, or keep the picture beside the model file.");

            if (t != null && t.Kind == TargetKind.Unit && pieces != null && pieces.Count > 0)
            {
                var names = new HashSet<string>(pieces.Where(p => !string.IsNullOrEmpty(p)), System.StringComparer.OrdinalIgnoreCase);
                var follow = f.NodeNames.Where(names.Contains).Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();
                if (follow.Count > 0) Add(Level.Good, $"{follow.Count} parts follow the original's animation: {string.Join(", ", follow.Take(8))}.");
                else Add(Level.Note, "No part is named like a piece of the original, so the whole model moves as one. Name parts " + string.Join(", ", pieces.Take(6)) + " to animate them.");
            }
            return list;
        }

        static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

        static string Describe(float r)
        {
            if (r > 1f) return r >= 1.95f ? $"about {r:0.#} times as tall as" : "much taller than";
            return r <= 0.55f ? $"only {r * 100f:0}% as tall as" : "much shorter than";
        }

        public static StudioFix Apply(FixKind k, ModelFacts f, StudioFix fix, StudioTarget t, Bounds? original = null)
        {
            if (f == null || !f.HasGeometry) return fix;
            var b = fix.Apply(f.Bounds);
            switch (k)
            {
                case FixKind.Recentre:
                    return Recentred(fix, f);
                case FixKind.StandOnGround:
                    return fix.Moved(new Vector3(0, -b.min.y, 0));
                case FixKind.MatchSize:
                {
                    float want = ExpectedHeight(t, original);
                    if (want <= 0 || b.size.y <= 0) return fix;
                    return Recentred(fix.Scaled(want / b.size.y), f);
                }
                case FixKind.FitFootprint:
                {
                    float across = Mathf.Max(b.size.x, b.size.z), foot = t != null ? Mathf.Max(t.Footprint.x, t.Footprint.y) : 1;
                    if (across <= 0) return fix;
                    return Recentred(fix.Scaled(foot / across), f);
                }
                case FixKind.Shrink100:
                    return Recentred(fix.Scaled(0.01f), f);
                case FixKind.TurnQuarter:
                    return fix.Turned(1);
                default:
                    return fix;
            }
        }

        // Centred across, with the ground line kept where it was exported. A
        // resize scales about the anchor on the ground, so it keeps the
        // ground line and a foundation's share, and centres across after.
        static StudioFix Recentred(StudioFix fix, ModelFacts f)
        {
            var b = fix.Apply(f.Bounds);
            return fix.Moved(new Vector3(-b.center.x, 0, -b.center.z));
        }

        public static bool Blocks(IEnumerable<Issue> issues) => issues.Any(i => i.Level == Level.Problem);
    }
}
