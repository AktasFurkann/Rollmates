using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using LudoFriends.Networking;
using LudoFriends.Services;

public class LobbyManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject panelButtons;     // Create/Join butonları
    [SerializeField] private GameObject panelWaiting;     // Bekleme ekranı

    [Header("Buttons")]
    [SerializeField] private Button btnCreate;
    [SerializeField] private Button btnJoin;
    [SerializeField] private Button btnBack;
    [SerializeField] private Button btnShare;
    [SerializeField] private Button btnStartGame;
    [SerializeField] private Button btnSpectate;
    [SerializeField] private TMP_InputField inputRoomCode;

    [Header("Texts")]
    [SerializeField] private TMP_Text txtStatus;
    [SerializeField] private TMP_Text txtRoomCode;
    [SerializeField] private TMP_Text txtCountdown;
    [SerializeField] private TMP_Text txtPlayerCount;
    [SerializeField] private TMP_Text txtSelectRoomType;
    [SerializeField] private TMP_Text txtShareButton;

    [Header("Player Slots")]
    [SerializeField] private Image[] playerSlotImages;
    [SerializeField] private TMP_Text[] playerSlotNames;
    [SerializeField] private GameObject[] playerSlotObjects;

    [Header("Config")]
    [SerializeField] private string gameSceneName = "GameScene";
    [SerializeField] private byte maxPlayers = 4;
    [SerializeField] private float countdownDuration = 15f;
    [SerializeField] private int minPlayersToStart = 2;

    [Header("Audio")]
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioClip clickSound;
    [SerializeField] private AudioClip gameStartSound;

    [Header("Create Choice Panel")]
    [SerializeField] private GameObject panelCreateChoice;
    [SerializeField] private Button btnPublic;
    [SerializeField] private Button btnPrivate;
    [SerializeField] private Button btnCancelChoice;

    private bool _gameStarted = false;
    private bool _localIsSpectating = false;
    private Coroutine _countdownCoroutine;
    private string _currentRoomCode = "";
    private SocketIONetworkBridge _bridge;

    private readonly Color[] _playerColors = new Color[]
    {
        new Color(0.9f, 0.2f, 0.2f),  // Kırmızı
        new Color(0.95f, 0.85f, 0.1f), // Sarı
        new Color(0.2f, 0.8f, 0.2f),  // Yeşil
        new Color(0.2f, 0.4f, 0.9f)   // Mavi
    };

    private enum LobbyStatus
    {
        None, Connecting, Connected, Ready, RoomCreated, Searching,
        RoomNotFound, RoomNotFoundFull, Disconnected, InviteCopied,
        NotEnoughPlayers, WaitingForPlayers, LoadingGame, WaitingForHost
    }
    private LobbyStatus _currentStatus = LobbyStatus.None;

    private void OnEnable()
    {
        LocalizationManager.OnLanguageChanged += RefreshLocalization;
    }

    private void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= RefreshLocalization;
        UnsubscribeBridge();
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

        // Lokalize UI elemanlarını güncelle
        if (txtSelectRoomType != null) txtSelectRoomType.text = LocalizationManager.Get("select_room_type");
        if (txtShareButton != null) txtShareButton.text = LocalizationManager.Get("share");
        if (inputRoomCode != null)
        {
            var placeholder = inputRoomCode.placeholder as TMP_Text;
            if (placeholder != null) placeholder.text = LocalizationManager.Get("room_code_input");
        }
        if (txtRoomCode != null && !string.IsNullOrWhiteSpace(txtRoomCode.text) && !string.IsNullOrEmpty(_currentRoomCode))
            SetRoomCodeUI(_currentRoomCode);
    }

    private void Start()
    {
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
        btnStartGame?.onClick.AddListener(OnStartGameClicked);
        btnSpectate?.onClick.AddListener(OnSpectateClicked);

        if (panelCreateChoice != null) panelCreateChoice.SetActive(false);

        // İlk lokalizasyonu uygula
        if (txtSelectRoomType != null) txtSelectRoomType.text = LocalizationManager.Get("select_room_type");
        if (txtShareButton != null) txtShareButton.text = LocalizationManager.Get("share");
        if (inputRoomCode != null)
        {
            var placeholder = inputRoomCode.placeholder as TMP_Text;
            if (placeholder != null) placeholder.text = LocalizationManager.Get("room_code_input");
        }

        // Player slot'ları gizle
        HideAllPlayerSlots();

        if (txtCountdown != null) txtCountdown.text = "";
        if (btnStartGame != null) btnStartGame.gameObject.SetActive(false);
        if (btnSpectate != null) btnSpectate.gameObject.SetActive(false);

        // SocketIO bridge'i bul veya oluştur
        _bridge = SocketIONetworkBridge.Instance;
        if (_bridge == null)
        {
            var go = new GameObject("SocketIONetworkBridge");
            _bridge = go.AddComponent<SocketIONetworkBridge>();
        }

        SubscribeBridge();

        // Sunucuya bağlan
        _currentStatus = LobbyStatus.Connecting;
        txtStatus.text = LocalizationManager.Get("connecting");

        // GPGS giriş yapılmışsa Google Play adını kullan
        string nickname = PlayerPrefs.GetString("PlayerName", "Player");
        var gpgs = GPGSManager.Instance;
        if (gpgs != null && gpgs.IsAuthenticated && !string.IsNullOrEmpty(gpgs.PlayerName))
            nickname = gpgs.PlayerName;

        _bridge.Connect(nickname);

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
        btnStartGame?.onClick.RemoveListener(OnStartGameClicked);
        btnSpectate?.onClick.RemoveListener(OnSpectateClicked);

        UnsubscribeBridge();

        if (_countdownCoroutine != null)
            StopCoroutine(_countdownCoroutine);
    }

    // ==================== BRIDGE EVENT SUBSCRIPTIONS ====================

    private void SubscribeBridge()
    {
        if (_bridge == null) return;
        _bridge.OnIdentified += OnBridgeIdentified;
        _bridge.OnRoomCreated += OnBridgeRoomCreated;
        _bridge.OnJoinedRoom += OnBridgeJoinedRoom;
        _bridge.OnPlayerJoined += OnBridgePlayerJoined;
        _bridge.OnPlayerLeft += OnBridgePlayerLeft;
        _bridge.OnJoinFailed += OnBridgeJoinFailed;
        _bridge.OnHostChanged += OnBridgeHostChanged;
        _bridge.OnGameStarted += OnBridgeGameStarted;
        _bridge.OnCountdownTick += OnBridgeCountdownTick;
        _bridge.OnDisconnectedEvent += OnBridgeDisconnected;
    }

    private void UnsubscribeBridge()
    {
        if (_bridge == null) return;
        _bridge.OnIdentified -= OnBridgeIdentified;
        _bridge.OnRoomCreated -= OnBridgeRoomCreated;
        _bridge.OnJoinedRoom -= OnBridgeJoinedRoom;
        _bridge.OnPlayerJoined -= OnBridgePlayerJoined;
        _bridge.OnPlayerLeft -= OnBridgePlayerLeft;
        _bridge.OnJoinFailed -= OnBridgeJoinFailed;
        _bridge.OnHostChanged -= OnBridgeHostChanged;
        _bridge.OnGameStarted -= OnBridgeGameStarted;
        _bridge.OnCountdownTick -= OnBridgeCountdownTick;
        _bridge.OnDisconnectedEvent -= OnBridgeDisconnected;
    }

    // ==================== BRIDGE CALLBACKS ====================

    private void OnBridgeIdentified(IdentifiedPayload data)
    {
        if (!data.success)
        {
            _currentStatus = LobbyStatus.Disconnected;
            txtStatus.text = LocalizationManager.Get("disconnected");
            return;
        }

        _currentStatus = LobbyStatus.Ready;
        txtStatus.text = LocalizationManager.Get("ready");
        if (panelButtons != null) panelButtons.SetActive(true);
        if (panelWaiting != null) panelWaiting.SetActive(false);

        if (btnCreate != null) btnCreate.interactable = true;
        if (btnJoin != null) btnJoin.interactable = true;

        // Reconnection check
        if (!string.IsNullOrEmpty(data.reconnectRoomCode))
        {
            _bridge.JoinRoom(data.reconnectRoomCode);
            return;
        }

        CheckAndConsumeDeepLink();
    }

    private void OnBridgeRoomCreated(RoomCreatedPayload data)
    {
        _currentRoomCode = data.code;
        _currentStatus = LobbyStatus.RoomCreated;
        txtStatus.text = LocalizationManager.Get("room_created");
        SetRoomCodeUI(_currentRoomCode);
    }

    private void OnBridgeJoinedRoom(JoinedRoomPayload data)
    {
        // Oyun zaten başlamışsa → spectator olarak direkt game scene'e git
        if (data.gameStarted)
        {
            SceneManager.LoadScene(gameSceneName);
            return;
        }

        _localIsSpectating = false;

        if (panelButtons != null) panelButtons.SetActive(false);
        if (panelWaiting != null) panelWaiting.SetActive(true);

        _currentRoomCode = data.code;
        SetRoomCodeUI(_currentRoomCode);
        UpdatePlayerList();

        if (btnStartGame != null)
        {
            btnStartGame.gameObject.SetActive(data.isHost);
            btnStartGame.interactable = data.players.Length >= minPlayersToStart;
        }

        if (btnSpectate != null)
            btnSpectate.gameObject.SetActive(false);

        if (txtCountdown != null)
        {
            if (data.isHost)
            {
                txtCountdown.text = "";
                // Host: geri sayım başlat
                if (!_gameStarted)
                {
                    _gameStarted = true;
                    _countdownCoroutine = StartCoroutine(CountdownAndStart());
                }
            }
            else
            {
                _currentStatus = LobbyStatus.WaitingForHost;
                txtCountdown.text = LocalizationManager.Get("waiting_for_host");
            }
        }
    }

    private void OnBridgePlayerJoined(PlayerJoinedPayload data)
    {
        UpdatePlayerList();
    }

    private void OnBridgePlayerLeft(PlayerLeftPayload data)
    {
        UpdatePlayerList();
    }

    private void OnBridgeJoinFailed(JoinFailedPayload data)
    {
        switch (data.reason)
        {
            case "no_rooms_available":
            case "room_not_found":
                _currentStatus = LobbyStatus.RoomNotFound;
                txtStatus.text = LocalizationManager.Get("room_not_found");
                break;
            case "room_full":
                _currentStatus = LobbyStatus.RoomNotFoundFull;
                txtStatus.text = LocalizationManager.Get("room_not_found_full");
                break;
            default:
                txtStatus.text = $"Katılma hatası: {data.reason}";
                break;
        }

        if (btnCreate != null) btnCreate.interactable = true;
        if (btnJoin != null) btnJoin.interactable = true;
    }

    private void OnBridgeHostChanged(HostChangedPayload data)
    {
        bool amIHost = data.newHostPlayerId == NetworkConfig.PlayerId;
        if (btnStartGame != null)
            btnStartGame.gameObject.SetActive(amIHost);
        UpdatePlayerList();
    }

    private void OnBridgeGameStarted(GameStartedPayload data)
    {
        _currentStatus = LobbyStatus.LoadingGame;
        if (txtStatus != null) txtStatus.text = LocalizationManager.Get("loading_game");

        if (sfxSource != null && gameStartSound != null)
            sfxSource.PlayOneShot(gameStartSound);

        SceneManager.LoadScene(gameSceneName);
    }

    private void OnBridgeCountdownTick(CountdownTickPayload data)
    {
        if (txtCountdown != null)
            txtCountdown.text = string.Format(LocalizationManager.Get("starting_in"), data.secondsRemaining);
    }

    private void OnBridgeDisconnected()
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
        if (panelCreateChoice != null)
            panelCreateChoice.SetActive(true);
    }

    private void OnStartGameClicked()
    {
        PlayClick();

        if (!_bridge.IsHost)
        {
            Debug.LogWarning("[OnStartGameClicked] Only host can start game!");
            return;
        }

        int playerCount = _bridge.PlayerCount;
        if (playerCount < minPlayersToStart)
        {
            Debug.LogWarning($"[OnStartGameClicked] Need {minPlayersToStart} players, have {playerCount}");
            return;
        }

        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null;
        }

        Debug.Log("[OnStartGameClicked] Host manually starting game!");
        _bridge.StartGame();
    }

    private void OnSpectateClicked()
    {
        PlayClick();
        _localIsSpectating = !_localIsSpectating;
        _bridge.SetSpectator(_localIsSpectating);
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
            _currentStatus = LobbyStatus.Searching;
            txtStatus.text = LocalizationManager.Get("searching");
            if (btnCreate != null) btnCreate.interactable = false;
            if (btnJoin != null) btnJoin.interactable = false;
            _bridge.JoinRandom();
        }
        else
        {
            JoinRoomByCode(code);
        }
    }

    private void OnBackClicked()
    {
        PlayClick();

        if (_bridge != null && _bridge.IsInRoom)
            _bridge.LeaveRoom(true);

        if (_bridge != null)
            _bridge.Disconnect();

        SceneManager.LoadScene("MainMenu");
    }

    private void OnShareClicked()
    {
        PlayClick();

        if (string.IsNullOrEmpty(_currentRoomCode)) return;

        string shareUrl = $"https://AktasFurkann.github.io/rollmateslink/?code={_currentRoomCode}";
        string message = LocalizationManager.CurrentLanguage == LocalizationManager.Language.Turkish
            ? $"Rollmates'te benimle oyna! Odama katıl: {shareUrl}"
            : $"Play with me on Rollmates! Join my room: {shareUrl}";

        GUIUtility.systemCopyBuffer = shareUrl;
        _currentStatus = LobbyStatus.InviteCopied;
        txtStatus.text = LocalizationManager.Get("invite_copied");
        StartCoroutine(ResetStatusAfterDelay(2f));

#if !UNITY_EDITOR
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
        if (_bridge == null || !_bridge.IsConnected) return;
        CheckAndConsumeDeepLink();
    }

    // ==================== GAME LOGIC ====================

    private void CreateRoom(bool isPrivate)
    {
        if (btnCreate != null) btnCreate.interactable = false;
        if (btnJoin != null) btnJoin.interactable = false;

        _bridge.CreateRoom(isPrivate);
    }

    private void JoinRoomByCode(string roomCode)
    {
        txtStatus.text = $"Oda aranıyor: {roomCode}";
        if (btnCreate != null) btnCreate.interactable = false;
        if (btnJoin != null) btnJoin.interactable = false;

        _bridge.JoinRoom(roomCode);
    }

    private IEnumerator CountdownAndStart()
    {
        float timeRemaining = countdownDuration;

        while (timeRemaining > 0)
        {
            if (txtCountdown != null)
                txtCountdown.text = string.Format(LocalizationManager.Get("starting_in"), Mathf.CeilToInt(timeRemaining));

            UpdatePlayerList();

            yield return new WaitForSeconds(1f);
            timeRemaining -= 1f;
        }

        int finalPlayerCount = _bridge.PlayerCount;

        if (finalPlayerCount >= minPlayersToStart)
        {
            _bridge.StartGame();
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

    // ==================== UI HELPERS ====================

    private void UpdatePlayerList()
    {
        if (_bridge == null || !_bridge.IsInRoom) return;

        int playerCount = _bridge.PlayerCount;

        if (txtPlayerCount != null)
            txtPlayerCount.text = $"{playerCount} / {maxPlayers}";

        HideAllPlayerSlots();

        var players = _bridge.GetPlayers();
        int index = 0;
        foreach (var player in players)
        {
            if (index >= 4) break;
            if (player.isSpectator) continue;

            if (playerSlotObjects != null && index < playerSlotObjects.Length)
                playerSlotObjects[index].SetActive(true);

            if (playerSlotImages != null && index < playerSlotImages.Length)
                playerSlotImages[index].color = _playerColors[index];

            if (playerSlotNames != null && index < playerSlotNames.Length)
            {
                string playerName = player.nickname;
                if (string.IsNullOrEmpty(playerName))
                    playerName = $"Oyuncu {index + 1}";

                string suffix = player.isHost ? " (Host)" : "";
                playerSlotNames[index].text = $"{LocalizationManager.GetColorName(index)}: {playerName}{suffix}";
            }

            index++;
        }

        if (btnStartGame != null && _bridge.IsHost)
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
            txtRoomCode.text = string.IsNullOrWhiteSpace(code) ? "" : string.Format(LocalizationManager.Get("room_label"), code);
    }

    private IEnumerator ResetStatusAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (_bridge != null && _bridge.IsInRoom)
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
