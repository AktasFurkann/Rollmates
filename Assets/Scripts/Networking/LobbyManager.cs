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
    [SerializeField] private Button btnShare;
    [SerializeField] private Button btnStartGame; // ✅ Enhancement 1: Host manual start
    [SerializeField] private Button btnSpectate;  // İzleyici modu toggle
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
    [SerializeField] private AudioClip gameStartSound; // ✅ Oyun başlama sesi

    [Header("Create Choice Panel")]
[SerializeField] private GameObject panelCreateChoice;
[SerializeField] private Button btnPublic;
[SerializeField] private Button btnPrivate;
[SerializeField] private Button btnCancelChoice;

    private bool _gameStarted = false;
    private bool _localIsSpectating = false;
    private Coroutine _countdownCoroutine;
    private string _currentRoomCode = "";

    private readonly Color[] _playerColors = new Color[]
    {
        new Color(0.9f, 0.2f, 0.2f),  // Kırmızı
        new Color(0.95f, 0.85f, 0.1f), // Sarı (Was Green)
        new Color(0.2f, 0.8f, 0.2f),  // Yeşil (Was Yellow)
        new Color(0.2f, 0.4f, 0.9f)   // Mavi
    };

    private enum LobbyStatus
    {
        None, Connecting, Connected, Ready, RoomCreated, Searching,
        RoomNotFound, RoomNotFoundFull, Disconnected, InviteCopied,
        NotEnoughPlayers, WaitingForPlayers, LoadingGame, WaitingForHost
    }
    private LobbyStatus _currentStatus = LobbyStatus.None;

    public override void OnEnable()
    {
        base.OnEnable();
        LocalizationManager.OnLanguageChanged += RefreshLocalization;
    }

    public override void OnDisable()
    {
        base.OnDisable();
        LocalizationManager.OnLanguageChanged -= RefreshLocalization;
    }

    private void RefreshLocalization()
    {
        switch (_currentStatus)
        {
            case LobbyStatus.Connecting:        if (txtStatus != null) txtStatus.text = LocalizationManager.Get("connecting"); break;
            case LobbyStatus.Connected:         if (txtStatus != null) txtStatus.text = LocalizationManager.Get("connected"); break;
            case LobbyStatus.Ready:             if (txtStatus != null) txtStatus.text = LocalizationManager.Get("ready"); break;
            case LobbyStatus.RoomCreated:       if (txtStatus != null) txtStatus.text = LocalizationManager.Get("room_created"); break;
            case LobbyStatus.Searching:         if (txtStatus != null) txtStatus.text = LocalizationManager.Get("searching"); break;
            case LobbyStatus.RoomNotFound:      if (txtStatus != null) txtStatus.text = LocalizationManager.Get("room_not_found"); break;
            case LobbyStatus.RoomNotFoundFull:  if (txtStatus != null) txtStatus.text = LocalizationManager.Get("room_not_found_full"); break;
            case LobbyStatus.Disconnected:      if (txtStatus != null) txtStatus.text = LocalizationManager.Get("disconnected"); break;
            case LobbyStatus.InviteCopied:      if (txtStatus != null) txtStatus.text = LocalizationManager.Get("invite_copied"); break;
            case LobbyStatus.NotEnoughPlayers:  if (txtCountdown != null) txtCountdown.text = LocalizationManager.Get("not_enough_players"); break;
            case LobbyStatus.WaitingForPlayers: if (txtStatus != null) txtStatus.text = LocalizationManager.Get("waiting_for_players"); break;
            case LobbyStatus.LoadingGame:       if (txtStatus != null) txtStatus.text = LocalizationManager.Get("loading_game"); break;
            case LobbyStatus.WaitingForHost:    if (txtCountdown != null) txtCountdown.text = LocalizationManager.Get("waiting_for_host"); break;
        }
        UpdatePlayerList();
    }

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
        btnShare?.onClick.AddListener(OnShareClicked);

        btnPublic?.onClick.AddListener(OnPublicClicked);
btnPrivate?.onClick.AddListener(OnPrivateClicked);
btnCancelChoice?.onClick.AddListener(OnCancelChoiceClicked);
        btnStartGame?.onClick.AddListener(OnStartGameClicked); // ✅ Enhancement 1
        btnSpectate?.onClick.AddListener(OnSpectateClicked);

