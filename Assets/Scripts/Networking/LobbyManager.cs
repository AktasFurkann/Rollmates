using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class LobbyManager : MonoBehaviourPunCallbacks
{
    [Header("Panels")]
    [SerializeField] private GameObject panelButtons;     // Create/Join butonları
    [SerializeField] private GameObject panelWaiting;     // Bekleme ekranı

    [Header("Buttons")]
    [SerializeField] private Button btnCreate;
    [SerializeField] private Button btnJoin;
    [SerializeField] private Button btnBack;
    [SerializeField] private Button btnCopyCode;
    [SerializeField] private TMP_InputField inputRoomCode;

    [Header("Texts")]
    [SerializeField] private TMP_Text txtStatus;
    [SerializeField] private TMP_Text txtRoomCode;
    [SerializeField] private TMP_Text txtCountdown;
    [SerializeField] private TMP_Text txtPlayerCount;

    [Header("Player Slots")]
    [SerializeField] private Image[] playerSlotImages;    // 4 slot (renk gösterimi)
    [SerializeField] private TMP_Text[] playerSlotNames;  // 4 slot (isim)
    [SerializeField] private GameObject[] playerSlotObjects; // 4 slot objesi (aktif/pasif)

    [Header("Config")]
    [SerializeField] private string gameSceneName = "GameScene";
    [SerializeField] private byte maxPlayers = 4;
    [SerializeField] private float countdownDuration = 15f;
    [SerializeField] private int minPlayersToStart = 2;

    [Header("Audio")]
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioClip clickSound;

    [Header("Create Choice Panel")]
[SerializeField] private GameObject panelCreateChoice;
[SerializeField] private Button btnPublic;
[SerializeField] private Button btnPrivate;
[SerializeField] private Button btnCancelChoice;

    private bool _gameStarted = false;
    private Coroutine _countdownCoroutine;
    private string _currentRoomCode = "";

    private readonly Color[] _playerColors = new Color[]
    {
        new Color(0.9f, 0.2f, 0.2f),  // Kırmızı
        new Color(0.2f, 0.8f, 0.2f),  // Yeşil
        new Color(0.95f, 0.85f, 0.1f), // Sarı
        new Color(0.2f, 0.4f, 0.9f)   // Mavi
    };

    private readonly string[] _colorNames = { "Kırmızı", "Yeşil", "Sarı", "Mavi" };

    private void Start()
    {
        PhotonNetwork.AutomaticallySyncScene = true;

        // Panelleri ayarla
        if (panelButtons != null) panelButtons.SetActive(false);
        if (panelWaiting != null) panelWaiting.SetActive(false);

        // Buton listener'ları
        btnCreate?.onClick.AddListener(OnCreateClicked);
        btnJoin?.onClick.AddListener(OnJoinClicked);
        btnBack?.onClick.AddListener(OnBackClicked);
        btnCopyCode?.onClick.AddListener(OnCopyCodeClicked);

        btnPublic?.onClick.AddListener(OnPublicClicked);
btnPrivate?.onClick.AddListener(OnPrivateClicked);
btnCancelChoice?.onClick.AddListener(OnCancelChoiceClicked);

if (panelCreateChoice != null) panelCreateChoice.SetActive(false);

        // Player slot'ları gizle
        HideAllPlayerSlots();

        txtStatus.text = "Sunucuya bağlanılıyor...";
        if (txtCountdown != null) txtCountdown.text = "";

        PhotonNetwork.ConnectUsingSettings();
    }

    private void OnDestroy()
    {
        btnCreate?.onClick.RemoveListener(OnCreateClicked);
        btnJoin?.onClick.RemoveListener(OnJoinClicked);
        btnBack?.onClick.RemoveListener(OnBackClicked);
        btnCopyCode?.onClick.RemoveListener(OnCopyCodeClicked);
        btnPublic?.onClick.RemoveListener(OnPublicClicked);
btnPrivate?.onClick.RemoveListener(OnPrivateClicked);
btnCancelChoice?.onClick.RemoveListener(OnCancelChoiceClicked);

        if (_countdownCoroutine != null)
            StopCoroutine(_countdownCoroutine);
    }

    // ==================== PHOTON CALLBACKS ====================

    public override void OnConnectedToMaster()
    {
        txtStatus.text = "Bağlandı!";
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        txtStatus.text = "Hazır!";
        if (panelButtons != null) panelButtons.SetActive(true);
        if (panelWaiting != null) panelWaiting.SetActive(false);

        if (btnCreate != null) btnCreate.interactable = true;
        if (btnJoin != null) btnJoin.interactable = true;
    }

    public override void OnCreatedRoom()
    {
        _currentRoomCode = PhotonNetwork.CurrentRoom.Name;
        txtStatus.text = "Oda oluşturuldu!";
        SetRoomCodeUI(_currentRoomCode);
    }

    public override void OnJoinedRoom()
    {
        // Buton panelini gizle, bekleme panelini göster
        if (panelButtons != null) panelButtons.SetActive(false);
        if (panelWaiting != null) panelWaiting.SetActive(true);

        _currentRoomCode = PhotonNetwork.CurrentRoom.Name;
        SetRoomCodeUI(_currentRoomCode);
        UpdatePlayerList();

        // Host: geri sayım başlat
        if (PhotonNetwork.IsMasterClient && !_gameStarted)
        {
            _gameStarted = true;
            _countdownCoroutine = StartCoroutine(CountdownAndStart());
        }
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
{
    txtStatus.text = "Oda bulunamadı!";

    if (btnCreate != null) btnCreate.interactable = true;
    if (btnJoin != null) btnJoin.interactable = true;
}

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        UpdatePlayerList();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        UpdatePlayerList();
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
{
    txtStatus.text = "Oda bulunamadı veya dolu!";

    if (btnCreate != null) btnCreate.interactable = true;
    if (btnJoin != null) btnJoin.interactable = true;
}

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        txtStatus.text = $"Oda oluşturulamadı: {message}";
        if (btnCreate != null) btnCreate.interactable = true;
        if (btnJoin != null) btnJoin.interactable = true;
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        txtStatus.text = "Bağlantı kesildi!";
        if (panelButtons != null) panelButtons.SetActive(false);
        if (panelWaiting != null) panelWaiting.SetActive(false);
    }

    // ==================== BUTTON HANDLERS ====================

    private void OnCreateClicked()
{
    PlayClick();

    // Seçenek panelini göster
    if (panelCreateChoice != null)
        panelCreateChoice.SetActive(true);
}

    private void OnJoinClicked()
{
    PlayClick();

    string code = inputRoomCode != null ? inputRoomCode.text.Trim() : "";

    if (string.IsNullOrEmpty(code))
    {
        // ✅ Kod yoksa: public odaları ara
        txtStatus.text = "Oyun aranıyor...";
        if (btnCreate != null) btnCreate.interactable = false;
        if (btnJoin != null) btnJoin.interactable = false;
        PhotonNetwork.JoinRandomRoom();
    }
    else
    {
        // ✅ Kod varsa: o odaya katıl
        JoinRoomByCode(code);
    }
}

    private void OnBackClicked()
    {
        PlayClick();

        if (PhotonNetwork.InRoom)
            PhotonNetwork.LeaveRoom();

        if (PhotonNetwork.IsConnected)
            PhotonNetwork.Disconnect();

        SceneManager.LoadScene("MainMenu");
    }

    private void OnCopyCodeClicked()
    {
        PlayClick();

        if (!string.IsNullOrEmpty(_currentRoomCode))
        {
            GUIUtility.systemCopyBuffer = _currentRoomCode;
            txtStatus.text = "Oda kodu kopyalandı!";
            StartCoroutine(ResetStatusAfterDelay(2f));
        }
    }

    // ==================== GAME LOGIC ====================

    private void CreateRoom(bool isPrivate)
{
    if (btnCreate != null) btnCreate.interactable = false;
    if (btnJoin != null) btnJoin.interactable = false;

    string roomCode = Random.Range(100000, 999999).ToString();

    var opts = new RoomOptions
    {
        MaxPlayers = maxPlayers,
        IsVisible = !isPrivate,  // ✅ Public: görünür, Private: gizli
        IsOpen = true
    };

    PhotonNetwork.CreateRoom(roomCode, opts, TypedLobby.Default);
}

    private void JoinRoom()
    {
        txtStatus.text = "Oyun aranıyor...";
        if (btnCreate != null) btnCreate.interactable = false;
        if (btnJoin != null) btnJoin.interactable = false;

        PhotonNetwork.JoinRandomRoom();
    }

    private void JoinRoomByCode(string roomCode)
{
    txtStatus.text = $"Oda aranıyor: {roomCode}";
    if (btnCreate != null) btnCreate.interactable = false;
    if (btnJoin != null) btnJoin.interactable = false;

    PhotonNetwork.JoinRoom(roomCode);
}

    private IEnumerator CountdownAndStart()
    {
        float timeRemaining = countdownDuration;

        while (timeRemaining > 0)
        {
            int playerCount = PhotonNetwork.CurrentRoom.PlayerCount;

            if (txtCountdown != null)
                txtCountdown.text = $"Başlamaya {Mathf.CeilToInt(timeRemaining)} saniye...";

            UpdatePlayerList();

            yield return new WaitForSeconds(1f);
            timeRemaining -= 1f;
        }

        int finalPlayerCount = PhotonNetwork.CurrentRoom.PlayerCount;

        if (finalPlayerCount >= minPlayersToStart)
        {
            StartGame();
        }
        else
        {
            if (txtCountdown != null)
                txtCountdown.text = "Yeterli oyuncu yok, bekleniyor...";

            _gameStarted = false;
            _countdownCoroutine = StartCoroutine(CountdownAndStart());
        }
    }

    private void StartGame()
    {
        if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom)
        {
            txtStatus.text = "Oyun yükleniyor...";
            PhotonNetwork.CurrentRoom.IsOpen = false;
            PhotonNetwork.CurrentRoom.IsVisible = false;
            PhotonNetwork.LoadLevel(gameSceneName);
        }
    }

    // ==================== UI HELPERS ====================

    private void UpdatePlayerList()
    {
        if (!PhotonNetwork.InRoom) return;

        int playerCount = PhotonNetwork.CurrentRoom.PlayerCount;

        if (txtPlayerCount != null)
            txtPlayerCount.text = $"{playerCount} / {maxPlayers}";

        // Tüm slotları gizle
        HideAllPlayerSlots();

        // Aktif oyuncuları göster
        int index = 0;
        foreach (var player in PhotonNetwork.CurrentRoom.Players)
        {
            if (index >= 4) break;

            if (playerSlotObjects != null && index < playerSlotObjects.Length)
                playerSlotObjects[index].SetActive(true);

            if (playerSlotImages != null && index < playerSlotImages.Length)
                playerSlotImages[index].color = _playerColors[index];

            if (playerSlotNames != null && index < playerSlotNames.Length)
            {
                string playerName = player.Value.NickName;
                if (string.IsNullOrEmpty(playerName))
                    playerName = $"Oyuncu {index + 1}";

                string suffix = player.Value.IsMasterClient ? " (Host)" : "";
                playerSlotNames[index].text = $"{_colorNames[index]}: {playerName}{suffix}";
            }

            index++;
        }
    }

    private void HideAllPlayerSlots()
    {
        if (playerSlotObjects == null) return;

        for (int i = 0; i < playerSlotObjects.Length; i++)
        {
            if (playerSlotObjects[i] != null)
                playerSlotObjects[i].SetActive(false);
        }
    }

    private void SetRoomCodeUI(string code)
    {
        if (txtRoomCode != null)
            txtRoomCode.text = string.IsNullOrWhiteSpace(code) ? "" : $"Oda: {code}";
    }

    private IEnumerator ResetStatusAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (PhotonNetwork.InRoom)
        {
            int playerCount = PhotonNetwork.CurrentRoom.PlayerCount;
            txtStatus.text = $"Oyuncular bekleniyor...";
        }
    }

    private void PlayClick()
    {
        if (sfxSource != null && clickSound != null)
            sfxSource.PlayOneShot(clickSound);
    }
    private void OnPublicClicked()
{
    PlayClick();
    if (panelCreateChoice != null) panelCreateChoice.SetActive(false);
    CreateRoom(isPrivate: false);
}

private void OnPrivateClicked()
{
    PlayClick();
    if (panelCreateChoice != null) panelCreateChoice.SetActive(false);
    CreateRoom(isPrivate: true);
}

private void OnCancelChoiceClicked()
{
    PlayClick();
    if (panelCreateChoice != null) panelCreateChoice.SetActive(false);
}
}