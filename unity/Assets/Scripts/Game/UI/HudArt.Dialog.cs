// HudArt.Dialog.cs - the dialogs' painted parts in the HUD's skin: the
// frame, the purple title band, the plate's four faces, the picker's arrows,
// the close cross, the bar's trough and fill, a field and the focus ring.
using UnityEngine;

namespace OpenKingdomsUnity.Game.UI
{
    public static partial class HudArt
    {
        // Sizes and 9-slice borders of the sheets below, in canvas units.
        public const float DialogFrameSize = 18f, DialogBorder = 8f, BandSize = 12f, BandBorder = 4f;
        public const float PlateSize = 16f, PlateBorder = 5f, TroughSize = 10f, TroughBorder = 3f, RingSize = 10f, RingBorder = 4f;

        // The plate's looks: at rest, lit under the pointer, pressed, and
        // washed out when it cannot be used.
        public const int PlateRest = 0, PlateLit = 1, PlateDown = 2, PlateOff = 3;

        // Keylines are never under a pixel, and a unit wide from 1080p up.
        static float Keyline(float s) => Mathf.Max(1f, 1f / s);

        // Inside distance from a rounded rect's edge, in units.
        static float Inside(float x, float y, float w, float h, float r)
        {
            float qx = Mathf.Abs(x - w / 2f) - (w / 2f - r), qy = Mathf.Abs(y - h / 2f) - (h / 2f - r);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
            return -outside;
        }

        // A dialog's frame, clear inside: an ink keyline, a bevelled gold
        // band three units wide lit from the top left, a keyline, and a gold
        // hairline inside it.
        public static Sheet DialogFrame(float s)
        {
            const float size = DialogFrameSize;
            float ink = Keyline(s), aa = 0.6f / s, hair = Mathf.Max(0.75f, 0.75f / s);
            var hairline = new Color(Gold.r, Gold.g, Gold.b, 0.75f);
            return Paint(size, size, s, (x, y) =>
            {
                float d = Mathf.Min(Mathf.Min(x, size - x), Mathf.Min(y, size - y));
                var c = Color.clear;
                float gold = Band(d, ink, ink + 3f, aa);
                if (gold > 0f)
                {
                    float light = Mathf.Clamp01((ink + 3f - d) / 3f);
                    c = Over(c, Color.Lerp(GoldShadow, GoldHi, x + y < size ? light : 1f - light), gold);
                }
                c = Over(c, Ink, Band(d, 0f, ink, aa));
                c = Over(c, Ink, Band(d, ink + 3f, 2f * ink + 3f, aa));
                return Over(c, hairline, Band(d, 2f * ink + 4f, 2f * ink + 4f + hair, aa));
            });
        }

        // The purple title band: ink keylines along its top and foot, each
        // with a gold rule inside.
        public static Sheet TitleBand(float s)
        {
            const float size = BandSize;
            float ink = Keyline(s), aa = 0.6f / s;
            return Paint(size, size, s, (x, y) =>
            {
                float d = Mathf.Min(y, size - y);
                var c = Color.Lerp(Purple * 1.12f, Purple * 0.86f, y / size);
                c.a = 1f;
                c = Over(c, Ink, Band(d, 0f, ink, aa));
                return Over(c, Gold, Band(d, ink + 1f, 2f * ink + 1f, aa));
            });
        }

        // A plate: a vellum face in a two unit gold bezel inside an ink
        // keyline, its corners eased. Lit has the light face and a bright
        // bezel, pressed the edge's face and a shadowed one.
        public static Sheet PlateFace(float s, int look)
        {
            const float size = PlateSize;
            float ink = Keyline(s), aa = 0.6f / s;
            Color face = look == PlateLit ? Vellum : look == PlateDown ? VellumEdge : look == PlateOff ? Color.Lerp(VellumShade, Vellum, 0.4f) : VellumShade;
            Color hi = look == PlateLit ? GoldHi : look == PlateDown ? GoldShadow : look == PlateOff ? VellumEdge : Color.Lerp(Gold, GoldHi, 0.45f);
            Color lo = look == PlateLit ? Gold : look == PlateDown ? Color.Lerp(GoldShadow, Ink, 0.3f) : look == PlateOff ? VellumEdge : GoldShadow;
            return Paint(size, size, s, (x, y) =>
            {
                float d = Inside(x, y, size, size, 2f);
                if (d < -aa) return Color.clear;
                Color c;
                if (d < ink) c = Ink;
                else if (d < ink + 2f)
                {
                    // Lit from the top left, or from below when pressed in.
                    bool upper = x + y < size;
                    c = look == PlateDown ? (upper ? lo : hi) : (upper ? hi : lo);
                }
                else c = d < ink + 2f + 0.8f / s && look != PlateOff ? Color.Lerp(face, Ink, 0.25f) : face;
                c.a = Mathf.Clamp01((d + aa) / (2f * aa));
                return c;
            });
        }

