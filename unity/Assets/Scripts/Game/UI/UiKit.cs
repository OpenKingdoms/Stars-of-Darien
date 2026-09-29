// UiKit.cs - the menu look built from code: stone, parchment and gold
// trim painted procedurally, Cinzel for titles and EB Garamond for text
// (both SIL Open Font License), and small builders for panels, buttons,
// cycling pickers and bars. The canvas scales from 1920 by 1080, and
// fonts render at the final pixel size, so text is sharp at 1080p and 4K.
using System;
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
}
