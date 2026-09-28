// Afloat.cs - where a unit over water is drawn. Before API 20 the engine
// reported every unit at the ground under it, a ship on the sea floor, and
// now it reports a floater at the sea. Either way the backends draw a
// ship's origin just under the surface and a hovering unit on it.
// Presentation only: the simulation never sees these heights.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public enum FloatKind { None, Ship, Hover }

    public static class Afloat
    {
        // How far a unit's origin sits under the surface when the backends
        // lift a ship. The drawn hull sinks further, to its own waterline
        // (ShipHull).
        public const float Draft = 0.28f;

        // canhover = 1 in the game's unit files. The engine does not report
        // it yet, so the names stand in until it does.
        static readonly HashSet<string> hovering = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "aragod", "targod", "tarlich", "vergod", "verlihr", "vermage", "zonlord", "zontrain",
        };

        // Worked out from the def's fields. UnitDef.Float keeps the answer,
        // so a frame asks this once per def, not once per unit.
        public static FloatKind KindOf(UnitDef d)
        {
            // A flyer keeps its own height, over water as over land.
            if (d == null || d.IsBuilding || d.CanFly) return FloatKind.None;
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

        // How far both backends lift a unit reported at the ground.
        public static float Lift(UnitDef d, float ground, float sea) =>
            d == null ? 0f : Height(d.Float, ground, sea) - ground;

        // Whether a word stands alone in a list split by spaces, tabs or
        // commas, ignoring case, without allocating.
        public static bool HasWord(string text, string word)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word)) return false;
            int from = 0;
            while (from <= text.Length - word.Length)
            {
                int at = text.IndexOf(word, from, StringComparison.OrdinalIgnoreCase);
                if (at < 0) return false;
                int end = at + word.Length;
                if ((at == 0 || Gap(text[at - 1])) && (end == text.Length || Gap(text[end]))) return true;
                from = at + 1;
            }
            return false;
        }

        static bool Gap(char c) => c == ' ' || c == '\t' || c == ',';
    }
}
