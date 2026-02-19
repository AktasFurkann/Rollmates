using System;

namespace LudoFriends.Networking
{
    public interface IGameNetwork
    {
        event Action<int, int> OnRoll;
        event Action<int, int, int, int> OnMove; // ✅ moveId parametresi eklendi
        event Action<int> OnTurn;
        event Action<int, int, int> OnMoveRequest; // playerIndex, pawnId, roll
        event Action OnRequestAdvanceTurn;
        event Action<int> OnRollRequest; // ✅ YENİ
        event Action<float> OnTimerStart;  // ✅ NEW: Timer sync events
        event Action OnTimerStop;          // ✅ NEW

        bool IsHost { get; }

        void SendRollRequest(int playerIndex); // ✅ YENİ
        void BroadcastRoll(int playerIndex, int roll);
        void BroadcastMove(int playerIndex, int pawnId, int roll, int moveId); // ✅ moveId parametresi eklendi
        void BroadcastTurn(int nextPlayerIndex);
        void SendMoveRequest(int playerIndex, int pawnId, int roll);
        void RequestAdvanceTurn();

        // ✅ State Persistence Methods (Bug 1 fix)
        void SyncGameState(int turn, int roll, int phase, int sixes, int extraTurns);
        bool TryGetGameState(out int turn, out int roll, out int phase, out int sixes, out int extraTurns);
        void SavePawnStates(string serializedStates);
        string GetPawnStates();

        // ✅ Timer Synchronization Methods (Fix 1)
        void BroadcastTimerStart(float duration);
        void BroadcastTimerStop();

        // ✅ Timer State Persistence (Fix 2)
        void SaveTimerState(double startTime, float duration);
        bool TryGetTimerState(out double startTime, out float duration);
        void ClearTimerState();

        // Chat
        event Action<string, int> OnChatMessage; // (message, senderPlayerIndex)
        void BroadcastChatMessage(string message, int senderPlayerIndex);
    }
}