using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LudoFriends.Services;

namespace LudoFriends.Presentation
{
    /// <summary>
    /// Tek bir dice skin slot'u: preview, isim, durum (selected/owned/locked).
    /// Inventory grid'i için prefab.
    /// </summary>
    public class DiceSkinSlotView : MonoBehaviour
    {
        [SerializeField] private Image imgPreview;
        [SerializeField] private TextMeshProUGUI txtName;
        [SerializeField] private Button btnSlot;

        [Header("Status visuals")]
        [SerializeField] private GameObject selectedBadge;       // "Seçili" göstergesi
        [SerializeField] private GameObject lockOverlay;          // Kilitli skin için karartı + ikon
        [SerializeField] private TextMeshProUGUI txtLockCost;     // Kilit maliyeti yazısı (örn. "100" veya "1.99$")
        [SerializeField] private Image imgLockIcon;               // Kilit türü ikonu (coin/ad/iap)

        [Header("Lock icon sprites (atanmazsa gösterilmez)")]
        [SerializeField] private Sprite iconCoin;
        [SerializeField] private Sprite iconAd;
        [SerializeField] private Sprite iconIap;

        private DiceSkin _skin;
        private System.Action<DiceSkin> _onClick;

        public void Bind(DiceSkin skin, bool isSelected, bool isOwned, System.Action<DiceSkin> onClick)
        {
            _skin = skin;
            _onClick = onClick;

            if (imgPreview != null)
            {
                imgPreview.sprite = skin.previewIcon;
                imgPreview.enabled = skin.previewIcon != null;
            }
            if (txtName != null) txtName.text = skin.displayName;

            if (selectedBadge != null) selectedBadge.SetActive(isSelected);

            bool showLock = !isOwned;
            if (lockOverlay != null) lockOverlay.SetActive(showLock);

            if (showLock)
            {
                if (txtLockCost != null)
                {
                    switch (skin.unlockType)
                    {
                        case DiceSkinUnlockType.Coin:
                            txtLockCost.text = skin.unlockCost.ToString();
                            break;
                        case DiceSkinUnlockType.Ad:
                            if (skin.unlockCost > 1)
                            {
                                int progress = DiceSkinManager.GetAdProgress(skin.id);
                                txtLockCost.text = $"{progress}/{skin.unlockCost}";
                            }
                            else
                            {
                                txtLockCost.text = "";
                            }
                            break;
                        case DiceSkinUnlockType.Iap: txtLockCost.text = ""; break; // IAP fiyatı dinamik gelir, boş bırak
                        default: txtLockCost.text = ""; break;
                    }
                }
                if (imgLockIcon != null)
                {
                    Sprite s = null;
                    switch (skin.unlockType)
                    {
                        case DiceSkinUnlockType.Coin: s = iconCoin; break;
                        case DiceSkinUnlockType.Ad: s = iconAd; break;
                        case DiceSkinUnlockType.Iap: s = iconIap; break;
                    }
                    imgLockIcon.sprite = s;
                    imgLockIcon.enabled = s != null;
                }
            }

            if (btnSlot != null)
            {
                btnSlot.onClick.RemoveAllListeners();
                btnSlot.onClick.AddListener(HandleClick);
            }
        }

        private void HandleClick()
        {
            _onClick?.Invoke(_skin);
        }
    }
}
