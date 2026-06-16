using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using LudoFriends.Networking;
using LudoFriends.Services;

namespace LudoFriends.Presentation
{
    public class MainMenuManager : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button btnPlay;
        [SerializeField] private Button btnPlayWithBots;
        [SerializeField] private Button btnSettings;
        [SerializeField] private Button btnExit;

        [Header("Leaderboard & GPGS")]
        [SerializeField] private Button btnLeaderboard;
        [SerializeField] private Button btnSignIn;
        [SerializeField] private GameObject signInPanel;

        [Header("Panels")]
        [SerializeField] private GameObject settingsPanel;

        [Header("Settings")]
        [SerializeField] private Slider sliderMusic;
        [SerializeField] private Slider sliderSfx;
        [SerializeField] private Button btnCloseSettings;
        [SerializeField] private TextMeshProUGUI txtSettingsTitle;  // "AYARLAR" / "SETTINGS"
        [SerializeField] private TextMeshProUGUI txtMusicLabel;     // "Müzik" / "Music"
        [SerializeField] private TextMeshProUGUI txtSfxLabel;       // "Ses Efektleri" / "Sound Effects"

        [Header("Language")]
        [SerializeField] private Button btnLanguageTR;
        [SerializeField] private Button btnLanguageEN;

        [Header("Audio")]
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioClip clickSound;       // ✅ YENİ
        [SerializeField] private AudioSource sfxSource;       // ✅ YENİ

        [Header("Version Update")]
        [SerializeField] private GameObject versionUpdatePanel;
        [SerializeField] private Button btnUpdate;
        [SerializeField] private Button btnLater;
        [SerializeField] private TextMeshProUGUI txtUpdateTitle;
        [SerializeField] private TextMeshProUGUI txtUpdateMessage;
        [SerializeField] private Button btnVersionLabel;     // Settings altındaki "Sürüm 1.1.0" tap-to-check
        [SerializeField] private TextMeshProUGUI lblVersion;

        private VersionCheckResult _cachedVersionResult;

        private void Awake()
        {
            if (settingsPanel != null)
                settingsPanel.SetActive(false);

            btnPlay.onClick.AddListener(OnPlayClicked);
            btnSettings.onClick.AddListener(OnSettingsClicked);
            btnExit.onClick.AddListener(OnExitClicked);

            if (btnPlayWithBots != null)
                btnPlayWithBots.onClick.AddListener(OnPlayWithBotsClicked);

            if (btnLeaderboard != null)
                btnLeaderboard.onClick.AddListener(OnLeaderboardClicked);

            if (btnSignIn != null)
                btnSignIn.onClick.AddListener(OnSignInClicked);

            if (btnCloseSettings != null)
                btnCloseSettings.onClick.AddListener(OnCloseSettingsClicked);

            if (btnLanguageTR != null)
                btnLanguageTR.onClick.AddListener(OnLanguageTRClicked);
            if (btnLanguageEN != null)
                btnLanguageEN.onClick.AddListener(OnLanguageENClicked);

            // Kayıtlı ses ayarlarını yükle
            if (sliderMusic != null)
            {
                sliderMusic.value = PlayerPrefs.GetFloat("MusicVolume", 1f);
                sliderMusic.onValueChanged.AddListener(OnMusicVolumeChanged);
            }

            if (sliderSfx != null)
            {
                sliderSfx.value = PlayerPrefs.GetFloat("SfxVolume", 1f);
                sliderSfx.onValueChanged.AddListener(OnSfxVolumeChanged);
            }

            // GPGS auth durumunu dinle
            if (GPGSManager.Instance != null)
                GPGSManager.Instance.OnAuthChanged += OnGPGSAuthChanged;

            UpdateSignInUI();

            // Version update modal başta kapalı
            if (versionUpdatePanel != null)
                versionUpdatePanel.SetActive(false);

            if (btnUpdate != null) btnUpdate.onClick.AddListener(OnUpdateClicked);
            if (btnLater != null) btnLater.onClick.AddListener(OnLaterClicked);
            if (btnVersionLabel != null) btnVersionLabel.onClick.AddListener(OnVersionLabelClicked);

            RefreshVersionLabel();
            RefreshSettingsTexts();

            // Dil değiştiğinde TMP'leri tazele (PNG butonların yazısı değişmez — onlar sprite'da gömülü)
            LocalizationManager.OnLanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            RefreshVersionLabel();
            RefreshSettingsTexts();
            // Modal açıksa mesajı yeni dile göre tazele
            if (versionUpdatePanel != null && versionUpdatePanel.activeSelf
                && _cachedVersionResult != null && txtUpdateMessage != null)
            {
                txtUpdateMessage.text = _cachedVersionResult.ForceUpdate
                    ? LocalizationManager.Get("update_force_msg")
                    : _cachedVersionResult.GetLocalizedMessage();
            }
        }

