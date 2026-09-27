// StudioTargets.cs - what a model can replace: features from the sprite
// catalog (made by extract.py from the player's files) and the backend,
// every unit whole, and the few units that stand on a painted card.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEditor;
using UnityEngine;

namespace OpenKingdomsUnity.Studio
{
    public static class StudioTargets
    {
        public const string CatalogKey = "oku.sprites.dir";
        public static string CatalogDir
        {
            get => EditorPrefs.GetString(CatalogKey, "");
            set => EditorPrefs.SetString(CatalogKey, value ?? "");
        }

        // A monarch stands 4 cells tall (araking.3do, tools/sprite-replace/handkit.py).
        public const float MonarchHeight = 4f;

        // The units that are mostly a painted card: the model, the piece a
        // card model stands in for, that piece's texture, and a name for it.
        public static readonly (string obj, string piece, string texture, string what)[] Cards =
        {
            ("ARALODE", "aralode", "araplainlode", "Aramon lodestone"),
            ("ARAMANA", "aramana", "aramanadivinelodestone", "Aramon divine lodestone"),
            ("TARLODE", "tarlode", "Tarlode_tarosianstoneofevilfun", "Taros lodestone"),
            ("TARMANA", "tarlode", "tarmana_tarosmeetsdivine", "Taros divine lodestone"),
            ("VERLODE", "VerLode", "verlode_regularlodestone", "Veruna lodestone"),
            ("VERMANA", "VerLode", "vermana_divinelodestone", "Veruna divine lodestone"),
            ("ZONLODE", "zonlode", "zhonlode", "Zhon lodestone"),
            ("ZONMANA", "zonmana", "Zonmanalodestone", "Zhon divine lodestone"),
            ("ZONFIRE", "zonfire", "zonsmallsacredfire", "Zhon sacred fire"),
            ("ZONGLYPH", "zonglyph", "zondeathtotemwithring", "Zhon glyph"),
            ("NPCTHESH", "base", "theshstand", "Thesh's stand"),
        };

        static bool Mock(IGameBackend b) => b != null && b.Name == "Mock";

        // The catalog's features, with the maps each is on when extract.py
        // recorded them.
        public static List<StudioTarget> FromCatalog(string json, string dir)
        {
            var list = new List<StudioTarget>();
            object root;
            try { root = MiniJson.Parse(json); }
            catch (Exception) { return list; }
            if (!(root is List<object> items)) return list;
            foreach (var o in items)
            {
                var fp = MiniJson.Arr(o, "footprint");
                var sprite = MiniJson.Obj(o, "sprite");
                var hot = MiniJson.Arr(sprite, "hotspot");
                string name = MiniJson.Text(o, "name", "");
                if (name.Length == 0) continue;
                var t = new StudioTarget
                {
                    Kind = TargetKind.Feature, Name = name,
                    Description = MiniJson.Text(o, "description", "") ?? "",
                    Footprint = fp != null && fp.Count == 2 ? new Vector2Int((int)(double)fp[0], (int)(double)fp[1]) : Vector2Int.one,
                    Height = MiniJson.Int(o, "height", 0) / 16f,
                };
                if (sprite != null)
                {
                    t.SpriteSize = new Vector2(MiniJson.Int(sprite, "w", 0), MiniJson.Int(sprite, "h", 0));
                    t.Width = t.SpriteSize.x / 16f;
                    if (hot != null && hot.Count == 2) t.Hotspot = new Vector2((float)(double)hot[0], (float)(double)hot[1]);
                    string png = Path.Combine(dir ?? "", "sprites", name + ".png");
                    if (File.Exists(png)) t.SpriteFile = png;
                }
                foreach (var m in MiniJson.Arr(o, "mapNames") ?? new List<object>()) if (m is string s) t.Maps.Add(s);
                list.Add(t);
            }
            return list;
        }

        public static List<StudioTarget> Catalog()
        {
            string dir = CatalogDir;
            if (string.IsNullOrEmpty(dir)) return new List<StudioTarget>();
            string path = Path.Combine(dir, "catalog.json");
            return File.Exists(path) ? FromCatalog(File.ReadAllText(path), dir) : new List<StudioTarget>();
        }

