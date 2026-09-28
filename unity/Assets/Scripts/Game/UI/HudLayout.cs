// HudLayout.cs - where every part of the battle HUD goes. The plan is the
// original's 640x480 in-game dialog (araingame.gui): a 128 cp sidebar on
// the right with the minimap over the orders, a 49 cp strip along the
// bottom, and the build menu's 64x48 cells over the play area. One factor,
// S screen pixels per classic pixel (cp), scales it all. Rects are in cp,
// x right and y down from the canvas's top left, as the .gui files write
// them. No Unity objects, so tests can check every screen size.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.UI
{
    public sealed class HudLayout
    {
        public const float SidebarW = 128f, BlockH = 352f, StripH = 49f;
        public const float CellW = 64f, CellH = 48f, OrderSize = 29f, WeaponSize = 32f, HitMin = 32f;
        public const float BandW = 6f, StripBand = 4f, MinMapH = 96f, MapMaxW = 112f;
        public const float BodyFloor = 12f, BadgeFloor = 10f;
        public static readonly int[] ScaleStops = { 60, 70, 80, 90, 100, 115, 130 };
        public const int DefaultScale = 80;
        // What the original's sidebar and strip cover of its 640x480 screen.
        public const float OriginalShare = (SidebarW * 480f + 512f * StripH) / (640f * 480f);

        public readonly int ScreenW, ScreenH, Percent;
        public readonly float S, W, H;
        public readonly Rect Sidebar, Block, MapSlot, MapPanel, Strip, Play;
        // Above 100 percent the slot over the orders gets short, and the
        // minimap hangs left of the sidebar at the top instead.
        public readonly bool MapHangs;

        public HudLayout(int screenW, int screenH, int percent)
        {
            ScreenW = Mathf.Max(1, screenW);
            ScreenH = Mathf.Max(1, screenH);
            Percent = percent;
            S = ScaleFor(ScreenW, ScreenH, percent);
            W = ScreenW / S;
            H = ScreenH / S;
            Sidebar = new Rect(W - SidebarW, 0, SidebarW, H);
            Block = new Rect(W - SidebarW, H - BlockH, SidebarW, BlockH);
            MapSlot = new Rect(W - SidebarW, 0, SidebarW, Mathf.Max(0, H - BlockH));
            Strip = new Rect(0, H - StripH, W - SidebarW, StripH);
            Play = new Rect(0, 0, W - SidebarW, H - StripH);
            MapHangs = MapSlot.height < MinMapH;
            MapPanel = MapHangs ? new Rect(W - 2 * SidebarW, 0, SidebarW, MinMapH) : MapSlot;
        }

        // Screen pixels per cp: by height, so wider screens only widen the
        // play area, never more than a quarter of the width for the sidebar,
        // and never smaller than the original's own pixels.
        public static float ScaleFor(int w, int h, int percent)
        {
            float s = percent / 100f * h / 480f;
            s = Mathf.Min(s, 0.25f * w / SidebarW);
            return Mathf.Max(s, 1f);
        }

        public static int NearestStop(int percent)
        {
            int best = DefaultScale;
            foreach (int p in ScaleStops) if (Mathf.Abs(p - percent) < Mathf.Abs(best - percent)) best = p;
            return best;
        }

        // The world camera's viewport, as Camera.rect takes it (y up).
        public Rect Viewport => new Rect(0, StripH / H, Play.width / W, Play.height / H);

        // How much of the screen the panels cover, 0 to 1.
        public float Covered => (Sidebar.width * H + Strip.width * Strip.height + (MapHangs ? MapPanel.width * MapPanel.height : 0)) / (W * H);

        // A text size in cp that is never under floorPx screen pixels.
        public int Font(float cp, float floorPx) => Mathf.CeilToInt(Mathf.Max(cp, floorPx / S) - 0.001f);

        // A button's clickable rect: its picture, grown to HitMin.
        public static Rect Hit(Rect r)
        {
            float w = Mathf.Max(r.width, HitMin), h = Mathf.Max(r.height, HitMin);
            return new Rect(r.center.x - w / 2, r.center.y - h / 2, w, h);
        }

        public Rect InBlock(Rect local) => new Rect(Block.x + local.x, Block.y + local.y, local.width, local.height);
        public Rect InStrip(Rect local) => new Rect(Strip.x + local.x, Strip.y + local.y, local.width, local.height);

        // ---- The minimap ----

        // The map by its own shape, top-aligned in its panel, right of the band.
        public Rect MapRect(float aspect)
        {
            aspect = Mathf.Max(0.05f, aspect);
            float maxH = Mathf.Max(8f, MapPanel.height - 10f);
            float w = MapMaxW, h = w / aspect;
            if (h > maxH) { h = maxH; w = h * aspect; }
            float left = MapPanel.x + BandW, room = MapPanel.width - BandW;
            return new Rect(left + (room - w) / 2, MapPanel.y + 5, w, h);
        }

        // The carpet panel in whatever of the slot the map leaves, or empty.
        public Rect Carpet(Rect map)
        {
            float top = MapHangs ? MapSlot.y + 5 : map.yMax + 7;
            float bottom = MapSlot.yMax - 7;
            if (bottom - top < 16f) return Rect.zero;
            return new Rect(MapSlot.x + BandW + 6, top, SidebarW - BandW - 12, bottom - top);
        }

        // ---- The order block, relative to its top left ----

        public static readonly Rect Header = new Rect(0, 0, 128, 25);
        public static readonly Rect TopBand = new Rect(BandW, 0, 128 - BandW, 4);
        public static readonly Rect MenuButton = new Rect(12, 6, 42, 16);
        public static readonly Rect Clock = new Rect(58, 6, 56, 16);
        public static readonly Rect CentreBand = new Rect(52, 25, 29, 99);
        public static readonly Rect WeaponFrame = new Rect(BandW + 4, 177, 128 - BandW - 8, 36);
        public static readonly Rect Help = new Rect(14, 269, 104, 34);
        public static readonly Rect Income = new Rect(10, 314, 35, 20);
        public static readonly Rect Ball = new Rect(49, 306, 36, 36);
        public static readonly Rect Spend = new Rect(90, 314, 35, 20);
        public static readonly Rect[] Knots = { new Rect(0, -6, 12, 12), new Rect(116, -6, 12, 12), new Rect(0, BlockH - StripH - 6, 12, 12) };
        public const float GutterY = 211f, GutterH = 10f;

        static readonly Dictionary<string, Rect> slots = new Dictionary<string, Rect>
        {
            ["O1L"] = new Rect(17, 25, 29, 29), ["O1R"] = new Rect(87, 25, 29, 29),
            ["O2L"] = new Rect(17, 60, 29, 29), ["O2R"] = new Rect(87, 60, 29, 29),
            ["O3L"] = new Rect(17, 95, 29, 29), ["O3R"] = new Rect(87, 95, 29, 29),
            ["O4L"] = new Rect(17, 130, 29, 29), ["O4C"] = new Rect(52, 130, 29, 29), ["O4R"] = new Rect(87, 130, 29, 29),
            ["C1"] = new Rect(52, 25, 29, 29), ["C2"] = new Rect(52, 60, 29, 29), ["C3"] = new Rect(52, 95, 29, 29),
            ["W1"] = new Rect(16, 179, 32, 32), ["W2"] = new Rect(51, 179, 32, 32), ["W3"] = new Rect(86, 179, 32, 32),
            ["W1c"] = new Rect(17, 180, 29, 29), ["W2c"] = new Rect(52, 180, 29, 29),
            ["S1"] = new Rect(17, 221, 29, 29), ["S2"] = new Rect(52, 221, 29, 29), ["S3"] = new Rect(87, 221, 29, 29),
        };
        static readonly string[] Overflow = { "C1", "C2", "C3", "O4L", "O4R" };

        public static IEnumerable<string> SlotNames => slots.Keys;
        public static Rect Slot(string name) => slots[name];

        static bool IsWeapon(string id) => id == "PrimaryWeapon" || id == "SecondaryWeapon" || id == "SpecialWeapon";

        // The original's slot for a widget name (legacy:150749-150831).
        static string Wants(string id, bool spells)
        {
            switch (id)
            {
                case "MOVE": return "O1L";
                case "PATROL": return "O1R";
                case "ATTACK": return "O2L";
                case "GUARD": return "O2R";
                case "HEAL": case "LOAD": return "O3L";
                case "CLEAR": case "UNLOAD": return "O3R";
                case "STOP": return "O4C";
                case "PrimaryWeapon": return "W1";
                case "SecondaryWeapon": return "W2";
                case "SpecialWeapon": return "W3";
                case "Uncloaked": return spells ? "O4L" : "W1c";
                case "Cloaked": return spells ? "O4R" : "W2c";
                case "Active": return "W1c";
                case "Inactive": return "W2c";
                case "Offensive": return "S1";
                case "Defensive": return "S2";
                case "Passive": return "S3";
                default: return null;
            }
        }

        // Orders, spells and stances claim their slots before the cloak and
        // gate pairs, which share the weapon row, and anything unknown last.
        static int Rank(string id)
        {
            switch (id)
            {
                case "Uncloaked": case "Cloaked": case "Active": case "Inactive": return 1;
                default: return Wants(id, false) != null ? 0 : 2;
            }
        }

        // Each action id's slot. Where the original's slot is taken (HEAL
        // and LOAD share one) the action overflows into the centre column,
        // then the corners beside STOP. Ids with no room are left out.
        public static Dictionary<string, string> PlaceActions(IReadOnlyList<string> ids)
        {
            bool spells = false;
            foreach (var id in ids) spells |= IsWeapon(id);
            var placed = new Dictionary<string, string>();
            var used = new List<Rect>();
            bool Free(string slot)
            {
                var r = slots[slot];
                foreach (var u in used) if (u.Overlaps(r)) return false;
                return true;
            }
            var ordered = new List<string>(ids);
            var index = new Dictionary<string, int>();
            for (int i = 0; i < ordered.Count; i++) if (!index.ContainsKey(ordered[i])) index[ordered[i]] = i;
            ordered.Sort((a, b) => Rank(a) != Rank(b) ? Rank(a).CompareTo(Rank(b)) : index[a].CompareTo(index[b]));
            foreach (var id in ordered)
            {
                if (placed.ContainsKey(id)) continue;
                string want = Wants(id, spells);
                string got = want != null && Free(want) ? want : null;
                if (got == null) foreach (var o in Overflow) if (Free(o)) { got = o; break; }
                if (got == null) continue;
                placed[id] = got;
                used.Add(slots[got]);
            }
            return placed;
        }

        // ---- The strip, relative to its top left ----

        public float StripW => Strip.width;
        public static readonly Rect EndCap = new Rect(0, StripBand, 58, StripH - StripBand);
        public static readonly Rect Portrait = new Rect(59, 9, 48, 36);
        public static readonly Rect Shield = new Rect(61, 21, 11, 22);
        public static readonly Rect Name = new Rect(114, 7, 126, 18);
        public static readonly Rect HealthTrough = new Rect(110, 28, 106, 6);
        public static readonly Rect ManaTrough = new Rect(110, 36, 106, 6);
        public static readonly Rect Kills = new Rect(218, 27, 31, 16);
        public static readonly Rect Status = new Rect(252, 7, 130, 18);
        public static readonly Rect Numbers = new Rect(252, 27, 130, 16);
        public const float TargetW = 130f;

        // A trough's fill: 97 cp of its 106, 3 cp tall, centred.
        public static Rect Fill(Rect trough) => new Rect(trough.x + 5, trough.y + 1.5f, 97, 3);

        // The target panel sits at the strip's right end, beside the sidebar,
        // where the original has it. A strip narrower than the original's
        // has no room for it.
        public bool HasTarget => StripW >= 512f - 0.01f;
        public Rect Target => HasTarget ? new Rect(StripW - TargetW, 0, TargetW, StripH) : Rect.zero;
        public Rect TargetPip => HasTarget ? new Rect(Target.x + 4, 12, 6, 8) : Rect.zero;
        public Rect TargetName => HasTarget ? new Rect(Target.x + 13, 7, 113, 18) : Rect.zero;
        public Rect TargetHealth => HasTarget ? new Rect(Target.x + 4, 28, 106, 6) : Rect.zero;
        public Rect TargetMana => HasTarget ? new Rect(Target.x + 4, 36, 106, 6) : Rect.zero;
        public Rect TargetShield => HasTarget ? new Rect(Target.x + 114, 25, 11, 22) : Rect.zero;

        // A group's make-up, where the strip is wider than the original's.
        public Rect Group
        {
            get
            {
                float w = StripW - 386 - TargetW - 6;
                return HasTarget && w >= 48 ? new Rect(386, 7, w, 18) : Rect.zero;
            }
        }

        public IEnumerable<(string name, Rect rect)> StripTexts()
        {
            yield return ("Name", Name);
            yield return ("Kills", Kills);
            yield return ("Status", Status);
            yield return ("Numbers", Numbers);
            if (Group.width > 0) yield return ("Group", Group);
            if (HasTarget) yield return ("TargetName", TargetName);
        }

        public IEnumerable<(string name, Rect rect)> StripTroughs()
        {
            yield return ("Health", HealthTrough);
            yield return ("Mana", ManaTrough);
            if (HasTarget) { yield return ("TargetHealth", TargetHealth); yield return ("TargetMana", TargetMana); }
        }

        // ---- The build row ----

        public struct BuildGrid
        {
            public int Count, Cols, Rows, MaxRows, PerPage;
            public float CellW, CellH;
            public bool Paged;
        }

        // Every option at once in the original's 64x48 cells, left to right
        // from the play area's bottom left, rows stacking upward
        // (legacy:150181-150340). Cells shrink before a fourth row, and a
        // page turner appears only past what the original could ever show.
        public BuildGrid Builds(int n)
        {
            float cw = CellW, ch = CellH;
            float room = Play.height - (MapHangs ? MapPanel.height : 0f);
            int Cols(float w) => Mathf.Max(1, Mathf.FloorToInt(Play.width / w + 0.001f));
            int cols = Cols(cw);
            int rows = Mathf.CeilToInt(n / (float)cols);
            while (rows > 3 && cw > 48f)
            {
                cw -= 8f;
                ch = cw * 0.75f;
                cols = Cols(cw);
                rows = Mathf.CeilToInt(n / (float)cols);
            }
            int maxRows = Mathf.Max(1, Mathf.FloorToInt(room / ch + 0.001f));
            bool paged = rows > maxRows;
            return new BuildGrid
            {
                Count = n, Cols = cols, Rows = paged ? maxRows : rows, MaxRows = maxRows,
                PerPage = paged ? Mathf.Max(1, maxRows * cols - 1) : n, CellW = cw, CellH = ch, Paged = paged,
            };
        }

        public Rect BuildCell(in BuildGrid g, int slot) =>
            new Rect(Play.x + (slot % g.Cols) * g.CellW, Play.yMax - (slot / g.Cols + 1) * g.CellH, g.CellW, g.CellH);
    }
}
