using UnityEngine;
using LudoFriends.Services;

namespace LudoFriends.Presentation
{
    /// <summary>
    /// Geliştirme için coin manipulation. Production build'de GameObject pasif yapılabilir
    /// veya bu component kaldırılabilir.
    /// </summary>
    public class CoinDebugController : MonoBehaviour
    {
        [SerializeField] private int addAmount = 1000;
        [SerializeField] private int spendAmount = 100;

        public void DebugAdd() => CoinManager.Add(addAmount);

        public void DebugSpend() => CoinManager.TrySpend(spendAmount);

        public void DebugReset()
        {
            PlayerPrefs.DeleteKey("coin_balance");
            PlayerPrefs.DeleteKey("coin_first_launch_done");
            PlayerPrefs.Save();
            Debug.Log("[CoinDebugController] Coin state reset. Restart scene or call CoinManager.Balance to re-grant default.");
        }

        public void DebugResetSkins()
        {
            DiceSkinManager.DebugReset();
        }

        public void DebugResetAll()
        {
            DebugReset();
            DebugResetSkins();
        }

        [ContextMenu("Log Balance")]
        private void LogBalance() => Debug.Log($"[CoinManager] Balance = {CoinManager.Balance}");
    }
}
