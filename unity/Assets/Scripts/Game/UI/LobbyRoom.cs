// LobbyRoom.cs - the multiplayer room (battlemenumulti.gui) and the map
// choice over it (choosemap.gui). The room's seats claim starts on the map
// view set into its chat panel, and the host moves any seat.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Game.UI
{
    public sealed partial class LobbyScreens
    {
        public sealed class RoomPage
        {
            readonly LobbyScreens l;
            public readonly GuiPage Page;
            public readonly StartMap Map;
            readonly RoomRow[] rows = new RoomRow[SeatRows];
            readonly List<(CheckBox box, RoomRules rule)> rules = new List<(CheckBox, RoomRules)>();
            readonly Text title, mapName, mapInfo, code, units;
            readonly ListView chat;
            public readonly InputField Say;
            readonly ArtButton choose, play;
            int chatSeen = -1;

            public sealed class RoomRow
            {
                public CheckBox Ready;
                public Clicker Name, Start, Side, Colour, Team;
                public RawImage Jewel, Emblem;
                public Text Number;
            }

            IGameRooms Rooms => l.root.Backend.Rooms;
            RoomState Room => Rooms?.Room;

            public RoomPage(LobbyScreens l, GuiPage p)
            {
                this.l = l;
                Page = p;
                var root = l.root;
                l.Plaques = new[] { new Rect(194, 395, 252, 80) };
                l.Backdrop(p, "battlemultiscreens.gaf", "BattleMulti", 0, 0, new[]
                {
                    new Rect(55, 47, 330, 193), new Rect(398, 47, 189, 193), new Rect(55, 264, 532, 128),
                });
                l.Rubric(p, "Ready", 52, 30, 42, 20, TextAnchor.MiddleCenter, 8.5f);
                l.Rubric(p, "Name", 94, 30, 83, 20);
                l.Rubric(p, "Start", 181, 30, 51, 20);
                l.Rubric(p, "Side", 233, 30, 48, 20);
                l.Rubric(p, "Color", 281, 30, 49, 20);
                l.Rubric(p, "Team", 325, 30, 53, 20);
                for (int i = 0; i < SeatRows; i++) rows[i] = Row(i, 61 + 22 * i);

                l.Rubric(p, "Game Information", 402, 30, 178, 20, TextAnchor.MiddleCenter);
                title = p.Label(p.Root, "", 412, 61, 164, 20, 10f, LobbyInk.Head, TextAnchor.MiddleLeft, LobbyInk.Uncial);
                title.name = "Room name";
                (string label, RoomRules rule)[] flags =
                {
                    ("Unit sight reveals the map", RoomRules.LineOfSight), ("Terrain visible at start", RoomRules.MapRevealed),
                    ("Game goes on if a monarch dies", RoomRules.MonarchExpendable), ("Monarchs placed at random", RoomRules.RandomStarts),
                };
                for (int k = 0; k < flags.Length; k++)
                {
                    float y = 83 + 22 * k;
                    var rule = flags[k].rule;
                    p.Label(p.Root, flags[k].label, 412, y, 143, 20, 8.5f, LobbyInk.Text);
                    var box = CheckBox.Make(p, p.Root, 558, y + 4, () => Room != null && (Room.Rules & rule) != 0,
                        () => { if (Room != null) Rooms.Edit(RoomEdit.Options, -1, (int)(Room.Rules ^ rule)); Refresh(); }, flags[k].label);
                    rules.Add((box, rule));
                }
                p.Label(p.Root, "Code", 412, 171, 60, 20, 9f, LobbyInk.Text);
                code = p.Label(p.Root, "", 470, 171, 104, 20, 9.5f, LobbyInk.Head, TextAnchor.MiddleRight, LobbyInk.Caps);
                p.Hover(p.Wash(p.Root, 412, 171, 162, 20, LobbyInk.Clear).gameObject, "Friends join with this code");
                p.Label(p.Root, "Maximum units", 412, 215, 100, 20, 9f, LobbyInk.Text);
                units = Clicker.Make(p, p.Root, "Units", 514, 215, 60, 20, 9.5f, by =>
                {
                    if (Room == null || !Room.YouHost) return;
                    Rooms.Edit(RoomEdit.UnitCap, -1, Mathf.Clamp(Room.UnitCap + by * 25, 50, 500));
                    Refresh();
                }, "Units for each kingdom", TextAnchor.MiddleRight, LobbyInk.Caps).Label;

                // The map view sits in the chat panel's left end, the chat beside it.
                Map = new StartMap(p, 68, 277, 104, 104);
                Map.OwnerOf = Owner;
                Map.Take = i =>
                {
                    var r = Room;
                    if (r == null) return false;
                    var mine = r.Seats[r.YourSeat];
                    if (mine.Start == i) return Rooms.Edit(RoomEdit.Start, -1, -1);
                    int holder = Holder(i);
                    if (holder >= 0 && r.YouHost) return Rooms.Edit(RoomEdit.MoveStart, r.YourSeat, i);
                    return Rooms.Edit(RoomEdit.Start, -1, i);
                };
                Map.Move = (a, b) =>
                {
                    var r = Room;
                    int holder = Holder(a);
                    if (r == null || holder < 0) return false;
                    if (r.YouHost) return Rooms.Edit(RoomEdit.MoveStart, holder, b);
                    return holder == r.YourSeat && Rooms.Edit(RoomEdit.Start, -1, b);
                };
                Map.Changed = Refresh;

                chat = ListView.Make(p, "Chat", 180, 276, 374, 21, 4, (pg, row) =>
                {
                    row.Cells.Add(pg.Label(row.transform, "", 2, 0, 58, 21, 8.5f, LobbyInk.Head, TextAnchor.MiddleLeft, LobbyInk.Caps));
                    row.Cells.Add(pg.Label(row.transform, "", 62, 0, 310, 21, 9.5f, LobbyInk.Text));
                    return 0;
                });
                chat.Bind = (i, row) =>
                {
                    var line = Rooms.ChatLog[i];
                    GuiPage.Fit(row.Cells[0], line.From);
                    GuiPage.Fit(row.Cells[1], line.Text);
                };
                ScrollRail.Make(p, chat, 556, 274, 25, 88, "BattleChatBar");
                Say = Field(p, p.Root, 180, 364, 374, 17, "Say something, then press Enter", 9f);
                Say.name = "Say";
                Say.onEndEdit.AddListener(v =>
                {
                    if (string.IsNullOrWhiteSpace(v)) return;
                    Rooms?.Chat(v);
                    Say.text = "";
                    Refresh();
                });

                l.Plaque(p, 198, 399, 244, 46);
                mapName = p.Label(p.Root, "", 198, 398, 244, 16, 11f, HudArt.GoldHi, TextAnchor.MiddleCenter, LobbyInk.Uncial);
                mapInfo = p.Label(p.Root, "", 198, 414, 244, 30, 8.5f, LobbyInk.Text, TextAnchor.UpperCenter, LobbyInk.Caps);

                ArtButton.Make(p, "Back", "battlemultiscreens.gaf", "CancelButton", 59, 407, 39, 51, "Leave", () => root.Flow.Fire(FlowEvent.Back), "Leave the game");
                choose = ArtButton.Make(p, "Choose map", "battlemultiscreens.gaf", "MapButton", 113, 406, 58, 56, "Map", () => root.Flow.Fire(FlowEvent.ChooseMap), "Select Map");
                ArtButton.Make(p, "Options", "battlemultiscreens.gaf", "OptionsButton", 470, 406, 60, 57, "Options", () => root.Flow.Fire(FlowEvent.OpenOptions), "Options");
                play = ArtButton.Make(p, "Start", "battlemultiscreens.gaf", "OKButton", 544, 407, 39, 51, "Play", Play, "Start Game");
                l.HelpLine(p, 208, 452, 224, 30, "");
            }

            RoomRow Row(int i, float y)
            {
                var p = Page;
                var r = new RoomRow();
                r.Ready = CheckBox.Make(p, p.Root, 70, y + 3, () => Room != null && i < Room.Seats.Count && Room.Seats[i].Ready,
                    () => { if (Room != null && i == Room.YourSeat) Rooms.Edit(RoomEdit.Ready, -1, 0); Refresh(); }, "Click when ready to play");
                r.Name = Clicker.Make(p, p.Root, "Name", 92, y, 92, 20, 9.5f, by => StepSeat(i, by), "Select player or AI");
                r.Start = Clicker.Make(p, p.Root, "Start", 198, y + 1, 18, 18, 8f, by => StepStart(i, by), "Start position: click for the next free one", TextAnchor.MiddleCenter);
                r.Jewel = p.Picture(r.Start.transform, "Jewel", null, 1.5f, 1.5f, 15, 15);
                r.Number = p.Label(r.Start.transform, "", 1.5f, 1.5f, 15, 15, 8f, HudArt.Ink, TextAnchor.MiddleCenter, LobbyInk.Caps);
                r.Start.Label.gameObject.SetActive(false);
                r.Side = Clicker.Make(p, p.Root, "Side", 230, y, 55, 20, 9.5f, by => Own(i, RoomEdit.Side, 0, l.NextSide(Room.Seats[i].Side, by)), "Click to select side");
                r.Colour = Clicker.Make(p, p.Root, "Colour", 294, y, 21, 20, 8f, by => Own(i, RoomEdit.Colour, (Room.Seats[i].Colour + by + 8) % 8, null), "Click to select color");
                r.Emblem = p.Picture(r.Colour.transform, "Emblem", null, 2, 1.5f, 17, 17);
                r.Colour.Label.gameObject.SetActive(false);
                r.Team = Clicker.Make(p, p.Root, "Team", 322, y, 50, 20, 9.5f, by => Own(i, RoomEdit.Team, (Room.Seats[i].Team + by + 4) % 4, null), "Click to create teams");
                return r;
            }

            void Own(int seat, RoomEdit field, int value, string text)
            {
                if (Room == null || seat != Room.YourSeat) return;
                Rooms.Edit(field, -1, value, text);
                Refresh();
            }

            // The host opens, closes and fills empty seats with computers.
            void StepSeat(int i, int by)
            {
                var r = Room;
                if (r == null || !r.YouHost || i == r.YourSeat) return;
                var s = r.Seats[i];
                switch (s.Kind)
                {
                    case RoomSeatKind.Empty: Rooms.Edit(by > 0 ? RoomEdit.AddComputer : RoomEdit.BlockSlot, i, 0); break;
                    case RoomSeatKind.Computer: Rooms.Edit(RoomEdit.RemoveComputer, i, 0); if (by > 0) Rooms.Edit(RoomEdit.BlockSlot, i, 0); break;
                    case RoomSeatKind.Blocked: Rooms.Edit(RoomEdit.UnblockSlot, i, 0); if (by < 0) Rooms.Edit(RoomEdit.AddComputer, i, 0); break;
                }
                Refresh();
            }

            // Your own start steps through "any" and the free starts. The
            // host steps any seat through every start, swapping.
            void StepStart(int i, int by)
            {
                var r = Room;
                if (r == null) return;
                int n = Starts;
                bool mine = i == r.YourSeat;
                if (!mine && !r.YouHost) return;
                var choices = new List<int> { -1 };
                for (int k = 0; k < n; k++)
                {
                    int h = Holder(k);
                    if (h < 0 || h == i || (r.YouHost && !mine)) choices.Add(k);
                }
                int at = Mathf.Max(0, choices.IndexOf(r.Seats[i].Start));
                int next = choices[(at + by + choices.Count) % choices.Count];
                if (mine && !r.YouHost) Rooms.Edit(RoomEdit.Start, -1, next);
                else Rooms.Edit(RoomEdit.MoveStart, i, next);
                Refresh();
            }

            void Play()
            {
                var r = Room;
                if (r == null) return;
                if (r.YouHost)
                {
                    if (!Rooms.StartMatch()) Page.ShowHelp(Rooms.Why);
                }
                else
                {
                    Rooms.Edit(RoomEdit.Ready, -1, 0);
                    Refresh();
                }
            }

            int Starts => l.MapById(Room?.MapId) is MapInfo m && m.Starts != null ? m.Starts.Length : 0;

            int Holder(int start)
            {
                var r = Room;
                if (r == null) return -1;
                for (int i = 0; i < r.Seats.Count; i++)
                    if ((r.Seats[i].Kind == RoomSeatKind.Human || r.Seats[i].Kind == RoomSeatKind.Computer) && r.Seats[i].Start == start) return i;
                return -1;
            }

            StartMap.Owner Owner(int i)
            {
                var r = Room;
                int h = Holder(i);
                if (r == null || h < 0) return new StartMap.Owner { Colour = -1 };
                var s = r.Seats[h];
                return new StartMap.Owner { Colour = s.Colour, Held = true, Mine = h == r.YourSeat, Who = s.Name };
            }

            public void Refresh()
            {
                var r = Room;
                if (r == null) return;
                var map = l.MapById(r.MapId);
                title.text = r.Name;
                code.text = r.Code;
                units.text = r.UnitCap.ToString();
                foreach (var (box, _) in rules) { box.Enabled = r.YouHost; box.Paint(); }
                for (int i = 0; i < SeatRows; i++)
                {
                    var row = rows[i];
                    var s = i < r.Seats.Count ? r.Seats[i] : null;
                    bool playing = s != null && (s.Kind == RoomSeatKind.Human || s.Kind == RoomSeatKind.Computer);
                    bool mine = i == r.YourSeat;
                    string name = s == null ? "" : s.Kind == RoomSeatKind.Empty ? "Open" : s.Kind == RoomSeatKind.Blocked ? "Closed" : s.Name;
                    GuiPage.Fit(row.Name.Label, name);
                    row.Name.Enabled = r.YouHost && !mine && s != null && s.Kind != RoomSeatKind.Human;
                    row.Name.Rest = mine ? LobbyInk.Head : playing ? LobbyInk.Text : LobbyInk.Dim;
                    row.Name.Paint();
                    foreach (var c in new Component[] { row.Start, row.Side, row.Colour, row.Team, row.Ready }) c.gameObject.SetActive(playing);
                    if (!playing) continue;
                    row.Ready.Enabled = mine;
                    row.Ready.gameObject.SetActive(s.Kind == RoomSeatKind.Human);
                    row.Ready.Paint();
                    row.Side.Enabled = row.Colour.Enabled = row.Team.Enabled = mine;
                    row.Start.Enabled = mine || r.YouHost;
                    GuiPage.Fit(row.Side.Label, l.SideName(s.Side));
                    row.Team.Label.text = "Team " + (s.Team + 1);
                    l.Emblem(Page, row.Emblem, s.Side, s.Colour);
                    var stone = s.Start < 0 ? HudArt.Pearl : (Color)Tint(s.Colour);
                    row.Jewel.texture = Page.Paint(s.Start < 0 ? "seatFree" : "seat" + s.Colour, sc => HudArt.Boss(14f, sc, stone));
                    row.Number.text = s.Start < 0 ? "?" : (s.Start + 1).ToString();
                    row.Number.color = stone.grayscale > 0.55f ? HudArt.Ink : Color.white;
                    foreach (var c in new[] { row.Side, row.Team, row.Start }) c.Paint();
                }
                Map.Show(map, map != null ? l.Preview(map.Id) : null);
                if (map != null)
                {
                    GuiPage.Fit(mapName, MapCatalog.DisplayName(map));
                    mapInfo.text = $"{MapCatalog.SizeLabel(map)}, {MapCatalog.PlayersOf(map)} players";
                }
                choose.GetComponent<HelpHover>().Line = r.YouHost ? "Select Map" : "View Map";
                var me = r.Seats[r.YourSeat];
                Page.RestingHelp = r.YouHost ? "You are the host. Start when everyone is ready." : me.Ready ? "Ready. Waiting for the host." : "Press Play when you are ready.";
                int n = Rooms.ChatLog.Count;
                if (n != chatSeen)
                {
                    chatSeen = n;
                    chat.Count = n;
                    chat.Top = Mathf.Max(0, n - chat.Visible);
                }
                chat.SetCount(n);
            }
        }

        // ---- The map choice (choosemap.gui), over the room ----

        public sealed class ChoicePage
        {
            readonly LobbyScreens l;
            public readonly GuiPage Page;
            public readonly MapBrowser Browser;
            public readonly StartMap Map;
            readonly Text name, info, text;
            string chosen;

            public ChoicePage(LobbyScreens l, GuiPage p)
            {
                this.l = l;
                Page = p;
                var root = l.root;
                l.Plaques = new[] { new Rect(200, 408, 240, 38) };
                l.Backdrop(p, "mapdialogue.gaf", "MapDialogue", 77, 33, new[]
                {
                    new Rect(77, 33, 486, 415), new Rect(119, 81, 220, 152), new Rect(361, 81, 160, 152), new Rect(119, 251, 402, 118),
                }, dim: true);
                Browser = new MapBrowser(l, p, l.choiceQuery, new BrowserRects
                {
                    StripY = 60, StripH = 18, SearchX = 124, SearchW = 118, PlayersX = 246, PlayersW = 76, SizeX = 326, SizeW = 62, SortX = 392, SortW = 124,
                    ListX = 130, ListY = 94, ListW = 178, RowH = 19, Rows = 7, RailX = 309, RailW = 24, Rail = "Map2DBar", SizeColumn = false, TextCp = 9.5f,
                }, () => chosen, id => { chosen = id; Refresh(); });
                Map = new StartMap(p, 378, 97, 128, 128);
                Map.OwnerOf = _ => new StartMap.Owner { Colour = -1 };
                name = p.Label(p.Root, "", 139, 266, 349, 19, 11f, LobbyInk.Head, TextAnchor.MiddleLeft, LobbyInk.Uncial);
                name.name = "Map name";
                info = p.Label(p.Root, "", 139, 285, 349, 19, 8.5f, LobbyInk.Text, TextAnchor.MiddleLeft, LobbyInk.Caps);
                text = p.Label(p.Root, "", 139, 304, 349, 57, 9.5f, LobbyInk.Text, TextAnchor.UpperLeft);
                ArtButton.Make(p, "OK", "mapdialogue.gaf", "OKButton", 482, 385, 37, 52, "OK", Pick, "Play this map");
                ArtButton.Make(p, "Cancel", "mapdialogue.gaf", "CancelButton", 121, 385, 38, 52, "Cancel", () => root.Flow.Fire(FlowEvent.Back), "Cancel");
                l.HelpLine(p, 208, 412, 223, 35, "");
            }

            public void Opened()
            {
                chosen = l.root.Backend.Rooms?.Room?.MapId ?? l.root.Setup.MapId;
                Refresh();
            }

            void Pick()
            {
                var rooms = l.root.Backend.Rooms;
                if (chosen != null && rooms?.Room != null) rooms.Edit(RoomEdit.Map, -1, 0, chosen);
                l.root.Flow.Fire(FlowEvent.Back);
            }

            // Enter picks the map, as OK does, and Escape cancels.
            public bool Key(KeyCode k)
            {
                if (k == KeyCode.Return || k == KeyCode.KeypadEnter) { Pick(); return true; }
                if (k == KeyCode.Escape) { l.root.Flow.Fire(FlowEvent.Back); return true; }
                return false;
            }

            public void Refresh()
            {
                Browser.Refresh();
                var map = l.MapById(chosen);
                Map.Show(map, map != null ? l.Preview(map.Id) : null);
                if (map == null) return;
                GuiPage.Fit(name, MapCatalog.DisplayName(map));
                string climate = string.IsNullOrEmpty(map.Climate) ? "" : ", " + map.Climate;
                info.text = $"{MapCatalog.SizeLabel(map)}, {MapCatalog.PlayersOf(map)} players{climate}";
                text.text = MapCatalog.Restates(map.Description, map) ? "" : map.Description;
            }
        }
    }
}
