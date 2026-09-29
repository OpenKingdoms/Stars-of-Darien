// LobbyKit.cs - the original's menu screens rebuilt on a Unity canvas. A
// GuiPage is the original's 640 by 480 screen scaled to fit, and every
// gadget sits at the rect its .gui file gives it, in classic pixels (cp).
// The original's art comes from the player's own files through the
// backend and is drawn sharp at any scale. Where it is missing the HUD's
// Carolingian paint stands in. Text is set in the HUD's fonts at the
// screen's own resolution.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OpenKingdomsUnity.Game.UI
{
    // The original's art, read once and kept until the menus close.
    public sealed class LobbyArt
    {
        public struct Frame
        {
            public Texture2D Tex;
            public Vector2Int Origin;
            public int Frames;
        }

        readonly IGameBackend backend;
        readonly Dictionary<string, Frame> frames = new Dictionary<string, Frame>();
        Material sharp;

        public LobbyArt(IGameBackend backend) { this.backend = backend; }

        public Material Sharp
        {
            get
            {
                if (sharp == null)
                {
                    var sh = Shader.Find("OpenKingdoms/Presentation/SharpUi");
                    if (sh != null) sharp = new Material(sh) { hideFlags = HideFlags.DontSave };
                }
                return sharp;
            }
        }

        public Frame Get(string gaf, string entry, int frame = 0)
        {
            string key = gaf + "|" + entry + "|" + frame;
            if (frames.TryGetValue(key, out var f)) return f;
            ArtFrame a = null;
            try { a = backend.InterfaceArt(gaf, entry, frame); }
            catch (Exception e) { Debug.LogWarning("Interface art " + key + ": " + e.Message); }
            if (a?.Image != null)
            {
                f.Tex = UiKit.ToTexture(a.Image);
                f.Tex.filterMode = FilterMode.Bilinear;
                f.Origin = a.Origin;
                f.Frames = a.Frames;
            }
            frames[key] = f;
            return f;
        }

        public bool Has(string gaf, string entry) => Get(gaf, entry).Tex != null;

        public void Dispose()
        {
            foreach (var f in frames.Values) if (f.Tex != null) World.Looks.Release(f.Tex);
            frames.Clear();
            if (sharp != null) World.Looks.Release(sharp);
        }
    }

    // One screen: the page, its scale, and the painted pieces made for it.
    public sealed class GuiPage
    {
        public const float W = 640f, H = 480f;

        public readonly RectTransform Screen, Root;
        public readonly float K;            // canvas units per cp
        public readonly float Density;      // screen pixels per cp
        public readonly LobbyArt Art;
        public Text Help;                   // where hover help goes
        public string RestingHelp = "";
        readonly Dictionary<string, Texture2D> paint;
        readonly List<Object> owned;

        public GuiPage(RectTransform screen, Vector2 canvas, float canvasScale, LobbyArt art, Dictionary<string, Texture2D> paint, List<Object> owned)
        {
            Screen = screen;
            Art = art;
            this.paint = paint;
            this.owned = owned;
            K = Mathf.Min(canvas.x / W, canvas.y / H);
            Density = HudArt.Density(K * canvasScale);
            Root = UiKit.Rect(screen, "Page");
            Root.anchorMin = Root.anchorMax = Root.pivot = new Vector2(0.5f, 0.5f);
            Root.sizeDelta = new Vector2(W * K, H * K);
        }

        // A rect at x, y (from the parent's top left, y down), w by h cp.
        public RectTransform Put(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = UiKit.Rect(parent ?? Root, name);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x * K, -y * K);
            rt.sizeDelta = new Vector2(w * K, h * K);
            return rt;
        }

        public RectTransform Put(string name, float x, float y, float w, float h) => Put(Root, name, x, y, w, h);

        public int Font(float cp) => Mathf.Max(1, Mathf.RoundToInt(cp * K));

        public Text Label(Transform parent, string text, float x, float y, float w, float h, float sizeCp, Color colour,
            TextAnchor align = TextAnchor.MiddleLeft, Font font = null)
        {
            var rt = Put(parent, "Text", x, y, w, h);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font ?? UiKit.BodyFont;
            t.fontSize = Font(sizeCp);
            t.color = colour;
            t.alignment = align;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            t.supportRichText = false;
            return t;
        }

        // A texture painted once for this screen's scale.
        public Texture2D Paint(string key, Func<float, HudArt.Sheet> make)
        {
            if (paint.TryGetValue(key, out var t)) return t;
            t = HudArt.ToTexture(make(Density));
            owned.Add(t);
            paint[key] = t;
            return t;
        }

        public RawImage Picture(Transform parent, string name, Texture tex, float x, float y, float w, float h)
        {
            var img = Put(parent, name, x, y, w, h).gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            return img;
        }

        // A frame of the original's art placed as the game places it, its
        // anchor on (x, y). Null when the player's files do not have it.
        public RawImage ArtImage(Transform parent, string gaf, string entry, int frame, float x, float y)
        {
            var f = Art.Get(gaf, entry, frame);
            if (f.Tex == null) return null;
            var img = Picture(parent, entry, f.Tex, x - f.Origin.x, y - f.Origin.y, f.Tex.width, f.Tex.height);
            img.material = Art.Sharp;
            return img;
        }

        // A 9-sliced painted piece whose border stays borderCp wide.
        public Image Sliced(Transform parent, string name, string key, Func<float, HudArt.Sheet> make, float borderCp, float x, float y, float w, float h)
        {
            if (!sprites.TryGetValue(key, out var sp))
            {
                var tex = Paint(key, make);
                // Pixels per unit so the border keeps its width in cp.
                sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f * Density / K, 0,
                    SpriteMeshType.FullRect, Vector4.one * borderCp * Density);
                sp.hideFlags = HideFlags.DontSave;
                owned.Add(sp);
                sprites[key] = sp;
            }
            var img = Put(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            img.sprite = sp;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            return img;
        }

        readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        public Image Wash(Transform parent, float x, float y, float w, float h, Color c)
        {
            var img = Put(parent, "Wash", x, y, w, h).gameObject.AddComponent<Image>();
            img.sprite = UiKit.White;
            img.color = c;
            return img;
        }

        public void Hover(GameObject go, string line)
        {
            var h = go.GetComponent<HelpHover>();
            if (h == null) h = go.AddComponent<HelpHover>();
            h.Page = this;
            h.Line = line;
        }

        public void ShowHelp(string line)
        {
            if (Help != null) Help.text = string.IsNullOrEmpty(line) ? RestingHelp : line;
        }

        // Shortens text with an ellipsis until it fits its rect on one line.
        public static void Fit(Text t, string s)
        {
            t.text = s ?? "";
            float room = t.rectTransform.rect.width;
            if (room <= 0 || t.preferredWidth <= room) return;
            int lo = 0, hi = t.text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                t.text = s.Substring(0, mid).TrimEnd() + "…";
                if (t.preferredWidth <= room) lo = mid; else hi = mid - 1;
            }
            t.text = s.Substring(0, lo).TrimEnd() + "…";
        }
    }

    public static class LobbyInk
    {
        public static readonly Color Text = HudArt.Silver, Head = HudArt.GoldHi, Dim = new Color(0.72f, 0.7f, 0.66f);
        public static readonly Color Picked = new Color(HudArt.Gold.r, HudArt.Gold.g, HudArt.Gold.b, 0.3f);
        public static readonly Color Over = new Color(HudArt.GoldHi.r, HudArt.GoldHi.g, HudArt.GoldHi.b, 0.13f);
        public static readonly Color Clear = new Color(1, 1, 1, 0);
        public static Font Uncial => UiKit.UncialFont;
        public static Font Caps => UiKit.TitleFont;
        public static Font Body => UiKit.BodyFont;
    }

    // Hover help, as the original writes it in the strip at the bottom.
    public sealed class HelpHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public GuiPage Page;
        public string Line;
        public void OnPointerEnter(PointerEventData e) => Page?.ShowHelp(Line);
        public void OnPointerExit(PointerEventData e) => Page?.ShowHelp(null);
    }

    // A text gadget that steps through choices: the left button forward,
    // the right back, as the original's side and team cells do.
    public sealed class Clicker : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Action<int> Step;
        public Image Wash;
        public Text Label;
        public Color Rest = LobbyInk.Text;
        public bool Enabled = true;
        bool over;

        public void OnPointerClick(PointerEventData e)
        {
            if (!Enabled || Step == null) return;
            UiKit.Play("menubutton.wav");
            Step(e.button == PointerEventData.InputButton.Right ? -1 : 1);
        }

        public void OnPointerEnter(PointerEventData e) { over = true; Paint(); }
        public void OnPointerExit(PointerEventData e) { over = false; Paint(); }

        public void Paint()
        {
            bool live = Enabled && Step != null;
            if (Wash != null) Wash.color = over && live ? LobbyInk.Over : LobbyInk.Clear;
            if (Label != null) Label.color = !Enabled ? LobbyInk.Dim : over && live ? LobbyInk.Head : Rest;
        }

        public static Clicker Make(GuiPage p, Transform parent, string name, float x, float y, float w, float h, float sizeCp,
            Action<int> step, string help, TextAnchor align = TextAnchor.MiddleLeft, Font font = null)
        {
            var wash = p.Wash(parent, x, y, w, h, LobbyInk.Clear);
            wash.name = name;
            var c = wash.gameObject.AddComponent<Clicker>();
            c.Wash = wash;
            c.Label = p.Label(wash.transform, "", 2, 0, w - 4, h, sizeCp, LobbyInk.Text, align, font);
            c.Step = step;
            if (help != null) p.Hover(wash.gameObject, help);
            c.Paint();
            return c;
        }
    }

    // A button in the original's art: frame 0 at rest, frame 1 pressed. A
    // warm light rises behind it under the pointer. Without the art it is
    // a vellum lozenge lettered in minium. A plain Button takes the click,
    // so it presses as every other menu button does.
    public sealed class ArtButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Action Clicked;
        public string Sound;
        public RawImage Face;
        public Texture2D[] Frames;
        public Image Glow;
        public Text Label;
        public bool Enabled = true;
        bool over, down;
        float glow;

        public void Press()
        {
            if (!Enabled) return;
            UiKit.Play(Sound);
            Clicked?.Invoke();
        }

        // A Button with no look of its own, for the click.
        public static Button Plain(GameObject go, Action press)
        {
            var b = go.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => press());
            return b;
        }

        public void OnPointerEnter(PointerEventData e) { over = true; Paint(); }
        public void OnPointerExit(PointerEventData e) { over = false; down = false; Paint(); }
        public void OnPointerDown(PointerEventData e) { down = true; Paint(); }
        public void OnPointerUp(PointerEventData e) { down = false; Paint(); }

        public void Paint()
        {
            if (Face != null && Frames != null && Frames.Length > 0)
            {
                Face.texture = Frames[down && over && Enabled && Frames.Length > 1 && Frames[1] != null ? 1 : 0];
                Face.color = Enabled ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
            }
            if (Label != null) Label.color = Enabled ? (over ? HudArt.MiniumDeep : HudArt.Minium) : new Color(HudArt.Ink.r, HudArt.Ink.g, HudArt.Ink.b, 0.4f);
        }

        void Update()
        {
            if (Glow == null) return;
            glow = Mathf.MoveTowards(glow, over && Enabled ? 1f : 0f, Time.unscaledDeltaTime * 5f);
            var c = Glow.color;
            c.a = glow * 0.5f;
            Glow.color = c;
        }

        public static ArtButton Make(GuiPage p, string name, string gaf, string entry, float x, float y, float w, float h,
            string fallback, Action clicked, string help)
        {
            var holder = p.Put(name, x, y, w, h);
            var glow = UiKit.Picture(holder, "Glow", UiKit.Glow, new Color(1f, 0.78f, 0.42f, 0f));
            glow.rectTransform.Place(0, 0, 1, 1, -w * p.K * 0.35f, -h * p.K * 0.3f, -w * p.K * 0.35f, -h * p.K * 0.3f);
            glow.raycastTarget = false;
            var b = holder.gameObject.AddComponent<ArtButton>();
            Plain(holder.gameObject, b.Press);
            b.Clicked = clicked;
            b.Sound = UiKit.SoundFor(name);
            b.Glow = glow;
            var f0 = gaf != null ? p.Art.Get(gaf, entry, 0) : default;
            if (f0.Tex != null)
            {
                var f1 = p.Art.Get(gaf, entry, 1);
                b.Frames = new[] { f0.Tex, f1.Tex };
                b.Face = p.Picture(holder, "Face", f0.Tex, -f0.Origin.x, -f0.Origin.y, f0.Tex.width, f0.Tex.height);
                b.Face.material = p.Art.Sharp;
                b.Face.raycastTarget = true;
            }
            else
            {
                float lw = Mathf.Max(56f, w), lh = 22f;
                float lx = (w - lw) / 2f, ly = (h - lh) / 2f;
                var face = p.Picture(holder, "Face", p.Paint($"lozenge{lw}x{lh}", s => HudArt.Lozenge(lw, lh, s, false)), lx, ly, lw, lh);
                face.raycastTarget = true;
                b.Label = p.Label(face.transform, fallback, 0, 0, lw, lh, 9.5f, HudArt.Minium, TextAnchor.MiddleCenter, LobbyInk.Caps);
            }
            if (help != null) p.Hover(holder.gameObject, help);
            b.Paint();
            return b;
        }
    }

    // The original's checkbox: frame 0 off, 1 on, 2 and 4 under the
    // pointer, 5 when it cannot change. Painted rings stand in without it.
    public sealed class CheckBox : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Action Toggle;
        public Func<bool> On;
        public bool Enabled = true;
        public RawImage Face;
        public Texture2D[] Frames;   // off, on, off lit, on lit, dim
        bool over;

        public void OnPointerClick(PointerEventData e)
        {
            if (!Enabled) return;
            UiKit.Play("toggle.wav");
            Toggle?.Invoke();
            Paint();
        }
        public void OnPointerEnter(PointerEventData e) { over = true; Paint(); }
        public void OnPointerExit(PointerEventData e) { over = false; Paint(); }

        public void Paint()
        {
            bool on = On != null && On();
            bool lit = over && Enabled;
            int i = on ? (lit ? 3 : 1) : (lit ? 2 : 0);
            Face.texture = Frames[i] ?? Frames[on ? 1 : 0];
        }

        public static CheckBox Make(GuiPage p, Transform parent, float x, float y, Func<bool> on, Action toggle, string help)
        {
            var hit = p.Wash(parent, x - 2, y - 2, 17, 17, LobbyInk.Clear);
            hit.name = "Check";
            var c = hit.gameObject.AddComponent<CheckBox>();
            c.On = on;
            c.Toggle = toggle;
            var a = p.Art;
            if (a.Has("scrollbars.gaf", "CheckBox"))
            {
                c.Frames = Array.ConvertAll(new[] { 0, 1, 2, 4, 5 }, i => a.Get("scrollbars.gaf", "CheckBox", i).Tex);
                c.Face = p.Picture(hit.transform, "Face", c.Frames[0], 2, 2, 13, 13);
                c.Face.material = a.Sharp;
            }
            else
            {
                var off = p.Paint("check0", s => HudArt.Check(13, s, false));
                var onT = p.Paint("check1", s => HudArt.Check(13, s, true));
                c.Frames = new[] { off, onT, off, onT, off };
                c.Face = p.Picture(hit.transform, "Face", off, 2, 2, 13, 13);
            }
            if (help != null) p.Hover(hit.gameObject, help);
            c.Paint();
            return c;
        }
    }

    // A list of many rows with a few made: rows are filled from the data
    // as it scrolls, so thousands of maps cost what five do.
    public sealed class ListView : MonoBehaviour, IScrollHandler
    {
        public int Count, Top, Selected = -1;
        public readonly List<ListRow> Rows = new List<ListRow>();
        public Action<int, ListRow> Bind;
        public Action<int> Picked;
        public ScrollRail Rail;

        public int Visible => Rows.Count;
        public int MaxTop => Mathf.Max(0, Count - Visible);

        public void SetCount(int n)
        {
            Count = n;
            Top = Mathf.Clamp(Top, 0, MaxTop);
            Refresh();
        }

        public void ScrollTo(int index)
        {
            if (index < 0) return;
            if (index < Top) Top = index;
            else if (index >= Top + Visible) Top = index - Visible + 1;
            Top = Mathf.Clamp(Top, 0, MaxTop);
        }

        public void ScrollBy(int by)
        {
            Top = Mathf.Clamp(Top + by, 0, MaxTop);
            Refresh();
        }

        public void Refresh()
        {
            for (int r = 0; r < Rows.Count; r++)
            {
                int i = Top + r;
                var row = Rows[r];
                row.Index = i < Count ? i : -1;
                row.gameObject.SetActive(i < Count);
                if (i < Count) { Bind?.Invoke(i, row); row.Paint(); }
            }
            Rail?.Set(Top, Count, Visible);
        }

        public void OnScroll(PointerEventData e)
        {
            if (Mathf.Abs(e.scrollDelta.y) < 0.01f) return;
            ScrollBy(e.scrollDelta.y > 0 ? -1 : 1);
        }

        public static ListView Make(GuiPage p, string name, float x, float y, float w, float rowH, int rows, Func<GuiPage, ListRow, int> cells)
        {
            var box = p.Wash(p.Root, x, y, w, rowH * rows, LobbyInk.Clear);
            box.name = name;
            var lv = box.gameObject.AddComponent<ListView>();
            for (int r = 0; r < rows; r++)
            {
                var wash = p.Wash(box.transform, 0, r * rowH, w, rowH, LobbyInk.Clear);
                wash.name = "Row " + r;
                var row = wash.gameObject.AddComponent<ListRow>();
                row.List = lv;
                row.Wash = wash;
                cells(p, row);
                lv.Rows.Add(row);
            }
            return lv;
        }
    }

    public sealed class ListRow : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IScrollHandler
    {
        public ListView List;
        public Image Wash;
        public readonly List<Text> Cells = new List<Text>();
        public int Index = -1;
        bool over;

        public void OnPointerClick(PointerEventData e)
        {
            if (Index < 0 || e.button != PointerEventData.InputButton.Left) return;
            List.Selected = Index;
            List.Picked?.Invoke(Index);
            List.Refresh();
        }

        public void OnPointerEnter(PointerEventData e) { over = true; Paint(); }
        public void OnPointerExit(PointerEventData e) { over = false; Paint(); }
        public void OnScroll(PointerEventData e) => List.OnScroll(e);

        public void Paint()
        {
            bool picked = Index >= 0 && Index == List.Selected;
            Wash.color = picked ? LobbyInk.Picked : over ? LobbyInk.Over : LobbyInk.Clear;
            foreach (var c in Cells) c.color = picked || over ? LobbyInk.Head : LobbyInk.Text;
        }
    }

    // The original's scroll bar: an arrow at each end, a bar, a thumb.
    public sealed class ScrollRail : MonoBehaviour, IDragHandler, IPointerDownHandler
    {
        public ListView List;
        public RectTransform Thumb, Track;
        float thumbH;

        public void Set(int top, int count, int visible)
        {
            if (Thumb == null || Track == null) return;
            float travel = Mathf.Max(0f, Track.rect.height - thumbH);
            float t = count > visible ? top / (float)(count - visible) : 0f;
            Thumb.anchoredPosition = new Vector2(Thumb.anchoredPosition.x, Track.anchoredPosition.y - t * travel);
            Thumb.gameObject.SetActive(count > visible);
        }

        public void OnPointerDown(PointerEventData e) => Drag(e);
        public void OnDrag(PointerEventData e) => Drag(e);

        void Drag(PointerEventData e)
        {
            if (List == null || List.MaxTop <= 0) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Track, e.position, e.pressEventCamera, out var local)) return;
            float h = Track.rect.height;
            float t = Mathf.Clamp01((-local.y - thumbH / 2f) / Mathf.Max(1f, h - thumbH));
            List.Top = Mathf.RoundToInt(t * List.MaxTop);
            List.Refresh();
        }

        // Rail rect in cp: x, y, w, h, the whole bar with its arrows. bar is
        // the original's entry for the bar between them.
        public static ScrollRail Make(GuiPage p, ListView list, float x, float y, float w, float h, string bar)
        {
            var a = p.Art;
            bool art = a.Has("scrollbars.gaf", "ScrollThumb");
            float arrowH = 21f;
            var track = p.Wash(p.Root, x, y + arrowH, w, h - 2 * arrowH, LobbyInk.Clear);
            track.name = "Rail";
            var rail = track.gameObject.AddComponent<ScrollRail>();
            rail.List = list;
            rail.Track = track.rectTransform;
            list.Rail = rail;
            if (art)
            {
                // The bar art's anchor sits above its own top, as the .gui places it.
                p.ArtImage(p.Root, "scrollbars.gaf", bar, 0, x, y)?.transform.SetSiblingIndex(track.transform.GetSiblingIndex());
                var th = a.Get("scrollbars.gaf", "ScrollThumb");
                rail.thumbH = th.Tex.height * p.K;
                var thumb = p.Picture(p.Root, "Thumb", th.Tex, x + 1, y + arrowH, th.Tex.width, th.Tex.height);
                thumb.material = a.Sharp;
                rail.Thumb = thumb.rectTransform;
            }
            else
            {
                p.Picture(p.Root, "Bar", p.Paint($"rail{w}x{h}", s => HudArt.Rail(10, h - 2 * arrowH, s)), x + (w - 10) / 2f, y + arrowH, 10, h - 2 * arrowH);
                rail.thumbH = 14f * p.K;
                var thumb = p.Picture(p.Root, "Thumb", p.Paint("thumb", s => HudArt.Boss(13f, s, HudArt.Sapphire)), x + (w - 14) / 2f, y + arrowH, 14, 14);
                rail.Thumb = thumb.rectTransform;
            }
            ArrowButton(p, list, x, y, w, arrowH, true);
            ArrowButton(p, list, x, y + h - arrowH, w, arrowH, false);
            return rail;
        }

        static void ArrowButton(GuiPage p, ListView list, float x, float y, float w, float h, bool up)
        {
            string entry = up ? "ScrollInc" : "ScrollDec";
            if (p.Art.Has("scrollbars.gaf", entry))
            {
                var b = ArtButton.Make(p, entry, "scrollbars.gaf", entry, x, y, w, h, "", () => list.ScrollBy(up ? -1 : 1), null);
                b.Glow.enabled = false;
                // The original lights the arrow while it is held.
                return;
            }
            var hit = p.Wash(p.Root, x, y, w, h, LobbyInk.Clear);
            hit.name = entry;
            p.Picture(hit.transform, "Arrow", p.Paint(up ? "arrowUp" : "arrowDown", s => HudArt.Arrow(12, s, up)), (w - 12) / 2f, (h - 12) / 2f, 12, 12);
            var c = hit.gameObject.AddComponent<Clicker>();
            c.Wash = hit;
            c.Step = _ => list.ScrollBy(up ? -1 : 1);
        }
    }

    // A slow breath in scale, for the start the player holds.
    public sealed class Breathe : MonoBehaviour
    {
        public float Amount = 0.07f, Period = 1.8f;
        void Update()
        {
            float k = 1f + Amount * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI / Period));
            transform.localScale = new Vector3(k, k, 1f);
        }
    }

    // A warm light drifting over the page, slow as a candle carried past.
    public sealed class Drift : MonoBehaviour
    {
        public Vector2 Span = new Vector2(300, 120);
        public float Period = 23f;
        Vector2 home;
        bool placed;
        void Update()
        {
            var rt = (RectTransform)transform;
            if (!placed) { home = rt.anchoredPosition; placed = true; }
            float t = Time.unscaledTime * 2f * Mathf.PI / Period;
            rt.anchoredPosition = home + new Vector2(Mathf.Sin(t) * Span.x, Mathf.Sin(t * 0.61f + 1.3f) * Span.y);
        }
    }

    // Fades a screen's page in when it opens. Captures turn it off.
    public sealed class FadeIn : MonoBehaviour
    {
        public static bool Off;
        public float Seconds = 0.22f;
        CanvasGroup group;
        float start;
        void OnEnable()
        {
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            start = Time.unscaledTime;
            group.alpha = Off ? 1f : 0f;
        }
        void Update()
        {
            if (group == null) return;
            group.alpha = Off ? 1f : Mathf.Clamp01((Time.unscaledTime - start) / Seconds);
        }
    }
}
