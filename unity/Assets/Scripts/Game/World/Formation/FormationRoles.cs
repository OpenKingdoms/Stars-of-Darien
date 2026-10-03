// FormationRoles.cs - where a unit stands in a formation: its layer
// (ground, water or air), its role (melee in front, archers behind,
// cavalry on the wings, the monarch at the back) and its footprint. The
// stock roster is a table, since categories alone get several units wrong,
// and anything else falls back to rules on its category.
using System;
using System.Collections.Generic;

namespace OpenKingdomsUnity.Game.World
{
    public enum FormationLayer { Ground, Water, Air }

    public enum FormationRole { Melee, Cavalry, Ranged, Caster, Siege, Command }

    // How a unit moves, which decides the ground it can stand on.
    public enum FormationMover { Walker, Hover, Boat, Flyer }

    public struct FormationKind
    {
        public FormationLayer Layer;
        public FormationRole Role;
        public FormationMover Mover;
        public int Footprint;       // cells
        public int Spacing;         // cells a ship's hull needs between slots, 0 for its footprint
        public float Speed;         // FBI maxvelocity, 0 when unknown
    }

    public static class FormationRoles
    {
        public const int DefaultFootprint = 2;

        static FormationKind K(FormationMover m, FormationRole r, int fp, float speed) => new FormationKind
        {
            Layer = LayerOf(m), Role = r, Mover = m, Footprint = fp, Speed = speed,
        };

        const FormationMover G = FormationMover.Walker, H = FormationMover.Hover, W = FormationMover.Boat, A = FormationMover.Flyer;
        const FormationRole Me = FormationRole.Melee, Ca = FormationRole.Cavalry, Ra = FormationRole.Ranged,
            Cs = FormationRole.Caster, Si = FormationRole.Siege, Co = FormationRole.Command;

        // The four kingdoms' units, from their FBIs.
        static readonly Dictionary<string, FormationKind> Stock = new Dictionary<string, FormationKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["ARAARCH"] = K(G, Ra, 2, 1.25f), ["ARABOW"] = K(G, Ra, 2, 1.4f), ["ARABROAD"] = K(G, Me, 2, 1.0f),
            ["ARABUILD"] = K(G, Co, 2, 1.35f), ["ARACAN"] = K(G, Si, 4, 0.55f), ["ARADRAG"] = K(A, Cs, 4, 7f),
            ["ARAFAST"] = K(A, Co, 2, 5f), ["ARAGOD"] = K(H, Me, 3, 1.55f), ["ARAKING"] = K(G, Co, 2, 1.7f),
            ["ARAKNIGH"] = K(G, Ca, 3, 2.9f), ["ARAPAL"] = K(G, Ca, 3, 3.3f), ["ARAPRIE2"] = K(G, Co, 2, 1.1f),
            ["ARAPRIES"] = K(G, Co, 2, 1.1f), ["ARAPULT"] = K(G, Si, 4, 0.85f), ["ARASMITH"] = K(G, Me, 2, 1.05f),
            ["ARASPY"] = K(G, Ra, 2, 2.0f), ["ARASWORD"] = K(G, Me, 2, 1.1f), ["ARAWAR"] = K(W, Ra, 4, 2.9f),

            ["TARARCH"] = K(G, Ra, 2, 1.2f), ["TARBEAK"] = K(A, Ra, 2, 3.5f), ["TARBLACK"] = K(G, Ca, 3, 2.9f),
            ["TARDEMON"] = K(G, Me, 2, 1.9f), ["TARDRAG"] = K(A, Cs, 4, 9f), ["TARFIRE"] = K(G, Si, 3, 1.0f),
            ["TARGARG"] = K(A, Co, 2, 3.9f), ["TARGOD"] = K(H, Me, 3, 1.8f), ["TARKNIGH"] = K(A, Cs, 4, 4.5f),
            ["TARLICH"] = K(H, Me, 2, 1.0f), ["TARMAGE"] = K(G, Cs, 4, 1.9f), ["TARMIND"] = K(G, Cs, 4, 1.65f),
            ["TARNECRO"] = K(G, Co, 2, 1.5f), ["TARPRIE2"] = K(A, Co, 3, 5f), ["TARPRIES"] = K(A, Co, 3, 5f),
            ["TARSHIP"] = K(A, Ra, 3, 2f), ["TARSPOUT"] = K(G, Ra, 3, 1.2f), ["TARTB"] = K(G, Co, 2, 1.45f),
            ["TARTROOP"] = K(G, Me, 2, 1.15f), ["TARWITCH"] = K(G, Cs, 2, 1.35f), ["TARZOM"] = K(G, Me, 2, 1.0f),

