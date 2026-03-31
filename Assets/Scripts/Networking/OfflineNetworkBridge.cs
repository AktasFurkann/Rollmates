using System;
using UnityEngine;

namespace LudoFriends.Networking
{
    /// <summary>
    /// Offline loopback implementation of IGameNetwork.
    /// All Broadcast/Send methods invoke their corresponding events synchronously,
    /// simulating a local server. Used for single-player bot games.
    /// </summary>
    public class OfflineNetworkBridge : MonoBehaviour, IGameNetwork
    {
        public static OfflineNetworkBridge Instance { get; private set; }

        // ── IGameNetwork Events ──
        public event Action<int, int> OnRoll;
        public event Action<int, int, int, int> OnMove;
        public event Action<int> OnTurn;
        public event Action<int, int, int> OnMoveRequest;
        public event Action OnRequestAdvanceTurn;
        public event Action<int> OnRollRequest;
        public event Action<float> OnTimerStart;
        public event Action OnTimerStop;
        public event Action<string, int> OnChatMessage;

        public bool IsHost => true;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Send / Broadcast (loopback — invoke events directly) ──

        public void SendRollRequest(int playerIndex)
            => OnRollRequest?.Invoke(playerIndex);

        public void BroadcastRoll(int playerIndex, int roll)
            => OnRoll?.Invoke(playerIndex, roll);

        public void SendMoveRequest(int playerIndex, int pawnId, int roll)
            => OnMoveRequest?.Invoke(playerIndex, pawnId, roll);

        public void BroadcastMove(int playerIndex, int pawnId, int roll, int moveId)
            => OnMove?.Invoke(playerIndex, pawnId, roll, moveId);

        public void BroadcastTurn(int nextPlayerIndex)
            => OnTurn?.Invoke(nextPlayerIndex);

        public void RequestAdvanceTurn()
            => OnRequestAdvanceTurn?.Invoke();

        public void BroadcastTimerStart(float duration, int playerIndex)
            => OnTimerStart?.Invoke(duration);

        public void BroadcastTimerStop()
            => OnTimerStop?.Invoke();

        public void BroadcastChatMessage(string message, int senderPlayerIndex)
            => OnChatMessage?.Invoke(message, senderPlayerIndex);

        // ── State Persistence (no-op in offline — state lives in memory only) ──

        public void SyncGameState(int turn, int roll, int phase, int sixes, int extraTurns) { }

        public bool TryGetGameState(out int turn, out int roll, out int phase, out int sixes, out int extraTurns)
        {
            turn = roll = phase = sixes = extraTurns = 0;
            return false;
        }

        public void SavePawnStates(string serializedStates) { }
        public string GetPawnStates() => null;

        public void SaveTimerState(double startTime, float duration) { }

        public bool TryGetTimerState(out double startTime, out float duration)
        {
            startTime = 0;
            duration = 0;
            return false;
        }

        public void ClearTimerState() { }

        public void SaveFinishOrder(int[] finishOrder) { }
        public int[] GetFinishOrder() => null;
    }
}
