using UnityEngine;
using System;
#if UNITY_ANDROID
using GoogleMobileAds.Api;
#endif

namespace LudoFriends.Services
{
    /// <summary>
    /// Google AdMob - Interstitial reklam yöneticisi.
    /// Singleton, sahneler arası yaşar.
    /// </summary>
    public class AdManager : MonoBehaviour
    {
        public static AdManager Instance { get; private set; }

        // AdMob ID'leri
#if UNITY_ANDROID
        private const string INTERSTITIAL_AD_UNIT_ID = "ca-app-pub-4853705736713696/9066643940";
#else
        private const string INTERSTITIAL_AD_UNIT_ID = "unused";
#endif

#if UNITY_ANDROID
        private InterstitialAd _interstitialAd;
#endif

        private bool _isInitialized;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            InitializeAds();
        }

        /// <summary>
        /// AdMob SDK'yı başlat.
        /// </summary>
        private void InitializeAds()
        {
#if UNITY_ANDROID
            MobileAds.Initialize(initStatus =>
            {
                Debug.Log("[AdManager] AdMob initialized.");
                _isInitialized = true;
                LoadInterstitialAd();
            });
#endif
        }

        /// <summary>
        /// Interstitial reklamı yükle (arka planda).
        /// </summary>
        public void LoadInterstitialAd()
        {
#if UNITY_ANDROID
            if (!_isInitialized) return;

            // Eski reklamı temizle
            if (_interstitialAd != null)
            {
                _interstitialAd.Destroy();
                _interstitialAd = null;
            }

            var adRequest = new AdRequest();

            InterstitialAd.Load(INTERSTITIAL_AD_UNIT_ID, adRequest, (InterstitialAd ad, LoadAdError error) =>
            {
                if (error != null || ad == null)
                {
                    Debug.LogWarning($"[AdManager] Interstitial yüklenemedi: {error}");
                    return;
                }

                Debug.Log("[AdManager] Interstitial yüklendi.");
                _interstitialAd = ad;

                // Reklam kapandığında yeni reklam yükle
                _interstitialAd.OnAdFullScreenContentClosed += () =>
                {
                    Debug.Log("[AdManager] Interstitial kapandı, yeni reklam yükleniyor.");
                    LoadInterstitialAd();
                };

                _interstitialAd.OnAdFullScreenContentFailed += (AdError adError) =>
                {
                    Debug.LogWarning($"[AdManager] Interstitial gösterilemedi: {adError}");
                    LoadInterstitialAd();
                };
            });
#endif
        }

        /// <summary>
        /// Interstitial reklamı göster.
        /// Reklam hazır değilse sessizce geçer.
        /// </summary>
        public void ShowInterstitial(Action onComplete = null)
        {
#if UNITY_ANDROID
            if (_interstitialAd != null && _interstitialAd.CanShowAd())
            {
                // Reklam kapandığında callback çağır
                if (onComplete != null)
                {
                    _interstitialAd.OnAdFullScreenContentClosed += () =>
                    {
                        onComplete?.Invoke();
                    };
                }

                Debug.Log("[AdManager] Interstitial gösteriliyor.");
                _interstitialAd.Show();
            }
            else
            {
                Debug.Log("[AdManager] Interstitial hazır değil, atlaniyor.");
                onComplete?.Invoke();
            }
#else
            Debug.Log("[AdManager-Editor] Interstitial simüle edildi.");
            onComplete?.Invoke();
#endif
        }

        /// <summary>
        /// Reklam hazır mı?
        /// </summary>
        public bool IsInterstitialReady()
        {
#if UNITY_ANDROID
            return _interstitialAd != null && _interstitialAd.CanShowAd();
#else
            return false;
#endif
        }

        private void OnDestroy()
        {
#if UNITY_ANDROID
            if (_interstitialAd != null)
            {
                _interstitialAd.Destroy();
                _interstitialAd = null;
            }
#endif
            if (Instance == this) Instance = null;
        }
    }
}
