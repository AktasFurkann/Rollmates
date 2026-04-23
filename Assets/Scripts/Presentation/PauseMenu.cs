using TMPro;
using LudoFriends.Networking;
using LudoFriends.Services;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PauseMenu : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Button btnResume;
    [SerializeField] private Button btnRestart;
    [SerializeField] private Toggle tglSfx;
    [SerializeField] private TMP_Dropdown ddlSpeed;

    [Header("Refs")]
    [SerializeField] private SfxPlayer sfx;
    [SerializeField] private LudoFriends.Gameplay.PawnMover pawnMover;
    [SerializeField] private GameBootstrapper game; // input kilitlemek için
    [SerializeField] private LudoFriends.Presentation.PlayerMuteListController muteList;

    private bool _isOpen;
    private float _sessionStartTime;

    // Reklam göstermek için oyunun en az bu kadar sürmüş olması gerekir (saniye)
    private const float MIN_SESSION_FOR_AD = 30f;

    private const string PrefSfxMuted = "prefs_sfx_muted";
    private const string PrefSpeed = "prefs_speed"; // 0 normal, 1 fast

    private void Awake()
    {
        _sessionStartTime = Time.realtimeSinceStartup;

        if (panel != null) panel.SetActive(false);

        if (btnResume != null) btnResume.onClick.AddListener(Resume);
        if (btnRestart != null) btnRestart.onClick.AddListener(ExitToMainMenu);

        // prefs yükle
        bool muted = PlayerPrefs.GetInt(PrefSfxMuted, 0) == 1;
        int speed = PlayerPrefs.GetInt(PrefSpeed, 0);

        if (tglSfx != null) tglSfx.isOn = !muted; // Toggle: ON = açık
        if (ddlSpeed != null) ddlSpeed.value = speed;

        ApplySfx(!muted);
        ApplySpeed(speed);

        if (tglSfx != null) tglSfx.onValueChanged.AddListener(OnSfxToggle);
        if (ddlSpeed != null) ddlSpeed.onValueChanged.AddListener(OnSpeedChanged);
    }

    private void OnDestroy()
    {
        if (btnResume != null) btnResume.onClick.RemoveListener(Resume);
        if (btnRestart != null) btnRestart.onClick.RemoveListener(ExitToMainMenu);

        if (tglSfx != null) tglSfx.onValueChanged.RemoveListener(OnSfxToggle);
        if (ddlSpeed != null) ddlSpeed.onValueChanged.RemoveListener(OnSpeedChanged);
    }

    private void Update()
    {
        // ESC ile pause
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_isOpen) Resume();
            else Open();
        }
    }

    public void Open()
{
    if (_isOpen) return;
    _isOpen = true;

    if (panel != null) panel.SetActive(true);

    // ❌ Time.timeScale = 0f; YOK

    // ✅ sadece input kilitle
    if (game != null) game.SetPaused(true);

    // Rakip oyuncu mute listesini doldur
    if (muteList != null) muteList.Populate();
}

public void Resume()
{
    if (!_isOpen) return;
    _isOpen = false;

    if (panel != null) panel.SetActive(false);

    // ❌ Time.timeScale = 1f; YOK

    if (game != null) game.SetPaused(false);

    // Satirlari temizle (mute state corner paneller uzerinde korunur)
    if (muteList != null) muteList.Clear();
}


    private void ExitToMainMenu()
    {
        Time.timeScale = 1f;

        float sessionDuration = Time.realtimeSinceStartup - _sessionStartTime;
        bool sessionLongEnough = sessionDuration >= MIN_SESSION_FOR_AD;

        // Yalnızca yeterince uzun oynandıysa reklam göster
        if (sessionLongEnough && AdManager.Instance != null && AdManager.Instance.IsInterstitialReady())
        {
            AdManager.Instance.ShowInterstitial(() => DoExitToMainMenu());
            return;
        }

        DoExitToMainMenu();
    }

    private void DoExitToMainMenu()
    {
        if (game != null)
        {
            game.ExitToMainMenu();
            return;
        }

        // Fallback: GameBootstrapper yoksa direkt çık
        var bridge = SocketIONetworkBridge.Instance;
        if (bridge != null && bridge.IsInRoom)
            bridge.LeaveRoom(true);

        if (bridge != null && bridge.IsConnected)
            bridge.Disconnect();

        SceneManager.LoadScene(0);
    }

    private void OnSfxToggle(bool isOn)
    {
        // Toggle ON = ses açık
        ApplySfx(isOn);
        PlayerPrefs.SetInt(PrefSfxMuted, isOn ? 0 : 1);
        PlayerPrefs.Save();
    }

    private void OnSpeedChanged(int index)
    {
        ApplySpeed(index);
        PlayerPrefs.SetInt(PrefSpeed, index);
        PlayerPrefs.Save();
    }

    private void ApplySfx(bool enabled)
    {
        if (sfx != null) sfx.SetMuted(!enabled);
    }

    private void ApplySpeed(int speedIndex)
    {
        // 0=Normal, 1=Fast
        if (pawnMover == null) return;

        if (speedIndex == 1) pawnMover.SetStepDuration(0.08f); // fast
        else pawnMover.SetStepDuration(0.15f); // normal
    }
}