        // The focus ring a keyboard puts round a row: gold highlight on an
        // ink hairline, clear inside.
        public static Sheet Ring(float s)
        {
            const float size = RingSize;
            float ink = Keyline(s), aa = 0.6f / s;
            return Paint(size, size, s, (x, y) =>
            {
                float d = Inside(x, y, size, size, 2.5f);
                if (d < -aa) return Color.clear;
                var c = Over(Color.clear, Ink, Band(d, 0f, ink, aa) * 0.7f);
                return Over(c, GoldHi, Band(d, ink, ink + 2f, aa));
            });
        }

        // A bar's trough: vellum shade in an ink keyline, shaded along its top.
        public static Sheet Trough(float s)
        {
            const float size = TroughSize;
            float ink = Keyline(s), aa = 0.6f / s;
            return Paint(size, size, s, (x, y) =>
            {
                float d = Mathf.Min(Mathf.Min(x, size - x), Mathf.Min(y, size - y));
                var c = Color.Lerp(VellumShade * 0.82f, VellumShade, Mathf.Clamp01((y - ink) / 2f));
                c.a = 1f;
                return Over(c, Ink, Band(d, 0f, ink, aa));
            });
        }

        // A bar's gold, lit along its top and shadowed along its foot.
        public static Sheet GoldFill(float hCp, float s)
        {
            float ink = Keyline(s) * 0.75f;
            return Paint(4f, hCp, s, (x, y) =>
            {
                float t = y / hCp;
                var c = t < 0.35f ? Color.Lerp(GoldHi, Gold, t / 0.35f) : Color.Lerp(Gold, GoldShadow, (t - 0.35f) / 0.65f);
                if (hCp - y < ink) c = Color.Lerp(c, Ink, 0.5f);
                c.a = 1f;
                return c;
            });
        }

        // A field to type in: vellum in an ink keyline, gold highlight when
        // it has the keys.
        public static Sheet Field(float s, bool focused)
        {
            const float size = TroughSize;
            float ink = Keyline(s), aa = 0.6f / s;
            return Paint(size, size, s, (x, y) =>
            {
                float d = Mathf.Min(Mathf.Min(x, size - x), Mathf.Min(y, size - y));
                var c = Vellum;
                c = Over(c, focused ? GoldHi : Ink, Band(d, 0f, focused ? ink + 1f : ink, aa));
                if (focused) c = Over(c, Ink, Band(d, 0f, ink * 0.6f, aa));
                return c;
            });
        }

        // A small gold arrowhead for the picker, pointing left or right.
        public static Sheet PickerArrow(float w, float h, float s, bool right, bool lit)
        {
            float aa = 0.6f / s, ink = Keyline(s);
            return Paint(w, h, s, (x, y) =>
            {
                float u = right ? x : w - x, half = h / 2f;
                // The tip at u = w, the foot at u = 0, as wide as the height.
                float edge = (w - u) / w * half - Mathf.Abs(y - half);
                float d = Mathf.Min(edge * w / Mathf.Sqrt(w * w + half * half), u);
                if (d < -aa) return Color.clear;
                Color c = d < ink ? Ink : Color.Lerp(lit ? GoldHi : Color.Lerp(Gold, GoldHi, 0.4f), lit ? Gold : GoldShadow, y / h);
                c.a = Mathf.Clamp01((d + aa) / (2f * aa));
                return c;
            });
        }

        // The close cross at a title band's end: two gold strokes in ink.
        public static Sheet Cross(float size, float s, bool lit)
        {
            float aa = 0.6f / s, ink = Keyline(s), core = size * 0.07f, half = size / 2f, arm = size * 0.3f;
            return Paint(size, size, s, (x, y) =>
            {
                float px = x - half, py = y - half;
                float along1 = (px + py) / 1.4142f, across1 = (px - py) / 1.4142f;
                float d1 = Mathf.Max(Mathf.Abs(across1) - core, Mathf.Abs(along1) - arm);
                float d2 = Mathf.Max(Mathf.Abs(along1) - core, Mathf.Abs(across1) - arm);
                float d = Mathf.Min(d1, d2);
                var c = Over(Color.clear, Ink, Cover(d, ink, aa));
                return Over(c, lit ? GoldHi : Color.Lerp(Gold, GoldHi, 0.35f), Cover(d, 0f, aa));
            });
        }
    }
}
