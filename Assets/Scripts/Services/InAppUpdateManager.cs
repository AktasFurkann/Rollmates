using UnityEngine;
using System;

namespace LudoFriends.Services
{
    /// <summary>
    /// Google Play In-App Updates API.
    /// Uygulama açılışında güncelleme var mı kontrol eder.
    /// Varsa Google'ın native "Güncelleme gerekli" ekranını gösterir.
    /// Sahneye elle eklemek gerekmez — RuntimeInitializeOnLoad ile kendini oluşturur.
    /// </summary>
    public class InAppUpdateManager : MonoBehaviour
    {
        public static InAppUpdateManager Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("InAppUpdateManager");
            go.AddComponent<InAppUpdateManager>();
            DontDestroyOnLoad(go);
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
            // Google Play In-App Updates devre dışı — yerine VersionCheckManager kullanılıyor.
            // (Sunucu tabanlı kontrol daha güvenilir; Play API edge case'leri var.)
#if UNITY_ANDROID && !UNITY_EDITOR
            // CheckForUpdate();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void CheckForUpdate()
        {
            try
            {
                var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

                var factory = new AndroidJavaClass("com.google.android.play.core.appupdate.AppUpdateManagerFactory");
                var appUpdateManager = factory.CallStatic<AndroidJavaObject>("create", activity);

                var infoTask = appUpdateManager.Call<AndroidJavaObject>("getAppUpdateInfo");
                infoTask.Call<AndroidJavaObject>("addOnSuccessListener", new OnSuccessProxy(info =>
                {
                    // UPDATE_AVAILABLE = 2
                    int availability = info.Call<int>("updateAvailability");
                    if (availability == 2)
                    {
                        Debug.Log("[InAppUpdate] Güncelleme mevcut, başlatılıyor.");
                        // IMMEDIATE = 1
                        appUpdateManager.Call<int>("startUpdateFlowForResult", info, 1, activity, 1001);
                    }
                    else
                    {
                        Debug.Log("[InAppUpdate] Güncelleme yok.");
                    }
                }));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[InAppUpdate] Kontrol başarısız: {e.Message}");
            }
        }

        private class OnSuccessProxy : AndroidJavaProxy
        {
            private readonly Action<AndroidJavaObject> _onSuccess;

            public OnSuccessProxy(Action<AndroidJavaObject> onSuccess)
                : base("com.google.android.gms.tasks.OnSuccessListener")
            {
                _onSuccess = onSuccess;
            }

            public void onSuccess(AndroidJavaObject result)
            {
                _onSuccess?.Invoke(result);
            }
        }
#endif
    }
}
