// EngineRooms.cs - multiplayer rooms on the engine's relay session. The
// okx_net_* calls are bound in OkEngine, and this waits for the engine's
// start positions (okx API 22) before it drives them.
using System.Collections.Generic;
using OpenKingdomsUnity.Game;

namespace OpenKingdomsUnity.Engine
{
    public sealed class EngineRooms : IGameRooms
    {
        const string NotYet = "Multiplayer rooms arrive with the next okengine.";
        static readonly List<RoomListing> NoRooms = new List<RoomListing>();
        static readonly List<ChatLine> NoChat = new List<ChatLine>();

        public RoomSession State => RoomSession.Off;
        public string Why => NotYet;
        public bool Connect(string address, string playerName) => false;
        public void Disconnect() { }
        public RoomSession Pump() => RoomSession.Off;
        public bool ListRooms() => false;
        public IReadOnlyList<RoomListing> Rooms => NoRooms;
        public bool CreateRoom(string name, string mapId, RoomRules rules) => false;
        public bool JoinRoom(uint id, string code) => false;
        public bool LeaveRoom() => false;
        public RoomState Room => null;
        public bool Edit(RoomEdit field, int seat, int value, string text = null) => false;
        public bool Chat(string text) => false;
        public IReadOnlyList<ChatLine> ChatLog => NoChat;
        public bool StartMatch() => false;
    }
}
