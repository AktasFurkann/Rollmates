using System;
using Photon.Pun;
using UnityEngine;

namespace LudoFriends.Networking
{
    public class PhotonNetworkBridge : MonoBehaviourPunCallbacks, IGameNetwork
    {
        public event Action<int, int> OnRoll;
        public event Action<int, int, int, int> OnMove; // ✅ moveId parametresi eklendi
        public event Action<int> OnTurn;
        public event Action<int, int> OnMoveRequest;
        public event Action OnRequestAdvanceTurn;
        public event Action<int> OnRollRequest; // ✅ YENİ

        public bool IsHost => PhotonNetwork.IsMasterClient;

        // ✅ YENİ: Client zar atma isteği gönderir
        public void SendRollRequest(int playerIndex)
        {
            photonView.RPC(nameof(RPC_RollRequest), RpcTarget.MasterClient, playerIndex);
        }

        [PunRPC]
        private void RPC_RollRequest(int playerIndex)
        {
            Debug.Log($"[RPC_RollRequest] P{playerIndex} wants to roll");
            OnRollRequest?.Invoke(playerIndex);
        }

        public void BroadcastRoll(int playerIndex, int roll)
        {
            photonView.RPC(nameof(RPC_Roll), RpcTarget.All, playerIndex, roll);
        }

        [PunRPC]
        private void RPC_Roll(int playerIndex, int roll)
        {
            Debug.Log($"[RPC_Roll] P{playerIndex} rolled {roll}");
            OnRoll?.Invoke(playerIndex, roll);
        }

        public void BroadcastMove(int playerIndex, int pawnId, int roll, int moveId) // ✅ moveId eklendi
        {
            photonView.RPC(nameof(RPC_Move), RpcTarget.All, playerIndex, pawnId, roll, moveId);
        }

        [PunRPC]
        private void RPC_Move(int playerIndex, int pawnId, int roll, int moveId) // ✅ moveId eklendi
        {
            Debug.Log($"[RPC_Move] P{playerIndex} moves pawn {pawnId} with {roll}, moveId={moveId}");
            OnMove?.Invoke(playerIndex, pawnId, roll, moveId);
        }

        public void BroadcastTurn(int nextPlayerIndex)
        {
            photonView.RPC(nameof(RPC_Turn), RpcTarget.All, nextPlayerIndex);
        }

        [PunRPC]
        private void RPC_Turn(int nextPlayerIndex)
        {
            Debug.Log($"[RPC_Turn] Next: P{nextPlayerIndex}");
            OnTurn?.Invoke(nextPlayerIndex);
        }

        public void SendMoveRequest(int playerIndex, int pawnId)
        {
            photonView.RPC(nameof(RPC_MoveRequest), RpcTarget.MasterClient, playerIndex, pawnId);
        }

        [PunRPC]
        private void RPC_MoveRequest(int playerIndex, int pawnId)
        {
            Debug.Log($"[RPC_MoveRequest] P{playerIndex} wants pawn {pawnId}");
            OnMoveRequest?.Invoke(playerIndex, pawnId);
        }

        public void RequestAdvanceTurn()
        {
            photonView.RPC(nameof(RPC_RequestAdvanceTurn), RpcTarget.MasterClient);
        }

        [PunRPC]
        private void RPC_RequestAdvanceTurn()
        {
            Debug.Log("[RPC_RequestAdvanceTurn] Client wants turn advance");
            OnRequestAdvanceTurn?.Invoke();
        }

        // ✅ ========== STATE PERSISTENCE METHODS (Bug 1 fix) ==========

        // Constants for room properties
        private const string PROP_CURRENT_TURN = "turn";
        private const string PROP_CURRENT_ROLL = "roll";
        private const string PROP_PHASE = "phase";
        private const string PROP_CONSECUTIVE_SIXES = "sixes";
        private const string PROP_EXTRA_TURNS = "extraTurns";
        private const string PROP_PAWN_STATES = "pawnStates";
        private const string PROP_TIMER_START_TIME = "timerStart";
        private const string PROP_TIMER_DURATION = "timerDuration";

        /// <summary>
        /// Host saves current game state to room properties for late joiners
        /// </summary>
        public void SyncGameState(int turn, int roll, int phase, int sixes, int extraTurns)
        {
            if (!PhotonNetwork.IsMasterClient) return;

            var props = new ExitGames.Client.Photon.Hashtable
            {
                [PROP_CURRENT_TURN] = turn,
                [PROP_CURRENT_ROLL] = roll,
                [PROP_PHASE] = phase,
                [PROP_CONSECUTIVE_SIXES] = sixes,
                [PROP_EXTRA_TURNS] = extraTurns
            };

            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            Debug.Log($"[StateSync] Saved: Turn={turn}, Roll={roll}, Phase={phase}");
        }

