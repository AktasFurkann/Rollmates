using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace LudoFriends.Presentation
{
    public class MainMenuManager : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button btnPlay;
        [SerializeField] private Button btnSettings;
        [SerializeField] private Button btnExit;

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
        }

        private void OnPlayClicked()
        {
            PlayClick();
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

        private void OnDestroy()
        {
            btnLanguageTR?.onClick.RemoveListener(OnLanguageTRClicked);
            btnLanguageEN?.onClick.RemoveListener(OnLanguageENClicked);
        }

        private void PlayClick()
        {
            if (sfxSource != null && clickSound != null)
                sfxSource.PlayOneShot(clickSound);
        }
    }

}