// MockRooms.cs - a made-up relay for the multiplayer screens: a few rooms
// to list and join, a room to host that a guest joins at once, and the
// relay's rules for seats, starts, readiness and the host's edits. The
// battle it starts plays locally on the mock, the other humans as
// computers.
using System;
using System.Collections.Generic;

namespace OpenKingdomsUnity.Game
{
    public sealed class MockRooms : IGameRooms
    {
        public const int SeatCount = 8;
        public const string GuestName = "Anselm";

        readonly Func<string, MapInfo> mapById;
        readonly Func<string> firstMap;
        readonly List<RoomListing> rooms = new List<RoomListing>();
        readonly List<ChatLine> chat = new List<ChatLine>();
        string you = "You";
        bool guestComing;
        uint nextId = 7001;

        public MockRooms(Func<string, MapInfo> mapById, Func<string> firstMap)
        {
            this.mapById = mapById;
            this.firstMap = firstMap;
        }

        public RoomSession State { get; private set; }
        public string Why { get; private set; } = "";
        public IReadOnlyList<RoomListing> Rooms => rooms;
        public RoomState Room { get; private set; }
        public IReadOnlyList<ChatLine> ChatLog => chat;

        public bool Connect(string address, string playerName)
        {
            if (string.IsNullOrWhiteSpace(address)) { Why = "Give the relay's address."; State = RoomSession.Refused; return false; }
            you = string.IsNullOrWhiteSpace(playerName) ? "You" : playerName.Trim();
            Why = "";
            State = RoomSession.Connecting;
            return true;
        }

        public void Disconnect()
        {
            State = RoomSession.Off;
            Room = null;
            rooms.Clear();
            chat.Clear();
            guestComing = false;
        }

        public RoomSession Pump()
        {
            switch (State)
            {
                case RoomSession.Connecting:
                    State = RoomSession.Lobby;
                    ListRooms();
                    break;
                case RoomSession.Room:
                    if (guestComing && Room != null)
                    {
                        guestComing = false;
                        int s = FirstEmpty();
                        if (s >= 0)
                        {
                            Room.Seats[s] = new RoomSeat
                            {
                                Kind = RoomSeatKind.Human, Name = GuestName, Side = "VERUNA", Colour = FreeColour(s), Team = 1,
                                Ready = true, Connected = true, HasMap = true,
                            };
                            chat.Add(new ChatLine { From = GuestName, Text = "Good evening. Veruna, if nobody minds." });
                        }
                    }
                    break;
                case RoomSession.Loading:
                    // The relay says go at once: the next pump plays.
                    State = RoomSession.Playing;
                    return RoomSession.Loading;
            }
            return State;
        }

        public bool ListRooms()
        {
            if (State != RoomSession.Lobby && State != RoomSession.Room) return false;
            rooms.Clear();
            string m0 = firstMap() ?? "";
            rooms.Add(new RoomListing { Id = 5101, Code = "ASHFORD", Name = "Evening crusade", Host = "Aldric", MapId = Pick("mock_highlands", m0), Players = 2, MaxPlayers = 4, Joinable = true });
            rooms.Add(new RoomListing { Id = 5102, Code = "NIGHTFEN", Name = "Tarosian night", Host = "Mirelle", MapId = Pick("mock_marches", m0), Players = 5, MaxPlayers = 8, Joinable = true });
            rooms.Add(new RoomListing { Id = 5103, Code = "OLDOAK", Name = "Old friends only", Host = "Bertram", MapId = Pick("mock_isles", m0), Players = 4, MaxPlayers = 4, Playing = true });
            return true;
        }

        string Pick(string id, string fallback) => mapById(id) != null ? id : fallback;

        public bool CreateRoom(string name, string mapId, RoomRules rules)
        {
            if (State != RoomSession.Lobby) { Why = "Connect to a relay first."; return false; }
            if (mapById(mapId) == null) { Why = "That map is not here."; return false; }
            Room = new RoomState
            {
                Id = nextId++, Code = "WYVERN", Name = string.IsNullOrWhiteSpace(name) ? you + "'s game" : name.Trim(),
                MapId = mapId, Rules = rules, UnitCap = 250, YouHost = true, YourSeat = 0,
            };
            for (int i = 0; i < SeatCount; i++) Room.Seats.Add(new RoomSeat { Kind = RoomSeatKind.Empty, Colour = i, Team = i });
            Room.Seats[0] = new RoomSeat { Kind = RoomSeatKind.Human, Name = you, Colour = 0, Team = 0, Connected = true, HasMap = true };
            chat.Clear();
            guestComing = true;
            State = RoomSession.Room;
            return true;
        }

