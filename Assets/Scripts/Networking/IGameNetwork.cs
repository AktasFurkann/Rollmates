using System;

namespace LudoFriends.Networking
{
    public interface IGameNetwork
    {
        event Action<int, int> OnRoll;
        event Action<int, int, int> OnMove;
        event Action<int> OnTurn;
        event Action<int, int> OnMoveRequest;
        event Action OnRequestAdvanceTurn;
        event Action<int> OnRollRequest; // ✅ YENİ

        bool IsHost { get; }

        void SendRollRequest(int playerIndex); // ✅ YENİ
        void BroadcastRoll(int playerIndex, int roll);
        void BroadcastMove(int playerIndex, int pawnId, int roll);
        void BroadcastTurn(int nextPlayerIndex);
        void SendMoveRequest(int playerIndex, int pawnId);
        void RequestAdvanceTurn();
    }
}