// CardOverride.cs - 3D models for the few original units that are mostly
// a painted card (lodestones, divine lodestones, the Zhon fire and glyph,
// Thesh's stand). Overrides/Generated/Units/<OBJECT>.glb carries in its
// root node's glTF extras the piece it replaces ("replacesPiece") and that
// piece's texture ("replacesTexture"). The unit keeps every other piece,
// hides the replaced one and its inactive twin (<piece>_off or <piece>off),
// and draws the glb once at the unit's origin, facing with it.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class CardOverride
    {
        public const string Folder = OverrideIndex.Root + "/Generated/Units";

        public OverrideModel Model;
        public string ReplacesPiece = "", ReplacesTexture = "";

        public bool Hides(string piece)
        {
            if (string.IsNullOrEmpty(piece) || ReplacesPiece.Length == 0) return false;
            return string.Equals(piece, ReplacesPiece, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(piece, ReplacesPiece + "_off", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(piece, ReplacesPiece + "off", StringComparison.OrdinalIgnoreCase);
        }

        static readonly Dictionary<string, CardOverride> cache = new Dictionary<string, CardOverride>(StringComparer.OrdinalIgnoreCase);

        // The override for a unit's object name, or null.
        public static CardOverride For(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return null;
            if (cache.TryGetValue(objectName, out var c)) return c;
            c = null;
            string path = Path.Combine(OverrideLoader.ProjectDir, Folder, objectName.ToUpperInvariant() + ".glb");
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
                }
            }
            cache[objectName] = c;
            return c;
        }

        public static void Forget() => cache.Clear();
    }
}