        // Features: the catalog's first, then any the backend knows that the
        // catalog does not. The stand-in world's are not to scale.
        public static List<StudioTarget> Features(IGameBackend b, List<StudioTarget> catalog)
        {
            var list = new List<StudioTarget>((catalog ?? new List<StudioTarget>()).Select(t => t.Clone()));
            var seen = new HashSet<string>(list.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
            if (b != null)
                foreach (var d in b.FeatureDefs)
                {
                    var hit = list.FirstOrDefault(t => string.Equals(t.Name, d.Name, StringComparison.OrdinalIgnoreCase));
                    if (hit != null) { hit.FeatureDef = d.Id; continue; }
                    if (!seen.Add(d.Name)) continue;
                    list.Add(new StudioTarget
                    {
                        Kind = TargetKind.Feature, Name = d.Name, ObjectName = d.ObjectName ?? "", Description = d.Category ?? "",
                        Footprint = d.Footprint, Height = d.Height, FeatureDef = d.Id, ToScale = !Mock(b),
                    });
                }
            return list;
        }

        // Every unit, or for cards the card units: those the backend has,
        // and the rest by name, so they can be aimed at without the game.
        public static List<StudioTarget> Units(IGameBackend b, bool card)
        {
            var list = new List<StudioTarget>();
            if (!card)
            {
                if (b == null) return list;
                foreach (var d in b.UnitDefs) list.Add(FromDef(b, d, TargetKind.Unit));
                return list;
            }
            foreach (var c in Cards)
            {
                var d = b?.UnitDefs.FirstOrDefault(u => string.Equals(u.ObjectName, c.obj, StringComparison.OrdinalIgnoreCase));
                var t = d != null ? FromDef(b, d, TargetKind.UnitCard) : new StudioTarget { Kind = TargetKind.UnitCard, Name = c.obj, ObjectName = c.obj, Description = c.what };
                t.ReplacesPiece = c.piece;
                t.ReplacesTexture = c.texture;
                list.Add(t);
            }
            return list;
        }

        static StudioTarget FromDef(IGameBackend b, UnitDef d, TargetKind kind) => new StudioTarget
        {
            Kind = kind, Name = d.Name, ObjectName = d.ObjectName ?? d.Name,
            Description = !string.IsNullOrEmpty(d.Title) ? d.Title : d.Description ?? "", Side = d.Side ?? "",
            Footprint = d.Footprint.x > 0 ? d.Footprint : Vector2Int.one, UnitDef = d.Id, ToScale = !Mock(b),
        };

        // The card piece to preselect: the one the table names when the model
        // has it, or else the first piece with an inactive "_off" twin.
        public static string CardPiece(IList<string> pieces, string preferred)
        {
            if (pieces == null || pieces.Count == 0) return preferred ?? "";
            var hit = pieces.FirstOrDefault(p => string.Equals(p, preferred, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
            var set = new HashSet<string>(pieces.Where(p => p != null), StringComparer.OrdinalIgnoreCase);
            return pieces.FirstOrDefault(p => !string.IsNullOrEmpty(p) && (set.Contains(p + "_off") || set.Contains(p + "off"))) ?? preferred ?? "";
        }

        // A target typed in by hand, for things the backend does not know.
        public static StudioTarget Typed(TargetKind kind, string name, Vector2Int footprint, float height, string piece = "")
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || kind == TargetKind.None) return null;
            var t = new StudioTarget { Kind = kind, Name = name, Typed = true, Description = "typed in" };
            if (kind == TargetKind.Feature)
            {
                t.Footprint = new Vector2Int(Mathf.Max(1, footprint.x), Mathf.Max(1, footprint.y));
                t.Height = Mathf.Max(0, height);
            }
            else
            {
                t.ObjectName = name.ToUpperInvariant();
                var card = Cards.FirstOrDefault(c => string.Equals(c.obj, name, StringComparison.OrdinalIgnoreCase));
                if (kind == TargetKind.UnitCard)
                {
                    t.ReplacesPiece = string.IsNullOrEmpty(piece) ? card.piece ?? "" : piece.Trim();
                    t.ReplacesTexture = card.texture ?? "";
                }
            }
            return t;
        }

        // The monarch for scale: a unit named or described as one, or a
        // king, of the first kingdom if it has one.
        public static UnitDef Monarch(IGameBackend b)
        {
            if (b == null) return null;
            string side = b.Sides.Count > 0 ? b.Sides[0].Id : "";
            UnitDef best = null;
            int bestScore = 0;
            foreach (var d in b.UnitDefs)
            {
                string text = $"{d.Name} {d.Title} {d.Description} {d.Category}".ToLowerInvariant();
                int score = text.Contains("monarch") ? 3 : (d.Name ?? "").EndsWith("king", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
                if (score == 0 || d.IsBuilding) continue;
                if (string.Equals(d.Side, side, StringComparison.OrdinalIgnoreCase)) score++;
                if (score > bestScore) { best = d; bestScore = score; }
            }
            return best;
        }

        // The backend map that matches one of the names, ignoring case,
        // spaces and underscores, or null.
        public static MapInfo MapNamed(IGameBackend b, IEnumerable<string> names)
        {
            if (b == null || names == null) return null;
            string Norm(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            foreach (var n in names)
            {
                string want = Norm(Path.GetFileNameWithoutExtension(n));
                foreach (var m in b.Maps)
                    if (Norm(m.Id) == want || Norm(m.Name) == want) return m;
            }
            return null;
        }
    }
}
