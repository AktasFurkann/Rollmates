using UnityEngine;
using System;
using System.Collections.Generic;
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
        private const string REWARDED_AD_UNIT_ID = "ca-app-pub-4853705736713696/2242562717";
#else
        private const string INTERSTITIAL_AD_UNIT_ID = "unused";
        private const string REWARDED_AD_UNIT_ID = "unused";
#endif

#if UNITY_ANDROID
        private InterstitialAd _interstitialAd;
        private RewardedAd _rewardedAd;
#endif

        private bool _isInitialized;

        // Reklamlar arasındaki minimum süre (saniye). AdMob politikası gereği.
        // Rewarded reklamlar bu cooldown'a tabi değildir (kullanıcı bilinçli izliyor).
        private const float MIN_AD_INTERVAL_SECONDS = 60f;
        private float _lastAdShownTime = -999f;

        // Google Mobile Ads SDK callback'leri arka plan thread'inden gelir; Unity API'leri
        // sadece main thread'den çağrılabilir. Bu kuyruk callback'leri main thread'e marshal eder.
        private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();
        private readonly object _queueLock = new object();

        private void RunOnMainThread(Action action)
        {
            if (action == null) return;
            lock (_queueLock) _mainThreadQueue.Enqueue(action);
        }

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

        private void Update()
        {
            // Background thread'den gelen callback'leri main thread'de çalıştır
            lock (_queueLock)
            {
                while (_mainThreadQueue.Count > 0)
                {
                    var action = _mainThreadQueue.Dequeue();
                    try { action?.Invoke(); }
                    catch (Exception e) { Debug.LogError($"[AdManager] Main thread queue exception: {e}"); }
                }
            }
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
                LoadRewardedAd();
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

                // Reklam kapandığında yeni reklam yükle (main thread'de)
                _interstitialAd.OnAdFullScreenContentClosed += () =>
                {
                    RunOnMainThread(() =>
                    {
                        Debug.Log("[AdManager] Interstitial kapandı, yeni reklam yükleniyor.");
                        LoadInterstitialAd();
                    });
                };

                _interstitialAd.OnAdFullScreenContentFailed += (AdError adError) =>
                {
                    RunOnMainThread(() =>
                    {
                        Debug.LogWarning($"[AdManager] Interstitial gösterilemedi: {adError}");
                        LoadInterstitialAd();
                    });
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
            float elapsed = Time.realtimeSinceStartup - _lastAdShownTime;
            if (elapsed < MIN_AD_INTERVAL_SECONDS)
            {
                Debug.Log($"[AdManager] Cooldown aktif, reklam atlanıyor. Kalan: {MIN_AD_INTERVAL_SECONDS - elapsed:F0}s");
                onComplete?.Invoke();
                return;
            }

            if (_interstitialAd != null && _interstitialAd.CanShowAd())
            {
                _lastAdShownTime = Time.realtimeSinceStartup;

                // Reklam kapandığında callback çağır (main thread'de)
                if (onComplete != null)
                {
                    _interstitialAd.OnAdFullScreenContentClosed += () =>
                    {
                        RunOnMainThread(() => onComplete?.Invoke());
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

        // ====================== REWARDED ======================

        /// <summary>
        /// Rewarded reklamı yükle (arka planda).
        /// </summary>
        public void LoadRewardedAd()
        {
#if UNITY_ANDROID
            if (!_isInitialized) return;

            if (_rewardedAd != null)
            {
                _rewardedAd.Destroy();
                _rewardedAd = null;
            }

            var adRequest = new AdRequest();

            RewardedAd.Load(REWARDED_AD_UNIT_ID, adRequest, (RewardedAd ad, LoadAdError error) =>
            {
                if (error != null || ad == null)
                {
                    Debug.LogWarning($"[AdManager] Rewarded yüklenemedi: {error}");
                    return;
                }

                Debug.Log("[AdManager] Rewarded yüklendi.");
                _rewardedAd = ad;

                _rewardedAd.OnAdFullScreenContentClosed += () =>
                {
                    RunOnMainThread(() =>
                    {
                        Debug.Log("[AdManager] Rewarded kapandı, yeni reklam yükleniyor.");
                        LoadRewardedAd();
                    });
                };

                _rewardedAd.OnAdFullScreenContentFailed += (AdError adError) =>
                {
                    RunOnMainThread(() =>
                    {
                        Debug.LogWarning($"[AdManager] Rewarded gösterilemedi: {adError}");
                        LoadRewardedAd();
                    });
                };
            });
#endif
        }

        /// <summary>
        /// Rewarded reklamı göster. Kullanıcı reklamı tamamlarsa onReward callback'i çağrılır.
        /// Reklam hazır değilse onUnavailable çağrılır (onReward çağrılmaz).
        /// onReward gerçek ödülü vermek için kullanılmalı; reklam yarıda kapatılırsa çağrılmaz.
        /// </summary>
        public void ShowRewardedAd(Action onReward, Action onUnavailable = null)
        {
#if UNITY_ANDROID
            if (_rewardedAd != null && _rewardedAd.CanShowAd())
            {
                Debug.Log("[AdManager] Rewarded gösteriliyor.");
                _rewardedAd.Show((Reward reward) =>
                {
                    // Bu callback Google Mobile Ads SDK tarafindan ARKA PLAN thread'inden cagrilir.
                    // onReward Unity API'leri (Instantiate vb.) cagirabilir, o yuzden main thread'e marshal et.
                    RunOnMainThread(() =>
                    {
                        Debug.Log($"[AdManager] Rewarded earned: {reward.Type} x{reward.Amount}");
                        onReward?.Invoke();
                    });
                });
            }
            else
            {
                Debug.Log("[AdManager] Rewarded hazır değil.");
                onUnavailable?.Invoke();
            }
#else
            Debug.Log("[AdManager-Editor] Rewarded simüle edildi → onReward direkt çağrılıyor.");
            onReward?.Invoke();
#endif
        }

        public bool IsRewardedReady()
        {
#if UNITY_ANDROID
            return _rewardedAd != null && _rewardedAd.CanShowAd();
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
            if (_rewardedAd != null)
            {
                _rewardedAd.Destroy();
                _rewardedAd = null;
            }
#endif
            if (Instance == this) Instance = null;
        }
    }
}
