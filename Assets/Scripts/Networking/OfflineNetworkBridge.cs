// using UnityEngine;

// namespace LudoFriends.Networking
// {
//     // Offline’da her şey “lokalde” anında çalışır.
//     public class OfflineNetworkBridge : MonoBehaviour, IGameNetwork
//     {
//         public bool IsHost => true;

//         // Bu event’leri GameBootstrapper dinleyecek
//         public System.Action<int, int> OnRoll;          // (playerIndex, roll)
//         public System.Action<int, int, int> OnMove;     // (playerIndex, pawnId, roll)
//         public System.Action<int> OnTurn;               // (nextPlayerIndex)

//         public void BroadcastRoll(int playerIndex, int roll)
//         {
//             OnRoll?.Invoke(playerIndex, roll);
//         }

//         public void SendMoveRequest(int playerIndex, int pawnId)
//         {
//             // Offline’da “host” da biziz, request’i direkt kabul edip broadcast edeceğiz.
//             // Roll’u GameBootstrapper yönetecek, o yüzden burada roll yok.
//             // GameBootstrapper, request gelince “mevcut roll” ile BroadcastMove çağıracak.
//         }

//         public void BroadcastMove(int playerIndex, int pawnId, int roll)
//         {
//             OnMove?.Invoke(playerIndex, pawnId, roll);
//         }

//         public void BroadcastTurn(int nextPlayerIndex)
//         {
//             OnTurn?.Invoke(nextPlayerIndex);
//         }
//         public void RequestAdvanceTurn()
// {
//     // Offline'da gerek yok ama interface için implement et
// }
//     }
// }
