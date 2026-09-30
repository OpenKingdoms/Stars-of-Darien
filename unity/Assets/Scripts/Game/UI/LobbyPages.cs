// LobbyPages.cs - each of the lobby's screens, gadget by gadget at the
// original's rects (cp, from its .gui files).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Game.UI
{
    public sealed partial class LobbyScreens
    {
        // ---- The opening screen (mainmenu.gui) ----

        public sealed class MenuPage
        {
            readonly LobbyScreens l;
            public readonly GuiPage Page;
            public readonly List<Door> Doors = new List<Door>();

            // The doors: the .gui rect, the art's anchor rect and nudge from
            // the original's menu setup, and what each opens.
            static readonly (string gaf, string entry, Rect gui, Vector2 anchor, Vector2 nudge, string help)[] DoorArt =
            {
                ("singlemachine.gaf", "SingleMachine", new Rect(40, 192, 149, 188), new Vector2(71, 219), new Vector2(-2, -3), "Play the Machine"),
                ("bodgirl.gaf", "BODGirl", new Rect(242, 202, 148, 192), new Vector2(289, 217), Vector2.zero, "Play the Adventure"),
                ("multiknight.gaf", "MultiKnight", new Rect(419, 136, 161, 243), new Vector2(487, 216), new Vector2(3, -1), "Play an Opponent"),
            };

            public MenuPage(LobbyScreens l, GuiPage p)
            {
                this.l = l;
                Page = p;
                var root = l.root;
                l.Plaques = new[] { new Rect(166, 437, 308, 38) };
                l.Backdrop(p, "mainscreen.gaf", "MainBG", 0, 0, new Rect[0]);
                bool art = p.Art.Has("mainscreen.gaf", "MainBG");
                if (!art)
                {
                    var title = p.Label(p.Root, GameRoot.Title, 60, 44, 520, 70, 40f, HudArt.Minium, TextAnchor.MiddleCenter, LobbyInk.Uncial);
                    title.name = "Title";
                    p.Label(p.Root, "A remaster of Total Annihilation: Kingdoms", 60, 112, 520, 20, 11f, HudArt.Ink, TextAnchor.MiddleCenter, LobbyInk.Caps);
                }
                Action[] open =
                {
                    () => root.Flow.Fire(FlowEvent.OpenSkirmish),
                    () => p.ShowHelp("The Adventure comes after skirmish and multiplayer."),
                    () => root.Flow.Fire(FlowEvent.OpenMultiplayer),
                };
                string[] names = { "Skirmish", "Adventure", "Multiplayer" };
                for (int i = 0; i < DoorArt.Length; i++)
                    Doors.Add(Door.Make(p, names[i], DoorArt[i], open[i]));
                Doors[0].Sound = "skirmish.wav";

                ArtButton.Make(p, "Quit", "mainscreen.gaf", "ExitButton", 68, 407, 39, 51, "Quit", () => root.Flow.Fire(FlowEvent.Exit), "Leave the game");
                ArtButton.Make(p, "Options", "mainscreen.gaf", "OptionsButton", 524, 406, 58, 56, "Options", () => root.Flow.Fire(FlowEvent.OpenOptions), "Options");
                // The remaster's two more doors, lettered on the parchment
                // beside the plaque.
                ArtButton.Make(p, "Load game", null, null, 66, 391, 96, 20, "Load game", () => root.Flow.Fire(FlowEvent.OpenLoad), "Load a saved game");
                ArtButton.Make(p, "Map editor", null, null, 466, 391, 100, 20, "Map editor", () => root.Flow.Fire(FlowEvent.OpenEditor), "Change a map, or make one");

                var version = p.Label(p.Root, GameRoot.Title + ", free and open, played with your own game files", 172, 442, 296, 13, 8.5f, HudArt.GoldHi, TextAnchor.MiddleCenter);
                version.name = "Version";
                l.HelpLine(p, 172, 455, 296, 17, Resting(root));
            }

            static string Resting(GameRoot root)
            {
                string engine = root.Backend is MockBackend ? "Mock engine, made-up maps." : "Engine: " + root.Backend.Name + ".";
                return string.IsNullOrEmpty(root.BackendProblem) ? engine : engine + " " + root.BackendProblem;
            }

            public void Refresh()
            {
                Page.RestingHelp = Resting(l.root);
                Page.ShowHelp(null);
            }
        }

        // A character door: at rest its first picture, and under the pointer
        // the rest in turn at eight a second, finishing the round after it
        // leaves, as the original does without its clips.
        public sealed class Door : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
        {
            public Action Clicked;
            public RawImage Face;
            public readonly List<LobbyArt.Frame> Frames = new List<LobbyArt.Frame>();
            public Vector2 Anchor;
            public GuiPage Page;
            public int Current;
            bool over, playing;
            float clock;

            public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) { over = true; playing = true; }
            public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) { over = false; }
            public string Sound = "menubutton.wav";

            public void Press()
            {
                UiKit.Play(Sound);
                Clicked?.Invoke();
            }

            void Update()
            {
                if (!playing || Frames.Count < 2) return;
                clock += Time.unscaledDeltaTime;
                if (clock < 0.125f) return;
                clock -= 0.125f;
                Current = (Current + 1) % Frames.Count;
                if (Current == 0 && !over) playing = false;
                Place();
            }

            public void Place()
            {
                if (Face == null || Frames.Count == 0) return;
                var f = Frames[Current];
                if (f.Tex == null) return;
                Face.texture = f.Tex;
                var rt = Face.rectTransform;
                rt.anchoredPosition = new Vector2((Anchor.x - f.Origin.x) * Page.K, -(Anchor.y - f.Origin.y) * Page.K);
                rt.sizeDelta = new Vector2(f.Tex.width * Page.K, f.Tex.height * Page.K);
            }

            public static Door Make(GuiPage p, string name, (string gaf, string entry, Rect gui, Vector2 anchor, Vector2 nudge, string help) d, Action clicked)
            {
                var hit = p.Wash(p.Root, d.gui.x, d.gui.y, d.gui.width, d.gui.height, LobbyInk.Clear);
                hit.name = name;
                var door = hit.gameObject.AddComponent<Door>();
                ArtButton.Plain(hit.gameObject, door.Press);
                door.Clicked = clicked;
                door.Page = p;
                door.Anchor = d.anchor + d.nudge;
                for (int i = 0; i < 8; i++)
                {
                    var f = p.Art.Get(d.gaf, d.entry + i);
                    if (f.Tex == null) break;
                    door.Frames.Add(f);
                }
                if (door.Frames.Count > 0)
                {
                    door.Face = p.Picture(p.Root, name + " art", door.Frames[0].Tex, 0, 0, 1, 1);
                    door.Face.material = p.Art.Sharp;
                    door.Face.transform.SetSiblingIndex(hit.transform.GetSiblingIndex());
                    door.Place();
                }
                else
                {
                    var arch = p.Picture(hit.transform, "Arch", p.Paint($"arch{d.gui.width}x{d.gui.height}", s => HudArt.Arch(d.gui.width - 20f, d.gui.height - 30f, s)), 10, 20, d.gui.width - 20f, d.gui.height - 30f);
                    var t = p.Label(arch.transform, d.help, 10, 30, d.gui.width - 40f, d.gui.height - 70f, 12f, HudArt.GoldHi, TextAnchor.MiddleCenter, LobbyInk.Uncial);
                    t.name = "Door name";
                }
                p.Hover(hit.gameObject, d.help);
                return door;
            }
        }

        // ---- The map browser, for the skirmish and the map choice ----

        // Where the browser's gadgets go on a page, in cp.
        public struct BrowserRects
        {
            public float StripY, StripH, SearchX, SearchW, PlayersX, PlayersW, SizeX, SizeW, SortX, SortW;
            public float ListX, ListY, ListW, RowH;
            public int Rows;
            public float RailX, RailW;
            public string Rail;
            public bool SizeColumn;
            public float TextCp;
        }

        public sealed class MapBrowser
        {
            static readonly string[] Sorts = { "Name, A to Z", "Name, Z to A", "Fewest players", "Most players", "Smallest", "Largest" };
            // The search and the filters, in the list's letters.
            const float FilterCp = 11f;

            readonly LobbyScreens l;
            readonly GuiPage p;
            readonly MapQuery q;
            readonly Func<string> current;
            readonly Action<string> pick;
            public readonly ListView List;
            public readonly InputField Search;
            public readonly Clicker Players, Size, Sort;
            public readonly List<MapInfo> Shown = new List<MapInfo>();
            string placedFor;

            public MapBrowser(LobbyScreens l, GuiPage p, MapQuery q, BrowserRects r, Func<string> current, Action<string> pick)
            {
                this.l = l;
                this.p = p;
                this.q = q;
                this.current = current;
                this.pick = pick;
                Search = Field(p, p.Root, r.SearchX, r.StripY + 1, r.SearchW, r.StripH - 2, "Search maps", FilterCp);
                Search.name = "Search";
                Search.text = q.Text;
                Search.onValueChanged.AddListener(v => { q.Text = v; Refresh(true); });
                p.Hover(Search.gameObject, "Type part of a map's name");
                Players = Chooser("Players", r.PlayersX, r.StripY, r.PlayersW, r.StripH, by => { q.Players = Step(q.Players, by); Refresh(true); }, "Show maps for this many kingdoms");
                Size = Chooser("Size", r.SizeX, r.StripY, r.SizeW, r.StripH, by => { q.Size = (MapSize)(((int)q.Size + by + 5) % 5); Refresh(true); }, "Show maps of this size");
                Sort = Chooser("Sort", r.SortX, r.StripY, r.SortW, r.StripH, by =>
                {
                    int i = ((int)q.Sort * 2 + (q.Descending ? 1 : 0) + by + 6) % 6;
                    q.Sort = (MapSort)(i / 2);
                    q.Descending = i % 2 == 1;
                    Refresh(true);
                }, "Order the maps");

                float nameW = r.ListW - 8f - (r.SizeColumn ? 76f : 26f);
                List = ListView.Make(p, "Maps", r.ListX, r.ListY, r.ListW, r.RowH, r.Rows, (pg, row) =>
                {
                    row.Cells.Add(pg.Label(row.transform, "", 6, 0, nameW, r.RowH, r.TextCp, LobbyInk.Text));
                    row.Cells.Add(pg.Label(row.transform, "", 6 + nameW, 0, 22, r.RowH, r.TextCp - 1f, LobbyInk.Text, TextAnchor.MiddleRight, LobbyInk.Caps));
                    if (r.SizeColumn) row.Cells.Add(pg.Label(row.transform, "", 6 + nameW + 24, 0, 46, r.RowH, r.TextCp - 1.5f, LobbyInk.Text, TextAnchor.MiddleRight, LobbyInk.Caps));
                    pg.Hover(row.gameObject, "Select map");
                    return 0;
                });
                List.Bind = (i, row) =>
                {
                    var m = Shown[i];
                    GuiPage.Fit(row.Cells[0], MapCatalog.DisplayName(m));
                    row.Cells[1].text = MapCatalog.PlayersOf(m).ToString();
                    if (row.Cells.Count > 2) row.Cells[2].text = MapCatalog.SizeLabel(m);
                };
                List.Picked = i => pick(Shown[i].Id);
                ScrollRail.Make(p, List, r.RailX, r.ListY, r.RailW, r.RowH * r.Rows, r.Rail);
            }

            // Light letters in a dark well like the search's, so they read on
            // any parchment.
            Clicker Chooser(string name, float x, float y, float w, float h, Action<int> step, string help)
            {
                var well = Well(p, p.Root, x, y + 1, w, h - 2);
                well.name = name + " well";
                well.raycastTarget = false;
                return Clicker.Make(p, well.transform, name, 0, -1, w, h, FilterCp, step, help, TextAnchor.MiddleCenter, LobbyInk.Body);
            }

            static int Step(int players, int by)
            {
                // Any, then 2 to 8 starts.
                int i = players == 0 ? 0 : players - 1;
                i = (i + by + 8) % 8;
                return i == 0 ? 0 : i + 1;
            }

            // The list for the query. The chosen map stays chosen and comes
            // into view when it changes from outside.
            public void Refresh(bool queryChanged = false)
            {
                l.catalog.Use(l.root.Backend.Maps);
                l.catalog.Query(q, Shown);
                Players.Label.text = q.Players == 0 ? "Any players" : q.Players + " players";
                Size.Label.text = MapCatalog.SizeNames[(int)q.Size];
                Sort.Label.text = Sorts[(int)q.Sort * 2 + (q.Descending ? 1 : 0)];
                string id = current();
                int sel = Shown.FindIndex(m => m.Id == id);
                List.Selected = sel;
                List.Count = Shown.Count;
                if (queryChanged) List.Top = 0;
                if (sel >= 0 && (placedFor != id || queryChanged)) List.ScrollTo(sel);
                placedFor = id;
                List.SetCount(Shown.Count);
            }

            // Up and down move the choice through the list.
            public void Step(int by)
            {
                if (Shown.Count == 0) return;
                int i = Mathf.Clamp((List.Selected < 0 ? 0 : List.Selected + by), 0, Shown.Count - 1);
                pick(Shown[i].Id);
            }
        }

        // ---- The skirmish setup (battlemenusingle.gui) ----

        public sealed class SkirmishPage
        {
            readonly LobbyScreens l;
            public readonly GuiPage Page;
            public readonly MapBrowser Browser;
            public readonly StartMap Map;
            readonly SeatParts[] seats = new SeatParts[SeatRows];
            readonly List<CheckBox> checks = new List<CheckBox>();
            readonly Clicker weather, speed;
            readonly Text mapName, mapInfo, mapText, units;
            readonly RectTransform unitThumb;
            public Text Error;
            string errorText;

            public sealed class SeatParts
            {
                public RectTransform Row;
                public Clicker Start, Name, Side, Team;
                public Text StartNumber;
                public RawImage Jewel, Emblem;
                public Clicker Colour;
            }

            SkirmishSetup Setup => l.root.Setup;

            public SkirmishPage(LobbyScreens l, GuiPage p)
            {
                this.l = l;
                Page = p;
                var root = l.root;
                l.Plaques = new[] { new Rect(194, 395, 252, 80) };
                l.Backdrop(p, "battleskirmscreen.gaf", "BattleSkirm", 0, 0, new[]
                {
                    new Rect(55, 47, 330, 193), new Rect(398, 47, 189, 193), new Rect(55, 264, 135, 136), new Rect(198, 264, 389, 136),
                });

                // Seats, their heading written on the parchment above.
                l.Rubric(p, "Name", 88, 30, 100, 20);
                l.Rubric(p, "Side", 200, 30, 70, 20);
                l.Rubric(p, "Color", 271, 30, 49, 20);
                l.Rubric(p, "Team", 314, 30, 63, 20);
                for (int i = 0; i < SeatRows; i++) seats[i] = SeatRow(i, 61 + 22 * i);

                // Game information.
                l.Rubric(p, "Game Information", 403, 30, 178, 20, TextAnchor.MiddleCenter);
                string[] labels = { "Unit sight reveals the map", "Terrain visible at start", "Game goes on if a monarch dies", "Monarchs placed at random" };
                Func<bool>[] get = { () => Setup.LineOfSight, () => Setup.MapRevealed, () => Setup.MonarchExpendable, () => Setup.RandomStarts };
                Action[] flip =
                {
                    () => Setup.LineOfSight = !Setup.LineOfSight, () => Setup.MapRevealed = !Setup.MapRevealed,
                    () => Setup.MonarchExpendable = !Setup.MonarchExpendable, () => { Setup.RandomStarts = !Setup.RandomStarts; Refresh(); },
                };
                for (int k = 0; k < labels.Length; k++)
                {
                    float y = 61 + 22 * k;
                    p.Label(p.Root, labels[k], 413, y, 143, 20, 8.5f, LobbyInk.Text);
                    checks.Add(CheckBox.Make(p, p.Root, 558, y + 4, get[k], flip[k], labels[k]));
                }
                p.Label(p.Root, "Weather", 413, 149, 70, 20, 9f, LobbyInk.Text);
                weather = Clicker.Make(p, p.Root, "Weather", 486, 149, 90, 20, 9f, by =>
                {
                    root.Options.Weather = (WeatherChoice)(((int)root.Options.Weather + by + 5) % 5);
                    root.Options.Save();
                    Refresh();
                }, "The weather, or the map's own", TextAnchor.MiddleRight);
                p.Label(p.Root, "Game speed", 413, 171, 70, 20, 9f, LobbyInk.Text);
                speed = Clicker.Make(p, p.Root, "Speed", 486, 171, 90, 20, 9f, by =>
                {
                    root.Options.GameSpeed = root.Options.GameSpeed == 1 ? 2 : 1;
                    root.Options.Save();
                    Refresh();
                }, "How fast the battle runs", TextAnchor.MiddleRight);
                p.Label(p.Root, "Maximum units", 413, 193, 110, 20, 9f, LobbyInk.Text);
                units = p.Label(p.Root, "", 527, 193, 47, 20, 9f, LobbyInk.Text, TextAnchor.MiddleRight, LobbyInk.Caps);
                unitThumb = UnitSlider(p, () => Setup.UnitLimit, v => { Setup.UnitLimit = v; Refresh(); });

                // The map list, its search and filters on the heading line.
                Browser = new MapBrowser(l, p, l.skirmishQuery, new BrowserRects
                {
                    StripY = 244, StripH = 18, SearchX = 200, SearchW = 124, PlayersX = 330, PlayersW = 76, SizeX = 410, SizeW = 62, SortX = 476, SortW = 108,
                    ListX = 210, ListY = 275, ListW = 346, RowH = 22, Rows = 5, RailX = 558, RailW = 25, Rail = "BattleBar", SizeColumn = true, TextCp = 10f,
                }, () => Setup.MapId, id => { Setup.MapId = id; Refresh(); });

                Map = new StartMap(p, 67, 276, 110, 110);
                Map.OwnerOf = Owner;
                Map.Take = i =>
                {
                    int n = Starts;
                    if (Setup.Seats[0].Start == i) { StartPositions.Release(Setup.Seats, 0); return true; }
                    return StartPositions.Move(Setup.Seats, 0, i, n);
                };
                Map.Move = (a, b) =>
                {
                    int holder = StartPositions.HolderOf(Setup.Seats, a);
                    return holder >= 0 && StartPositions.Move(Setup.Seats, holder, b, Starts);
                };
                Map.Changed = Refresh;

                l.Plaque(p, 198, 399, 244, 46);
                mapName = p.Label(p.Root, "", 198, 398, 244, 16, 11f, HudArt.GoldHi, TextAnchor.MiddleCenter, LobbyInk.Uncial);
                mapName.name = "Map name";
                mapInfo = p.Label(p.Root, "", 198, 413, 244, 12, 8.5f, LobbyInk.Text, TextAnchor.MiddleCenter, LobbyInk.Caps);
                mapText = p.Label(p.Root, "", 200, 424, 240, 22, 8.5f, LobbyInk.Text, TextAnchor.UpperCenter);
                mapText.verticalOverflow = VerticalWrapMode.Truncate;

                ArtButton.Make(p, "Back", "battleskirmscreen.gaf", "CancelButton", 59, 407, 39, 51, "Back", () => root.Flow.Fire(FlowEvent.Back), "Previous Screen");
                ArtButton.Make(p, "Load", "battleskirmscreen.gaf", "MapButton", 113, 406, 60, 57, "Load", () => root.Flow.Fire(FlowEvent.OpenLoad), "Load Game");
                ArtButton.Make(p, "Options", "battleskirmscreen.gaf", "OptionsButton", 470, 406, 60, 57, "Options", () => root.Flow.Fire(FlowEvent.OpenOptions), "Options");
                ArtButton.Make(p, "Start", "battleskirmscreen.gaf", "OKButton", 544, 407, 39, 51, "Play", () => l.screens.StartGame(), "Start Game");
                Error = l.HelpLine(p, 208, 452, 224, 30, "");
            }

            SeatParts SeatRow(int i, float y)
            {
                var p = Page;
                var s = new SeatParts { Row = p.Put("Seat " + i, 64, y, 311, 20) };
                s.Start = Clicker.Make(p, p.Root, "Start", 66, y + 1, 18, 18, 8f, by => { StepStart(i, by); }, "Start position: click for the next free one", TextAnchor.MiddleCenter);
                s.Jewel = p.Picture(s.Start.transform, "Jewel", null, 1.5f, 1.5f, 15, 15);
                s.StartNumber = p.Label(s.Start.transform, "", 1.5f, 1.5f, 15, 15, 8f, HudArt.Ink, TextAnchor.MiddleCenter, LobbyInk.Caps);
                s.Start.Label.gameObject.SetActive(false);
                s.Name = Clicker.Make(p, p.Root, "Name", 88, y, 107, 20, 9.5f, i == 0 ? (Action<int>)null : by => StepKind(i, by), "Select player or AI");
                s.Side = Clicker.Make(p, p.Root, "Side", 196, y, 77, 20, 9.5f, by => { var st = Setup.Seats[i]; st.Side = l.NextSide(st.Side, by); Refresh(); }, "Click to select side");
                s.Colour = Clicker.Make(p, p.Root, "Colour", 284, y, 21, 20, 8f, by => { var st = Setup.Seats[i]; st.Colour = (st.Colour + by + 8) % 8; Refresh(); }, "Click to select color");
                s.Emblem = p.Picture(s.Colour.transform, "Emblem", null, 2, 1.5f, 17, 17);
                s.Colour.Label.gameObject.SetActive(false);
                s.Team = Clicker.Make(p, p.Root, "Team", 317, y, 50, 20, 9.5f, by => { var st = Setup.Seats[i]; st.Team = (st.Team + by + 4) % 4; Refresh(); }, "Click to create teams");
                return s;
            }

            void StepKind(int i, int by)
            {
                var st = Setup.Seats[i];
                int k = st.Kind == SeatKind.Closed ? 0 : 1 + (int)st.Difficulty;
                k = (k + by + 5) % 5;
                st.Kind = k == 0 ? SeatKind.Closed : SeatKind.Computer;
                if (k > 0) st.Difficulty = (AiDifficulty)(k - 1);
                StartPositions.Tidy(Setup.Seats, Starts);
                Refresh();
            }

            // A seat's start steps through "any" and the starts nobody holds.
            void StepStart(int i, int by)
            {
                var seats = Setup.Seats;
                if (!StartPositions.Open(seats[i])) return;
                var choices = new List<int> { -1 };
                for (int k = 0; k < Starts; k++)
                {
                    int h = StartPositions.HolderOf(seats, k);
                    if (h < 0 || h == i) choices.Add(k);
                }
                int at = Mathf.Max(0, choices.IndexOf(seats[i].Start));
                int next = choices[(at + by + choices.Count) % choices.Count];
                if (next < 0) StartPositions.Release(seats, i); else StartPositions.Claim(seats, i, next, Starts);
                Refresh();
            }

            RectTransform UnitSlider(GuiPage p, Func<int> get, Action<int> set)
            {
                const int min = 50, max = 500, step = 25;
                Action<int> by = d => set(Mathf.Clamp(get() + d * step, min, max));
                var art = p.Art;
                RectTransform thumb;
                if (art.Has("scrollbars.gaf", "UnitBattleBar"))
                {
                    p.ArtImage(p.Root, "scrollbars.gaf", "UnitBattleBar", 0, 415, 218);
                    var dec = ArtButton.Make(p, "Fewer units", "scrollbars.gaf", "UnitBattleInc", 415, 218, 12, 13, "", () => by(-1), "Fewer units for each kingdom");
                    dec.Glow.enabled = false;
                    var inc = ArtButton.Make(p, "More units", "scrollbars.gaf", "UnitBattleDec", 560, 218, 12, 13, "", () => by(1), "More units for each kingdom");
                    inc.Glow.enabled = false;
                    var th = art.Get("scrollbars.gaf", "UnitBattleThumb");
                    var img = p.Picture(p.Root, "Units thumb", th.Tex, 431, 218, 13, 13);
                    img.material = art.Sharp;
                    thumb = img.rectTransform;
                }
                else
                {
                    p.Wash(p.Root, 430, 224, 128, 1.2f, new Color(HudArt.Gold.r, HudArt.Gold.g, HudArt.Gold.b, 0.9f)).raycastTarget = false;
                    var dec = Clicker.Make(p, p.Root, "Fewer units", 413, 216, 14, 16, 11f, _ => by(-1), "Fewer units for each kingdom", TextAnchor.MiddleCenter, LobbyInk.Caps);
                    dec.Label.text = "‹";
                    var inc = Clicker.Make(p, p.Root, "More units", 560, 216, 14, 16, 11f, _ => by(1), "More units for each kingdom", TextAnchor.MiddleCenter, LobbyInk.Caps);
                    inc.Label.text = "›";
                    thumb = p.Picture(p.Root, "Units thumb", p.Paint("unitThumb", s => HudArt.Boss(12f, s, HudArt.Sapphire)), 431, 218, 13, 13).rectTransform;
                }
                return thumb;
            }

            int Starts
            {
                get
                {
                    var m = l.root.CurrentMap();
                    return m != null && m.Starts != null ? m.Starts.Length : 0;
                }
            }

            StartMap.Owner Owner(int i)
            {
                var seats = Setup.Seats;
                int holder = StartPositions.HolderOf(seats, i);
                if (holder >= 0) return new StartMap.Owner { Colour = seats[holder].Colour, Held = true, Mine = holder == 0, Who = SeatName(holder) };
                if (!Setup.RandomStarts)
                {
                    var dealt = StartPositions.Assign(seats, Starts, false, 0);
                    for (int k = 0; k < dealt.Length; k++)
                        if (dealt[k] == i) return new StartMap.Owner { Colour = seats[k].Colour, Held = false, Mine = false, Who = SeatName(k) };
                }
                return new StartMap.Owner { Colour = -1 };
            }

            string SeatName(int i)
            {
                var st = Setup.Seats[i];
                if (i == 0) return "You";
                return st.Kind == SeatKind.Closed ? "Closed" : "Computer (" + Difficulty[(int)st.Difficulty] + ")";
            }

            public void Opened()
            {
                errorText = null;
                var seats = Setup.Seats;
                while (seats.Count < SeatRows)
                {
                    int c = 0;
                    while (seats.Exists(x => x.Colour == c) && c < 7) c++;
                    seats.Add(new SeatSetup { Kind = SeatKind.Closed, Side = "", Colour = c, Team = seats.Count % 4 });
                }
                Refresh();
            }

            public void ShowError(string text)
            {
                errorText = text;
                Page.RestingHelp = text ?? "";
                Page.ShowHelp(null);
                Error.color = string.IsNullOrEmpty(text) ? LobbyInk.Text : new Color(1f, 0.62f, 0.5f);
            }

            public void Refresh()
            {
                var root = l.root;
                var map = root.CurrentMap();
                if (map != null) Setup.MapId = map.Id;
                StartPositions.Tidy(Setup.Seats, Starts);
                var dealt = StartPositions.Assign(Setup.Seats, Starts, false, 0);
                for (int i = 0; i < SeatRows; i++)
                {
                    var s = seats[i];
                    bool exists = i < Setup.Seats.Count;
                    var st = exists ? Setup.Seats[i] : null;
                    bool open = StartPositions.Open(st);
                    s.Name.Label.text = !exists ? "" : SeatName(i);
                    s.Name.Rest = open ? LobbyInk.Text : LobbyInk.Dim;
                    s.Name.Paint();
                    foreach (var c in new[] { s.Side, s.Team, s.Colour, s.Start }) c.gameObject.SetActive(open);
                    if (!open) continue;
                    GuiPage.Fit(s.Side.Label, l.SideName(st.Side));
                    s.Team.Label.text = "Team " + (st.Team + 1);
                    l.Emblem(Page, s.Emblem, st.Side, st.Colour);
                    int start = st.Start >= 0 ? st.Start : Setup.RandomStarts ? -1 : dealt[i];
                    bool held = st.Start >= 0;
                    var stone = start < 0 ? HudArt.Pearl : (Color)Tint(st.Colour);
                    s.Jewel.texture = Page.Paint(start < 0 ? "seatFree" : "seat" + st.Colour, sc => HudArt.Boss(14f, sc, stone));
                    s.Jewel.color = held || start < 0 ? Color.white : new Color(1, 1, 1, 0.62f);
                    s.StartNumber.text = start < 0 ? "?" : (start + 1).ToString();
                    s.StartNumber.color = stone.grayscale > 0.55f ? HudArt.Ink : Color.white;
                    s.Side.Paint(); s.Team.Paint();
                }
                foreach (var c in checks) c.Paint();
                weather.Label.text = new[] { "By map", "Clear", "Rain", "Snow", "Fog" }[(int)root.Options.Weather % 5];
                speed.Label.text = root.Options.GameSpeed > 1 ? "Fast" : "Normal";
                weather.Paint(); speed.Paint();
                units.text = Setup.UnitLimit.ToString();
                float t = Mathf.InverseLerp(50, 500, Setup.UnitLimit);
                unitThumb.anchoredPosition = new Vector2(Mathf.Lerp(427, 547, t) * Page.K, unitThumb.anchoredPosition.y);

                Browser.Refresh();
                Map.Show(map, map != null ? l.Preview(map.Id) : null);
                if (map != null)
                {
                    GuiPage.Fit(mapName, MapCatalog.DisplayName(map));
                    string climate = string.IsNullOrEmpty(map.Climate) ? "" : ", " + map.Climate;
                    mapInfo.text = $"{MapCatalog.SizeLabel(map)}, {MapCatalog.PlayersOf(map)} players{climate}";
                    mapText.text = map.Description;
                }
                if (errorText == null) ShowError(root.LastError);
            }
        }

        // ---- The room list (selectgame.gui) ----

        public sealed class NetPage
        {
            public const string NameKey = "oku.mp.name", RelayKey = "oku.mp.relay";
            readonly LobbyScreens l;
            public readonly GuiPage Page;
            public readonly ListView List;
            public readonly InputField Name, Relay, Code;
            readonly Clicker connect;
            readonly Text status;
            readonly Text[] info = new Text[6];
            readonly GameObject codeDialog;
            IGameRooms Rooms => l.root.Backend.Rooms;

            public NetPage(LobbyScreens l, GuiPage p)
            {
                this.l = l;
                Page = p;
                var root = l.root;
                l.Plaques = new[] { new Rect(200, 447, 240, 32) };
                l.Backdrop(p, "connectionscreen.gaf", "Connection", 0, 0, new[]
                {
                    new Rect(55, 44, 225, 42), new Rect(55, 117, 330, 266), new Rect(398, 117, 189, 266),
                });
                // Who you are, where the original asks your name.
                Name = Field(p, p.Root, 69, 57, 194, 21, "Your name", 10f);
                Name.name = "Name";
                Name.text = PlayerPrefs.GetString(NameKey, "");
                Name.onEndEdit.AddListener(v => { PlayerPrefs.SetString(NameKey, v); PlayerPrefs.Save(); });
                p.Hover(Name.gameObject, "Your name, as the other players see it");

                // The relay, in the corner where the original offered its
                // matchmaking services.
                l.Panel(p, p.Root, new Rect(398, 26, 189, 62));
                p.Label(p.Root, "Relay", 408, 32, 60, 16, 9f, LobbyInk.Head, TextAnchor.MiddleLeft, LobbyInk.Caps);
                Relay = Field(p, p.Root, 452, 32, 126, 16, "ws://relay address", 8.5f);
                Relay.name = "Relay";
                Relay.text = PlayerPrefs.GetString(RelayKey, root.Backend is MockBackend ? "mock://relay" : "");
                Relay.onEndEdit.AddListener(v => { PlayerPrefs.SetString(RelayKey, v); PlayerPrefs.Save(); });
                status = p.Label(p.Root, "", 408, 55, 118, 26, 8.5f, LobbyInk.Text, TextAnchor.MiddleLeft);
                status.name = "Status";
                connect = Clicker.Make(p, p.Root, "Connect", 524, 58, 58, 20, 9f, _ => ToggleConnection(), "Connect to the relay, or leave it", TextAnchor.MiddleCenter, LobbyInk.Caps);

                l.Rubric(p, "Game", 67, 96, 149, 20);
                l.Rubric(p, "Host", 218, 96, 100, 20);
                l.Rubric(p, "Game Information", 400, 96, 184, 20, TextAnchor.MiddleCenter);
                List = ListView.Make(p, "Rooms", 65, 126, 288, 22, 11, (pg, row) =>
                {
                    row.Cells.Add(pg.Label(row.transform, "", 6, 0, 145, 22, 9.5f, LobbyInk.Text));
                    row.Cells.Add(pg.Label(row.transform, "", 157, 0, 96, 22, 9.5f, LobbyInk.Text));
                    row.Cells.Add(pg.Label(row.transform, "", 250, 0, 34, 22, 8.5f, LobbyInk.Text, TextAnchor.MiddleRight, LobbyInk.Caps));
                    pg.Hover(row.gameObject, "Select a game");
                    return 0;
                });
                List.Bind = (i, row) =>
                {
                    var r = Rooms.Rooms[i];
                    GuiPage.Fit(row.Cells[0], r.Name);
                    GuiPage.Fit(row.Cells[1], r.Host);
                    row.Cells[2].text = r.Players + "/" + r.MaxPlayers;
                };
                List.Picked = _ => Refresh();
                ScrollRail.Make(p, List, 355, 126, 25, 243, "ConnectionBar");
                string[] labels = { "Game", "Host", "Map", "Players", "Status", "Code" };
                for (int k = 0; k < labels.Length; k++)
                {
                    p.Label(p.Root, labels[k], 410, 126 + 22 * k, 64, 22, 8.5f, LobbyInk.Head, TextAnchor.MiddleLeft, LobbyInk.Caps);
                    info[k] = p.Label(p.Root, "", 476, 126 + 22 * k, 100, 22, 9f, LobbyInk.Text);
                }
                ArtButton.Make(p, "Update", "connectionscreen.gaf", "UpdateButton", 193, 369, 52, 40, "Update", () => { Rooms?.ListRooms(); Refresh(); }, "Update List");
                ArtButton.Make(p, "Back", "connectionscreen.gaf", "CancelButton", 58, 407, 37, 51, "Back", () => root.Flow.Fire(FlowEvent.Back), "Previous Screen");
                ArtButton.Make(p, "Join by code", "connectionscreen.gaf", "IPButton", 113, 406, 57, 57, "Code", () => codeDialogOpen(true), "Join a game by its code");
                ArtButton.Make(p, "Host", "connectionscreen.gaf", "NewUserButton", 467, 406, 57, 57, "Host", Host, "Host a Game");
                ArtButton.Make(p, "Join", "connectionscreen.gaf", "OKButton", 541, 407, 39, 54, "Join", Join, "Join Selected Game");
                l.HelpLine(p, 208, 453, 224, 27, "");

                // Join by code, on the original's address dialog.
                var d = p.Put("Code dialog", 0, 0, GuiPage.W, GuiPage.H);
                codeDialog = d.gameObject;
                var dim = p.Wash(d, -400, -400, GuiPage.W + 800, GuiPage.H + 800, new Color(0, 0, 0, 0.55f));
                dim.name = "Dim";
                var dp = p.ArtImage(d, "ipdialogue.gaf", "IPDialogue", 0, 185, 126);
                if (dp == null) l.Panel(p, d, new Rect(185, 126, 263, 179));
                p.Label(d, "Give the game's code", 213, 170, 207, 20, 10f, dp == null ? LobbyInk.Head : HudArt.Ink, TextAnchor.MiddleCenter, LobbyInk.Caps);
                Code = Field(p, d, 242, 199, 153, 20, "Code", 10f);
                Code.name = "Code";
                ArtButton.Make(p, "Code OK", "ipdialogue.gaf", "OKButton", 378, 242, 39, 51, "Join", JoinByCode, "Join").transform.SetParent(d, true);
                ArtButton.Make(p, "Code cancel", "ipdialogue.gaf", "CancelButton", 217, 242, 39, 51, "Cancel", () => codeDialogOpen(false), "Cancel").transform.SetParent(d, true);
                codeDialog.SetActive(false);
            }

            void codeDialogOpen(bool open)
            {
                codeDialog.SetActive(open);
                if (open) Code.text = "";
            }

            string PlayerName => string.IsNullOrWhiteSpace(Name.text) ? "Player" : Name.text.Trim();

            void ToggleConnection()
            {
                var r = Rooms;
                if (r == null) return;
                if (r.State == RoomSession.Off || r.State == RoomSession.Refused || r.State == RoomSession.Gone)
                {
                    PlayerPrefs.SetString(RelayKey, Relay.text);
                    r.Connect(Relay.text, PlayerName);
                }
                else r.Disconnect();
                Refresh();
            }

            void Host()
            {
                var r = Rooms;
                if (r == null) return;
                if (r.State != RoomSession.Lobby) { Page.ShowHelp("Connect to a relay first."); return; }
                var map = l.root.CurrentMap();
                if (map != null && r.CreateRoom(PlayerName + "'s game", map.Id, RoomRules.LineOfSight)) l.root.Flow.Fire(FlowEvent.EnterRoom);
                else Page.ShowHelp(r.Why);
            }

            void Join()
            {
                var r = Rooms;
                if (r == null || List.Selected < 0 || List.Selected >= r.Rooms.Count) { Page.ShowHelp("Select a game to join."); return; }
                if (r.JoinRoom(r.Rooms[List.Selected].Id, null)) l.root.Flow.Fire(FlowEvent.EnterRoom);
                else Page.ShowHelp(r.Why);
            }

            void JoinByCode()
            {
                var r = Rooms;
                if (r != null && r.JoinRoom(0, Code.text)) { codeDialogOpen(false); l.root.Flow.Fire(FlowEvent.EnterRoom); }
                else Page.ShowHelp(r?.Why ?? "");
            }

            public void Opened()
            {
                codeDialogOpen(false);
                var r = Rooms;
                if (r != null && r.State == RoomSession.Lobby) r.ListRooms();
                Refresh();
            }

            public void Refresh()
            {
                var r = Rooms;
                var st = r?.State ?? RoomSession.Off;
                bool on = st == RoomSession.Lobby || st == RoomSession.Room || st == RoomSession.Connecting;
                connect.Label.text = on ? "Leave" : "Connect";
                connect.Paint();
                status.text = r == null ? "No multiplayer here." : st == RoomSession.Lobby ? "Connected" : st == RoomSession.Connecting ? "Connecting…" : string.IsNullOrEmpty(r.Why) ? "Not connected" : r.Why;
                int n = r?.Rooms.Count ?? 0;
                if (List.Selected >= n) List.Selected = -1;
                List.SetCount(on ? n : 0);
                var sel = on && List.Selected >= 0 ? r.Rooms[List.Selected] : null;
                string[] vals = sel == null ? new string[6] : new[]
                {
                    sel.Name, sel.Host, MapCatalog.DisplayName(l.MapById(sel.MapId)) ?? sel.MapId, sel.Players + " of " + sel.MaxPlayers,
                    sel.Playing ? "In battle" : sel.Joinable ? "Waiting" : "Cannot join", sel.Code,
                };
                for (int k = 0; k < info.Length; k++) GuiPage.Fit(info[k], vals[k] ?? "");
            }
        }
    }
}
