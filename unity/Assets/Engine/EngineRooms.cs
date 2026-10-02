// EngineRooms.cs - multiplayer rooms on the engine's relay session, the
// okx_net_* calls one to one. The engine keeps the session and the room;
// this turns what it reports into the presentation's rooms and seats.
using System;
using System.Collections.Generic;
using System.Text;
using OpenKingdomsUnity.Game;

namespace OpenKingdomsUnity.Engine
{
    public sealed class EngineRooms : IGameRooms
    {
        readonly List<RoomListing> rooms = new List<RoomListing>();
        readonly List<ChatLine> chat = new List<ChatLine>();
        readonly OkxNetRoom[] roomBuf = new OkxNetRoom[32];
        readonly byte[] fromBuf = new byte[64], textBuf = new byte[512];
        readonly Random pick = new Random();
        RoomState room;
        int chatSeen;

        public RoomSession State { get; private set; } = RoomSession.Off;
        public string Why => OkEngine.NetWhy;
        public IReadOnlyList<RoomListing> Rooms => rooms;
        public RoomState Room => room;
        public IReadOnlyList<ChatLine> ChatLog => chat;

        public bool Connect(string address, string playerName)
        {
            if (string.IsNullOrWhiteSpace(address)) return false;
            string name = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim();
            chat.Clear();
            chatSeen = OkEngine.okx_net_last_chat(null, 0, null, 0);
            if (OkEngine.okx_net_connect(address.Trim(), name) != 0) return false;
            State = RoomSession.Connecting;
            return true;
        }

        public void Disconnect()
        {
            OkEngine.okx_net_disconnect();
            State = RoomSession.Off;
            room = null;
            rooms.Clear();
            chat.Clear();
        }

        public RoomSession Pump()
        {
            State = (RoomSession)OkEngine.okx_net_pump();
            int n = OkEngine.okx_net_rooms(roomBuf, roomBuf.Length);
            rooms.Clear();
            for (int i = 0; i < n && i < roomBuf.Length; i++)
            {
                var r = roomBuf[i];
                rooms.Add(new RoomListing
                {
                    Id = r.id, Code = r.code ?? "", Name = r.name ?? "", Host = r.host ?? "", MapId = r.map ?? "",
                    Players = r.players, MaxPlayers = r.maxPlayers,
                    // TAK_ROOM_LOADING and on.
                    Playing = r.status >= 2, Joinable = r.joinable != 0,
                });
            }
            room = State == RoomSession.Room || State == RoomSession.Loading ? ReadRoom() : null;
            // The engine keeps the last line and a count, so a line is
            // taken as the count moves.
            int count = OkEngine.okx_net_last_chat(fromBuf, fromBuf.Length, textBuf, textBuf.Length);
            if (count != chatSeen)
            {
                chatSeen = count;
                chat.Add(new ChatLine { From = Text(fromBuf), Text = Text(textBuf) });
            }
            return State;
        }

        static string Text(byte[] b)
        {
            int n = Array.IndexOf(b, (byte)0);
            return Encoding.ASCII.GetString(b, 0, n < 0 ? b.Length : n);
        }

        RoomState ReadRoom()
        {
            if (OkEngine.okx_net_room(out var r) != 0) return null;
            var s = new RoomState
            {
                Id = r.id, Code = r.code ?? "", Name = r.name ?? "", MapId = r.map ?? "",
                Rules = (RoomRules)r.options, UnitCap = r.unitCap, YouHost = r.youHost != 0, YourSeat = r.yourSeat,
            };
            for (int i = 0; i < r.seatCount && r.seats != null && i < r.seats.Length; i++)
            {
                var e = r.seats[i];
                s.Seats.Add(new RoomSeat
                {
                    Kind = (RoomSeatKind)Math.Max(0, Math.Min(3, e.kind)), Name = e.name ?? "",
                    Side = e.side >= 0 && e.side < EngineBackend.SideIds.Length ? EngineBackend.SideIds[e.side] : "",
                    Colour = e.colour, Team = EngineBackend.PlayerTeam(e.team),
                    Ready = e.ready != 0, Connected = e.connected != 0, HasMap = e.hasMap != 0,
                    LoadPercent = e.loadPercent, Start = e.start,
                });
            }
            return s;
        }

        // Rooms keep the engine's own numbers, as the browser game does:
        // 0 for a kingdom alone, then Team 1 to 4.
        public static int RoomTeam(int team) => team >= 0 ? team + 1 : 0;

        public bool ListRooms() => OkEngine.okx_net_list_rooms() == 0;

        public bool CreateRoom(string name, string mapId, RoomRules rules) =>
            OkEngine.okx_net_create_room(string.IsNullOrWhiteSpace(name) ? "Game" : name.Trim(), mapId ?? "", (int)rules) == 0;

        public bool JoinRoom(uint id, string code) => OkEngine.okx_net_join_room(id, id != 0 ? null : (code ?? "").Trim()) == 0;

        public bool LeaveRoom() => OkEngine.okx_net_leave_room() == 0;

        public bool Edit(RoomEdit field, int seat, int value, string text = null)
        {
            switch (field)
            {
                case RoomEdit.Side:
                {
                    // The side by its id, "" for a kingdom drawn here.
                    int side = EngineBackend.SideIndex(text);
                    if (side < 0 || side > 3) side = pick.Next(4);
                    return OkEngine.okx_net_edit((int)field, seat, side, null) == 0;
                }
                case RoomEdit.Team:
                    return OkEngine.okx_net_edit((int)field, seat, RoomTeam(value), null) == 0;
                case RoomEdit.UnitCap:
                    // The relay holds the cap to the engine's own bounds.
                    return OkEngine.okx_net_edit((int)field, -1, Math.Max(200, Math.Min(2000, value)), null) == 0;
                case RoomEdit.Map:
                    return OkEngine.okx_net_edit((int)field, -1, 0, text ?? "") == 0;
                default:
                    return OkEngine.okx_net_edit((int)field, seat, value, text) == 0;
            }
        }

        public bool Chat(string text) => !string.IsNullOrWhiteSpace(text) && OkEngine.okx_net_chat(text) == 0;

        public bool StartMatch() => OkEngine.okx_net_start() == 0;
    }
}
