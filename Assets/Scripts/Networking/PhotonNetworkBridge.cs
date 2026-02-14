using System;
using Photon.Pun;
using UnityEngine;

namespace LudoFriends.Networking
{
    public class PhotonNetworkBridge : MonoBehaviourPunCallbacks, IGameNetwork
    {
        public event Action<int, int> OnRoll;
        public event Action<int, int, int> OnMove;
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

        public void BroadcastMove(int playerIndex, int pawnId, int roll)
        {
            photonView.RPC(nameof(RPC_Move), RpcTarget.All, playerIndex, pawnId, roll);
        }

        [PunRPC]
        private void RPC_Move(int playerIndex, int pawnId, int roll)
        {
            Debug.Log($"[RPC_Move] P{playerIndex} moves pawn {pawnId} with {roll}");
            OnMove?.Invoke(playerIndex, pawnId, roll);
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
    }
}