        public bool JoinRoom(uint id, string code)
        {
            if (State != RoomSession.Lobby) { Why = "Connect to a relay first."; return false; }
            var l = rooms.Find(r => id != 0 ? r.Id == id : string.Equals(r.Code, (code ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            if (l == null) { Why = "There is no such room."; return false; }
            if (!l.Joinable || l.Playing || l.Players >= l.MaxPlayers) { Why = l.Name + " cannot take another player."; return false; }
            Room = new RoomState { Id = l.Id, Code = l.Code, Name = l.Name, MapId = l.MapId, Rules = RoomRules.LineOfSight, UnitCap = 250 };
            for (int i = 0; i < SeatCount; i++) Room.Seats.Add(new RoomSeat { Kind = RoomSeatKind.Empty, Colour = i, Team = i });
            string[] names = { l.Host, "Ysolde", "Garrick", "Oswin", "Hild", "Cuthbert", "Maud" };
            string[] sides = { "ARAMON", "TAROS", "ZHON", "VERUNA" };
            for (int i = 0; i < l.Players; i++)
                Room.Seats[i] = new RoomSeat
                {
                    Kind = RoomSeatKind.Human, Name = names[i % names.Length], Side = sides[i % sides.Length], Colour = i, Team = SeatTeam.Alone,
                    Ready = i > 0, Connected = true, HasMap = true, Start = i == 0 ? 0 : -1,
                };
            Room.YourSeat = l.Players;
            Room.Seats[l.Players] = new RoomSeat { Kind = RoomSeatKind.Human, Name = you, Colour = FreeColour(l.Players), Team = SeatTeam.Alone, Connected = true, HasMap = true };
            chat.Clear();
            chat.Add(new ChatLine { From = l.Host, Text = "Welcome. Take any start you like." });
            State = RoomSession.Room;
            return true;
        }

        public bool LeaveRoom()
        {
            if (Room == null) return false;
            Room = null;
            guestComing = false;
            State = RoomSession.Lobby;
            ListRooms();
            return true;
        }

        public bool Chat(string text)
        {
            if (Room == null || string.IsNullOrWhiteSpace(text)) return false;
            chat.Add(new ChatLine { From = Room.Seats[Room.YourSeat].Name, Text = text.Trim() });
            return true;
        }

        int StartCount => mapById(Room.MapId) is MapInfo m ? MapCatalog.PlayersOf(m) : 0;

        static bool Playing(RoomSeat s) => s.Kind == RoomSeatKind.Human || s.Kind == RoomSeatKind.Computer;

        public bool Edit(RoomEdit field, int seat, int value, string text = null)
        {
            if (Room == null) return false;
            var r = Room;
            int me = r.YourSeat;
            if ((int)field < 32)
            {
                if (seat >= 0 && seat != me) { Why = "Only your own row."; return false; }
                var s = r.Seats[me];
                switch (field)
                {
                    case RoomEdit.Side: s.Side = text ?? ""; break;
                    case RoomEdit.Colour: s.Colour = FreeColour(me, value); break;
                    case RoomEdit.Team: s.Team = value < 0 ? SeatTeam.Alone : value; break;
                    case RoomEdit.Name: if (!string.IsNullOrWhiteSpace(text)) s.Name = text.Trim(); break;
                    case RoomEdit.Ready: s.Ready = !s.Ready; return true;
                    case RoomEdit.Start:
                        if (value < 0) s.Start = -1;
                        else if (value >= StartCount || HolderOf(value) >= 0 && HolderOf(value) != me) { Why = "That start is taken."; return false; }
                        else s.Start = value;
                        break;
                    default: return false;
                }
                s.Ready = false;
                return true;
            }
            if (!r.YouHost) { Why = "Only the host may do that."; return false; }
            bool inRange = seat >= 0 && seat < r.Seats.Count;
            switch (field)
            {
                case RoomEdit.Map:
                    if (mapById(text) == null) return false;
                    r.MapId = text;
                    foreach (var s in r.Seats) { s.Start = -1; if (s.Kind == RoomSeatKind.Human && r.Seats.IndexOf(s) != me) s.Ready = s.Name == GuestName; }
                    return true;
                case RoomEdit.Options: r.Rules = (RoomRules)value; return true;
                case RoomEdit.UnitCap: r.UnitCap = Math.Max(0, value); return true;
                case RoomEdit.AddComputer:
                    if (!inRange || r.Seats[seat].Kind != RoomSeatKind.Empty) return false;
                    r.Seats[seat] = new RoomSeat { Kind = RoomSeatKind.Computer, Name = "Computer", Colour = FreeColour(seat), Team = SeatTeam.Alone, Ready = true, Connected = true, HasMap = true };
                    return true;
                case RoomEdit.RemoveComputer:
                    if (!inRange || r.Seats[seat].Kind != RoomSeatKind.Computer) return false;
                    r.Seats[seat] = new RoomSeat { Kind = RoomSeatKind.Empty, Colour = seat, Team = seat };
                    return true;
                case RoomEdit.BlockSlot:
                    if (!inRange || r.Seats[seat].Kind != RoomSeatKind.Empty) return false;
                    r.Seats[seat].Kind = RoomSeatKind.Blocked;
                    return true;
                case RoomEdit.UnblockSlot:
                    if (!inRange || r.Seats[seat].Kind != RoomSeatKind.Blocked) return false;
                    r.Seats[seat].Kind = RoomSeatKind.Empty;
                    return true;
                case RoomEdit.Kick:
                    if (!inRange || seat == me || r.Seats[seat].Kind != RoomSeatKind.Human) return false;
                    r.Seats[seat] = new RoomSeat { Kind = RoomSeatKind.Empty, Colour = seat, Team = seat };
                    return true;
                case RoomEdit.MoveStart:
                    if (!inRange || !Playing(r.Seats[seat])) return false;
                    if (value < 0) { r.Seats[seat].Start = -1; return true; }
                    if (value >= StartCount) return false;
                    int holder = HolderOf(value);
                    if (holder >= 0 && holder != seat) r.Seats[holder].Start = r.Seats[seat].Start;
                    r.Seats[seat].Start = value;
                    return true;
            }
            return false;
        }

        public bool StartMatch()
        {
            if (Room == null || !Room.YouHost) { Why = "Only the host starts the battle."; return false; }
            int players = 0;
            foreach (var s in Room.Seats)
            {
                if (!Playing(s)) continue;
                players++;
                if (s.Kind == RoomSeatKind.Human && Room.Seats.IndexOf(s) != Room.YourSeat && !s.Ready) { Why = s.Name + " is not ready."; return false; }
            }
            if (players < 2) { Why = "A battle needs two kingdoms."; return false; }
            if (SkirmishCheck.OnOneTeam(Room.Seats.FindAll(Playing).ConvertAll(x => x.Team))) { Why = "Not everyone can be on one team."; return false; }
            if (players > StartCount) { Why = $"This map holds {StartCount} kingdoms."; return false; }
            Why = "";
            State = RoomSession.Loading;
            return true;
        }

        int HolderOf(int start)
        {
            for (int i = 0; i < Room.Seats.Count; i++) if (Playing(Room.Seats[i]) && Room.Seats[i].Start == start) return i;
            return -1;
        }

        int FirstEmpty()
        {
            for (int i = 0; i < Room.Seats.Count; i++) if (Room.Seats[i].Kind == RoomSeatKind.Empty) return i;
            return -1;
        }

        // The colour asked for, or the next one nobody else wears.
        int FreeColour(int seat, int want = -1)
        {
            for (int k = 0; k < 8; k++)
            {
                int c = ((want < 0 ? seat : want) + k) % 8;
                bool used = false;
                for (int i = 0; i < Room.Seats.Count; i++)
                    if (i != seat && Playing(Room.Seats[i]) && Room.Seats[i].Colour == c) used = true;
                if (!used) return c;
            }
            return seat % 8;
        }
    }
}
