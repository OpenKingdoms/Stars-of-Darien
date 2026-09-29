// CardOverride.cs - 3D models for the few original units that are mostly
// a painted card (lodestones, divine lodestones, the Zhon fire and glyph,
// Thesh's stand). Overrides/Generated/Units/<OBJECT>.glb carries in its
// root node's glTF extras the piece it replaces ("replacesPiece") and that
// piece's texture ("replacesTexture"). The unit keeps every other piece,
// hides the replaced one, its inactive twin (<piece>_off or <piece>off)
// and any logo plate, and draws the glb once at the unit's origin, facing
// with it.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class CardOverride
    {
        public const string Folder = OverrideIndex.Root + "/Generated/Units";
        // A hand-made card, Units/<OBJECT>.glb with its keys in <OBJECT>.json, beats a generated one.
        public const string HandMadeFolder = OverrideIndex.Root + "/Units";

        public OverrideModel Model;
        public string ReplacesPiece = "", ReplacesTexture = "";
        // A lodestone's crystal, which breathes light (LodestonePulse), or null.
        public LodestoneGlow Glow;

        public bool Hides(string piece)
        {
            if (string.IsNullOrEmpty(piece) || ReplacesPiece.Length == 0) return false;
            // The model carries the whole look, so the original's flat logo
            // plates go too; drawn from both sides they show through it.
            return string.Equals(piece, ReplacesPiece, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(piece, ReplacesPiece + "_off", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(piece, ReplacesPiece + "off", StringComparison.OrdinalIgnoreCase) ||
                   piece.IndexOf("logo", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static readonly Dictionary<string, CardOverride> cache = new Dictionary<string, CardOverride>(StringComparer.OrdinalIgnoreCase);

        // The override for a unit's object name, or null.
        public static CardOverride For(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return null;
            if (cache.TryGetValue(objectName, out var c)) return c;
            c = null;
            string path = Path.Combine(OverrideLoader.ProjectDir, HandMadeFolder, objectName.ToUpperInvariant() + ".glb");
            if (!File.Exists(path) || !File.Exists(Path.ChangeExtension(path, ".json"))) path = Path.Combine(OverrideLoader.ProjectDir, Folder, objectName.ToUpperInvariant() + ".glb");
            if (!File.Exists(path)) path = Path.Combine(OverrideLoader.ProjectDir, Folder, objectName + ".glb");
            if (File.Exists(path))
            {
                var go = GlbLoader.Load(path, out var error);
                if (go == null) Debug.LogWarning($"Unit override {path} was not read: {error}");
                else
                {
                    var extras = go.GetComponentInChildren<GlbExtras>(true);
                    c = new CardOverride
                    {
                        Model = OverrideModel.From(go, null, path),
                        ReplacesPiece = extras != null ? extras.ReplacesPiece ?? "" : "",
                        ReplacesTexture = extras != null ? extras.ReplacesTexture ?? "" : "",
                    };
                    ReadSidecar(Path.ChangeExtension(path, ".json"), c);
                    if (LodestonePulse.IsLodestone(objectName)) c.Glow = LodestoneGlow.Of(c.Model);
                }
            }
            cache[objectName] = c;
            return c;
        }

        // The sidecar's keys win over the glb's extras.
        static void ReadSidecar(string path, CardOverride c)
        {
            if (!File.Exists(path)) return;
            try
            {
                var o = MiniJson.Parse(File.ReadAllText(path));
                c.ReplacesPiece = MiniJson.Text(o, "replacesPiece", c.ReplacesPiece) ?? "";
                c.ReplacesTexture = MiniJson.Text(o, "replacesTexture", c.ReplacesTexture) ?? "";
            }
            catch (Exception e) { Debug.LogWarning($"Unit override sidecar {path} was not read: {e.Message}"); }
        }

        // Puts a model in place for an object name, as a test's stand-in.
        public static void Use(string objectName, CardOverride c) => cache[objectName] = c;

        public static void Forget() => cache.Clear();
    }
}
