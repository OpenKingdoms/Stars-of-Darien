// Afloat.cs - where a unit over water is drawn. The engine reports every
// unit at the ground under it, which for a ship is the sea floor, so the
// backends lift ships to sit in the surface and hovering units to stand
// on it. Presentation only: the simulation never sees these heights.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public enum FloatKind { None, Ship, Hover }

    public static class Afloat
    {
        // How far a hull's keel sits under the surface, in world units.
        public const float Draft = 0.28f;

        // canhover = 1 in the game's unit files. The engine does not report
        // it yet, so the names stand in until it does.
        static readonly HashSet<string> hovering = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "aragod", "targod", "tarlich", "vergod", "verlihr", "vermage", "zonlord", "zontrain",
        };

        public static FloatKind KindOf(UnitDef d)
        {
            if (d == null || d.IsBuilding) return FloatKind.None;
            if (HasWord(d.Category, "BOAT")) return FloatKind.Ship;
            if (d.Name != null && hovering.Contains(d.Name)) return FloatKind.Hover;
            return FloatKind.None;
        }

        // The height to draw a unit at over ground at that height.
        public static float Height(FloatKind kind, float ground, float sea)
        {
            if (kind == FloatKind.None || sea <= 0) return ground;
            return Mathf.Max(ground, kind == FloatKind.Ship ? sea - Draft : sea);
        }

        static bool HasWord(string text, string word)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var w in text.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries))
                if (string.Equals(w, word, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
