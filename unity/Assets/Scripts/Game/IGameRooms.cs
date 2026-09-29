// IGameRooms.cs - multiplayer as the presentation sees it: a session with a
// relay, the rooms it lists, and the room we sit in. It mirrors the
// engine's okx_net_* calls and TAK_EDIT_* fields, with start positions
// added. MockRooms fakes a relay, and EngineRooms binds the engine.
using System;
using System.Collections.Generic;

namespace OpenKingdomsUnity.Game
{
    // OKX_NET_*.
    public enum RoomSession { Off, Connecting, Lobby, Room, Loading, Playing, Refused, Gone }

    // TAK_ROOMOPT_*, the rules every seat's world is built from.
    [Flags]
    public enum RoomRules
    {
        None = 0, LineOfSight = 1, MapRevealed = 2, MonarchExpendable = 4, RandomStarts = 8,
        PowerCodes = 16, SlowGame = 32,
    }

    public enum RoomSeatKind { Empty, Human, Computer, Blocked }

    // TAK_EDIT_* by number. Your own row's fields clear your ready, and the
    // host's map, rules and cap clear everyone's. Start and MoveStart are new.
    public enum RoomEdit
    {
        Side = 1, Colour = 2, Team = 3, Name = 4, Watch = 5, Ready = 6, HaveMap = 7,
        // Your own row: take the free start `value`, or -1 to give yours back.
        Start = 8,
        Map = 32, Options = 33, UnitCap = 34, Timeout = 35, AllowWatching = 36,
        AddComputer = 37, RemoveComputer = 38, BlockSlot = 39, UnblockSlot = 40, Kick = 41,
        // The host's: move `seat` to start `value`, swapping with the seat
        // that holds it, or -1 to free it.
        MoveStart = 42,
    }

    public sealed class RoomListing
    {
        public uint Id;
        public string Code = "", Name = "", Host = "", MapId = "";
        public int Players, MaxPlayers;
        public bool Playing;        // the match has begun
        public bool Joinable;       // this install may join
    }

    public sealed class RoomSeat
    {
        public RoomSeatKind Kind;
        public string Name = "";
        public string Side = "";    // SideInfo.Id, or "" for random
        public int Colour, Team;
        public bool Ready, Connected, HasMap;
        public int LoadPercent;
        public int Start = -1;      // an index into MapInfo.Starts, or -1
    }

    public sealed class RoomState
    {
        public uint Id;
        public string Code = "", Name = "", MapId = "";
        public RoomRules Rules;
        public int UnitCap;
        public bool YouHost;
        public int YourSeat;
        public List<RoomSeat> Seats = new List<RoomSeat>();
    }

    public struct ChatLine
    {
        public string From, Text;
    }

    public interface IGameRooms
    {
        RoomSession State { get; }
        // Why the session was refused or ended, "" otherwise.
        string Why { get; }
        // address is the relay's ws:// or wss:// address.
        bool Connect(string address, string playerName);
        void Disconnect();
        // Moves bytes and answers what needs answering. Call it every frame
        // while a multiplayer screen or match is up.
        RoomSession Pump();

        // Asks for the list, which fills Rooms as it arrives.
        bool ListRooms();
        IReadOnlyList<RoomListing> Rooms { get; }
        bool CreateRoom(string name, string mapId, RoomRules rules);
        // By id, or by code with id 0.
        bool JoinRoom(uint id, string code);
        bool LeaveRoom();
        // The room we sit in as the relay last described it, or null.
        RoomState Room { get; }
        // seat is the row an edit is about, -1 for your own or the room's.
        bool Edit(RoomEdit field, int seat, int value, string text = null);
        bool Chat(string text);
        IReadOnlyList<ChatLine> ChatLog { get; }
        // The host starts the match once every human is ready. The session
        // then says Loading, and the presentation loads the battle.
        bool StartMatch();
    }

    public static class RoomSetup
    {
        // The battle a room describes, with our seat first as SkirmishSetup
        // wants it, every start already dealt, and the other humans as
        // computers for a backend that plays them locally.
        public static SkirmishSetup ToSkirmish(RoomState room, MapInfo map, uint seed)
        {
            var s = new SkirmishSetup
            {
                MapId = room.MapId, Seed = seed,
                LineOfSight = (room.Rules & RoomRules.LineOfSight) != 0,
                MapRevealed = (room.Rules & RoomRules.MapRevealed) != 0,
                RandomStarts = (room.Rules & RoomRules.RandomStarts) != 0,
            };
            if (room.UnitCap > 0) s.UnitLimit = room.UnitCap;
            var seats = new List<SeatSetup>();
            foreach (var r in room.Seats)
                seats.Add(new SeatSetup
                {
                    Kind = r.Kind == RoomSeatKind.Human ? SeatKind.Human : r.Kind == RoomSeatKind.Computer ? SeatKind.Computer : SeatKind.Closed,
                    Side = r.Side, Colour = r.Colour, Team = r.Team, Start = r.Start,
                });
            var dealt = StartPositions.Assign(seats, map != null ? map.Starts.Length : 0, s.RandomStarts, seed);
            for (int i = 0; i < seats.Count; i++) seats[i].Start = dealt[i];
            int you = Math.Max(0, Math.Min(room.YourSeat, seats.Count - 1));
            if (seats.Count > 0) s.Seats.Add(seats[you]);
            for (int i = 0; i < seats.Count; i++)
            {
                if (i == you) continue;
                if (seats[i].Kind == SeatKind.Human) seats[i].Kind = SeatKind.Computer;
                s.Seats.Add(seats[i]);
            }
            return s;
        }
    }
}
