// StudioTypes.cs - what Studio Mode works with: the thing a model is meant
// to replace, the fixes applied to it, the material tweaks, and the facts
// the checks read from it.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    public enum TargetKind { None, Feature, Unit, UnitCard }

    // What the dropped model replaces in the game.
    [Serializable]
    public sealed class StudioTarget
    {
        public TargetKind Kind;
        public string Name = "";          // the feature, or the unit's name
        public string ObjectName = "";    // the unit's model, which names its override file
        public string Description = "";
        public string Side = "";
        public Vector2Int Footprint = Vector2Int.one;
        public float Height;              // cells, from the definition, 0 when unknown
        public float Width;               // cells across the original picture, 0 when unknown
        public string SpriteFile;         // the original's picture from the sprite catalog
        public Vector2 SpriteSize, Hotspot;
        public List<string> Maps = new List<string>();
        public int UnitDef = -1, FeatureDef = -1;
        public string ReplacesPiece = "", ReplacesTexture = "";
        public bool ToScale = true;       // false for the stand-in world's made-up things
        public bool Typed;                // typed in by hand, not from the game

        // How tall the original is drawn, in cells, and where that came from,
        // once the studio has it on the stage.
        [NonSerialized] public float DrawnHeight;
        [NonSerialized] public string DrawnFrom;

        public string Label => Kind == TargetKind.None ? "nothing yet"
            : string.IsNullOrEmpty(Description) ? Name : $"{Name} ({Description})";

        public bool IsUnit => Kind == TargetKind.Unit || Kind == TargetKind.UnitCard;

        public StudioTarget Clone()
        {
            var t = (StudioTarget)MemberwiseClone();
            t.Maps = new List<string>(Maps ?? new List<string>());
            return t;
        }
    }

    // Changes the studio makes to a model before it goes in: a uniform
    // scale, then quarter turns clockwise seen from above, then a move.
    [Serializable]
    public struct StudioFix
    {
        public float Scale;
        public int QuarterTurns;
        public Vector3 Offset;

        public static StudioFix None => new StudioFix { Scale = 1f };

        public bool IsIdentity => Mathf.Approximately(Scale, 1f) && QuarterTurns % 4 == 0 && Offset.sqrMagnitude < 1e-10f;

        public Matrix4x4 Matrix => Matrix4x4.TRS(Offset, Quaternion.Euler(0, QuarterTurns * 90f, 0), Vector3.one * Scale);

        public StudioFix Turned(int quarters)
        {
            var f = this;
            f.Offset = Quaternion.Euler(0, quarters * 90f, 0) * Offset;
            f.QuarterTurns = ((QuarterTurns + quarters) % 4 + 4) % 4;
            return f;
        }

        public StudioFix Scaled(float k)
        {
            var f = this;
            f.Scale *= k;
            f.Offset *= k;
            return f;
        }

        public StudioFix Moved(Vector3 d)
        {
            var f = this;
            f.Offset += d;
            return f;
        }

        // A box after the fix, as the box around its moved corners.
        public Bounds Apply(Bounds b)
        {
            var m = Matrix;
            var r = new Bounds(m.MultiplyPoint3x4(b.min), Vector3.zero);
            for (int i = 0; i < 8; i++)
                r.Encapsulate(m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z)));
            return r;
        }
    }

    // The Experiment panel's material changes, over every material.
    [Serializable]
    public sealed class MaterialTweaks
    {
        public Color Tint = Color.white;
        public float Brightness = 1f;
        public float Roughness = -1f;     // -1 keeps each material's own
        public float Emission;

        public bool IsDefault => Tint == Color.white && Mathf.Approximately(Brightness, 1f) && Roughness < 0 && Emission <= 0;

        public Color Apply(Color c) => new Color(c.r * Tint.r * Brightness, c.g * Tint.g * Brightness, c.b * Tint.b * Brightness, c.a);

        public MaterialTweaks Copy() => (MaterialTweaks)MemberwiseClone();
    }

    public sealed class TextureFact
    {
        public string Name;
        public int Width, Height;
        public bool Readable = true;      // false when the picture is in a format the game cannot read
    }

    // What the checks need to know about a model, before any fix.
    public sealed class ModelFacts
    {
        public Bounds Bounds;             // cells, around everything drawn, in the model's own space
        public bool HasGeometry;
        public int Triangles, Vertices, Meshes;
        public readonly List<TextureFact> Textures = new List<TextureFact>();
        public readonly List<string> MissingTextures = new List<string>();
        public readonly List<string> NodeNames = new List<string>();
        public readonly List<string> MaterialNames = new List<string>();
        // Opaque materials whose picture has clear parts, which draw solid.
        public readonly List<string> SolidSeeThrough = new List<string>();
        // Stamped by the sprite tools as made from the player's own files.
        public bool FromPlayersFiles;
    }
}
