using TMPro;
using Photon.Pun;
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

    private bool _isOpen;

    private const string PrefSfxMuted = "prefs_sfx_muted";
    private const string PrefSpeed = "prefs_speed"; // 0 normal, 1 fast

    private void Awake()
    {
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
}

public void Resume()
{
    if (!_isOpen) return;
    _isOpen = false;

    if (panel != null) panel.SetActive(false);

    // ❌ Time.timeScale = 1f; YOK

    if (game != null) game.SetPaused(false);
}


    private void ExitToMainMenu()
    {
        Time.timeScale = 1f;

        if (game != null)
        {
            game.ExitToMainMenu();
            return;
        }

        // Fallback: GameBootstrapper yoksa direkt çık
        if (PhotonNetwork.InRoom)
            PhotonNetwork.LeaveRoom();

        if (PhotonNetwork.IsConnected)
            PhotonNetwork.Disconnect();

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
