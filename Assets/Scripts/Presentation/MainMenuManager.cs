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
        private void PlayClick()
        {
            if (sfxSource != null && clickSound != null)
                sfxSource.PlayOneShot(clickSound);
        }
    }

}