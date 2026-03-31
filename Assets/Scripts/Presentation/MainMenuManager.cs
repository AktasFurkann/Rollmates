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

        [Header("Language")]
        [SerializeField] private Button btnLanguageTR;
        [SerializeField] private Button btnLanguageEN;

        [Header("Audio")]
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioClip clickSound;       // ✅ YENİ
        [SerializeField] private AudioSource sfxSource;       // ✅ YENİ

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
        }

        private void PlayClick()
        {
            if (sfxSource != null && clickSound != null)
                sfxSource.PlayOneShot(clickSound);
        }
    }

}