// HudArt.Lobby.cs - the menus' own pieces in the HUD's Carolingian skin:
// the gold clasp and curls of the original's panels, a panel to stand in
// for its art when the player's files are not there, a door niche, a
// checkbox, a scroll rail and its arrows. Painted at the menu's scale.
using UnityEngine;

namespace OpenKingdomsUnity.Game.UI
{
    public static partial class HudArt
    {
        // A dark panel with a gold frame, for 9-slicing with a border of
        // PanelBorderCp: a keyline, a bevelled gold band, a keyline and a
        // hairline inside, round a smoky purple ground.
        public const float PanelBorderCp = 7f;

        public static Sheet PanelFrame(float s)
        {
            const float size = 2f * PanelBorderCp + 2f;
            float ink = Mathf.Max(0.6f, 1f / s), aa = 0.6f / s;
            var ground = Purple * 0.55f;
            ground.a = 0.86f;
            return Paint(size, size, s, (x, y) =>
            {
                float d = Mathf.Min(Mathf.Min(x, size - x), Mathf.Min(y, size - y));
                var c = ground;
                float gold = Band(d, ink, 3f, aa);
                if (gold > 0f)
                {
                    float light = Mathf.Clamp01((3f - d) / 3f);
                    var g = Color.Lerp(GoldShadow, GoldHi, x + y < size ? light : 1f - light);
                    c = Over(c, g, gold);
                }
                c = Over(c, Ink, Band(d, 0f, ink, aa));
                c = Over(c, Ink, Band(d, 3f, 3f + ink, aa));
                c = Over(c, new Color(Gold.r, Gold.g, Gold.b, 0.55f), Band(d, 5f, 5f + ink * 0.8f, aa));
                return c;
            });
        }

        // The clasp over a panel's top edge: a gold bar with a garnet boss.
        public static Sheet Clasp(float s)
        {
            const float w = 34f, h = 10f;
            float aa = 0.6f / s, ink = Mathf.Max(0.6f, 1f / s);
            var boss = Boss(8f, s, Garnet);
            return Paint(w, h, s, (x, y) =>
            {
                float dy = Mathf.Abs(y - h / 2f), dx = Mathf.Abs(x - w / 2f);
                float bar = Mathf.Min(h * 0.28f - dy, w / 2f - 1f - dx);
                var c = Color.clear;
                if (bar > -aa)
                {
                    var g = Color.Lerp(GoldHi, GoldShadow, Mathf.Clamp01(0.5f + (y - h / 2f) / (h * 0.6f)));
                    c = Over(c, bar < ink ? Ink : g, Mathf.Clamp01((bar + aa) / (2f * aa)));
                }
                // The boss's own sheet, 9 cp square, over the middle.
                float bx = x - (w / 2f - 4.5f), by = y - (h / 2f - 4.5f);
                if (bx >= 0f && by >= 0f && bx < 9f && by < 9f)
                {
                    int px = Mathf.Clamp(Mathf.FloorToInt(bx / 9f * boss.W), 0, boss.W - 1);
                    int py = Mathf.Clamp(Mathf.FloorToInt(by / 9f * boss.H), 0, boss.H - 1);
                    var b = boss.Px[(boss.H - 1 - py) * boss.W + px];
                    c = Over(c, b, b.a);
                }
                return c;
            });
        }

        // A gold scroll curl, turning in, for a panel's lower corners.
        public static Sheet Curl(float s, bool mirror)
        {
            const float size = 11f;
            float aa = 0.6f / s, core = 0.9f, ink = Mathf.Max(0.45f, 0.8f / s);
            return Paint(size, size, s, (x, y) =>
            {
                float px = (mirror ? size - x : x) - size * 0.55f, py = y - size * 0.45f;
                float r = Mathf.Sqrt(px * px + py * py), a = Mathf.Atan2(py, px);
                // An Archimedean spiral: radius grows 1.3 cp a turn.
                float turn = (r - 0.6f) / 1.3f - (a + Mathf.PI) / (2f * Mathf.PI);
                float d = Mathf.Abs(turn - Mathf.Round(turn)) * 1.3f;
                if (r > size * 0.46f) return Color.clear;
                var g = Color.Lerp(GoldHi, GoldShadow, Mathf.Clamp01(r / (size * 0.46f)));
                var c = Over(Color.clear, Ink, Cover(d, core + ink, aa));
                return Over(c, g, Cover(d, core, aa));
            });
        }