        /// <summary>
        /// Client reads current game state from room properties
        /// </summary>
        public bool TryGetGameState(out int turn, out int roll, out int phase, out int sixes, out int extraTurns)
        {
            var props = PhotonNetwork.CurrentRoom.CustomProperties;

            if (props.ContainsKey(PROP_CURRENT_TURN))
            {
                turn = (int)props[PROP_CURRENT_TURN];
                roll = props.ContainsKey(PROP_CURRENT_ROLL) ? (int)props[PROP_CURRENT_ROLL] : -1;
                phase = props.ContainsKey(PROP_PHASE) ? (int)props[PROP_PHASE] : 0;
                sixes = props.ContainsKey(PROP_CONSECUTIVE_SIXES) ? (int)props[PROP_CONSECUTIVE_SIXES] : 0;
                extraTurns = props.ContainsKey(PROP_EXTRA_TURNS) ? (int)props[PROP_EXTRA_TURNS] : 0;
                return true;
            }

            turn = roll = phase = sixes = extraTurns = 0;
            return false;
        }

        /// <summary>
        /// Host saves serialized pawn states to room properties
        /// </summary>
        public void SavePawnStates(string serializedStates)
        {
            if (!PhotonNetwork.IsMasterClient) return;

            var props = new ExitGames.Client.Photon.Hashtable
            {
                [PROP_PAWN_STATES] = serializedStates
            };

            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }

        /// <summary>
        /// Client reads serialized pawn states from room properties
        /// </summary>
        public string GetPawnStates()
        {
            var props = PhotonNetwork.CurrentRoom.CustomProperties;
            return props.ContainsKey(PROP_PAWN_STATES) ? (string)props[PROP_PAWN_STATES] : null;
        }

        // ========== TIMER SYNCHRONIZATION (Fix 1) ==========

        public event Action<float> OnTimerStart;
        public event Action OnTimerStop;

        /// <summary>
        /// Host broadcasts timer start to all clients
        /// </summary>
        public void BroadcastTimerStart(float duration)
        {
            photonView.RPC(nameof(RPC_TimerStart), RpcTarget.All, duration);
        }

        [PunRPC]
        private void RPC_TimerStart(float duration)
        {
            Debug.Log($"[RPC_TimerStart] Timer started: {duration}s");
            OnTimerStart?.Invoke(duration);
        }

        /// <summary>
        /// Host broadcasts timer stop to all clients
        /// </summary>
        public void BroadcastTimerStop()
        {
            photonView.RPC(nameof(RPC_TimerStop), RpcTarget.All);
        }

        [PunRPC]
        private void RPC_TimerStop()
        {
            Debug.Log("[RPC_TimerStop] Timer stopped");
            OnTimerStop?.Invoke();
        }

        // ========== TIMER STATE PERSISTENCE (Fix 2) ==========

        /// <summary>
        /// Host saves timer start time and duration to room properties
        /// Uses PhotonNetwork.Time for network-synchronized timestamp
        /// </summary>
        public void SaveTimerState(double startTime, float duration)
        {
            if (!PhotonNetwork.IsMasterClient) return;

            var props = new ExitGames.Client.Photon.Hashtable
            {
                [PROP_TIMER_START_TIME] = startTime,
                [PROP_TIMER_DURATION] = duration
            };

            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            Debug.Log($"[TimerStateSync] Saved: Start={startTime:F2}, Duration={duration}s");
        }

        /// <summary>
        /// Client reads timer state from room properties
        /// </summary>
        public bool TryGetTimerState(out double startTime, out float duration)
        {
            var props = PhotonNetwork.CurrentRoom.CustomProperties;

            if (props.ContainsKey(PROP_TIMER_START_TIME))
            {
                startTime = (double)props[PROP_TIMER_START_TIME];
                duration = (float)props[PROP_TIMER_DURATION];
                return true;
            }

            startTime = 0;
            duration = 0;
            return false;
        }

        /// <summary>
        /// Clear timer state from room properties
        /// </summary>
        public void ClearTimerState()
        {
            if (!PhotonNetwork.IsMasterClient) return;

            var props = new ExitGames.Client.Photon.Hashtable
            {
                [PROP_TIMER_START_TIME] = null,
                [PROP_TIMER_DURATION] = null
            };

            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }
    }
}