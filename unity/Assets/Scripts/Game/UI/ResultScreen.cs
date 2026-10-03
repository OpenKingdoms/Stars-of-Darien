// ResultScreen.cs - victory and defeat as the original shows them: first
// the word over the field, as its VictoryText.gui and DefeatText.gui do,
// and three seconds on its statistics screen, victory<side>.gui or
// defeat.gui, on its own art from the player's files. The first page is
// its table, a row a kingdom with the badge, units built, kills, losses,
// time and score, and its Main Menu and Proceed stand where it put them.
// Remastered on the menus' page, with tabs along the top for what a
// modern end screen adds: graphs over the battle, one kingdom at a time,
// and the annals with their honours.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OpenKingdomsUnity.Game.UI
{
    public sealed class ResultScreen
    {
        public static readonly string[] Tabs = { "Tallies", "Graphs", "Kingdoms", "Annals" };
        public static readonly string[] Charts = { "Army", "Worth", "Income", "Mana", "Kills", "Lodestones" };
        static readonly string[] ChartHelp =
        {
            "Units in the field, buildings aside",
            "What the units in the field cost, in mana",
            "Mana gathered each second",
            "Mana in the pool",
            "Units of other kingdoms killed, all told",
            "Lodestones held",
        };

        // The original's rects in cp, from victory<side>.gui and defeat.gui.
        const float RowX = 81f, RowY = 70f, RowW = 479f, RowH = 22f;
        static readonly string[] Heads = { "Player", "Units", "Kills", "Losses", "Time", "Score" };
        static readonly float[] HeadX = { 78f, 239f, 314f, 376f, 438f, 500f }, HeadW = { 111f, 73f, 60f, 60f, 60f, 60f };
        static readonly float[] CellX = { 106f, 239f, 314f, 376f, 438f, 500f }, CellW = { 131f, 73f, 60f, 60f, 60f, 60f };
        static readonly Rect TitleRect = new Rect(149, 398, 342, 56);
        static readonly Rect MenuRect = new Rect(69, 407, 39, 51), ProceedRect = new Rect(534, 407, 39, 51);
        // Where the later pages unroll their panel over the painting.
        static readonly Rect Sheet = new Rect(62, 38, 516, 352);
        static readonly Rect Chart = new Rect(112, 76, 440, 226);
        static readonly Color Leaf = new Color(0.075f, 0.05f, 0.065f, 0.94f);
        // The original's headings, ink on its parchment band, and a fallen
        // kingdom's row, greyed.
        static readonly Color HeadInk = new Color(0.13f, 0.08f, 0.05f), Fallen = new Color(0.6f, 0.58f, 0.54f);

        readonly GameRoot root;
        readonly LobbyScreens lobby;
        readonly RectTransform screen;
        readonly Vector2 canvas;
        readonly float scale;
        readonly Dictionary<string, Texture2D> paint = new Dictionary<string, Texture2D>();
        readonly List<Object> owned = new List<Object>();
        readonly Dictionary<int, Texture2D> pictures = new Dictionary<int, Texture2D>();

        GuiPage p;
        Opening opening;
        CanvasGroup bannerGroup;
        Text banner, title, sentence;
        readonly RectTransform[] pages = new RectTransform[4];
        readonly Image[] tabFaces = new Image[4];
        readonly Text[] tabWords = new Text[4];
        RectTransform chartPicker, chartArea, kingdomPicker, kingdomBody;
        string builtFor;
        int tab, chart, kingdom = -1;
        bool won, bannerOn;
        string mapName;
        int endTick;
        BattleRecord record;

        public ResultScreen(GameRoot root, LobbyScreens lobby, RectTransform screen, Vector2 canvas, float scale)
        {
            this.root = root;
            this.lobby = lobby;
            this.screen = screen;
            this.canvas = canvas;
            this.scale = scale;
        }

        public Opening Opening => opening;
        public int Tab => tab;
        public BattleRecord Record => record;
        public GuiPage Page => p;

        // The page for this verdict, filled from the battle's record. hold
        // is how long the field and the banner show before it fades in, and
        // a fresh verdict opens on the tallies and the player's own kingdom.
        public void Show(bool won, int endTick, string mapName, float hold, bool fresh)
        {
            if (fresh)
            {
                tab = 0;
                chart = 0;
                kingdom = -1;
            }
            this.won = won;
            this.endTick = endTick;
            this.mapName = mapName;
            record = root.Backend.ReadBattle() ?? new BattleRecord();
            var local = root.Backend.PlayerById(root.Backend.LocalPlayer);
            string want = (won ? "won " : "lost ") + (local?.Side ?? "");
            if (want != builtFor) Build(won, local?.Side);
            builtFor = want;
            if (kingdom < 0 || record.Of(kingdom) == null) kingdom = root.Backend.LocalPlayer;
            Fill();
            if (!screen.gameObject.activeSelf) opening.Delay = hold;
            bannerOn = hold > 0.01f;
        }

        // The banner shows at the battle's end until the page has come in
        // over it, and not when Results brings the page back.
        public void Tick()
        {
            if (bannerGroup == null || opening == null) return;
            bannerGroup.alpha = bannerOn && !UiKit.Motion.Still ? 1f - opening.Group.alpha : 0f;
        }

        // Left and Right, Tab and Shift+Tab walk the tabs. Enter presses
        // Proceed and Escape Main Menu, as the dialog's accelerator line has
        // it. True when the key was used.
        public bool Key(KeyCode k, Action proceed, Action mainMenu)
        {
            if (opening == null || opening.Group.alpha < 1f) return false;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            switch (k)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter: UiKit.Play("ok.wav"); proceed(); return true;
                case KeyCode.Escape: UiKit.Play("cancel.wav"); mainMenu(); return true;
                case KeyCode.RightArrow: ShowTab((tab + 1) % Tabs.Length, true); return true;
                case KeyCode.LeftArrow: ShowTab((tab + Tabs.Length - 1) % Tabs.Length, true); return true;
                case KeyCode.Tab: ShowTab((tab + (shift ? Tabs.Length - 1 : 1)) % Tabs.Length, true); return true;
            }
            return false;
        }

        // The banner again, as at the battle's end, for a capture.
        public void ShowBanner() => bannerOn = true;

        public void ShowTab(int t, bool sound = false)
        {
            if (t < 0 || t >= Tabs.Length) return;
            if (sound && t != tab) UiKit.Play("menubutton.wav");
            tab = t;
            for (int i = 0; i < pages.Length; i++)
            {
                if (pages[i] != null) pages[i].gameObject.SetActive(i == t);
                if (tabFaces[i] == null) continue;
                tabFaces[i].sprite = TabFace(i == t);
                tabWords[i].color = i == t ? HudArt.MiniumDeep : HudArt.Ink;
            }
        }

        public void ShowChart(int c)
        {
            chart = Mathf.Clamp(c, 0, Charts.Length - 1);
            FillGraphs();
        }

        public void ShowKingdom(int player)
        {
            kingdom = player;
            FillKingdom();
        }

        public void Dispose()
        {
            foreach (var o in owned) World.Looks.Release(o);
            owned.Clear();
            paint.Clear();
            foreach (var t in pictures.Values) World.Looks.Release(t);
            pictures.Clear();
            tabLit = tabRest = null;
        }

        // ---- The page ----

        static string VictoryArt(string side)
        {
            switch ((side ?? "").ToUpperInvariant())
            {
                case "TAROS": return "TAKVTarosScreen.gaf";
                case "VERUNA": return "TAKVVerunaScreen.gaf";
                case "ZHON": return "TAKVZhonScreen.gaf";
                // Aramon's, as victorycre.gui gives Creon too.
                default: return "TAKVAramonScreen.gaf";
            }
        }

        void Build(bool won, string side)
        {
            for (int i = screen.childCount - 1; i >= 0; i--)
            {
                var c = screen.GetChild(i).gameObject;
                c.SetActive(false);
                World.Looks.Release(c);
            }
            Dispose();

            // The word over the field, centred on the play area.
            var bannerRt = UiKit.Rect(screen, "Banner").Fill();
            bannerGroup = bannerRt.gameObject.AddComponent<CanvasGroup>();
            bannerGroup.blocksRaycasts = false;
            var play = PlayArea();
            var glow = UiKit.Picture(bannerRt, "Banner glow", UiKit.Glow, new Color(0, 0, 0, 0.55f));
            glow.rectTransform.anchorMin = glow.rectTransform.anchorMax = new Vector2(0, 1);
            glow.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            glow.rectTransform.anchoredPosition = new Vector2(play.center.x, -play.center.y);
            glow.rectTransform.sizeDelta = new Vector2(play.width * 0.7f, play.height * 0.36f);
            glow.raycastTarget = false;
            banner = UiKit.Words(bannerRt, "Banner word", play, won ? "Victory" : "Defeat",
                Mathf.RoundToInt(Mathf.Min(play.height * 0.16f, play.width * 0.1f)), won ? HudArt.GoldHi : HudArt.Silver, UiKit.UncialFont);
            banner.raycastTarget = false;
            Shade(banner, 3f);

            // The page, over everything, faded in as one.
            var sheet = UiKit.Rect(screen, "Page").Fill();
            opening = sheet.gameObject.AddComponent<Opening>();
            opening.Seconds = MenuScreens.ResultFade;
            opening.Margin = 0f;
            opening.From = 0.985f;
            p = new GuiPage(sheet, canvas, scale, lobby.Art, paint, owned);
            lobby.Backdrop(p, won ? VictoryArt(side) : "takdefeatscreen.gaf", won ? "VictoryBG" : "DefeatBG", 0, 0,
                new[] { new Rect(RowX - 10f, RowY - 8f, RowW + 20f, 8 * RowH + 14f) });

            for (int i = 0; i < pages.Length; i++)
            {
                pages[i] = p.Put(Tabs[i], 0, 0, GuiPage.W, GuiPage.H);
                if (i == 0) continue;
                // A dark leaf over the painting, framed as the menus' panels are.
                var leaf = p.Wash(pages[i], Sheet.x + 3f, Sheet.y + 3f, Sheet.width - 6f, Sheet.height - 6f, Leaf);
                leaf.raycastTarget = true;
                lobby.Panel(p, pages[i], Sheet);
            }
            BuildTallies();
            BuildGraphs();
            BuildKingdoms();
            BuildAnnals();
            BuildTabs();

            // The title and the two buttons, where the dialog puts them.
            lobby.Plaque(p, TitleRect.x + 70f, TitleRect.y + 12f, TitleRect.width - 140f, TitleRect.height - 24f);
            title = p.Label(p.Root, won ? "Victory" : "Defeat", TitleRect.x, TitleRect.y, TitleRect.width, TitleRect.height, 30f,
                won ? HudArt.GoldHi : HudArt.Silver, TextAnchor.MiddleCenter, UiKit.UncialFont);
            title.name = "Title";
            Shade(title, 1.5f);
            string gaf = won ? VictoryArt(side) : "takdefeatscreen.gaf";
            var menu = ArtButton.Make(p, "Return to menu", gaf, "CancelButton", MenuRect.x, MenuRect.y, MenuRect.width, MenuRect.height,
                "Menu", () => root.Flow.Fire(FlowEvent.ToMenu), "Main Menu. Ends the battle and goes to the main menu");
            menu.Sound = "cancel.wav";
            var field = ArtButton.Make(p, "Look at the field", gaf, "OKButton", ProceedRect.x, ProceedRect.y, ProceedRect.width, ProceedRect.height,
                "Field", LookAtField, "Look at the field. Puts this away to see the battlefield, Results brings it back");
            field.Sound = "ok.wav";
            // The buttons take the click over their whole rect, art or not.
            foreach (var b in new[] { menu, field })
            {
                var hit = b.gameObject.AddComponent<Image>();
                hit.sprite = UiKit.White;
                hit.color = LobbyInk.Clear;
            }
            Caption("Main menu", MenuRect);
            Caption("To the field", ProceedRect);
            lobby.Plaque(p, 200f, 457f, 240f, 15f);
            lobby.HelpLine(p, 150f, 455f, 340f, 19f, "The battle is over. Look at the field, or return to the menu.");
            ShowTab(tab);
        }

        void LookAtField() => root.Screens.LookAtTheField();

        // A kingdom's badge as the original's end screen draws it: teamlogos.gaf
        // <side>Team at its colour, or the menus' emblem without it.
        void Badge(RawImage img, PlayerInfo pl)
        {
            string entry = null;
            switch ((pl.Side ?? "").ToUpperInvariant())
            {
                case "ARAMON": entry = "AraTeam"; break;
                case "TAROS": entry = "TarTeam"; break;
                case "VERUNA": entry = "VerTeam"; break;
                case "ZHON": entry = "ZonTeam"; break;
                case "CREON": entry = "CreTeam"; break;
            }
            var f = entry != null ? lobby.Art.Get("teamlogos.gaf", entry, LobbyScreens.LogoFrame(lobby.Art.Get("teamlogos.gaf", entry).Frames, pl.Colour)) : default;
            if (f.Tex == null)
            {
                lobby.Emblem(p, img, pl.Side, pl.Colour);
                return;
            }
            img.texture = f.Tex;
            img.material = lobby.Art.Sharp;
        }

        void Caption(string words, Rect under)
        {
            var t = p.Label(p.Root, words, under.center.x - 45f, under.yMax + 1f, 90f, 14f, 8f, HudArt.Pearl, TextAnchor.MiddleCenter, LobbyInk.Caps);
            t.name = "Caption " + words;
            Shade(t, 1f);
        }

        static void Shade(Text t, float px)
        {
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0, 0, 0, 0.85f);
            s.effectDistance = new Vector2(px, -px);
        }

        // The battle's view, in canvas units from the top left: the screen
        // less the HUD's sidebar and bottom strip.
        Rect PlayArea()
        {
            var px = canvas * scale;
            int percent = HudLayout.NearestStop(root.Options != null ? root.Options.UiScale : HudLayout.DefaultScale);
            var hl = new HudLayout(Mathf.RoundToInt(px.x), Mathf.RoundToInt(px.y), percent);
            float k = hl.S / Mathf.Max(0.01f, scale);
            return new Rect(hl.Play.x * k, hl.Play.y * k, hl.Play.width * k, hl.Play.height * k);
        }

        Sprite tabLit, tabRest;

        Sprite TabFace(bool lit)
        {
            if (lit && tabLit != null) return tabLit;
            if (!lit && tabRest != null) return tabRest;
            var tex = p.Paint("tab" + (lit ? "Lit" : ""), s => HudArt.Lozenge(78f, 17f, s, lit));
            var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            sp.hideFlags = HideFlags.DontSave;
            owned.Add(sp);
            return lit ? tabLit = sp : tabRest = sp;
        }

        void BuildTabs()
        {
            const float w = 78f, h = 17f, gap = 6f;
            float x0 = (GuiPage.W - (Tabs.Length * w + (Tabs.Length - 1) * gap)) / 2f;
            string[] help =
            {
                "The original's tallies for every kingdom",
                "Every kingdom over the battle, a graph at a time",
                "One kingdom's battle: what it made, spent and lost, and its champion",
                "The key moments of the battle, and the honours won",
            };
            for (int i = 0; i < Tabs.Length; i++)
            {
                int at = i;
                var holder = p.Put("Tab " + Tabs[i], x0 + i * (w + gap), 13f, w, h);
                var face = holder.gameObject.AddComponent<Image>();
                face.sprite = TabFace(false);
                face.raycastTarget = true;
                tabFaces[i] = face;
                tabWords[i] = p.Label(holder, Tabs[i], 0, 0, w, h, 8.5f, HudArt.Ink, TextAnchor.MiddleCenter, LobbyInk.Caps);
                var b = ArtButton.Plain(holder.gameObject, () => ShowTab(at, true));
                b.name = Tabs[i];
                holder.name = Tabs[i];
                p.Hover(holder.gameObject, help[i]);
            }
        }

        // ---- Tallies, the original's table ----

        readonly List<Text[]> rows = new List<Text[]>();
        readonly List<RawImage> badges = new List<RawImage>();
        readonly List<Image> washes = new List<Image>();

        void BuildTallies()
        {
            var page = pages[0];
            rows.Clear();
            badges.Clear();
            washes.Clear();
            for (int c = 0; c < Heads.Length; c++)
            {
                var h = p.Label(page, Heads[c], HeadX[c], 40f, HeadW[c], 20f, 11f, HeadInk, c == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, LobbyInk.Caps);
                h.fontStyle = FontStyle.Bold;
                h.name = "Heading " + Heads[c];
            }
            for (int r = 0; r < 8; r++)
            {
                float y = RowY + r * RowH;
                var wash = p.Wash(page, RowX, y + 1f, RowW, RowH - 2f, LobbyInk.Clear);
                wash.raycastTarget = false;
                washes.Add(wash);
                var badge = p.Picture(page, "Badge", null, RowX, y + 2f, 17f, 17f);
                badges.Add(badge);
                var cells = new Text[Heads.Length];
                for (int c = 0; c < Heads.Length; c++)
                {
                    cells[c] = p.Label(page, "", CellX[c], y, CellW[c], RowH - 2f, 10f, LobbyInk.Text, TextAnchor.MiddleCenter);
                    cells[c].name = Heads[c];
                }
                rows.Add(cells);
            }
            // How it ended, on a panel of its own under the table.
            p.Sliced(page, "Sentence panel", "panel", s => HudArt.PanelFrame(s), HudArt.PanelBorderCp, 110f, 252f, 420f, 26f);
            sentence = p.Label(page, "", 120f, 254f, 400f, 22f, 10f, HudArt.Pearl, TextAnchor.MiddleCenter);
            sentence.name = "Sentence";
            Shade(sentence, 1f);
        }

        void FillTallies()
        {
            var players = root.Backend.Players;
            for (int r = 0; r < rows.Count; r++)
            {
                var cells = rows[r];
                var pl = r < players.Count ? players[r] : null;
                var k = pl != null ? record.Of(pl.Index) : null;
                badges[r].gameObject.SetActive(pl != null);
                washes[r].color = pl != null && pl.IsLocal ? new Color(HudArt.GoldHi.r, HudArt.GoldHi.g, HudArt.GoldHi.b, 0.13f) : LobbyInk.Clear;
                if (pl == null)
                {
                    foreach (var t in cells) t.text = "";
                    continue;
                }
                Badge(badges[r], pl);
                // The player's own row keeps its ink on its gold wash.
                bool fell = k != null && !k.Standing && !pl.IsLocal;
                var ink = fell ? Fallen : LobbyInk.Text;
                int tps = record.TicksPerSecond;
                GuiPage.Fit(cells[0], MenuScreens.ResultName(pl));
                cells[1].text = k != null ? k.UnitsBuilt.ToString("N0") : "";
                cells[2].text = k != null ? k.Kills.ToString("N0") : "";
                cells[3].text = k != null ? k.Losses.ToString("N0") : "";
                cells[4].text = k != null ? BattleRecord.Clock(k.LastAliveTick, tps) : "";
                cells[5].text = k != null ? k.Score.ToString("N0") : "";
                foreach (var t in cells) t.color = ink;
            }
            int secs = endTick / Mathf.Max(1, root.Backend.TicksPerSecond);
            sentence.text = (won ? "Your enemies are vanquished" : "Your kingdom has fallen") +
                $" on {mapName ?? "the field"} after {secs / 60} min {secs % 60} s.";
        }

        // ---- Graphs ----

        LineGraph graph;
        readonly List<Text> axisY = new List<Text>(), axisX = new List<Text>();
        readonly List<Image> grid = new List<Image>();
        readonly List<Clicker> chartChoices = new List<Clicker>();
        RectTransform marks, legend;
        Text empty;

        void BuildGraphs()
        {
            var page = pages[1];
            chartChoices.Clear();
            chartPicker = p.Put(page, "Charts", 76f, 48f, 488f, 18f);
            float w = 488f / Charts.Length;
            for (int i = 0; i < Charts.Length; i++)
            {
                int at = i;
                var c = Clicker.Make(p, chartPicker, Charts[i], i * w, 0, w - 4f, 18f, 9f, _ => ShowChart(at), ChartHelp[i], TextAnchor.MiddleCenter, LobbyInk.Caps);
                c.Label.text = Charts[i];
                chartChoices.Add(c);
            }
            chartArea = p.Put(page, "Chart", Chart.x, Chart.y, Chart.width, Chart.height);
            var ground = chartArea.gameObject.AddComponent<Image>();
            ground.sprite = UiKit.White;
            ground.color = new Color(0, 0, 0, 0.22f);
            ground.raycastTarget = true;
            grid.Clear();
            axisY.Clear();
            axisX.Clear();
            for (int i = 0; i <= 4; i++)
            {
                var line = p.Wash(chartArea, 0, 0, Chart.width, 0.7f, new Color(HudArt.Gold.r, HudArt.Gold.g, HudArt.Gold.b, i == 0 ? 0.6f : 0.2f));
                line.raycastTarget = false;
                grid.Add(line);
                var t = p.Label(page, "", Chart.x - 44f, 0, 40f, 12f, 8f, LobbyInk.Dim, TextAnchor.MiddleRight);
                t.name = "Value";
                axisY.Add(t);
            }
            for (int i = 0; i < 7; i++)
            {
                var t = p.Label(page, "", 0, Chart.yMax + 2f, 44f, 12f, 8f, LobbyInk.Dim, TextAnchor.MiddleCenter);
                t.name = "Minute";
                axisX.Add(t);
            }
            var g = p.Put(chartArea, "Lines", 0, 0, Chart.width, Chart.height);
            if (g.GetComponent<CanvasRenderer>() == null) g.gameObject.AddComponent<CanvasRenderer>();
            graph = g.gameObject.AddComponent<LineGraph>();
            graph.raycastTarget = false;
            marks = p.Put(chartArea, "Moments", 0, 0, Chart.width, Chart.height);
            var hover = chartArea.gameObject.AddComponent<ChartHover>();
            hover.Screen = this;
            hover.Cursor = p.Wash(chartArea, 0, 0, 0.8f, Chart.height, new Color(HudArt.GoldHi.r, HudArt.GoldHi.g, HudArt.GoldHi.b, 0.7f));
            hover.Cursor.raycastTarget = false;
            hover.Cursor.gameObject.SetActive(false);
            empty = p.Label(chartArea, "The battle was too short to chart.", 0, 0, Chart.width, Chart.height, 10f, LobbyInk.Dim, TextAnchor.MiddleCenter);
            empty.name = "Empty";
            legend = p.Put(page, "Legend", 76f, Chart.yMax + 18f, 488f, 36f);
        }

        // The values a chart draws for a kingdom, one a sample.
        public float[] ChartValues(KingdomRecord k, int c)
        {
            if (c == 2) return record.Income(k);
            BattleSeries s = c == 0 ? BattleSeries.Army : c == 1 ? BattleSeries.Worth : c == 3 ? BattleSeries.Mana : c == 4 ? BattleSeries.Kills : BattleSeries.Lodestones;
            var v = k.Of(s);
            var f = new float[v.Length];
            for (int i = 0; i < v.Length; i++) f[i] = v[i];
            return f;
        }

        public int ChartIndex => chart;

        // A kingdom's colour as a line on the dark panel: dark ones lifted.
        public static Color LineColour(Color32 tint)
        {
            Color c = tint;
            float l = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
            return l < 0.42f ? Color.Lerp(c, Color.white, (0.42f - l) / 0.58f + 0.15f) : c;
        }

        void FillGraphs()
        {
            if (graph == null) return;
            for (int i = 0; i < chartChoices.Count; i++)
            {
                chartChoices[i].Rest = i == chart ? LobbyInk.Head : LobbyInk.Text;
                chartChoices[i].Paint();
            }
            var players = root.Backend.Players;
            int n = 0;
            float top = 0f;
            var lines = new List<KeyValuePair<PlayerInfo, float[]>>();
            foreach (var pl in players)
            {
                var k = record.Of(pl.Index);
                if (k == null) continue;
                var v = ChartValues(k, chart);
                n = Mathf.Max(n, v.Length);
                foreach (float x in v) top = Mathf.Max(top, x);
                lines.Add(new KeyValuePair<PlayerInfo, float[]>(pl, v));
            }
            int last = Mathf.Max(1, record.Tick);
            float axisTop = GraphScale.Top(Mathf.Max(top, 1f));
            graph.Lines.Clear();
            // The local kingdom last, so it lies over the rest.
            lines.Sort((a, b) => a.Key.IsLocal.CompareTo(b.Key.IsLocal));
            foreach (var kv in lines)
            {
                var v = kv.Value;
                var pts = new Vector2[v.Length];
                for (int i = 0; i < v.Length; i++)
                    pts[i] = new Vector2(record.SampleTick(i, v.Length) / (float)last, v[i] / axisTop);
                graph.Lines.Add(new LineGraph.Line { Points = pts, Colour = LineColour(kv.Key.Tint), Width = (kv.Key.IsLocal ? 2.2f : 1.5f) * p.K });
            }
            graph.SetVerticesDirty();
            empty.gameObject.SetActive(n < 2);
            graph.gameObject.SetActive(n >= 2);
            for (int i = 0; i < grid.Count; i++)
            {
                float y = Chart.height - Chart.height * i / (grid.Count - 1);
                var rt = grid[i].rectTransform;
                rt.anchoredPosition = new Vector2(0, -y * p.K);
                var t = axisY[i];
                t.rectTransform.anchoredPosition = new Vector2((Chart.x - 44f) * p.K, -(Chart.y + y - 6f) * p.K);
                t.text = n >= 2 ? GraphScale.Label(axisTop * i / (grid.Count - 1)) : "";
            }
            // Minutes along the foot, a step a reader takes in at a glance.
            float minutes = last / (60f * Mathf.Max(1, record.TicksPerSecond));
            float step = minutes <= 6f ? 1f : GraphScale.Step(minutes, 5);
            for (int i = 0; i < axisX.Count; i++)
            {
                float m = i * step;
                bool on = n >= 2 && m <= minutes + 0.001f;
                axisX[i].gameObject.SetActive(on);
                if (!on) continue;
                float x = Chart.x + Chart.width * m / Mathf.Max(0.001f, minutes);
                axisX[i].rectTransform.anchoredPosition = new Vector2((x - 22f) * p.K, -(Chart.yMax + 2f) * p.K);
                axisX[i].text = i == 0 ? "0" : Mathf.RoundToInt(m) + " min";
            }
            FillMarks(last);
            FillLegend();
        }

        void FillMarks(int last)
        {
            for (int i = marks.childCount - 1; i >= 0; i--) Object.Destroy(marks.GetChild(i).gameObject);
            foreach (var m in record.Moments)
            {
                if (m.Kind != MomentKind.MonarchSlain && m.Kind != MomentKind.Fell && m.Kind != MomentKind.Yielded) continue;
                var pl = root.Backend.PlayerById(m.Player);
                Color c = pl != null ? LineColour(pl.Tint) : HudArt.Silver;
                float x = Chart.width * Mathf.Clamp01(m.Tick / (float)last);
                var rule = p.Wash(marks, x - 0.4f, 0, 0.8f, Chart.height, new Color(c.r, c.g, c.b, 0.35f));
                rule.raycastTarget = false;
                var gem = p.Wash(marks, x - 3.5f, Chart.height - 3.5f, 7f, 7f, c);
                gem.rectTransform.localEulerAngles = new Vector3(0, 0, 45f);
                gem.name = m.Kind.ToString();
                p.Hover(gem.gameObject, BattleRecord.Short(m.Tick, record.TicksPerSecond) + "  " + Annal(m));
            }
        }

        void FillLegend()
        {
            for (int i = legend.childCount - 1; i >= 0; i--) Object.Destroy(legend.GetChild(i).gameObject);
            var players = root.Backend.Players;
            int cols = 4;
            float w = 488f / cols;
            for (int i = 0; i < players.Count; i++)
            {
                var pl = players[i];
                float x = (i % cols) * w, y = (i / cols) * 17f;
                var swatch = p.Wash(legend, x, y + 7f, 14f, pl.IsLocal ? 2.6f : 1.8f, LineColour(pl.Tint));
                swatch.raycastTarget = false;
                var img = p.Picture(legend, "Badge", null, x + 17f, y + 1.5f, 13f, 13f);
                Badge(img, pl);
                var t = p.Label(legend, "", x + 33f, y, w - 36f, 16f, 8.5f, LobbyInk.Text);
                GuiPage.Fit(t, MenuScreens.ResultName(pl));
            }
        }

        // The help line's reading of every kingdom at a point of the chart.
        public string ReadAt(float fraction)
        {
            int last = Mathf.Max(1, record.Tick);
            int tick = Mathf.RoundToInt(Mathf.Clamp01(fraction) * last);
            var parts = new List<string>();
            foreach (var pl in root.Backend.Players)
            {
                var k = record.Of(pl.Index);
                if (k == null) continue;
                var v = ChartValues(k, chart);
                if (v.Length == 0) continue;
                int best = 0;
                for (int i = 1; i < v.Length; i++)
                    if (Mathf.Abs(record.SampleTick(i, v.Length) - tick) < Mathf.Abs(record.SampleTick(best, v.Length) - tick)) best = i;
                parts.Add(MenuScreens.ResultName(pl) + " " + GraphScale.Label(v[best]));
            }
            return Charts[chart] + " at " + BattleRecord.Short(tick, record.TicksPerSecond) + ": " + string.Join(", ", parts);
        }

        // ---- One kingdom ----

        readonly List<Text> statNames = new List<Text>(), statValues = new List<Text>();
        static readonly string[] Stats =
        {
            "Units trained", "Buildings raised", "Kills", "Losses", "Damage dealt", "Damage taken", "Spells cast",
            "Mana gathered", "Mana spent", "Most lodestones held", "Largest army", "Fate",
        };

        void BuildKingdoms()
        {
            var page = pages[2];
            kingdomPicker = p.Put(page, "Kingdoms", 76f, 47f, 488f, 20f);
            kingdomBody = p.Put(page, "Kingdom", 0, 0, GuiPage.W, GuiPage.H);
            statNames.Clear();
            statValues.Clear();
            for (int i = 0; i < Stats.Length; i++)
            {
                float y = 80f + i * 18.5f;
                var n = p.Label(kingdomBody, Stats[i], 82f, y, 160f, 17f, 9.5f, LobbyInk.Text);
                n.name = "Stat " + Stats[i];
                var v = p.Label(kingdomBody, "", 230f, y, 80f, 17f, 9.5f, HudArt.GoldHi, TextAnchor.MiddleRight);
                v.name = Stats[i];
                var rule = p.Wash(kingdomBody, 82f, y + 17f, 228f, 0.6f, new Color(HudArt.Gold.r, HudArt.Gold.g, HudArt.Gold.b, 0.18f));
                rule.raycastTarget = false;
                statNames.Add(n);
                statValues.Add(v);
            }
            lobby.Rubric(p, "Made most", 336f, 74f, 220f, 16f).transform.SetParent(kingdomBody, true);
            lobby.Rubric(p, "Champion", 336f, 230f, 220f, 16f).transform.SetParent(kingdomBody, true);
            foreach (var r in kingdomBody.GetComponentsInChildren<Text>(true))
                if (r.name.StartsWith("Rubric")) r.color = HudArt.GoldHi;
        }

        RectTransform made, champion;
        const float PickerLine = 20f;

        void FillKingdom()
        {
            if (kingdomPicker == null) return;
            for (int i = kingdomPicker.childCount - 1; i >= 0; i--) Object.Destroy(kingdomPicker.GetChild(i).gameObject);
            var players = root.Backend.Players;
            // Past four kingdoms the names go on two lines, and the page
            // below steps down to make room.
            int lines = players.Count > 4 ? 2 : 1, across = Mathf.CeilToInt(players.Count / (float)lines);
            float w = Mathf.Min(122f, 488f / Mathf.Max(1, across));
            kingdomBody.anchoredPosition = new Vector2(0f, -(lines - 1) * PickerLine * p.K);
            for (int i = 0; i < players.Count; i++)
            {
                var pl = players[i];
                int id = pl.Index;
                bool on = id == kingdom;
                var c = Clicker.Make(p, kingdomPicker, "Kingdom " + id, (i % across) * w, (i / across) * PickerLine, w - 3f, 20f, 8.5f, _ => ShowKingdom(id),
                    "Shows " + MenuScreens.ResultName(pl) + "'s battle", TextAnchor.MiddleLeft);
                var lr = c.Label.rectTransform;
                lr.anchoredPosition += new Vector2(16f * p.K, 0f);
                lr.sizeDelta -= new Vector2(16f * p.K, 0f);
                GuiPage.Fit(c.Label, MenuScreens.ResultName(pl));
                c.Rest = on ? LobbyInk.Head : LobbyInk.Text;
                c.Paint();
                var img = p.Picture(c.transform, "Badge", null, 2f, 3.5f, 13f, 13f);
                Badge(img, pl);
                if (on) p.Wash(c.transform, 2f, 19f, w - 7f, 1.2f, HudArt.Gold).raycastTarget = false;
            }
            var k = record.Of(kingdom);
            var tps = record.TicksPerSecond;
            string[] values =
            {
                N(k?.UnitsTrained), N(k?.BuildingsRaised), N(k?.Kills), N(k?.Losses), N(k?.DamageDealt), N(k?.DamageTaken), N(k?.SpellsCast),
                N(k != null ? Mathf.RoundToInt(k.ManaGathered) : (int?)null), N(k != null ? Mathf.RoundToInt(k.ManaSpent) : (int?)null),
                N(k?.Peak(BattleSeries.Lodestones)), N(k?.Peak(BattleSeries.Army)),
                k == null ? "" : k.Standing ? "Standing" : "Fell at " + BattleRecord.Short(k.FellTick, tps),
            };
            for (int i = 0; i < statValues.Count; i++) statValues[i].text = values[i];

            if (made != null) Object.Destroy(made.gameObject);
            if (champion != null) Object.Destroy(champion.gameObject);
            made = p.Put(kingdomBody, "Made", 336f, 94f, 224f, 130f);
            champion = p.Put(kingdomBody, "Champion", 336f, 250f, 224f, 60f);
            var defs = root.Backend.UnitDefs;
            if (k == null || k.Made.Count == 0)
                p.Label(made, "Nothing finished in battle yet.", 0, 0, 224f, 18f, 9f, LobbyInk.Dim).name = "None made";
            for (int i = 0; k != null && i < k.Made.Count && i < 4; i++)
            {
                var kv = k.Made[i];
                float y = i * 32f;
                Portrait(made, kv.Key, 0, y, 28f);
                var name = p.Label(made, "", 34f, y, 150f, 28f, 9.5f, LobbyInk.Text);
                GuiPage.Fit(name, UnitWord(kv.Key));
                p.Label(made, kv.Value.ToString("N0"), 180f, y, 42f, 28f, 11f, HudArt.GoldHi, TextAnchor.MiddleRight, LobbyInk.Caps).name = "Made count";
            }
            if (k == null || k.ChampionDef < 0)
            {
                p.Label(champion, "No unit of this kingdom has killed yet.", 0, 0, 224f, 18f, 9f, LobbyInk.Dim).name = "No champion";
                return;
            }
            Portrait(champion, k.ChampionDef, 0, 2f, 44f);
            var who = p.Label(champion, "", 50f, 0, 174f, 18f, 10.5f, HudArt.GoldHi);
            who.name = "Champion name";
            GuiPage.Fit(who, UnitWord(k.ChampionDef));
            string rank = BattleHud.RankWord(k.ChampionRank);
            p.Label(champion, BattleRecord_Kills(k.ChampionKills) + (rank != "" ? ", " + rank : ""), 50f, 18f, 174f, 16f, 9f, LobbyInk.Text).name = "Champion kills";
            p.Label(champion, k.ChampionStanding ? "Still stands" : "Fell in battle", 50f, 34f, 174f, 16f, 9f, LobbyInk.Dim).name = "Champion fate";
        }

        static string BattleRecord_Kills(int n) => Honours.Count(n, "kill");

        static string N(int? v) => v.HasValue ? v.Value.ToString("N0") : "";

        string UnitWord(int def)
        {
            var defs = root.Backend.UnitDefs;
            if (def < 0 || def >= defs.Count) return "Unit";
            var d = defs[def];
            return !string.IsNullOrEmpty(d.Title) ? d.Title : !string.IsNullOrEmpty(d.Description) ? d.Description : d.Name;
        }

        void Portrait(Transform parent, int def, float x, float y, float size)
        {
            var frame = p.Wash(parent, x, y, size, size, new Color(HudArt.Gold.r, HudArt.Gold.g, HudArt.Gold.b, 0.85f));
            frame.raycastTarget = false;
            var img = p.Picture(frame.transform, "Picture", null, 1f, 1f, size - 2f, size - 2f);
            var tex = Picture(def);
            if (tex != null) img.texture = tex;
            else img.color = new Color(0.1f, 0.06f, 0.08f, 1f);
        }

        Texture2D Picture(int def)
        {
            if (pictures.TryGetValue(def, out var t)) return t;
            var raw = root.Backend.UnitPicture(def);
            t = raw != null ? UiKit.ToTexture(raw, true) : null;
            pictures[def] = t;
            return t;
        }

        // ---- The annals ----

        RectTransform annals, honours;

        void BuildAnnals()
        {
            var page = pages[3];
            lobby.Rubric(p, "Key moments", 82f, 48f, 280f, 16f).transform.SetParent(page, true);
            lobby.Rubric(p, "Honours", 392f, 48f, 170f, 16f).transform.SetParent(page, true);
            foreach (var r in page.GetComponentsInChildren<Text>(true))
                if (r.name.StartsWith("Rubric")) r.color = HudArt.GoldHi;
            p.Wash(page, 380f, 50f, 0.8f, 330f, new Color(HudArt.Gold.r, HudArt.Gold.g, HudArt.Gold.b, 0.3f)).raycastTarget = false;
            annals = p.Put(page, "Moments", 82f, 68f, 290f, 316f);
            honours = p.Put(page, "Honours", 392f, 68f, 172f, 316f);
        }

        // The key moments in plain sentences, with the battle's start and end.
        public List<KeyValuePair<int, string>> Annals()
        {
            var list = new List<KeyValuePair<int, string>>
            {
                new KeyValuePair<int, string>(0, $"The battle begins on {mapName ?? "the field"}."),
            };
            bool ended = false;
            foreach (var m in record.Moments)
            {
                if (!ended && m.Tick > endTick)
                {
                    list.Add(new KeyValuePair<int, string>(endTick, won ? "Victory." : "Defeat."));
                    ended = true;
                }
                list.Add(new KeyValuePair<int, string>(m.Tick, Annal(m)));
            }
            if (!ended) list.Add(new KeyValuePair<int, string>(endTick, won ? "Victory." : "Defeat."));
            return list;
        }

        string Annal(BattleMoment m)
        {
            var who = root.Backend.PlayerById(m.Player);
            var other = root.Backend.PlayerById(m.Other);
            switch (m.Kind)
            {
                case MomentKind.FirstBlood:
                    return Cap($"First blood: {Owner(who)} {UnitWord(m.Def)} fells {Owner(other)} {UnitWord(m.OtherDef)}.");
                case MomentKind.MonarchSlain:
                    return Cap($"{Owner(who)} monarch {UnitWord(m.Def)} falls" + (other != null ? $" to {Owner(other)} {UnitWord(m.OtherDef)}." : "."));
                case MomentKind.Fell:
                    return who != null && who.IsLocal ? "You have fallen." : $"{Name(who)} has fallen.";
                case MomentKind.Yielded:
                    return who != null && who.IsLocal ? "You yield." : $"{Name(who)} yields.";
            }
            return "";
        }

        static string Name(PlayerInfo pl) => pl == null ? "A kingdom" : MenuScreens.ResultName(pl);
        static string Owner(PlayerInfo pl) => pl == null ? "a" : pl.IsLocal ? "your" : MenuScreens.ResultName(pl) + "'s";
        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        void FillAnnals()
        {
            if (annals == null) return;
            for (int i = annals.childCount - 1; i >= 0; i--) Object.Destroy(annals.GetChild(i).gameObject);
            for (int i = honours.childCount - 1; i >= 0; i--) Object.Destroy(honours.GetChild(i).gameObject);
            var list = Annals();
            const float rowH = 15.5f;
            int room = Mathf.FloorToInt(316f / rowH);
            int shown = list.Count <= room ? list.Count : room - 1;
            for (int i = 0; i < shown; i++)
            {
                float y = i * rowH;
                p.Label(annals, BattleRecord.Short(list[i].Key, record.TicksPerSecond), 0, y, 34f, rowH, 8.5f, HudArt.GoldHi, TextAnchor.MiddleRight).name = "Moment time";
                var t = p.Label(annals, "", 40f, y, 250f, rowH, 8.5f, LobbyInk.Text);
                t.name = "Moment";
                GuiPage.Fit(t, list[i].Value);
                p.Hover(t.gameObject, list[i].Value);
                t.raycastTarget = true;
            }
            if (shown < list.Count)
                p.Label(annals, $"and {list.Count - shown} more", 40f, shown * rowH, 250f, rowH, 8.5f, LobbyInk.Dim).name = "Moment";

            var won = Honours.Award(record);
            for (int i = 0; i < won.Count && i < 6; i++)
            {
                var h = won[i];
                var pl = root.Backend.PlayerById(h.Player);
                float y = i * 52f;
                p.Label(honours, h.Title, 0, y, 172f, 16f, 9.5f, HudArt.GoldHi, TextAnchor.MiddleLeft, LobbyInk.Caps).name = "Honour";
                var img = p.Picture(honours, "Badge", null, 0, y + 18f, 13f, 13f);
                if (pl != null) Badge(img, pl);
                var t = p.Label(honours, "", 17f, y + 16f, 155f, 16f, 9f, LobbyInk.Text);
                t.name = "Honoured";
                GuiPage.Fit(t, Name(pl));
                var why = p.Label(honours, "", 17f, y + 32f, 155f, 15f, 8f, LobbyInk.Dim);
                why.name = "Why";
                GuiPage.Fit(why, h.Reason);
            }
            if (won.Count == 0) p.Label(honours, "None yet.", 0, 0, 172f, 16f, 9f, LobbyInk.Dim).name = "Honour";
        }

        void Fill()
        {
            FillTallies();
            FillGraphs();
            FillKingdom();
            FillAnnals();
            ShowTab(tab);
        }
    }
}
