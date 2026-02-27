using System;

namespace LudoFriends.Networking
{
    // ── Server → Client ──

    [Serializable]
    public class IdentifiedPayload
    {
        public bool success;
        public string reconnectRoomCode;
    }

    [Serializable]
    public class RoomCreatedPayload
    {
        public string code;
    }

    [Serializable]
    public class JoinedRoomPayload
    {
        public string code;
        public RoomPlayerInfo[] players;
        public int yourPlayerIndex;
        public bool isHost;
        public bool isSpectator;
        public bool gameStarted;
    }

    [Serializable]
    public class RoomPlayerInfo
    {
        public string playerId;
        public int playerIndex;
        public string nickname;
        public bool isHost;
        public bool isSpectator;
        public bool isConnected;
    }

    [Serializable]
    public class PlayerJoinedPayload
    {
        public string playerId;
        public int playerIndex;
        public string nickname;
    }

    [Serializable]
    public class PlayerLeftPayload
    {
        public string playerId;
        public int playerIndex;
        public bool isPermanent;
    }

    [Serializable]
    public class JoinFailedPayload
    {
        public string reason;
    }

    [Serializable]
    public class GameStartedPayload
    {
        public int initialPlayerCount;
        public PlayerAssignment[] playerAssignments;
    }

    [Serializable]
    public class PlayerAssignment
    {
        public string playerId;
        public int playerIndex;
    }

    [Serializable]
    public class CountdownTickPayload
    {
        public int secondsRemaining;
    }

    [Serializable]
    public class HostChangedPayload
    {
        public string newHostPlayerId;
        public int newHostPlayerIndex;
    }

    // ── Game Events ──

    [Serializable]
    public class RollPayload
    {
        public int playerIndex;
        public int roll;
    }

    [Serializable]
    public class MovePayload
    {
        public int playerIndex;
        public int pawnId;
        public int roll;
        public int moveId;
    }

    [Serializable]
    public class TurnPayload
    {
        public int nextPlayerIndex;
    }

    [Serializable]
    public class MoveRequestPayload
    {
        public int playerIndex;
        public int pawnId;
        public int roll;
    }

    [Serializable]
    public class RollRequestPayload
    {
        public int playerIndex;
    }

    [Serializable]
    public class TimerStartPayload
    {
        public float duration;
    }

    [Serializable]
    public class ExitBotPayload
    {
        public int playerIndex;
    }

    [Serializable]
    public class EnterBotPayload
    {
        public int playerIndex;
    }

    [Serializable]
    public class ServerTimerExpiredPayload
    {
        public int playerIndex;
    }

    [Serializable]
    public class ChatPayload
    {
        public string message;
        public int senderPlayerIndex;
    }

    // ── State ──

    [Serializable]
    public class GameStatePayload
    {
        public int turn;
        public int roll;
        public int phase;
        public int sixes;
        public int extraTurns;
        public string pawnStates;
        public double timerStartTime;
        public float timerDuration;
        public int[] finishOrder;
        public double serverTime;
    }

    [Serializable]
    public class ServerTimePayload
    {
        public double time;
    }
}
