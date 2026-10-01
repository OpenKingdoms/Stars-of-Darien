// UiKit.cs - the menus' kit built from code: dialogs on vellum with plates,
// pickers, a bar and a help line in the HUD's skin, and the older stone and
// parchment pieces. Fonts render at the final pixel size, sharp at 4K.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Game.UI
{
    public static class UiKit
    {
        public static readonly Color Gold = new Color(0.88f, 0.72f, 0.38f);
        public static readonly Color GoldBright = new Color(1f, 0.88f, 0.55f);
        public static readonly Color Ink = new Color(0.2f, 0.13f, 0.07f);
        public static readonly Color Pale = new Color(0.93f, 0.9f, 0.82f);
        public static readonly Color Dim = new Color(0.65f, 0.6f, 0.52f);

        static Font title, body, uncial;
        static Sprite stone, parchment, frame, button, bar, glow, white;

        public static Font TitleFont => title != null ? title : title = LoadFont("Fonts/Cinzel");
        public static Font BodyFont => body != null ? body : body = LoadFont("Fonts/EBGaramond");
        // Uncial Antiqua (SIL Open Font License), for the HUD's rubrics and names.
        public static Font UncialFont => uncial != null ? uncial : uncial = LoadFont("Fonts/UncialAntiqua");

        static Font LoadFont(string path)
        {
            var f = Resources.Load<Font>(path);
            return f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        public static Sprite Stone => stone != null ? stone : stone = MakeSprite(Paint(256, StonePixel), 0, FilterMode.Bilinear, TextureWrapMode.Repeat);
        public static Sprite Parchment => parchment != null ? parchment : parchment = MakeSprite(Paint(256, ParchmentPixel), 0, FilterMode.Bilinear, TextureWrapMode.Clamp);
        public static Sprite Frame => frame != null ? frame : frame = MakeSprite(Paint(64, FramePixel), 14, FilterMode.Bilinear, TextureWrapMode.Clamp);
        public static Sprite ButtonFace => button != null ? button : button = MakeSprite(Paint(64, ButtonPixel), 12, FilterMode.Bilinear, TextureWrapMode.Clamp);
        public static Sprite BarFill => bar != null ? bar : bar = MakeSprite(Paint(32, BarPixel), 6, FilterMode.Bilinear, TextureWrapMode.Clamp);
        public static Sprite Glow => glow != null ? glow : glow = MakeSprite(Paint(128, GlowPixel), 0, FilterMode.Bilinear, TextureWrapMode.Clamp);
        public static Sprite White => white != null ? white : white = MakeSprite(Paint(4, (x, y, s) => Color.white), 0, FilterMode.Point, TextureWrapMode.Clamp);

        // ---- Painting ----

        static Texture2D Paint(int size, Func<int, int, int, Color> pixel)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = pixel(x, y, size);
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static Sprite MakeSprite(Texture2D tex, int border, FilterMode filter, TextureWrapMode wrap)
        {
            tex.filterMode = filter;
            tex.wrapMode = wrap;
            var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            s.hideFlags = HideFlags.DontSave;
            return s;
        }

        static float Tile(float x, float y, float f, int seed, int size)
        {
            // Value noise that wraps at the texture edge, so stone tiles.
            int period = Mathf.Max(1, Mathf.RoundToInt(size * f));
            float fx = x * f, fy = y * f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            float H(int a, int b) => MockNoise.Hash(((a % period) + period) % period, ((b % period) + period) % period, seed);
            return Mathf.Lerp(Mathf.Lerp(H(x0, y0), H(x0 + 1, y0), tx), Mathf.Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), tx), ty);
        }

        static Color StonePixel(int x, int y, int s)
        {
            float n = Tile(x, y, 1 / 32f, 3, s) * 0.5f + Tile(x, y, 1 / 8f, 4, s) * 0.3f + Tile(x, y, 1 / 2f, 5, s) * 0.2f;
            // Rough blocks with darker joints.
            int row = y / 64;
            int bx = (x + (row % 2) * 48) % 96, by = y % 64;
            float joint = Mathf.Min(Mathf.Min(bx, 96 - bx), Mathf.Min(by, 64 - by));
            float j = Mathf.SmoothStep(0.55f, 1f, Mathf.Clamp01(joint / 4f));
            float v = Mathf.Lerp(0.16f, 0.3f, n) * Mathf.Lerp(0.55f, 1f, j);
            return new Color(v * 1.02f, v * 0.97f, v * 0.9f, 1f);
        }

        static Color ParchmentPixel(int x, int y, int s)
        {
            float n = Tile(x, y, 1 / 32f, 7, s) * 0.6f + Tile(x, y, 1 / 6f, 8, s) * 0.4f;
            float ex = Mathf.Min(x, s - 1 - x) / (float)s, ey = Mathf.Min(y, s - 1 - y) / (float)s;
            float edge = Mathf.Clamp01(Mathf.Min(ex, ey) * 6f);
            var c = Color.Lerp(new Color(0.78f, 0.66f, 0.46f), new Color(0.93f, 0.86f, 0.68f), n);
            c = Color.Lerp(c * 0.72f, c, edge);
            c.a = 1;
            return c;
        }

        // A gold bevelled border, clear in the middle, for 9-slicing.
        static Color FramePixel(int x, int y, int s)
        {
            float d = Mathf.Min(Mathf.Min(x, s - 1 - x), Mathf.Min(y, s - 1 - y));
            if (d > 7) return Color.clear;
            float light = (x < s / 2 ? 0.1f : -0.05f) + (y > s / 2 ? 0.1f : -0.05f);
            float ridge = 1f - Mathf.Abs(d - 3.5f) / 3.5f;
            var c = Color.Lerp(new Color(0.45f, 0.32f, 0.12f), GoldBright, Mathf.Clamp01(ridge * 0.85f + light));
            c.a = d < 0.5f ? 0.8f : 1f;
            return c;
        }

        static Color ButtonPixel(int x, int y, int s)
        {
            var c = StonePixel(x * 4, y * 4, 256);
            float d = Mathf.Min(Mathf.Min(x, s - 1 - x), Mathf.Min(y, s - 1 - y));
            if (d < 2) return Color.Lerp(new Color(0.3f, 0.22f, 0.1f), Gold, d / 2f);
            if (d < 5) c *= y > s / 2 ? 1.35f : 0.8f;
            c.a = 1;
            return c;
        }

        static Color BarPixel(int x, int y, int s)
        {
            float t = y / (float)(s - 1);
            var c = Color.Lerp(new Color(0.55f, 0.36f, 0.08f), GoldBright, Mathf.SmoothStep(0, 1, t));
            float d = Mathf.Min(Mathf.Min(x, s - 1 - x), Mathf.Min(y, s - 1 - y));
            if (d < 1) c *= 0.6f;
            c.a = 1;
            return c;
        }

        static Color GlowPixel(int x, int y, int s)
        {
            float dx = (x - s / 2f) / (s / 2f), dy = (y - s / 2f) / (s / 2f);
            float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
            return new Color(1, 1, 1, a * a);
        }

        // ---- Building ----

        public static Canvas MakeCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            canvas.pixelPerfect = false;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            return canvas;
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        // Anchors in 0..1 of the parent, then pixel offsets from them.
        public static RectTransform Place(this RectTransform rt, float x0, float y0, float x1, float y1,
            float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        public static RectTransform Fill(this RectTransform rt, float inset = 0) => rt.Place(0, 0, 1, 1, inset, inset, inset, inset);

        public static RectTransform Size(this RectTransform rt, float w, float h)
        {
            var le = rt.GetComponent<LayoutElement>();
            if (le == null) le = rt.gameObject.AddComponent<LayoutElement>();
            if (w > 0) le.preferredWidth = w;
            if (h > 0) { le.preferredHeight = h; le.minHeight = h; }
            rt.sizeDelta = new Vector2(w > 0 ? w : rt.sizeDelta.x, h > 0 ? h : rt.sizeDelta.y);
            return rt;
        }

        public static Image Picture(Transform parent, string name, Sprite sprite, Color colour, bool sliced = false)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = colour;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            if (sprite == Stone) { img.type = Image.Type.Tiled; }
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color colour,
            TextAnchor align = TextAnchor.MiddleCenter, bool titleFont = false)
        {
            var rt = Rect(parent, "Text");
            var t = rt.gameObject.AddComponent<Text>();
            t.font = titleFont ? TitleFont : BodyFont;
            t.fontSize = size;
            t.color = colour;
            t.alignment = align;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, colour.grayscale > 0.5f ? 0.7f : 0.15f);
            sh.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        // A framed panel on stone or parchment.
        public static RectTransform Panel(Transform parent, string name, bool onParchment)
        {
            var img = Picture(parent, name, onParchment ? Parchment : Stone, Color.white);
            if (onParchment) img.type = Image.Type.Simple;
            var trim = Picture(img.transform, "Trim", Frame, Color.white, true);
            trim.raycastTarget = false;
            trim.rectTransform.Fill(-4);
            return img.rectTransform;
        }

        // Plays one of the game's own interface sounds by file name, set by
        // GameRoot to the backend. Null leaves the buttons silent.
        public static Func<string, bool> Sound;

        public static bool Play(string wav) => Sound != null && Sound(wav);

        // The sound the original's .gui files give a button of this kind:
        // ok.wav to go ahead, cancel.wav to go back, the main menu's own
        // for skirmish and quit, and menubutton.wav for the rest.
        public static string SoundFor(string label)
        {
            switch ((label ?? "").Trim().ToLowerInvariant())
            {
                case "ok": case "start": case "resume": case "open": case "save game": case "save as new map": return "ok.wav";
                case "back": case "x": case "cancel": case "menu": case "quit to menu": case "return to menu": return "cancel.wav";
                case "skirmish": return "skirmish.wav";
                case "quit": return "previous.wav";
                default: return "menubutton.wav";
            }
        }

        public static Button MakeButton(Transform parent, string label, Action onClick, int fontSize = 30, string sound = null)
        {
            var img = Picture(parent, label, ButtonFace, Color.white, true);
            var b = img.gameObject.AddComponent<Button>();
            var cb = b.colors;
            cb.normalColor = new Color(0.92f, 0.9f, 0.86f);
            cb.highlightedColor = new Color(1.25f, 1.12f, 0.9f);
            cb.pressedColor = new Color(0.7f, 0.62f, 0.5f);
            cb.selectedColor = cb.normalColor;
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);
            cb.colorMultiplier = 1.2f;
            b.colors = cb;
            var t = Label(img.transform, label, fontSize, Gold, TextAnchor.MiddleCenter, true);
            t.rectTransform.Fill(6);
            string wav = sound ?? SoundFor(label);
            if (!string.IsNullOrEmpty(wav)) b.onClick.AddListener(() => Play(wav));
            if (onClick != null) b.onClick.AddListener(() => onClick());
            return b;
        }

        // A button that steps through choices: left click forward, right click back.
        public static CyclePicker Cycle(Transform parent, string[] choices, int index, Action<int> changed, int fontSize = 24)
        {
            var b = MakeButton(parent, choices.Length > 0 ? choices[index] : "", null, fontSize, "");
            var c = b.gameObject.AddComponent<CyclePicker>();
            c.Init(choices, index, changed, b.GetComponentInChildren<Text>());
            b.onClick.AddListener(() => c.Step(1));
            return c;
        }

        public static Image Bar(Transform parent, string name, out Image fill)
        {
            var back = Picture(parent, name, White, new Color(0.08f, 0.06f, 0.04f, 0.9f));
            var trim = Picture(back.transform, "Trim", Frame, Color.white, true);
            trim.rectTransform.Fill(-5);
            fill = Picture(back.transform, "Fill", BarFill, Color.white, true);
            fill.rectTransform.Place(0, 0, 0, 1, 3, 3, 3, 3);
            return back;
        }

        public static void SetBar(Image fill, float fraction)
        {
            var rt = fill.rectTransform;
            rt.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1);
        }

        public static VerticalLayoutGroup Column(RectTransform rt, float spacing, int pad = 0, TextAnchor align = TextAnchor.UpperCenter)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(pad, pad, pad, pad);
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        // A column that scrolls with the wheel or a drag, clipped to its
        // box. Returns the content to put rows in, and the caller places
        // the box (content.parent.parent).
        public static RectTransform ScrollList(Transform parent, string name, float spacing)
        {
            var box = Rect(parent, name);
            var scroll = box.gameObject.AddComponent<ScrollRect>();
            var hit = box.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var view = Rect(box, "Viewport").Fill();
            view.gameObject.AddComponent<RectMask2D>();
            var content = Rect(view, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            Column(content, spacing);
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = view;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            return content;
        }

        // A text field on parchment, with a faint hint while empty.
        public static InputField Input(Transform parent, string placeholder, int fontSize)
        {
            var img = Picture(parent, "Input", Parchment, Color.white);
            var trim = Picture(img.transform, "Trim", Frame, Color.white, true);
            trim.rectTransform.Fill(-4);
            trim.raycastTarget = false;
            var field = img.gameObject.AddComponent<InputField>();
            var text = Label(img.transform, "", fontSize, Ink, TextAnchor.MiddleLeft);
            text.GetComponent<Shadow>().enabled = false;
            text.rectTransform.Fill(10);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var hint = Label(img.transform, placeholder, fontSize, new Color(Ink.r, Ink.g, Ink.b, 0.45f), TextAnchor.MiddleLeft);
            hint.GetComponent<Shadow>().enabled = false;
            hint.rectTransform.Fill(10);
            field.textComponent = text;
            field.placeholder = hint;
            field.caretColor = Ink;
            field.lineType = InputField.LineType.SingleLine;
            return field;
        }

        public static HorizontalLayoutGroup Row(RectTransform rt, float spacing, int pad = 0)
        {
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(pad, pad, pad, pad);
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            return h;
        }

        // ---- The dialog kit, in the HUD's skin ----

        // The menus' animations, off for captures and tests. FadeIn.Off
        // turns them off too, so a capture that sets either sees no motion.
        public static class Motion
        {
            public static bool Off;
            public static bool Still => Off || FadeIn.Off;
        }

        // Vellum repeats every so many units: 192 px for its 256 at 1080p.
        public const float VellumUnits = 192f;

        static readonly Dictionary<string, Texture2D> paintings = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Sprite> slices = new Dictionary<string, Sprite>();

        // Screen pixels per canvas unit, rounded up to a quarter as the HUD
        // paints, so keylines never fall under a pixel or blur at 4K.
        public static float DensityOf(Canvas c)
        {
            float f = c != null ? c.rootCanvas.scaleFactor : 1f;
            return HudArt.Density(f > 0.01f ? f : 1f);
        }

        static string Keyed(string key, float density) => key + "@" + density.ToString("0.00", CultureInfo.InvariantCulture);

        // A painted sheet as a texture, painted once for each key and density.
        public static Texture2D Painting(string key, float density, Func<float, HudArt.Sheet> make)
        {
            string k = Keyed(key, density);
            if (paintings.TryGetValue(k, out var t) && t != null) return t;
            t = HudArt.ToTexture(make(density));
            paintings[k] = t;
            return t;
        }

        // The same as a sprite that 9-slices by border units of its size in
        // units, or stretches whole when border is 0.
        public static Sprite Slice(string key, float density, float size, float border, Func<float, HudArt.Sheet> make)
        {
            string k = Keyed(key, density);
            if (slices.TryGetValue(k, out var sp) && sp != null) return sp;
            var tex = Painting(key, density, make);
            float perUnit = size > 0f ? tex.width / size : density;
            sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f * perUnit, 0,
                SpriteMeshType.FullRect, Vector4.one * border * perUnit);
            sp.hideFlags = HideFlags.DontSave;
            slices[k] = sp;
            return sp;
        }

        // A rect at r: x right and y down from the parent's top left, in units.
        public static RectTransform At(Transform parent, string name, Rect r)
        {
            var rt = Rect(parent, name);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(r.x, -r.y);
            rt.sizeDelta = new Vector2(r.width, r.height);
            return rt;
        }

        // A rect grown about its middle to hold a line of letters this size.
        public static Rect Lead(Rect r, int size, float lead = 1.4f)
        {
            float h = Mathf.Max(r.height, size * lead);
            return new Rect(r.x, r.center.y - h / 2f, r.width, h);
        }

        // A painted part that stays sharp at any size: sliced when it has a border.
        public static Image PaintedImage(Transform parent, string name, Rect r, string key, float size, float border, Func<float, HudArt.Sheet> make)
        {
            var img = At(parent, name, r).gameObject.AddComponent<Image>();
            img.type = border > 0f ? Image.Type.Sliced : Image.Type.Simple;
            img.raycastTarget = false;
            img.gameObject.AddComponent<Painted>().Set(key, size, border, make);
            return img;
        }

        // A painted picture, repeated every tile units across and down (0 stretches).
        public static RawImage PaintedPicture(Transform parent, string name, Rect r, string key, Func<float, HudArt.Sheet> make, Vector2 tile = default)
        {
            var img = At(parent, name, r).gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            var p = img.gameObject.AddComponent<Painted>();
            p.Tile = tile;
            p.Set(key, 0f, 0f, make);
            return img;
        }

        // Words in one of the three letters, never taking clicks.
        public static Text Words(Transform parent, string name, Rect r, string text, int size, Color colour, Font font, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var t = At(parent, name, r).gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.color = colour;
            t.alignment = align;
            t.text = text ?? "";
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = false;
            return t;
        }

        // A dialog on vellum as DialogLayout lays it out: the frame, interlace
        // on azurite along its top with a knot at each corner, the purple
        // title band with its title, and the help line at its foot.
        public static Dialog MakeDialog(Transform screen, DialogBox box, string title, string resting)
        {
            var rt = Rect(screen, box.Name);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(box.Frame.center.x, -box.Frame.center.y);
            rt.sizeDelta = box.Frame.size;
            var d = rt.gameObject.AddComponent<Dialog>();
            rt.gameObject.AddComponent<Opening>();
            float w = box.Frame.width, h = box.Frame.height, b = DialogLayout.Border;
            var all = new Rect(0, 0, w, h);
            PaintedPicture(rt, "Vellum", all, "vellum", s => HudArt.VellumTile(HudArt.Vellum), Vector2.one * VellumUnits).raycastTarget = true;
            PaintedPicture(rt, "Interlace", box.Local(box.Interlace), "twist", s => HudArt.TwistTile(DialogLayout.Interlace, 20f, s, false, HudArt.Azurite), new Vector2(20f, 0f));
            var title0 = box.Local(box.Title);
            PaintedImage(rt, "Title band", new Rect(b, title0.y, w - 2 * b, title0.height), "titleband", HudArt.BandSize, HudArt.BandBorder, HudArt.TitleBand);
            d.Title = Words(rt, "Title", Lead(title0, box.TitleSize), title, box.TitleSize, HudArt.GoldHi, TitleFont);
            // Clear inside: drawing no middle keeps a stretched texel of the
            // hairline from tinting the whole dialog at fractional sizes.
            PaintedImage(rt, "Frame", all, "dialogframe", HudArt.DialogFrameSize, HudArt.DialogBorder, HudArt.DialogFrame).fillCenter = false;
            foreach (var k in box.Knots) PaintedPicture(rt, "Knot", box.Local(k), "knot", s => HudArt.SolomonKnot(DialogLayout.Knot, s));
            d.Help = Words(rt, "Help", Lead(box.Local(box.Help), DialogLayout.Help), resting, DialogLayout.Help, HudArt.Ink, BodyFont);
            // A long line shrinks to the floor rather than spill onto a plate.
            d.Help.verticalOverflow = VerticalWrapMode.Truncate;
            d.Help.resizeTextForBestFit = true;
            d.Help.resizeTextMinSize = DialogLayout.Floor;
            d.Help.resizeTextMaxSize = DialogLayout.Help;
            d.RestingHelp = resting ?? "";
            return d;
        }

        // A line for a dialog's help while the pointer is over go.
        public static HelpSpot Explain(GameObject go, Dialog d, string line)
        {
            var h = go.GetComponent<HelpSpot>();
            if (h == null) h = go.AddComponent<HelpSpot>();
            h.Owner = d;
            h.Line = line;
            return h;
        }

        static void Plain(Button b)
        {
            b.transition = Selectable.Transition.None;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        // A plate button labelled in Cinzel ink, with the original's sound
        // for it and a line for the dialog's help.
        public static Button MakePlate(Transform parent, string label, Rect r, Action onClick, Dialog help = null, string line = null,
            string sound = null, int size = DialogLayout.PlateLabel)
        {
            var face = PaintedImage(parent, label, r, "plate0", HudArt.PlateSize, HudArt.PlateBorder, s => HudArt.PlateFace(s, HudArt.PlateRest));
            face.raycastTarget = true;
            var whole = new Rect(0, 0, r.width, r.height);
            var lit = PaintedImage(face.transform, "Lit", whole, "plate1", HudArt.PlateSize, HudArt.PlateBorder, s => HudArt.PlateFace(s, HudArt.PlateLit));
            lit.color = new Color(1f, 1f, 1f, 0f);
            var text = Words(face.transform, "Label", Lead(whole, size), label, size, HudArt.Ink, TitleFont);
            var b = face.gameObject.AddComponent<Button>();
            Plain(b);
            b.targetGraphic = face;
            string wav = sound ?? SoundFor(label);
            if (!string.IsNullOrEmpty(wav)) b.onClick.AddListener(() => Play(wav));
            if (onClick != null) b.onClick.AddListener(() => onClick());
            face.gameObject.AddComponent<Plate>().Init(b, face.GetComponent<Painted>(), lit, text);
            if (help != null) Explain(face.gameObject, help, line);
            return b;
        }

        // The options' gadget: a plate with the choice and an arrow at each
        // end. A click or the right arrow steps on, a right click or the left
        // arrow steps back, as the original's click to cycle does.
        public static CyclePicker MakePicker(Transform parent, string name, Rect r, string[] choices, int index, Action<int> changed)
        {
            var b = MakePlate(parent, name, r, null, null, null, "", DialogLayout.PickerValue);
            var value = b.GetComponent<Plate>().Label;
            value.name = "Value";
            value.text = choices.Length > 0 ? choices[Mathf.Clamp(index, 0, choices.Length - 1)] : "";
            const float aw = DialogLayout.ArrowW;
            var vr = value.rectTransform;
            vr.anchoredPosition = new Vector2(aw, vr.anchoredPosition.y);
            vr.sizeDelta = new Vector2(r.width - 2f * aw, vr.sizeDelta.y);
            b.GetComponent<Plate>().Rehome();
            var c = b.gameObject.AddComponent<CyclePicker>();
            c.Init(choices, index, changed, value);
            b.onClick.AddListener(() => c.Step(1));
            Arrow(b.transform, false, r.height, () => c.Step(-1));
            Arrow(b.transform, true, r.height, () => c.Step(1));
            return c;
        }

        static void Arrow(Transform plate, bool right, float h, Action step)
        {
            var at = DialogLayout.PickerArrow(right);
            var hit = At(plate, right ? "Next" : "Previous", new Rect(at.x, 0, at.width, h));
            var img = hit.gameObject.AddComponent<Image>();
            img.sprite = White;
            img.color = new Color(1f, 1f, 1f, 0f);
            const float aw = 12f, ah = 18f;
            string key = right ? "arrowR" : "arrowL";
            var pic = PaintedPicture(hit, "Arrow", new Rect((at.width - aw) / 2f, (h - ah) / 2f, aw, ah), key, s => HudArt.PickerArrow(aw, ah, s, right, false));
            var b = hit.gameObject.AddComponent<Button>();
            Plain(b);
            b.onClick.AddListener(() => step());
            hit.gameObject.AddComponent<Glint>().Init(pic.GetComponent<Painted>(), key, s => HudArt.PickerArrow(aw, ah, s, right, false),
                key + "Lit", s => HudArt.PickerArrow(aw, ah, s, right, true));
        }

        // The close cross for a title band's end.
        public static Button MakeCross(Transform parent, string name, Rect r, Action onClick)
        {
            var hit = At(parent, name, r);
            var img = hit.gameObject.AddComponent<Image>();
            img.sprite = White;
            img.color = new Color(1f, 1f, 1f, 0f);
            float size = Mathf.Min(r.width, r.height);
            var pic = PaintedPicture(hit, "Cross", new Rect((r.width - size) / 2f, (r.height - size) / 2f, size, size), "cross", s => HudArt.Cross(size, s, false));
            var b = hit.gameObject.AddComponent<Button>();
            Plain(b);
            b.targetGraphic = img;
            b.onClick.AddListener(() => Play(SoundFor("x")));
            if (onClick != null) b.onClick.AddListener(() => onClick());
            hit.gameObject.AddComponent<Glint>().Init(pic.GetComponent<Painted>(), "cross", s => HudArt.Cross(size, s, false), "crossLit", s => HudArt.Cross(size, s, true));
            return b;
        }

        // A bar on vellum: a trough in an ink keyline with a gold fill that
        // SetBar moves.
        public static Image VellumBar(Transform parent, string name, Rect r, out Image fill)
        {
            var trough = PaintedImage(parent, name, r, "trough", HudArt.TroughSize, HudArt.TroughBorder, HudArt.Trough);
            var track = Rect(trough.transform, "Track").Fill(HudArt.TroughBorder - 1f);
            float h = Mathf.Max(1f, r.height - 2f * (HudArt.TroughBorder - 1f));
            fill = PaintedImage(track, "Fill", new Rect(0, 0, 0, h), "goldfill" + Mathf.RoundToInt(h), 0f, 0f, s => HudArt.GoldFill(h, s));
            var f = fill.rectTransform;
            f.anchorMin = Vector2.zero;
            f.anchorMax = new Vector2(0f, 1f);
            f.offsetMin = f.offsetMax = Vector2.zero;
            return trough;
        }

        // A field on vellum, its keyline gold highlight while it has the keys.
        public static InputField VellumField(Transform parent, string name, Rect r, string hint, int size)
        {
            var bg = PaintedImage(parent, name, r, "field0", HudArt.TroughSize, HudArt.TroughBorder, s => HudArt.Field(s, false));
            bg.raycastTarget = true;
            var field = bg.gameObject.AddComponent<InputField>();
            var inner = Lead(new Rect(14f, 0, r.width - 28f, r.height), size);
            var text = Words(bg.transform, "Text", inner, "", size, HudArt.Ink, BodyFont, TextAnchor.MiddleLeft);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var place = Words(bg.transform, "Hint", inner, hint, size, new Color(HudArt.Ink.r, HudArt.Ink.g, HudArt.Ink.b, 0.5f), BodyFont, TextAnchor.MiddleLeft);
            place.fontStyle = FontStyle.Italic;
            field.textComponent = text;
            field.placeholder = place;
            field.caretColor = HudArt.Ink;
            field.selectionColor = new Color(HudArt.Gold.r, HudArt.Gold.g, HudArt.Gold.b, 0.45f);
            field.lineType = InputField.LineType.SingleLine;
            bg.gameObject.AddComponent<FieldFocus>();
            return field;
        }

        // A ruled row for a list on vellum: ink ruling at its foot and a gold
        // wash under the pointer. It stretches to the list's width.
        public static Button ListRow(Transform parent, string name, string label, float height, int size, Color colour, Action onClick)
        {
            var row = Rect(parent, name).Size(0, height);
            var wash = row.gameObject.AddComponent<Image>();
            wash.sprite = White;
            var rule = Picture(row, "Ruling", White, new Color(HudArt.Ink.r, HudArt.Ink.g, HudArt.Ink.b, 0.12f));
            rule.raycastTarget = false;
            rule.rectTransform.Place(0, 0, 1, 0, 0, 0, 0, -1);
            var text = Words(row, "Label", new Rect(0, 0, 10, 10), label, size, colour, BodyFont, TextAnchor.MiddleLeft);
            text.rectTransform.Place(0, 0, 1, 1, 12, 0, 12, 0);
            var b = row.gameObject.AddComponent<Button>();
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.targetGraphic = wash;
            var cb = b.colors;
            cb.normalColor = new Color(1f, 1f, 1f, 0f);
            cb.highlightedColor = new Color(HudArt.Gold.r, HudArt.Gold.g, HudArt.Gold.b, 0.24f);
            cb.pressedColor = new Color(HudArt.Gold.r, HudArt.Gold.g, HudArt.Gold.b, 0.38f);
            cb.selectedColor = cb.normalColor;
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.1f;
            b.colors = cb;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            return b;
        }

        // The vellum page some screens stand on: vellum, interlace bands
        // along the top and foot with knots at the corners.
        public static RectTransform VellumPage(Transform parent, string name)
        {
            var page = Rect(parent, name).Fill();
            var ground = page.gameObject.AddComponent<RawImage>();
            ground.raycastTarget = true;
            var p = page.gameObject.AddComponent<Painted>();
            p.Tile = Vector2.one * VellumUnits;
            p.Set("vellum", 0f, 0f, s => HudArt.VellumTile(HudArt.Vellum));
            ground.color = new Color(0.93f, 0.9f, 0.86f);
            foreach (bool top in new[] { true, false })
            {
                var band = Rect(page, "Band").Place(0, top ? 1 : 0, 1, top ? 1 : 0, 0, top ? -DialogLayout.ScreenBand : 0, 0, top ? 0 : -DialogLayout.ScreenBand);
                var img = band.gameObject.AddComponent<RawImage>();
                img.raycastTarget = false;
                var bp = band.gameObject.AddComponent<Painted>();
                bp.Tile = new Vector2(24f, 0f);
                bp.Set("twist12", 0f, 0f, s => HudArt.TwistTile(DialogLayout.ScreenBand, 24f, s, false, HudArt.Azurite));
                foreach (bool left in new[] { true, false })
                {
                    float k = DialogLayout.Knot + 4f;
                    var knot = Rect(page, "Knot").Place(left ? 0 : 1, top ? 1 : 0, left ? 0 : 1, top ? 1 : 0,
                        left ? 0 : -k, top ? -k : 0, left ? -k : 0, top ? 0 : -k);
                    var ki = knot.gameObject.AddComponent<RawImage>();
                    ki.raycastTarget = false;
                    knot.gameObject.AddComponent<Painted>().Set("knot22", 0f, 0f, s => HudArt.SolomonKnot(DialogLayout.Knot + 4f, s));
                }
            }
            return page;
        }

        // The purple strip under the page's top band, with the game's name.
        public static Text TitleStrip(Transform parent, string title)
        {
            var strip = Rect(parent, "Title strip").Place(0, 1, 1, 1, 0, -(DialogLayout.ScreenBand + DialogLayout.StripH), 0, DialogLayout.ScreenBand);
            var img = strip.gameObject.AddComponent<Image>();
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            strip.gameObject.AddComponent<Painted>().Set("titleband", HudArt.BandSize, HudArt.BandBorder, HudArt.TitleBand);
            var t = Words(strip, "Title", new Rect(0, 0, 10, 10), title, DialogLayout.ScreenTitle, HudArt.GoldHi, TitleFont);
            t.rectTransform.Fill();
            return t;
        }

        // Row 0 of a picture is its top, and Unity uploads bottom first, so
        // by default the rows flip and the picture stands the right way up.
        // Model textures do not flip: their UVs expect the rows as they come.
        public static Texture2D ToTexture(RgbaImage img, bool mipmaps = false, bool flip = true)
        {
            if (img == null) return null;
            var tex = new Texture2D(img.Width, img.Height, TextureFormat.RGBA32, mipmaps) { hideFlags = HideFlags.DontSave };
            var data = img.Pixels;
            if (flip)
            {
                data = new byte[img.Pixels.Length];
                int stride = img.Width * 4;
                for (int y = 0; y < img.Height; y++)
                    Buffer.BlockCopy(img.Pixels, y * stride, data, (img.Height - 1 - y) * stride, stride);
            }
            tex.SetPixelData(data, 0);
            tex.Apply(mipmaps);
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
    }

    // Keeps a panel stretched to its parent's height no taller than Max,
    // centred.
    [ExecuteAlways]
    public sealed class MaxHeight : MonoBehaviour
    {
        public float Max = 1000, Margin = 24;

        void LateUpdate()
        {
            var rt = (RectTransform)transform;
            var parent = rt.parent as RectTransform;
            if (parent == null) return;
            float h = parent.rect.height;
            float margin = Mathf.Max(Margin, (h - Max) / 2f);
            if (!Mathf.Approximately(rt.offsetMin.y, margin) || !Mathf.Approximately(-rt.offsetMax.y, margin))
            {
                rt.offsetMin = new Vector2(rt.offsetMin.x, margin);
                rt.offsetMax = new Vector2(rt.offsetMax.x, -margin);
            }
        }
    }

    // Shrinks a fixed-size panel uniformly when its parent is smaller.
    public sealed class FitInParent : MonoBehaviour
    {
        public float Margin = 20;

        void LateUpdate()
        {
            var rt = (RectTransform)transform;
            var parent = rt.parent as RectTransform;
            if (parent == null) return;
            var size = rt.rect.size;
            float k = Mathf.Min(1f, (parent.rect.width - 2 * Margin) / Mathf.Max(1, size.x), (parent.rect.height - 2 * Margin) / Mathf.Max(1, size.y));
            rt.localScale = new Vector3(k, k, 1);
        }
    }

    public sealed class CyclePicker : MonoBehaviour, IPointerClickHandler
    {
        public int Index { get; private set; }
        string[] choices;
        Action<int> changed;
        Text text;

        public void Init(string[] c, int index, Action<int> onChange, Text label)
        {
            choices = c;
            Index = index;
            changed = onChange;
            text = label;
        }

        public void SetChoices(string[] c, int index)
        {
            choices = c;
            Index = Mathf.Clamp(index, 0, c.Length - 1);
            text.text = c[Index];
        }

        public void Step(int by)
        {
            if (choices.Length == 0) return;
            Index = (Index + by + choices.Length) % choices.Length;
            text.text = choices[Index];
            UiKit.Play("toggle.wav");
            changed?.Invoke(Index);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right) Step(-1);
        }
    }

    // A dialog's help line: a control's line under the pointer, or the
    // resting line.
    public sealed class Dialog : MonoBehaviour
    {
        public Text Title, Help;
        public string RestingHelp = "";

        public void ShowHelp(string line)
        {
            if (Help != null) Help.text = string.IsNullOrEmpty(line) ? RestingHelp : line;
        }

        public void Rest(string line)
        {
            RestingHelp = line ?? "";
            ShowHelp(null);
        }
    }

    // Writes a line in a dialog's help while the pointer is over it.
    public sealed class HelpSpot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Dialog Owner;
        public string Line;
        public void OnPointerEnter(PointerEventData e) => Owner?.ShowHelp(Line);
        public void OnPointerExit(PointerEventData e) => Owner?.ShowHelp(null);
    }

    // A painted part that paints itself again when its canvas's sharpness
    // changes, and keeps its tiling as its rect changes.
    public sealed class Painted : MonoBehaviour
    {
        public string Key;
        public float Size, Border;
        public Func<float, HudArt.Sheet> Make;
        // Units per repeat across and down, 0 to stretch.
        public Vector2 Tile;
        Canvas canvas;
        Image image;
        RawImage raw;
        float density = -1f;
        Vector2 tiled = -Vector2.one;

        public void Set(string key, float size, float border, Func<float, HudArt.Sheet> make)
        {
            Key = key;
            Size = size;
            Border = border;
            Make = make;
            density = -1f;
            Refresh();
        }

        void OnEnable()
        {
            canvas = null;
            density = -1f;
            Refresh();
        }

        void LateUpdate() => Refresh();

        public void Refresh()
        {
            if (Make == null) return;
            if (canvas == null) canvas = GetComponentInParent<Canvas>(true);
            float d = UiKit.DensityOf(canvas);
            if (d != density)
            {
                density = d;
                if (image == null) image = GetComponent<Image>();
                if (raw == null) raw = GetComponent<RawImage>();
                if (image != null) image.sprite = UiKit.Slice(Key, d, Size, Border, Make);
                else if (raw != null) raw.texture = UiKit.Painting(Key, d, Make);
                tiled = -Vector2.one;
            }
            if (raw == null || (Tile.x <= 0f && Tile.y <= 0f)) return;
            var size = ((RectTransform)transform).rect.size;
            if (size == tiled) return;
            tiled = size;
            raw.uvRect = new Rect(0, 0, Tile.x > 0f ? size.x / Tile.x : 1f, Tile.y > 0f ? size.y / Tile.y : 1f);
        }
    }

    // A dialog's button: at rest, lit under the pointer over a tenth of a
    // second, a unit down at once while pressed, washed out when it cannot
    // be used. The look changes in the frame of the pointer's event.
    public sealed class Plate : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Button Button { get; private set; }
        public Text Label { get; private set; }
        public bool Pressed => down;
        Painted face;
        Image lit;
        Vector2 home;
        bool over, down;
        float glow;
        int look = -1;

        public void Init(Button b, Painted f, Image l, Text label)
        {
            Button = b;
            face = f;
            lit = l;
            Label = label;
            Rehome();
        }

        // Where the label sits at rest, after its rect is moved.
        public void Rehome()
        {
            home = Label.rectTransform.anchoredPosition;
            look = -1;
            Paint();
        }

        public bool Enabled
        {
            get => Button == null || Button.interactable;
            set
            {
                if (Button != null) Button.interactable = value;
                Paint();
            }
        }

        public void OnPointerEnter(PointerEventData e) { over = true; Paint(); }
        public void OnPointerExit(PointerEventData e) { over = false; down = false; Paint(); }
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) down = true; Paint(); }
        public void OnPointerUp(PointerEventData e) { down = false; Paint(); }

        void OnDisable()
        {
            over = down = false;
            glow = 0f;
            Paint();
        }

        void Update()
        {
            float target = over && !down && Enabled ? 1f : 0f;
            glow = UiKit.Motion.Still ? target : Mathf.MoveTowards(glow, target, Time.unscaledDeltaTime / 0.1f);
            if (lit != null) lit.color = new Color(1f, 1f, 1f, down ? 0f : glow);
        }

        void Paint()
        {
            if (face == null || Label == null) return;
            bool on = Enabled;
            int want = !on ? HudArt.PlateOff : down ? HudArt.PlateDown : HudArt.PlateRest;
            if (want != look)
            {
                look = want;
                face.Set("plate" + want, HudArt.PlateSize, HudArt.PlateBorder, s => HudArt.PlateFace(s, want));
            }
            Label.rectTransform.anchoredPosition = home + (down && on ? new Vector2(0f, -1f) : Vector2.zero);
            var c = HudArt.Ink;
            c.a = on ? 1f : 0.4f;
            Label.color = c;
            if (lit != null && (down || !on)) lit.color = new Color(1f, 1f, 1f, 0f);
        }
    }

    // A small painted part of a button, the picker's arrows and the close
    // cross, that lights under the pointer and while pressed.
    public sealed class Glint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        Painted picture;
        string rest, lit;
        Func<float, HudArt.Sheet> restMake, litMake;
        bool over, down;

        public void Init(Painted p, string restKey, Func<float, HudArt.Sheet> restPaint, string litKey, Func<float, HudArt.Sheet> litPaint)
        {
            picture = p;
            rest = restKey;
            restMake = restPaint;
            lit = litKey;
            litMake = litPaint;
        }

        public void OnPointerEnter(PointerEventData e) { over = true; Paint(); }
        public void OnPointerExit(PointerEventData e) { over = down = false; Paint(); }
        public void OnPointerDown(PointerEventData e) { down = true; Paint(); }
        public void OnPointerUp(PointerEventData e) { down = false; Paint(); }
        void OnDisable() { over = down = false; Paint(); }

        void Paint()
        {
            if (picture == null) return;
            bool on = over || down;
            picture.Set(on ? lit : rest, 0f, 0f, on ? litMake : restMake);
        }
    }

    // Fades a dialog in and grows it from just under its size when it
    // opens, after a hold when one is asked for, and shrinks it to fit a
    // canvas smaller than it. Motion.Off shows it at once.
    public sealed class Opening : MonoBehaviour
    {
        public float Seconds = 0.18f, Delay, From = 0.98f, Margin = DialogLayout.Margin;
        CanvasGroup group;
        float start;

        public CanvasGroup Group
        {
            get
            {
                if (group == null) group = GetComponent<CanvasGroup>();
                if (group == null) group = gameObject.AddComponent<CanvasGroup>();
                return group;
            }
        }

        void OnEnable()
        {
            start = Time.unscaledTime;
            Apply();
        }

        // Opens again from now, holding first for delay seconds.
        public void Restart(float delay)
        {
            Delay = delay;
            start = Time.unscaledTime;
            Apply();
        }

        void LateUpdate() => Apply();

        void Apply()
        {
            float t = UiKit.Motion.Still ? 1f : Mathf.Clamp01((Time.unscaledTime - start - Delay) / Mathf.Max(0.01f, Seconds));
            var g = Group;
            g.alpha = t;
            g.blocksRaycasts = g.interactable = t > 0f;
            var rt = (RectTransform)transform;
            float fit = 1f;
            if (rt.parent is RectTransform parent)
            {
                var size = rt.rect.size;
                fit = Mathf.Min(1f, (parent.rect.width - 2f * Margin) / Mathf.Max(1f, size.x), (parent.rect.height - 2f * Margin) / Mathf.Max(1f, size.y));
                fit = Mathf.Max(0.05f, fit);
            }
            float grow = Mathf.Lerp(From, 1f, Mathf.SmoothStep(0f, 1f, t));
            rt.localScale = new Vector3(fit * grow, fit * grow, 1f);
        }
    }

    // Draws the words under it again when the canvas's scale changes, as
    // legacy Text keeps the size it was drawn at until its rect changes.
    public sealed class Resharpen : MonoBehaviour
    {
        Canvas canvas;
        float scale = -1f;

        void LateUpdate()
        {
            if (canvas == null) canvas = GetComponentInParent<Canvas>(true);
            if (canvas == null) return;
            float s = canvas.rootCanvas.scaleFactor;
            if (Mathf.Approximately(s, scale)) return;
            bool first = scale < 0f;
            scale = s;
            if (!first) foreach (var t in GetComponentsInChildren<Text>()) t.SetAllDirty();
        }
    }

    // Lights a vellum field's keyline while it has the keys.
    public sealed class FieldFocus : MonoBehaviour
    {
        InputField field;
        Painted face;
        bool lit;

        void LateUpdate()
        {
            if (field == null) field = GetComponent<InputField>();
            if (face == null) face = GetComponent<Painted>();
            if (field == null || face == null || field.isFocused == lit) return;
            lit = field.isFocused;
            bool on = lit;
            face.Set(on ? "field1" : "field0", HudArt.TroughSize, HudArt.TroughBorder, s => HudArt.Field(s, on));
        }
    }
}
