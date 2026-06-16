using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using LudoFriends.Networking;

namespace LudoFriends.Services
{
    /// <summary>
    /// Sunucudan versiyon bilgisi çekip Application.version ile karşılaştırır.
    /// MainMenu açıldığında modal göstermek için event firlatır.
    /// Internet/sunucu erişilemezse sessizce geçer — oyuncu engellenmez.
    /// </summary>
    public class VersionCheckManager : MonoBehaviour
    {
        public static VersionCheckManager Instance { get; private set; }

        public event Action<VersionCheckResult> OnVersionCheckCompleted;

        public VersionCheckResult LastResult { get; private set; }
        public bool IsCheckCompleted { get; private set; }

        private const string PostponedKey = "version_check_postponed_utc";
        private const float CooldownHours = 24f;
        private const int RequestTimeoutSec = 3;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("VersionCheckManager");
            go.AddComponent<VersionCheckManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            StartCoroutine(CheckRoutine());
        }

        /// <summary>Manuel kontrol (Settings altındaki version label için).</summary>
        public void CheckNow()
        {
            StopAllCoroutines();
            IsCheckCompleted = false;
            StartCoroutine(CheckRoutine());
        }

        /// <summary>"Sonra" butonu — 24 saatlik cooldown başlat.</summary>
        public void PostponeUpdate()
        {
            PlayerPrefs.SetString(PostponedKey, DateTime.UtcNow.ToBinary().ToString());
            PlayerPrefs.Save();
        }

        private IEnumerator CheckRoutine()
        {
            string url = NetworkConfig.ServerUrl.TrimEnd('/') + "/version";
            using var req = UnityWebRequest.Get(url);
            req.timeout = RequestTimeoutSec;
            yield return req.SendWebRequest();

            var result = new VersionCheckResult { CurrentVersion = Application.version };

            if (req.result != UnityWebRequest.Result.Success)
            {
                result.NetworkFailed = true;
                Debug.LogWarning($"[VersionCheck] Network failed: {req.error}");
                Complete(result);
                yield break;
            }

            VersionResponse data;
            try
            {
                data = JsonUtility.FromJson<VersionResponse>(req.downloadHandler.text);
            }
            catch (Exception e)
            {
                result.NetworkFailed = true;
                Debug.LogWarning($"[VersionCheck] JSON parse failed: {e.Message}");
                Complete(result);
                yield break;
            }

            if (data == null || string.IsNullOrEmpty(data.latestVersion))
            {
                result.NetworkFailed = true;
                Complete(result);
                yield break;
            }

            result.LatestVersion = data.latestVersion;
            result.StoreUrl = data.playStoreUrl;
            result.MessageTr = data.messageTr;
            result.MessageEn = data.messageEn;

            Version current = ParseVersion(Application.version);
            Version latest = ParseVersion(data.latestVersion);
            Version min = ParseVersion(data.minSupportedVersion);

            // Force update: client çok eski VEYA backend forceUpdate flag'i açık
            if ((min != null && current != null && current.CompareTo(min) < 0) || data.forceUpdate)
            {
                result.UpdateAvailable = true;
                result.ForceUpdate = true;
            }
            else if (latest != null && current != null && current.CompareTo(latest) < 0)
            {
                result.UpdateAvailable = true;
                result.ForceUpdate = false;
            }

            Complete(result);
        }

        private void Complete(VersionCheckResult result)
        {
            LastResult = result;
            IsCheckCompleted = true;
            OnVersionCheckCompleted?.Invoke(result);
        }

        /// <summary>
        /// "Sonra"ya basıldıysa 24 saat geçmedikçe modalın gösterilmemesi için kullanılır.
        /// Force update'te bypass.
        /// </summary>
        public bool ShouldShowModal(VersionCheckResult result)
        {
            if (result == null || !result.UpdateAvailable) return false;
            if (result.ForceUpdate) return true;

            string saved = PlayerPrefs.GetString(PostponedKey, "");
            if (string.IsNullOrEmpty(saved)) return true;

            if (!long.TryParse(saved, out long bin)) return true;
            try
            {
                DateTime postponed = DateTime.FromBinary(bin);
                return (DateTime.UtcNow - postponed).TotalHours >= CooldownHours;
            }
            catch
            {
                return true;
            }
        }

        private static Version ParseVersion(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            try { return new Version(s); }
            catch { return null; }
        }

        [Serializable]
        private class VersionResponse
        {
            public string latestVersion;
            public string minSupportedVersion;
            public bool forceUpdate;
            public string playStoreUrl;
            public string messageTr;
            public string messageEn;
        }
    }

    public class VersionCheckResult
    {
        public bool UpdateAvailable;
        public bool ForceUpdate;
        public bool NetworkFailed;
        public string CurrentVersion;
        public string LatestVersion;
        public string StoreUrl;
        public string MessageTr;
        public string MessageEn;

        /// <summary>Aktif dile göre mesajı döndürür; backend mesajı yoksa fallback localization key.</summary>
        public string GetLocalizedMessage()
        {
            string msg = LocalizationManager.CurrentLanguage == LocalizationManager.Language.English
                ? MessageEn
                : MessageTr;
            return string.IsNullOrEmpty(msg) ? LocalizationManager.Get("update_default_msg") : msg;
        }
    }
}