        private void RefreshSettingsTexts()
        {
            if (txtSettingsTitle != null) txtSettingsTitle.text = LocalizationManager.Get("settings_title");
            if (txtMusicLabel != null)    txtMusicLabel.text    = LocalizationManager.Get("music_label");
            if (txtSfxLabel != null)      txtSfxLabel.text      = LocalizationManager.Get("sfx_label");
        }

        private void Start()
        {
            // VersionCheckManager AfterSceneLoad'da bootstrap olur — bu sahne yüklendikten sonra
            // sonuç hazır olabilir (cache) veya event'le sonra gelir.
            if (VersionCheckManager.Instance == null) return;

            if (VersionCheckManager.Instance.IsCheckCompleted)
            {
                HandleVersionResult(VersionCheckManager.Instance.LastResult);
            }
            else
            {
                VersionCheckManager.Instance.OnVersionCheckCompleted += HandleVersionResult;
            }
        }

        private void HandleVersionResult(VersionCheckResult result)
        {
            _cachedVersionResult = result;
            if (result == null) return;
            if (!VersionCheckManager.Instance.ShouldShowModal(result)) return;
            ShowUpdateModal(result);
        }

        private void ShowUpdateModal(VersionCheckResult result)
        {
            if (versionUpdatePanel == null) return;

            versionUpdatePanel.SetActive(true);

            // Title sahnede varsa lokalize et; yoksa (PNG ile gömülü olabilir) atla
            if (txtUpdateTitle != null)
                txtUpdateTitle.text = LocalizationManager.Get("update_available");

            if (txtUpdateMessage != null)
            {
                txtUpdateMessage.text = result.ForceUpdate
                    ? LocalizationManager.Get("update_force_msg")
                    : result.GetLocalizedMessage();
            }

            // Force update'te "Sonra" gizli
            if (btnLater != null)
                btnLater.gameObject.SetActive(!result.ForceUpdate);
        }

        private void OnUpdateClicked()
        {
            PlayClick();
            string url = _cachedVersionResult != null && !string.IsNullOrEmpty(_cachedVersionResult.StoreUrl)
                ? _cachedVersionResult.StoreUrl
                : "https://play.google.com/store/apps/details?id=com.rollmates.game";
            Application.OpenURL(url);
            // Force update'te paneli kapatma — kullanıcı geri dönerse hala bloklu kalsın
        }

        private void OnLaterClicked()
        {
            PlayClick();
            VersionCheckManager.Instance?.PostponeUpdate();
            if (versionUpdatePanel != null)
                versionUpdatePanel.SetActive(false);
        }

        private void OnVersionLabelClicked()
        {
            PlayClick();
            if (VersionCheckManager.Instance == null) return;
            VersionCheckManager.Instance.OnVersionCheckCompleted -= OnManualCheckCompleted;
            VersionCheckManager.Instance.OnVersionCheckCompleted += OnManualCheckCompleted;
            VersionCheckManager.Instance.CheckNow();
        }

