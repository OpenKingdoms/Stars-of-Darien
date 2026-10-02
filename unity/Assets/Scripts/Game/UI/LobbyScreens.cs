// LobbyScreens.cs - the opening screen, the skirmish setup, the room list,
// the room and the map choice, each on the original's own screen: its
// background, its buttons and its gadgets where its .gui files put them
// (mainmenu, battlemenusingle, selectgame, battlemenumulti, choosemap).
// Remastered: art drawn sharp at any size, text in the HUD's lettering,
// jewels for the start positions, knotwork in the margins a wide screen
// leaves, a warm light drifting over the page. The map browser's search,
// filters and sorting sit in the line above the map list, where the
// original wrote the list's heading.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OpenKingdomsUnity.Game.UI
{
    public sealed partial class LobbyScreens
    {
        public const int SeatRows = 8;
        // Team 1 to 4 on the Team clicker, then Alone.
        public const int Teams = 4;

        // The team of a seat the page adds: yours keeps its own and every
        // other starts alone, so eight kingdoms are a free for all.
        public static int NewSeatTeam(int index, int yours) => index <= 0 ? yours : SeatTeam.Alone;

        // The Team clicker's next choice: Team 1 to 4, then Alone, the
        // order the original's setup cycles them.
        public static int NextTeam(int team, int by)
        {
            int k = team < 0 || team >= Teams ? Teams : team;
            k = ((k + by) % (Teams + 1) + Teams + 1) % (Teams + 1);
            return k == Teams ? SeatTeam.Alone : k;
        }

        public static string TeamLabel(int team) => team < 0 ? "Alone" : "Team " + (team + 1);
        static readonly string[] Owned = { "Menu", "Skirmish", "Multiplayer", "Room", "MapChoice" };
        static readonly string[] Difficulty = { "Easy", "Normal", "Hard", "Brutal" };

        readonly GameRoot root;
        readonly MenuScreens screens;
        readonly LobbyArt art;
        readonly List<Object> owned = new List<Object>();
        readonly Dictionary<string, Texture2D> paint = new Dictionary<string, Texture2D>();
        readonly MapCatalog catalog = new MapCatalog();
        readonly MapQuery skirmishQuery = new MapQuery(), choiceQuery = new MapQuery();
        Vector2 builtCanvas;
        float builtDensity = -1f;

        MenuPage menu;
        SkirmishPage skirmish;
        NetPage net;
        RoomPage room;
        ChoicePage choice;

        public LobbyScreens(GameRoot root, MenuScreens screens)
        {
            this.root = root;
            this.screens = screens;
            art = new LobbyArt(root.Backend);
        }

        public GameRoot Root => root;
        public IGameBackend Backend => root.Backend;
        public LobbyArt Art => art;
        public float Density => builtDensity;
        public SkirmishPage Skirmish => skirmish;
        public RoomPage RoomScreen => room;
        public ChoicePage Choice => choice;
        public NetPage Net => net;
        public MenuPage Menu => menu;

        // Builds the screens for a canvas of this size in canvas units at
        // this scale, again whenever the page's size or sharpness changes.
        public bool Fit(Vector2 canvas, float scale)
        {
            float k = Mathf.Min(canvas.x / GuiPage.W, canvas.y / GuiPage.H);
            float density = HudArt.Density(k * scale);
            if (menu != null && Mathf.Abs(density - builtDensity) < 0.001f && (canvas - builtCanvas).sqrMagnitude < 1f) return false;
            builtCanvas = canvas;
            builtDensity = density;
            foreach (var n in Owned) screens.DropScreen(n);
            foreach (var o in owned) World.Looks.Release(o);
            owned.Clear();
            paint.Clear();
            menu = new MenuPage(this, Page("Menu", canvas, scale));
            skirmish = new SkirmishPage(this, Page("Skirmish", canvas, scale));
            net = new NetPage(this, Page("Multiplayer", canvas, scale));
            room = new RoomPage(this, Page("Room", canvas, scale));
            choice = new ChoicePage(this, Page("MapChoice", canvas, scale));
            return true;
        }

        GuiPage Page(string name, Vector2 canvas, float scale)
        {
            var s = screens.NewScreenFor(name, false);
            s.gameObject.SetActive(false);
            var p = new GuiPage(s, canvas, scale, art, paint, owned);
            p.Root.gameObject.AddComponent<FadeIn>();
            return p;
        }

        public void Show(FlowState state)
        {
            switch (state)
            {
                case FlowState.MainMenu: menu?.Refresh(); break;
                case FlowState.Skirmish: skirmish?.Opened(); break;
                case FlowState.Multiplayer: net?.Opened(); break;
                case FlowState.Room: room?.Refresh(); break;
                case FlowState.MapChoice: choice?.Opened(); break;
            }
        }

        float nextNet;

        public void Tick()
        {
            // Up and down walk the map list, unless a text field has the keys.
            if (!Typing)
            {
                int by = Input.GetKeyDown(KeyCode.DownArrow) ? 1 : Input.GetKeyDown(KeyCode.UpArrow) ? -1 : 0;
                if (by != 0 && root.Flow.State == FlowState.Skirmish) skirmish?.Browser.Step(by);
                else if (by != 0 && root.Flow.State == FlowState.MapChoice) choice?.Browser.Step(by);
            }
            foreach (var k in PageKeys)
                if (Input.GetKeyDown(k)) { Key(k); break; }
            if (Time.unscaledTime < nextNet) return;
            nextNet = Time.unscaledTime + 0.2f;
            switch (root.Flow.State)
            {
                case FlowState.Multiplayer: net?.Refresh(); break;
                case FlowState.Room: room?.Refresh(); break;
            }
        }

        static readonly KeyCode[] PageKeys = { KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Escape };

        static bool Typing
        {
            get
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                var focus = es != null ? es.currentSelectedGameObject : null;
                return focus != null && focus.GetComponent<InputField>() != null;
            }
        }

        // Enter and Escape on the skirmish and map choice pages, unless a
        // text field has the keys. True when the page took the key.
        public bool Key(KeyCode k)
        {
            if (Typing) return false;
            switch (root.Flow.State)
            {
                case FlowState.Skirmish: return skirmish != null && skirmish.Key(k);
                case FlowState.MapChoice: return choice != null && choice.Key(k);
                default: return false;
            }
        }

        public void Dispose()
        {
            foreach (var o in owned) World.Looks.Release(o);
            owned.Clear();
            art.Dispose();
        }

        // ---- Shared parts ----

        // A kingdom's colour as the menus draw it.
        public static Color32 Tint(int colour) => MockBackend.Palette[Mathf.Abs(colour) % MockBackend.Palette.Length];

        public Texture2D Preview(string mapId) => mapId == null ? null : screens.PreviewFor(mapId);

        public MapInfo MapById(string id)
        {
            foreach (var m in root.Backend.Maps) if (m.Id == id) return m;
            return null;
        }

        public string SideName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Random";
            foreach (var s in root.Backend.Sides) if (s.Id == id) return s.Name;
            return id;
        }

        public string NextSide(string id, int by)
        {
            var ids = new List<string> { "" };
            foreach (var s in root.Backend.Sides) ids.Add(s.Id);
            int i = Mathf.Max(0, ids.IndexOf(id ?? ""));
            return ids[(i + by + ids.Count * 4) % ids.Count];
        }

        // The page under everything: the margins a wide screen leaves, the
        // original's background art or a painted page in its place, and the
        // warm light. panels are the fallback's dark panels, in cp.
        public void Backdrop(GuiPage p, string gaf, string entry, float ox, float oy, Rect[] panels, bool dim = false)
        {
            var screen = p.Screen;
            if (dim)
            {
                var shade = UiKit.Picture(screen, "Dim", UiKit.White, new Color(0, 0, 0, 0.72f));
                shade.rectTransform.Fill();
                shade.transform.SetAsFirstSibling();
            }
            else
            {
                var vellum = p.Paint("vellum", s => HudArt.VellumTile(HudArt.Vellum));
                vellum.wrapMode = TextureWrapMode.Repeat;
                var margin = UiKit.Rect(screen, "Margin").Fill().gameObject.AddComponent<RawImage>();
                margin.texture = vellum;
                margin.color = new Color(0.86f, 0.82f, 0.76f);
                var size = new Vector2(p.Root.sizeDelta.x / GuiPage.W * 96f, p.Root.sizeDelta.y / GuiPage.H * 96f);
                margin.uvRect = new Rect(0, 0, builtCanvas.x / Mathf.Max(1f, size.x), builtCanvas.y / Mathf.Max(1f, size.y));
                margin.raycastTarget = true;
                margin.transform.SetAsFirstSibling();
                float spare = (builtCanvas.x - GuiPage.W * p.K) / 2f / p.K;
                if (spare > 12f) Bands(p, spare);
            }
            p.Root.SetAsLastSibling();
            var bg = p.ArtImage(p.Root, gaf, entry, 0, ox, oy);
            if (bg != null)
            {
                bg.raycastTarget = true;
                bg.transform.SetAsFirstSibling();
                // The art leaves holes for its buttons, black as the game's
                // own screen under them, so no seam shows the margin.
                var under = p.Wash(p.Root, ox, oy, bg.texture.width, bg.texture.height, Color.black);
                under.name = "Under";
                under.raycastTarget = false;
                under.transform.SetAsFirstSibling();
            }
            else PaintedPage(p, ox, oy, panels, dim);
            var light = UiKit.Picture(p.Root, "Light", UiKit.Glow, new Color(1f, 0.84f, 0.58f, 0.07f));
            var lr = light.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0.5f);
            lr.sizeDelta = new Vector2(460f * p.K, 320f * p.K);
            light.raycastTarget = false;
            light.gameObject.AddComponent<Drift>().Span = new Vector2(170f * p.K, 90f * p.K);
        }

        // Knotwork down each side of the page, where a wide screen has room:
        // two-strand interlace on minium between gold keylines, a Solomon's
        // knot with a boss at each end.
        void Bands(GuiPage p, float spare)
        {
            const float band = 9f, knot = 16f;
            var tile = p.Paint("bandV", s => HudArt.TwistTile(band, 18f, s, true, HudArt.Minium));
            tile.wrapMode = TextureWrapMode.Repeat;
            var knotTex = p.Paint("knot", s => HudArt.SolomonKnot(knot, s));
            var boss = p.Paint("bandBoss", s => HudArt.Boss(7f, s, HudArt.Sapphire));
            foreach (float x in new[] { -band - 3f, GuiPage.W + 3f })
            {
                var r = p.Picture(p.Root, "Band", tile, x, knot, band, GuiPage.H - 2 * knot);
                r.uvRect = new Rect(0, 0, 1, (GuiPage.H - 2 * knot) / 18f);
                float kx = x + band / 2f - knot / 2f;
                foreach (float y in new[] { 0f, GuiPage.H - knot })
                {
                    p.Picture(p.Root, "Knot", knotTex, kx, y, knot, knot);
                    p.Picture(p.Root, "Boss", boss, kx + (knot - 8f) / 2f, y + (knot - 8f) / 2f, 8f, 8f);
                }
            }
        }

        // The Carolingian stand-in for the original's background: vellum, a
        // band of interlace along the top and bottom with knots at the
        // corners, and dark panels in gold frames with the clasp and curls.
        // Plaques the painted page puts under text the original sets on its
        // dark lower band, in cp.
        public Rect[] Plaques = new Rect[0];

        void PaintedPage(GuiPage p, float ox, float oy, Rect[] panels, bool dialog)
        {
            var area = p.Put("Painted", ox, oy, dialog && panels.Length > 0 ? panels[0].width : GuiPage.W, dialog && panels.Length > 0 ? panels[0].height : GuiPage.H);
            area.SetAsFirstSibling();
            float w = area.sizeDelta.x / p.K, h = area.sizeDelta.y / p.K;
            var vellum = p.Paint("vellum", s => HudArt.VellumTile(HudArt.Vellum));
            var ground = p.Picture(area, "Vellum", vellum, 0, 0, w, h);
            ground.uvRect = new Rect(0, 0, w / 96f, h / 96f);
            ground.raycastTarget = true;
            const float band = 11f, knot = 17f;
            var tile = p.Paint("bandH", s => HudArt.TwistTile(band, 22f, s, false, HudArt.Azurite));
            tile.wrapMode = TextureWrapMode.Repeat;
            var knotTex = p.Paint("knotBig", s => HudArt.SolomonKnot(knot, s));
            foreach (float y in new[] { 3f, h - band - 3f })
            {
                var b = p.Picture(area, "Band", tile, knot, y, w - 2 * knot, band);
                b.uvRect = new Rect(0, 0, (w - 2 * knot) / 22f, 1);
            }
            foreach (float x in new[] { 0f, w - knot })
                foreach (float y in new[] { 0f, h - knot })
                    p.Picture(area, "Knot", knotTex, x, y, knot, knot);
            for (int i = dialog ? 1 : 0; i < panels.Length; i++) Panel(p, area, panels[i], ox, oy);
            foreach (var r in Plaques) p.Sliced(area, "Plaque panel", "panel", s => HudArt.PanelFrame(s), HudArt.PanelBorderCp, r.x - ox, r.y - oy, r.width, r.height);
            Plaques = new Rect[0];
        }

        public void Panel(GuiPage p, Transform parent, Rect r, float ox = 0, float oy = 0)
        {
            float x = r.x - ox, y = r.y - oy;
            p.Sliced(parent, "Panel", "panel", s => HudArt.PanelFrame(s), HudArt.PanelBorderCp, x, y, r.width, r.height);
            p.Picture(parent, "Clasp", p.Paint("clasp", s => HudArt.Clasp(s)), x + r.width / 2f - 17f, y - 4f, 34f, 10f);
            p.Picture(parent, "Curl", p.Paint("curlL", s => HudArt.Curl(s, false)), x + 1f, y + r.height - 6f, 11f, 11f);
            p.Picture(parent, "Curl", p.Paint("curlR", s => HudArt.Curl(s, true)), x + r.width - 12f, y + r.height - 6f, 11f, 11f);
        }

        // The help line at the foot of the page, and a dark plaque behind
        // text that sits on the painted wood.
        public Text HelpLine(GuiPage p, float x, float y, float w, float h, string resting)
        {
            p.RestingHelp = resting ?? "";
            p.Help = p.Label(p.Root, p.RestingHelp, x, y, w, h, 9.5f, LobbyInk.Text, TextAnchor.MiddleCenter);
            p.Help.name = "Help";
            return p.Help;
        }

        public void Plaque(GuiPage p, float x, float y, float w, float h)
        {
            var shade = UiKit.Picture(p.Root, "Plaque", UiKit.Glow, new Color(0.05f, 0.03f, 0.02f, 0.75f));
            var rt = shade.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2((x - w * 0.12f) * p.K, -(y - h * 0.25f) * p.K);
            rt.sizeDelta = new Vector2(w * 1.24f * p.K, h * 1.5f * p.K);
            shade.raycastTarget = false;
        }

        // A heading written on the page's parchment, in ink capitals.
        public Text Rubric(GuiPage p, string text, float x, float y, float w, float h, TextAnchor align = TextAnchor.MiddleLeft, float size = 10f)
        {
            var t = p.Label(p.Root, text, x, y, w, h, size, HudArt.Ink, align, LobbyInk.Caps);
            t.name = "Rubric " + text;
            return t;
        }

        // A team's emblem as the original's colour column shows it, or a
        // painted jewel in the colour.
        public void Emblem(GuiPage p, RawImage img, string side, int colour)
        {
            string entry = null;
            switch ((side ?? "").ToUpperInvariant())
            {
                case "ARAMON": entry = "AraTeam"; break;
                case "TAROS": entry = "TarTeam"; break;
                case "VERUNA": entry = "VerTeam"; break;
                case "ZHON": entry = "ZonTeam"; break;
            }
            var f = entry != null ? art.Get("colorlogos2.gaf", entry, 2 + (colour & 7)) : default;
            if (f.Tex != null)
            {
                img.texture = f.Tex;
                img.material = art.Sharp;
                return;
            }
            var c = (Color)Tint(colour);
            img.texture = p.Paint("emblem" + (colour & 7), s => HudArt.Boss(16f, s, c));
            img.material = null;
        }

        // The dark well a field or a filter sits in, gold along its foot.
        public static readonly Color WellInk = new Color(HudArt.Purple.r * 0.5f, HudArt.Purple.g * 0.5f, HudArt.Purple.b * 0.5f, 0.94f);

        public static Image Well(GuiPage p, Transform parent, float x, float y, float w, float h)
        {
            var bg = p.Wash(parent, x, y, w, h, WellInk);
            p.Wash(bg.transform, 0, h - 0.9f, w, 0.9f, new Color(HudArt.Gold.r, HudArt.Gold.g, HudArt.Gold.b, 0.85f)).raycastTarget = false;
            return bg;
        }

        public static InputField Field(GuiPage p, Transform parent, float x, float y, float w, float h, string hint, float sizeCp)
        {
            var bg = Well(p, parent, x, y, w, h);
            bg.name = "Field";
            var field = bg.gameObject.AddComponent<InputField>();
            var text = p.Label(bg.transform, "", 3, 0, w - 6, h, sizeCp, LobbyInk.Text);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var place = p.Label(bg.transform, hint, 3, 0, w - 6, h, sizeCp, new Color(LobbyInk.Text.r, LobbyInk.Text.g, LobbyInk.Text.b, 0.8f));
            place.fontStyle = FontStyle.Italic;
            field.textComponent = text;
            field.placeholder = place;
            field.caretColor = HudArt.GoldHi;
            field.selectionColor = new Color(HudArt.Gold.r, HudArt.Gold.g, HudArt.Gold.b, 0.4f);
            field.lineType = InputField.LineType.SingleLine;
            return field;
        }
    }
}