            ["VERARCH"] = K(G, Ra, 2, 1.25f), ["VERBALL"] = K(A, Ra, 4, 1.5f), ["VERBERS"] = K(G, Me, 2, 2.5f),
            ["VERCRUS"] = K(G, Me, 2, 1.5f), ["VERDRAG"] = K(A, Cs, 4, 8f), ["VERFLAG"] = K(W, Co, 4, 3.3f),
            ["VERGOD"] = K(H, Me, 3, 1.5f), ["VERHARP"] = K(W, Ra, 4, 3.5f), ["VERKNIGH"] = K(G, Ca, 3, 2.7f),
            ["VERLIEGE"] = K(G, Co, 2, 1.4f), ["VERLIHR"] = K(H, Co, 2, 1.1f), ["VERMAGE"] = K(H, Co, 2, 1.75f),
            ["VERMAN"] = K(W, Ra, 4, 3.1f), ["VERMUSK"] = K(G, Ra, 2, 1.25f), ["VERPAR"] = K(A, Co, 2, 5f),
            ["VERPULT"] = K(G, Si, 4, 0.8f), ["VERSCOUT"] = K(W, Ra, 3, 4f), ["VERSWORD"] = K(G, Me, 2, 1.3f),
            ["VERTRANS"] = K(W, Co, 4, 2.5f), ["VERTRE"] = K(W, Si, 5, 1.8f),

            ["ZONBASIL"] = K(G, Cs, 4, 1.7f), ["ZONBAT"] = K(A, Co, 2, 3.9f), ["ZONDRAG"] = K(A, Cs, 4, 8f),
            ["ZONDRAKE"] = K(A, Cs, 3, 3.5f), ["ZONFLIES"] = K(A, Cs, 3, 4f), ["ZONGIANT"] = K(G, Si, 4, 0.8f),
            ["ZONGOB"] = K(G, Me, 2, 1.4f), ["ZONGOD"] = K(A, Cs, 3, 4.5f), ["ZONGRYP"] = K(A, Ra, 4, 3.7f),
            ["ZONHAND"] = K(G, Co, 2, 1.2f), ["ZONHARP"] = K(A, Cs, 2, 3.5f), ["ZONHUNT"] = K(A, Co, 2, 2.5f),
            ["ZONHURT"] = K(G, Co, 2, 1.5f), ["ZONKRAK"] = K(W, Ra, 4, 3.1f), ["ZONLORD"] = K(G, Co, 2, 1.55f),
        };

        public static FormationLayer LayerOf(FormationMover m) =>
            m == FormationMover.Flyer ? FormationLayer.Air : m == FormationMover.Boat ? FormationLayer.Water : FormationLayer.Ground;

        public static bool IsStock(string name) => name != null && Stock.ContainsKey(name);

        // A ship's hull in pixels as cells across any turn: twice the
        // farthest it reaches from its centre, as the engine keeps ships
        // apart (M-012).
        public static int HullCells(int fore, int aft, int halfBeam)
        {
            double reach = Math.Abs(fore - aft) * 0.5 + Math.Max(halfBeam, (fore + aft) * 0.5);
            return (int)Math.Ceiling(2.0 * reach / 16.0);
        }

        // A unit's kind: the stock table by name, else the rules on its
        // category. Speed and range are FBI units, negative when unknown.
        // canFly and waterClass come from the engine when it has them.
        public static FormationKind Classify(string name, string category, int footprint,
            float speed = -1f, float range = -1f, bool canFly = false, bool waterClass = false, bool hoverClass = false)
        {
            if (name != null && Stock.TryGetValue(name, out var k)) return k;
            var tokens = Tokens(category);
            int fp = footprint > 0 ? footprint : DefaultFootprint;
            FormationMover mover;
            if (canFly || Has(tokens, "FLY")) mover = FormationMover.Flyer;
            else if (waterClass || Has(tokens, "BOAT")) mover = FormationMover.Boat;
            else if (hoverClass) mover = FormationMover.Hover;
            else mover = FormationMover.Walker;
            return new FormationKind { Layer = LayerOf(mover), Mover = mover, Role = RoleOf(tokens, fp, speed, range), Footprint = fp, Speed = speed > 0 ? speed : 0 };
        }

        // The fallback rules, first match wins.
        static FormationRole RoleOf(string[] t, int footprint, float speed, float range)
        {
            if (Has(t, "MONARCH") || Has(t, "BUILDER")) return FormationRole.Command;
            if (Has(t, "GOD")) return FormationRole.Melee;
            if (footprint == 3 && speed >= 2.5f) return FormationRole.Cavalry;
            if (range >= 0 ? range >= 600 && speed >= 0 && speed <= 1.0f : footprint == 4 && Has(t, "BALLISTIC")) return FormationRole.Siege;
            if (Has(t, "MAGIC")) return FormationRole.Caster;
            if (Has(t, "BALLISTIC") || range >= 150) return FormationRole.Ranged;
            if (Has(t, "MELEE") || Has(t, "ATTACK")) return FormationRole.Melee;
            return FormationRole.Command;
        }

        static readonly char[] Space = { ' ', '\t', ',' };

        static string[] Tokens(string category) =>
            string.IsNullOrEmpty(category) ? Array.Empty<string>() : category.Split(Space, StringSplitOptions.RemoveEmptyEntries);

        static bool Has(string[] tokens, string word)
        {
            foreach (var t in tokens) if (string.Equals(t, word, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
