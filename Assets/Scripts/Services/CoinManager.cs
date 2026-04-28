using UnityEngine;

namespace LudoFriends.Services
{
    public static class CoinManager
    {
        private const string PREF_BALANCE = "coin_balance";
        private const string PREF_FIRST_LAUNCH = "coin_first_launch_done";
        private const int DEFAULT_STARTING_BALANCE = 500;

        public static event System.Action<int> OnBalanceChanged;

        public static int Balance
        {
            get
            {
                EnsureFirstLaunch();
                return PlayerPrefs.GetInt(PREF_BALANCE, 0);
            }
        }

        public static bool CanAfford(int amount) => Balance >= amount;

        public static void Add(int amount)
        {
            if (amount <= 0) return;
            EnsureFirstLaunch();
            int current = PlayerPrefs.GetInt(PREF_BALANCE, 0);
            int next = current + amount;
            PlayerPrefs.SetInt(PREF_BALANCE, next);
            PlayerPrefs.Save();
            OnBalanceChanged?.Invoke(next);
        }

        public static bool TrySpend(int amount)
        {
            if (amount <= 0) return true;
            EnsureFirstLaunch();
            int current = PlayerPrefs.GetInt(PREF_BALANCE, 0);
            if (current < amount) return false;
            int next = current - amount;
            PlayerPrefs.SetInt(PREF_BALANCE, next);
            PlayerPrefs.Save();
            OnBalanceChanged?.Invoke(next);
            return true;
        }

        private static void EnsureFirstLaunch()
        {
            if (PlayerPrefs.GetInt(PREF_FIRST_LAUNCH, 0) == 1) return;
            PlayerPrefs.SetInt(PREF_BALANCE, DEFAULT_STARTING_BALANCE);
            PlayerPrefs.SetInt(PREF_FIRST_LAUNCH, 1);
            PlayerPrefs.Save();
            Debug.Log($"[CoinManager] First launch: granted {DEFAULT_STARTING_BALANCE} coins");
        }
    }
}