        private void OnManualCheckCompleted(VersionCheckResult result)
        {
            VersionCheckManager.Instance.OnVersionCheckCompleted -= OnManualCheckCompleted;
            _cachedVersionResult = result;

            if (result != null && result.UpdateAvailable)
            {
                // Manuel kontrolde cooldown'u bypass et — kullanıcı zaten bilerek istedi
                ShowUpdateModal(result);
            }
            else
            {
                StartCoroutine(ShowLatestVersionFeedback());
            }
        }

        private IEnumerator ShowLatestVersionFeedback()
        {
            if (lblVersion == null) yield break;
            string original = lblVersion.text;
            lblVersion.text = LocalizationManager.Get("version_check_latest");
            yield return new WaitForSeconds(1.5f);
            lblVersion.text = original;
        }

        private void RefreshVersionLabel()
        {
            if (lblVersion != null)
                lblVersion.text = string.Format(LocalizationManager.Get("version_label"), Application.version);
        }

        private void OnPlayClicked()
        {
            PlayClick();
            SceneManager.LoadScene("LobbyScene");
        }

        private void OnPlayWithBotsClicked()
        {
            PlayClick();
            BotGameConfig.PendingBotLobby = true;
            SceneManager.LoadScene("LobbyScene");
        }

        private void OnSettingsClicked()
        {
            PlayClick();
            if (settingsPanel != null)
                settingsPanel.SetActive(true);
        }

        private void OnCloseSettingsClicked()
        {
            PlayClick();
            if (settingsPanel != null)
                settingsPanel.SetActive(false);
        }

        private void OnExitClicked()
        {
            PlayClick();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
        }

        private void OnMusicVolumeChanged(float value)
        {
            PlayerPrefs.SetFloat("MusicVolume", value);
            if (musicSource != null)
                musicSource.volume = value;
        }

        private void OnSfxVolumeChanged(float value)
        {
            PlayerPrefs.SetFloat("SfxVolume", value);
            AudioListener.volume = value;
        }
        private void OnLanguageTRClicked()
        {
            PlayClick();
            LocalizationManager.Instance?.SetLanguageTR();
        }

        private void OnLanguageENClicked()
        {
            PlayClick();
            LocalizationManager.Instance?.SetLanguageEN();
        }

        private void OnLeaderboardClicked()
        {
            PlayClick();
            if (GPGSManager.Instance != null)
                GPGSManager.Instance.ShowAllLeaderboardsUI();
        }

        private void OnSignInClicked()
        {
            PlayClick();
            if (GPGSManager.Instance != null)
                GPGSManager.Instance.ManualSignIn();
        }

        private void OnGPGSAuthChanged(bool isAuthenticated)
        {
            UpdateSignInUI();
        }

        private void UpdateSignInUI()
        {
            bool isAuth = GPGSManager.Instance != null && GPGSManager.Instance.IsAuthenticated;

            // Giriş yapılmışsa sign-in butonunu gizle, leaderboard butonunu göster
            if (btnSignIn != null)
                btnSignIn.gameObject.SetActive(!isAuth);
            if (btnLeaderboard != null)
                btnLeaderboard.gameObject.SetActive(isAuth);
            if (signInPanel != null)
                signInPanel.SetActive(!isAuth);
        }

        private void OnDestroy()
        {
            btnPlayWithBots?.onClick.RemoveListener(OnPlayWithBotsClicked);
            btnLanguageTR?.onClick.RemoveListener(OnLanguageTRClicked);
            btnLanguageEN?.onClick.RemoveListener(OnLanguageENClicked);

            if (GPGSManager.Instance != null)
                GPGSManager.Instance.OnAuthChanged -= OnGPGSAuthChanged;

            if (VersionCheckManager.Instance != null)
            {
                VersionCheckManager.Instance.OnVersionCheckCompleted -= HandleVersionResult;
                VersionCheckManager.Instance.OnVersionCheckCompleted -= OnManualCheckCompleted;
            }

            LocalizationManager.OnLanguageChanged -= OnLanguageChanged;
        }

        private void PlayClick()
        {
            if (sfxSource != null && clickSound != null)
                sfxSource.PlayOneShot(clickSound);
        }
    }

}