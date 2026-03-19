using UnityEngine;
#if UNITY_ANDROID
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif

namespace LudoFriends.Services
{
    /// <summary>
    /// Google Play Games Services - Authentication & Leaderboard Manager.
    /// Singleton, sahneler arası yaşar.
    /// </summary>
    public class GPGSManager : MonoBehaviour
    {
        public static GPGSManager Instance { get; private set; }

        public bool IsAuthenticated { get; private set; }
        public string PlayerId { get; private set; }
        public string PlayerName { get; private set; }

        // Leaderboard ID'leri - Google Play Console'dan alındı
        public const string LEADERBOARD_WINS = "CgkIz4GUuYceEAIQAQ";
        public const string LEADERBOARD_GAMES_PLAYED = "CgkIz4GUuYceEAIQAQ"; // TODO: İkinci leaderboard oluşturunca değiştir

        public event System.Action<bool> OnAuthChanged;

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
            Initialize();
        }

        /// <summary>
        /// GPGS'i başlat ve otomatik giriş yap.
        /// </summary>
        public void Initialize()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            PlayGamesPlatform.Activate();
            SignIn();
#else
            Debug.Log("[GPGS] Android dışı platformda çalışıyor, GPGS devre dışı.");
            // Editor'de test için fake auth
            IsAuthenticated = false;
#endif
        }

        /// <summary>
        /// Google Play Games'e giriş yap.
        /// Uygulama açılınca otomatik çağrılır, başarısız olursa
        /// kullanıcıya buton göster.
        /// </summary>
        public void SignIn()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            PlayGamesPlatform.Instance.Authenticate(status =>
            {
                if (status == SignInStatus.Success)
                {
                    IsAuthenticated = true;
                    PlayerId = PlayGamesPlatform.Instance.GetUserId();
                    PlayerName = PlayGamesPlatform.Instance.GetUserDisplayName();
                    Debug.Log($"[GPGS] Giriş başarılı: {PlayerName} ({PlayerId})");
                }
                else
                {
                    IsAuthenticated = false;
                    Debug.LogWarning($"[GPGS] Giriş başarısız: {status}");
                }
                OnAuthChanged?.Invoke(IsAuthenticated);
            });
#endif
        }

        /// <summary>
        /// Kullanıcı manual olarak giriş yapmak isterse (butonla).
        /// </summary>
        public void ManualSignIn()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            PlayGamesPlatform.Instance.ManuallyAuthenticate(status =>
            {
                if (status == SignInStatus.Success)
                {
                    IsAuthenticated = true;
                    PlayerId = PlayGamesPlatform.Instance.GetUserId();
                    PlayerName = PlayGamesPlatform.Instance.GetUserDisplayName();
                    Debug.Log($"[GPGS] Manual giriş başarılı: {PlayerName}");
                }
                else
                {
                    IsAuthenticated = false;
                    Debug.LogWarning($"[GPGS] Manual giriş başarısız: {status}");
                }
                OnAuthChanged?.Invoke(IsAuthenticated);
            });
#endif
        }

        // ══════════════════════════════════════
        // LEADERBOARD
        // ══════════════════════════════════════

        /// <summary>
        /// Kazanma sayısını leaderboard'a gönder.
        /// </summary>
        public void ReportWin()
        {
            int currentWins = PlayerPrefs.GetInt("total_wins", 0) + 1;
            PlayerPrefs.SetInt("total_wins", currentWins);
            PlayerPrefs.Save();

            ReportScore(LEADERBOARD_WINS, currentWins);
        }

        /// <summary>
        /// Oynanan oyun sayısını leaderboard'a gönder.
        /// </summary>
        public void ReportGamePlayed()
        {
            int currentGames = PlayerPrefs.GetInt("total_games", 0) + 1;
            PlayerPrefs.SetInt("total_games", currentGames);
            PlayerPrefs.Save();

            ReportScore(LEADERBOARD_GAMES_PLAYED, currentGames);
        }

        /// <summary>
        /// Belirli bir leaderboard'a skor gönder.
        /// </summary>
        public void ReportScore(string leaderboardId, long score)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsAuthenticated)
            {
                Debug.LogWarning("[GPGS] Skor gönderilemedi - giriş yapılmamış.");
                return;
            }

            Social.ReportScore(score, leaderboardId, success =>
            {
                if (success)
                    Debug.Log($"[GPGS] Skor gönderildi: {score} -> {leaderboardId}");
                else
                    Debug.LogWarning($"[GPGS] Skor gönderilemedi: {leaderboardId}");
            });
#else
            Debug.Log($"[GPGS-Editor] Skor simüle: {score} -> {leaderboardId}");
#endif
        }

        /// <summary>
        /// Google Play'in hazır leaderboard UI'ını göster.
        /// </summary>
        public void ShowLeaderboardUI()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsAuthenticated)
            {
                Debug.LogWarning("[GPGS] Leaderboard gösterilemedi - giriş yapılmamış.");
                ManualSignIn();
                return;
            }

            PlayGamesPlatform.Instance.ShowLeaderboardUI(LEADERBOARD_WINS);
#else
            Debug.Log("[GPGS-Editor] Leaderboard UI simüle edildi.");
#endif
        }

        /// <summary>
        /// Tüm leaderboard'ları göster.
        /// </summary>
        public void ShowAllLeaderboardsUI()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsAuthenticated)
            {
                Debug.LogWarning("[GPGS] Leaderboard gösterilemedi - giriş yapılmamış.");
                ManualSignIn();
                return;
            }

            Social.ShowLeaderboardUI();
#else
            Debug.Log("[GPGS-Editor] Tüm Leaderboard UI simüle edildi.");
#endif
        }

        // ══════════════════════════════════════
        // STATS (Lokal)
        // ══════════════════════════════════════

        public int GetTotalWins() => PlayerPrefs.GetInt("total_wins", 0);
        public int GetTotalGames() => PlayerPrefs.GetInt("total_games", 0);
    }
}