        // An arched niche for a door the player's files do not supply:
        // purple inside a gold arch with ink keylines, a pointed top.
        public static Sheet Arch(float w, float h, float s)
        {
            float aa = 0.6f / s, ink = Mathf.Max(0.6f, 1f / s), band = 4f;
            float r = w * 0.62f;
            return Paint(w, h, s, (x, y) =>
            {
                float spring = r;
                float dx = x - w / 2f;
                float d;
                if (y > spring) d = Mathf.Min(w / 2f - Mathf.Abs(dx), h - y);
                else
                {
                    // Two circles meeting in a point: the pointed arch.
                    float cxL = w - r, cxR = r;
                    float dl = r - Vector2.Distance(new Vector2(x, y), new Vector2(cxL, spring));
                    float dr = r - Vector2.Distance(new Vector2(x, y), new Vector2(cxR, spring));
                    d = Mathf.Min(dl, dr, w / 2f - Mathf.Abs(dx));
                }
                if (d < -aa) return Color.clear;
                Color c = d < ink ? Ink : d < band ? Color.Lerp(GoldHi, GoldShadow, Mathf.Clamp01(y / h)) : d < band + ink ? Ink : Color.Lerp(Purple * 0.9f, Purple * 0.45f, Mathf.Clamp01(y / h));
                c.a = Mathf.Clamp01((d + aa) / (2f * aa));
                return c;
            });
        }

        // A checkbox as the original's: a gold ring, a stone set in it when on.
        public static Sheet Check(float size, float s, bool on)
        {
            if (on) return Boss(size - 1f, s, Emerald);
            float aa = 0.6f / s, r = (size - 1f) / 2f, ink = Mathf.Max(0.5f, 0.9f / s);
            return Paint(size, size, s, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(r, r));
                if (d > r + aa) return Color.clear;
                var g = Color.Lerp(GoldHi, GoldShadow, Mathf.Clamp01(0.5f + (x + y - 2f * r) / (2.2f * r)));
                Color c = d > r - ink || (d > r - 1.6f && d < r - 1.6f + ink) ? Ink : d > r - 1.6f ? g : Purple * 0.4f;
                c.a = Cover(d, r, aa);
                return c;
            });
        }

        // A scroll rail: a dark channel between gold keylines.
        public static Sheet Rail(float w, float h, float s)
        {
            float aa = 0.6f / s, ink = Mathf.Max(0.5f, 0.9f / s);
            return Paint(w, h, s, (x, y) =>
            {
                float d = Mathf.Min(x, w - x);
                var c = Purple * 0.35f;
                c.a = 0.9f;
                c = Over(c, Gold, Band(d, ink, ink + 1.2f, aa));
                return Over(c, Ink, Band(d, 0f, ink, aa));
            });
        }

        // A small gold arrowhead for a rail's end, pointing up or down.
        public static Sheet Arrow(float size, float s, bool up)
        {
            float aa = 0.6f / s, ink = Mathf.Max(0.5f, 0.9f / s);
            return Paint(size, size, s, (x, y) =>
            {
                float v = up ? y : size - y;
                float half = v / size * size * 0.45f;
                float d = Mathf.Min(half - Mathf.Abs(x - size / 2f), Mathf.Min(v - 1f, size - 2f - v));
                if (d < -aa) return Color.clear;
                Color c = d < ink ? Ink : Color.Lerp(GoldHi, GoldShadow, v / size);
                c.a = Mathf.Clamp01((d + aa) / (2f * aa));
                return c;
            });
        }
    }
}
