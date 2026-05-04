using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LudoFriends.Networking;
using LudoFriends.Services;

namespace LudoFriends.Presentation
{
    /// <summary>
    /// Envanter paneli: tüm DiceSkin'leri grid'e doldurur, tıklamada select/unlock yönetir.
    /// </summary>
    public class DiceSkinInventoryController : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private GameObject panel;            // Açılıp kapanan panel root'u
        [SerializeField] private Transform gridContainer;     // Grid Layout Group olan container
        [SerializeField] private DiceSkinSlotView slotPrefab; // Tek slot prefab
        [SerializeField] private Button btnOpen;              // Envanteri açan buton
        [SerializeField] private Button btnClose;             // Envanteri kapatan buton

        private readonly List<DiceSkinSlotView> _slots = new List<DiceSkinSlotView>();

        private void Awake()
        {
            if (panel != null) panel.SetActive(false);
            if (btnOpen != null) btnOpen.onClick.AddListener(Open);
            if (btnClose != null) btnClose.onClick.AddListener(Close);
        }

        private void OnDestroy()
        {
            if (btnOpen != null) btnOpen.onClick.RemoveListener(Open);
            if (btnClose != null) btnClose.onClick.RemoveListener(Close);
        }

        public void Open()
        {
            if (panel != null) panel.SetActive(true);
            Populate();
        }

        public void Close()
        {
            if (panel != null) panel.SetActive(false);
        }

        public void Populate()
        {
            ClearSlots();

            var db = DiceSkinManager.Database;
            if (db == null || db.skins == null)
            {
                Debug.LogWarning("[DiceSkinInventoryController] Database not available");
                return;
            }

            string selectedId = DiceSkinManager.GetSelectedId();

            foreach (var skin in db.skins)
            {
                if (skin == null) continue;
                var slot = Instantiate(slotPrefab, gridContainer);
                bool isOwned = DiceSkinManager.IsOwned(skin.id);
                bool isSelected = isOwned && skin.id == selectedId;
                slot.Bind(skin, isSelected, isOwned, OnSlotClicked);
                _slots.Add(slot);
            }
        }

        private void ClearSlots()
        {
            foreach (var s in _slots)
            {
                if (s != null) Destroy(s.gameObject);
            }
            _slots.Clear();
        }

        private void OnSlotClicked(DiceSkin skin)
        {
            if (skin == null) return;

            if (DiceSkinManager.IsOwned(skin.id))
            {
                if (skin.id == DiceSkinManager.GetSelectedId())
                {
                    Debug.Log($"[Inventory] '{skin.id}' zaten seçili");
                    return;
                }
                DiceSkinManager.Select(skin.id);
                BroadcastSelectionIfOnline(skin.id);
                Populate(); // Görsel güncelle
                return;
            }

            // Kilitli — unlock akışı
            switch (skin.unlockType)
            {
                case DiceSkinUnlockType.Free:
                    // Free skin'ler ilk açılışta verilir; bu duruma düşmemeli ama defansif olarak handle et
                    DiceSkinManager.Unlock(skin.id);
                    DiceSkinManager.Select(skin.id);
                    BroadcastSelectionIfOnline(skin.id);
                    Populate();
                    break;

                case DiceSkinUnlockType.Coin:
                    if (CoinManager.TrySpend(skin.unlockCost))
                    {
                        DiceSkinManager.Unlock(skin.id);
                        DiceSkinManager.Select(skin.id);
                        BroadcastSelectionIfOnline(skin.id);
                        Debug.Log($"[Inventory] '{skin.id}' {skin.unlockCost} coin ile açıldı");
                        Populate();
                    }
                    else
                    {
                        Debug.Log($"[Inventory] Yetersiz coin: gerekli={skin.unlockCost}, mevcut={CoinManager.Balance}");
                        // TODO: "Yetersiz coin" toast/popup göster
                    }
                    break;

                case DiceSkinUnlockType.Ad:
                    if (AdManager.Instance != null)
                    {
                        AdManager.Instance.ShowRewardedAd(
                            onReward: () =>
                            {
                                DiceSkinManager.Unlock(skin.id);
                                DiceSkinManager.Select(skin.id);
                                BroadcastSelectionIfOnline(skin.id);
                                Debug.Log($"[Inventory] '{skin.id}' rewarded ad ile açıldı");
                                Populate();
                            },
                            onUnavailable: () =>
                            {
                                Debug.Log("[Inventory] Rewarded ad hazır değil, biraz sonra tekrar dene");
                                // TODO: kullanıcıya "ad şu an yok, sonra dene" toast göster
                            }
                        );
                    }
                    else
                    {
                        Debug.LogWarning("[Inventory] AdManager.Instance null");
                    }
                    break;

                case DiceSkinUnlockType.Iap:
                    Debug.Log($"[Inventory] IAP-unlock henüz implemente değil. Product: {skin.iapProductId}");
                    // TODO: IAPManager.Purchase(skin.iapProductId, () => { Unlock + Select + Populate });
                    break;
            }
        }

        private static void BroadcastSelectionIfOnline(string skinId)
        {
            var bridge = SocketIONetworkBridge.Instance;
            if (bridge == null || !bridge.IsConnected) return;
            // Bridge baglantisi varsa her zaman gonder. Room'daysa server diger oyunculara
            // broadcast eder; odada degilse server sadece socket cache'i guncel tutar (sonraki join icin).
            bridge.BroadcastDiceSkin(skinId);
        }
    }
}
