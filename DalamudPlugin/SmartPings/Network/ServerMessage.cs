using Dalamud.Game.Text;

namespace SmartPings.Network;

public struct ServerMessage
{
    public struct Payload
    {
        public enum Action : int
        {
            None = 0,
            UpdatePlayersInRoom = 1,
            AddGroundPing = 2,
            SendChatMessage = 3,

            Close = 10,
        }

        public struct GroundPingPayload
        {
            public GroundPing.Type pingType;
            public string author;
            public long startTimestamp;
            public string mapId;
            public float worldPositionX;
            public float worldPositionY;
            public float worldPositionZ;
        }

        public struct ChatMessagePayload
        {
            public XivChatType chatType;
            public byte[] message;
        }

        public Action action;
        public string[] players;
        public GroundPingPayload groundPingPayload;
        public ChatMessagePayload chatMessagePayload;
    }

    public string from;
    public string target;
    public Payload payload;
}