if (panelCreateChoice != null) panelCreateChoice.SetActive(false);

        // Player slot'ları gizle
        HideAllPlayerSlots();

        if (txtCountdown != null) txtCountdown.text = "";
        if (btnStartGame != null) btnStartGame.gameObject.SetActive(false); // ✅ Enhancement 1: Hide initially
        if (btnSpectate != null) btnSpectate.gameObject.SetActive(false);

        if (PhotonNetwork.IsConnectedAndReady)
        {
            _currentStatus = LobbyStatus.Connected;
            txtStatus.text = LocalizationManager.Get("connected");
            PhotonNetwork.JoinLobby();
        }
        else
        {
            _currentStatus = LobbyStatus.Connecting;
            txtStatus.text = LocalizationManager.Get("connecting");
            PhotonNetwork.ConnectUsingSettings();
        }

        DeepLinkManager.OnPendingCodeChanged += OnDeepLinkReceived;
    }

    private void OnDestroy()
    {
        btnCreate?.onClick.RemoveListener(OnCreateClicked);
        btnJoin?.onClick.RemoveListener(OnJoinClicked);
        btnBack?.onClick.RemoveListener(OnBackClicked);
        btnShare?.onClick.RemoveListener(OnShareClicked);
        DeepLinkManager.OnPendingCodeChanged -= OnDeepLinkReceived;
        btnPublic?.onClick.RemoveListener(OnPublicClicked);
btnPrivate?.onClick.RemoveListener(OnPrivateClicked);
btnCancelChoice?.onClick.RemoveListener(OnCancelChoiceClicked);
        btnStartGame?.onClick.RemoveListener(OnStartGameClicked); // ✅ Enhancement 1
        btnSpectate?.onClick.RemoveListener(OnSpectateClicked);

        if (_countdownCoroutine != null)
            StopCoroutine(_countdownCoroutine);
    }

    // ==================== PHOTON CALLBACKS ====================

    public override void OnConnectedToMaster()
    {
        _currentStatus = LobbyStatus.Connected;
        txtStatus.text = LocalizationManager.Get("connected");
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        _currentStatus = LobbyStatus.Ready;
        txtStatus.text = LocalizationManager.Get("ready");
        if (panelButtons != null) panelButtons.SetActive(true);
        if (panelWaiting != null) panelWaiting.SetActive(false);

        if (btnCreate != null) btnCreate.interactable = true;
        if (btnJoin != null) btnJoin.interactable = true;

        CheckAndConsumeDeepLink();
    }

    public override void OnCreatedRoom()
    {
        _currentRoomCode = PhotonNetwork.CurrentRoom.Name;
        _currentStatus = LobbyStatus.RoomCreated;
        txtStatus.text = LocalizationManager.Get("room_created");
        SetRoomCodeUI(_currentRoomCode);
    }

    public override void OnJoinedRoom()
    {
        // Oyun zaten başlamışsa → spectator olarak direkt game scene'e git
        if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("gs"))
        {
            SceneManager.LoadScene(gameSceneName);
            return;
        }

        // Yeni odaya katıldık → spec flag'ini sıfırla (geç katılım tespiti için)
        _localIsSpectating = false;

        // Buton panelini gizle, bekleme panelini göster
        if (panelButtons != null) panelButtons.SetActive(false);
        if (panelWaiting != null) panelWaiting.SetActive(true);

        _currentRoomCode = PhotonNetwork.CurrentRoom.Name;
        SetRoomCodeUI(_currentRoomCode);
        UpdatePlayerList();

        // ✅ Enhancement 1: Show/hide start button based on host status
        if (btnStartGame != null)
        {
            btnStartGame.gameObject.SetActive(PhotonNetwork.IsMasterClient);
            btnStartGame.interactable = PhotonNetwork.CurrentRoom.PlayerCount >= minPlayersToStart;
        }

        // İzleyici butonu lobide gizli kalır — izleyici yalnızca oyun başladıktan sonra katılan geç gelenlerdir.
        if (btnSpectate != null)
            btnSpectate.gameObject.SetActive(false);

        // ✅ NEW (Fix 3): Show waiting message for clients
        if (txtCountdown != null)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                // Host will display countdown when it starts
                txtCountdown.text = "";
            }
            else
            {
                // Clients see waiting message
                _currentStatus = LobbyStatus.WaitingForHost;
                txtCountdown.text = LocalizationManager.Get("waiting_for_host");
            }
        }

        // Host: geri sayım başlat
        if (PhotonNetwork.IsMasterClient && !_gameStarted)
        {
            _gameStarted = true;
            _countdownCoroutine = StartCoroutine(CountdownAndStart());
        }
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
{
    _currentStatus = LobbyStatus.RoomNotFound;
    txtStatus.text = LocalizationManager.Get("room_not_found");

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
    _currentStatus = LobbyStatus.RoomNotFoundFull;
    txtStatus.text = LocalizationManager.Get("room_not_found_full");

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
        _currentStatus = LobbyStatus.Disconnected;
        txtStatus.text = LocalizationManager.Get("disconnected");
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

    // ✅ Enhancement 1: Host manual start game button
    private void OnStartGameClicked()
    {
        PlayClick();

        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("[OnStartGameClicked] Only host can start game!");
            return;
        }

        int playerCount = PhotonNetwork.CurrentRoom.PlayerCount;
        if (playerCount < minPlayersToStart)
        {
            Debug.LogWarning($"[OnStartGameClicked] Need {minPlayersToStart} players, have {playerCount}");
            return;
        }

        // Stop countdown coroutine to prevent race condition
        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null;
        }

        Debug.Log("[OnStartGameClicked] Host manually starting game!");
        StartGame();
    }

    private void OnSpectateClicked()
    {
        PlayClick();
        _localIsSpectating = !_localIsSpectating;
        PhotonNetwork.LocalPlayer.SetCustomProperties(
            new ExitGames.Client.Photon.Hashtable { { "spec", _localIsSpectating } });
        UpdateSpectateButton();
        Debug.Log($"[LobbyManager] Spectate toggled: {_localIsSpectating}");
    }

    private void UpdateSpectateButton()
    {
        if (btnSpectate == null) return;
        var txt = btnSpectate.GetComponentInChildren<TMPro.TMP_Text>();
        if (txt != null)
            txt.text = _localIsSpectating
                ? LocalizationManager.Get("become_player")
                : LocalizationManager.Get("become_spectator");
    }

    private void OnJoinClicked()
{
    PlayClick();

    string code = inputRoomCode != null ? inputRoomCode.text.Trim() : "";

    if (string.IsNullOrEmpty(code))
    {
        // ✅ Kod yoksa: public odaları ara
        _currentStatus = LobbyStatus.Searching;
        txtStatus.text = LocalizationManager.Get("searching");
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

    private void OnShareClicked()
    {
        PlayClick();

        if (string.IsNullOrEmpty(_currentRoomCode)) return;

        string shareUrl = $"https://AktasFurkann.github.io/rollmateslink/?code={_currentRoomCode}";
        string message = $"Rollmates'te benimle oyna! Odama katıl: {shareUrl}";

        // Tüm platformlarda panoya kopyala
        GUIUtility.systemCopyBuffer = shareUrl;
        _currentStatus = LobbyStatus.InviteCopied;
        txtStatus.text = LocalizationManager.Get("invite_copied");
        StartCoroutine(ResetStatusAfterDelay(2f));

#if !UNITY_EDITOR
        // Android'de WhatsApp'ı da açmayı dene
        string encoded = UnityEngine.Networking.UnityWebRequest.EscapeURL(message);
        Application.OpenURL($"whatsapp://send?text={encoded}");
#endif
    }

    private void CheckAndConsumeDeepLink()
    {
        if (DeepLinkManager.Instance == null) return;

        string code = DeepLinkManager.Instance.PendingRoomCode;
        if (string.IsNullOrEmpty(code)) return;

        DeepLinkManager.Instance.ConsumePendingCode();
        if (panelButtons != null) panelButtons.SetActive(false);
        txtStatus.text = $"Davet ile katılınıyor: {code}";
        JoinRoomByCode(code);
    }

    private void OnDeepLinkReceived(string code)
    {
        if (!PhotonNetwork.InLobby) return;
        CheckAndConsumeDeepLink();
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
        IsOpen = true,
        PlayerTtl = 60000  // 60 saniye yeniden bağlanma penceresi
    };

    PhotonNetwork.CreateRoom(roomCode, opts, TypedLobby.Default);
}

    private void JoinRoom()
    {
        _currentStatus = LobbyStatus.Searching;
        txtStatus.text = LocalizationManager.Get("searching");
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
                txtCountdown.text = string.Format(LocalizationManager.Get("starting_in"), Mathf.CeilToInt(timeRemaining));

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
            {
                _currentStatus = LobbyStatus.NotEnoughPlayers;
                txtCountdown.text = LocalizationManager.Get("not_enough_players");
            }

            _gameStarted = false;
            _countdownCoroutine = StartCoroutine(CountdownAndStart());
        }
    }

    private void StartGame()
    {
        if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom)
        {
            _currentStatus = LobbyStatus.LoadingGame;
            txtStatus.text = LocalizationManager.Get("loading_game");

            // Odayı spectator'lara aç (oyuncu sayısı * 2 slot)
            PhotonNetwork.CurrentRoom.MaxPlayers = (byte)(maxPlayers * 2);
            PhotonNetwork.CurrentRoom.IsOpen = true;
            PhotonNetwork.CurrentRoom.IsVisible = false;

            // Spectator tespiti için room property'leri kaydet
            var gameProps = new ExitGames.Client.Photon.Hashtable
            {
                { "gs", true },
                { "ipc", (int)PhotonNetwork.CurrentRoom.PlayerCount }
            };
            PhotonNetwork.CurrentRoom.SetCustomProperties(gameProps);

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
                playerSlotNames[index].text = $"{LocalizationManager.GetColorName(index)}: {playerName}{suffix}";
            }

            index++;
        }

        // ✅ Enhancement 1: Update start button state based on player count
        if (btnStartGame != null && PhotonNetwork.IsMasterClient)
        {
            btnStartGame.interactable = playerCount >= minPlayersToStart;
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
            _currentStatus = LobbyStatus.WaitingForPlayers;
            txtStatus.text = LocalizationManager.Get("waiting_for_players");
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