// OverrideWriter.cs - writes a studio model where the game finds it, with
// the fixes and tweaks baked in: Features/<feature>.glb, Units/<OBJECT>.glb,
// and for a unit card a <OBJECT>.json naming the piece and texture.
using System;
using System.Collections.Generic;
using System.IO;
using OpenKingdomsUnity.Game;

namespace OpenKingdomsUnity.Studio
{
    public static class OverrideWriter
    {
        // The project-relative path the model goes to, or null.
        public static string PathFor(StudioTarget t)
        {
            if (t == null) return null;
            switch (t.Kind)
            {
                case TargetKind.Feature:
                    return string.IsNullOrEmpty(t.Name) ? null : OverrideIndex.Folder(OverrideKind.Feature) + "/" + Safe(t.Name) + ".glb";
                case TargetKind.Unit:
                case TargetKind.UnitCard:
                    string obj = string.IsNullOrEmpty(t.ObjectName) ? t.Name : t.ObjectName;
                    return string.IsNullOrEmpty(obj) ? null : OverrideIndex.Folder(OverrideKind.Unit) + "/" + Safe(obj).ToUpperInvariant() + ".glb";
                default:
                    return null;
            }
        }

        public static string SidecarFor(string glbPath) => Path.ChangeExtension(glbPath, ".json");

        static string Safe(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }

        // The glb as it will go in: the studio's fix wrapped round the scene
        // and the tweaks baked in.
        public static byte[] Prepare(byte[] glb, StudioFix fix, MaterialTweaks tweaks, out string error)
        {
            var f = GlbFile.Read(glb, out error);
            if (f == null) return null;
            if (fix.IsIdentity && (tweaks == null || tweaks.IsDefault)) return glb;
            f.Wrap(fix);
            if (tweaks != null) f.Bake(tweaks);
            return f.Write();
        }

        // Writes under projectDir and returns the project-relative paths written.
        public static List<string> Write(string projectDir, StudioTarget t, byte[] glb, StudioFix fix, MaterialTweaks tweaks, out string error)
        {
            var written = new List<string>();
            string rel = PathFor(t);
            if (rel == null) { error = "Pick what this model replaces first."; return written; }
            if (t.Kind == TargetKind.UnitCard && string.IsNullOrEmpty(t.ReplacesPiece)) { error = "Pick the piece this card model replaces."; return written; }
            var bytes = Prepare(glb, fix, tweaks, out error);
            if (bytes == null) return written;
            string full = Path.Combine(projectDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, bytes);
            written.Add(rel);
            string sidecar = SidecarFor(full);
            if (t.Kind == TargetKind.UnitCard)
            {
                var json = new Dictionary<string, object> { ["replacesPiece"] = t.ReplacesPiece, ["replacesTexture"] = t.ReplacesTexture ?? "" };
                File.WriteAllText(sidecar, JsonText.Write(json, true) + "\n");
                written.Add(SidecarFor(rel));
            }
            else if (t.IsUnit && File.Exists(sidecar))
            {
                // A whole unit model is not a card, so an old sidecar goes.
                File.Delete(sidecar);
                if (File.Exists(sidecar + ".meta")) File.Delete(sidecar + ".meta");
            }
            error = null;
            return written;
        }
    }
}
