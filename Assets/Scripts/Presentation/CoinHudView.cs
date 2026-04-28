using TMPro;
using UnityEngine;
using LudoFriends.Services;

namespace LudoFriends.Presentation
{
    public class CoinHudView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI txtBalance;

        private void OnEnable()
        {
            CoinManager.OnBalanceChanged += HandleBalanceChanged;
            Refresh();
        }

        private void OnDisable()
        {
            CoinManager.OnBalanceChanged -= HandleBalanceChanged;
        }

        private void HandleBalanceChanged(int newBalance)
        {
            if (txtBalance != null) txtBalance.text = newBalance.ToString();
        }

        private void Refresh()
        {
            if (txtBalance != null) txtBalance.text = CoinManager.Balance.ToString();
        }
    }
}
