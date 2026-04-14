using System.Collections;
using System.Collections.Generic;
using System.Linq; // Bug 2 fix: LINQ for deduplication cleanup
using LudoFriends.Core;
using LudoFriends.Gameplay;
using LudoFriends.Presentation;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using LudoFriends.Networking;
using LudoFriends.Services;

public class GameBootstrapper : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button btnRestart;
    [SerializeField] private Button btnRollDice;
    [SerializeField] private HudView hudView;
    [SerializeField] private GameObject winnerPanel;
    [SerializeField] private TMPro.TextMeshProUGUI txtWinner;

    [Header("Scoreboard")]
    [SerializeField] private GameObject scoreboardPanel;
    [SerializeField] private TMPro.TextMeshProUGUI txtScoreboardTitle;
    [SerializeField] private TMPro.TextMeshProUGUI[] scoreboardTexts; // 4 elemanli
    [SerializeField] private Button btnScoreboardClose;   // X butonu
    [SerializeField] private Button btnMainMenu;          // Ana Menu butonu

    [Header("Disconnect UI")]
    [SerializeField] private GameObject panelDisconnect;
    [SerializeField] private TMPro.TextMeshProUGUI txtDisconnectMessage;
    [SerializeField] private TMPro.TextMeshProUGUI txtDisconnectCountdown;
    [SerializeField] private Button btnDisconnectMainMenu;
    [SerializeField] private Button btnReconnect;

    [Header("Bot Mode UI")]
    [SerializeField] private Button btnTakeControl;

    [Header("Room Info")]
    [SerializeField] private TMPro.TextMeshProUGUI txtInGameRoomCode;

    private Coroutine _reconnectCoroutine;

    [Header("Home Click Areas")]
    [SerializeField] private HomeAreaClick homeClickRed;
    [SerializeField] private HomeAreaClick homeClickGreen;
    [SerializeField] private HomeAreaClick homeClickYellow;
    [SerializeField] private HomeAreaClick homeClickBlue;

    [Header("Board Click Area")]
    [SerializeField] private BoardAreaClick boardAreaClick;

    [Header("Board")]
    [SerializeField] private BoardWaypoints boardWaypoints;
    [SerializeField] private HomeSlots homeSlots;

    [Header("Networking")]
    private IGameNetwork _net;
    private SocketIONetworkBridge _bridge;

    [Header("Gameplay")]
    [SerializeField] private PawnSpawner pawnSpawner;
    [SerializeField] private PawnMover pawnMover;

    private bool _isRollingDice = false;
    private bool _isAnimating = false;
    private bool _localRollPending = false; // Kendi roll broadcast'imiz sunucudan donene kadar true
    private Coroutine _animationSafetyTimer;
    [SerializeField] private float diceRollDuration = 0.5f;
    [SerializeField] private float diceTickInterval = 0.12f;

    [Header("Positioning")]
    [SerializeField] private PawnPositionManager positionManager;

    // Pawn state'ine ek bilgi: hangi waypoint'te
    private readonly Dictionary<PawnView, int> _pawnCurrentWaypoint = new Dictionary<PawnView, int>();

    private GameState _state;
    private DiceService _dice;

    private int PlayerCount
    {
        get
        {
            if (_bridge != null && _bridge.IsInRoom)
                return _bridge.PlayerCount;
            else
                return 4; // Offline mod: 4 oyuncu
        }
    }
    private enum TurnPhase { AwaitRoll, AwaitMove }
    private TurnPhase _phase = TurnPhase.AwaitRoll;

    private int _currentRoll = -1;
    private int _extraTurnsEarned = 0;
    private int _consecutiveSixes = 0;
    private List<PawnView> _redPawns, _yellowPawns, _bluePawns, _greenPawns;
    private readonly Dictionary<PawnView, PawnState> _pawnStates = new Dictionary<PawnView, PawnState>();
    [SerializeField] private LudoFriends.Core.SafeSquares safeSquares;

    private readonly Dictionary<PawnView, int> _pawnOwner = new Dictionary<PawnView, int>();
    private bool _gameOver = false;
    private bool _isLeavingToMainMenu = false;
    private bool _isIntentionalDisconnect = false;
    private bool _localBotMode = false;
    private bool _isSpectator = false;
    private readonly List<int> _finishOrder = new List<int>();
    private readonly HashSet<int> _disconnectedPlayers = new HashSet<int>();
    private readonly HashSet<int> _tempDisconnectedPlayers = new HashSet<int>();
    private readonly HashSet<int> _botPlayers = new HashSet<int>();
    private readonly HashSet<int> _lobbyBots = new HashSet<int>();
    private bool _isBotGame = false;
    private readonly Dictionary<int, string> _cachedPlayerNames = new Dictionary<int, string>();
    private Coroutine _botTurnCoroutine;
    private const float BotAutoDelay = 1.5f;
    private bool _waitingForReconnectState = false;
    private bool _gamePaused = false;
    private Coroutine _pauseCountdownCoroutine;

    [Header("Pawn Sprites")]
    [SerializeField] private Sprite redPawnSprite;
    [SerializeField] private Sprite greenPawnSprite;
    [SerializeField] private Sprite yellowPawnSprite;
    [SerializeField] private Sprite bluePawnSprite;

    [Header("Audio")]
    [SerializeField] private SfxPlayer sfx;

    [Header("Chat")]
    [SerializeField] private ChatView chatView;
    [SerializeField] private QuickChatView quickChatView;

    [Header("Board Rotation")]
    [SerializeField] private BoardRotator boardRotator;

    [Header("Turn Timer")]
    [SerializeField] private float rollTimeLimit = 15f;
    [SerializeField] private float moveTimeLimit = 10f;
    private float _turnTimer = 0f;
    private bool _timerActive = false;
    private bool _clockPlayed = false;
    private Coroutine _timerDelayCoroutine; // StartTimerAfterDelay coroutine ref

    private string TurnName(int index) => LocalizationManager.GetColorName(index);

    /// <summary>
    /// Oyuncu adını döndürür: nickname varsa nickname, yoksa renk adı.
    /// </summary>
    private string PlayerDisplayName(int playerIndex)
    {
        if (_lobbyBots.Contains(playerIndex))
            return $"Bot {playerIndex}";

        // Önce canlı listeden dene
        if (_bridge != null && _bridge.IsInRoom)
        {
            var players = _bridge.GetPlayers();
            if (players != null)
            {
                foreach (var p in players)
                {
                    if (p.playerIndex == playerIndex && !string.IsNullOrEmpty(p.nickname))
                    {
                        _cachedPlayerNames[playerIndex] = p.nickname; // Önbelleğe al
                        return p.nickname;
                    }
                }
            }
        }

        // Oyuncu çıkmışsa önbellekten al
        if (_cachedPlayerNames.TryGetValue(playerIndex, out string cachedName))
            return cachedName;

        return TurnName(playerIndex);
    }

    private enum DisconnectStatus { None, Disconnected, Reconnecting, CouldNotConnect, Connecting, ReconnectFailed }
    private DisconnectStatus _disconnectStatus = DisconnectStatus.None;
    private float _reconnectTimeLeft = 0f;

    private readonly Dictionary<int, PawnView> _idToPawn = new Dictionary<int, PawnView>();
    private readonly Dictionary<PawnView, int> _pawnToId = new Dictionary<PawnView, int>();
    private int _nextPawnId = 1;

    // Bug 2 & 3 fixes: Move deduplication and rapid click protection
    private int _nextMoveId = 0;
    private readonly Dictionary<int, bool> _processedMoves = new Dictionary<int, bool>();
    private int _lastProcessedPawnId = -1;
    private float _lastMoveRequestTime = -999f;
    private const float MIN_MOVE_REQUEST_INTERVAL = 0.5f; // 500ms cooldown

    private int RegisterPawnId(PawnView pawn)
    {
        int id = _nextPawnId++;
        _idToPawn[id] = pawn;
        _pawnToId[pawn] = id;
        return id;
    }

    private bool _paused;

    // Her oyuncunun hangi player index'i oldugunu tutan map
    private int _localPlayerIndex = -1;
    private int _initialPlayerCount;

    private void Awake()
    {
        _state = new GameState();
        _dice = new DiceService();

        // ── Bot game mode detection ──
        if (BotGameConfig.IsActive)
        {
            _isBotGame = true;
            _bridge = null;

            // Create offline loopback bridge
            var offlineGo = new GameObject("OfflineNetworkBridge");
            _net = offlineGo.AddComponent<OfflineNetworkBridge>();

            _localPlayerIndex = 0;
            _initialPlayerCount = BotGameConfig.TotalPlayers;

            for (int i = 1; i < BotGameConfig.TotalPlayers; i++)
                _lobbyBots.Add(i);

            BotGameConfig.Reset();

            Debug.Log($"[GameBootstrapper] Bot game: {_initialPlayerCount} players, bots: {string.Join(",", _lobbyBots)}");
        }
        else
        {
            _bridge = SocketIONetworkBridge.Instance;
            _net = _bridge;
        }

        if (_net != null)
        {
            _net.OnRoll -= OnNetworkRoll;
            _net.OnMove -= OnNetworkMove;
            _net.OnTurn -= OnNetworkTurn;
            _net.OnMoveRequest -= OnNetworkMoveRequest;
            _net.OnRequestAdvanceTurn -= OnNetworkRequestAdvanceTurn;
            _net.OnChatMessage -= OnNetworkChatMessage;

            _net.OnRoll += OnNetworkRoll;
            _net.OnMove += OnNetworkMove;
            _net.OnTurn += OnNetworkTurn;
            _net.OnMoveRequest += OnNetworkMoveRequest;
            _net.OnRequestAdvanceTurn += OnNetworkRequestAdvanceTurn;
            _net.OnChatMessage += OnNetworkChatMessage;
        }

        // Subscribe to bridge-specific events
        if (_bridge != null)
        {
            _bridge.OnHostChanged += OnHostChanged;
            _bridge.OnDisconnectedEvent += OnBridgeDisconnected;
            _bridge.OnPlayerLeft += OnBridgePlayerLeft;
            _bridge.OnPlayerJoined += OnBridgePlayerJoined;
            _bridge.OnExitBot += OnNetworkExitBot;
            _bridge.OnEnterBot += OnNetworkEnterBot;
            _bridge.OnServerTimerExpired += OnServerTimerExpired;
            _bridge.OnServerTimerExpiredDisconnected += OnServerTimerExpiredDisconnected;
            _bridge.OnIdentified += OnBridgeIdentifiedInGame;
            _bridge.OnJoinedRoom += OnBridgeJoinedRoomInGame;
            _bridge.OnGameStateReceived += OnGameStateReceivedForReconnect;
            _bridge.OnGamePaused += OnGamePaused;
            _bridge.OnGameResumed += OnGameResumed;
            _bridge.OnPlayerPermanentlyLeft += OnPlayerPermanentlyLeft;
        }

        // Player index + spectator tespiti + initialPlayerCount
        if (_isBotGame)
        {
            // Already set in bot mode detection block above
        }
        else if (_bridge != null && _bridge.IsInRoom)
        {
            _isSpectator = _bridge.IsSpectator;
            _localPlayerIndex = _bridge.LocalPlayerIndex;
            _initialPlayerCount = _bridge.PlayerCount;
            if (_isSpectator)
                Debug.Log("[GameBootstrapper] Spectator mode");
            else
                Debug.Log($"[GameBootstrapper] PlayerIndex={_localPlayerIndex}, Color={TurnName(_localPlayerIndex)}");
        }
        else
        {
            _localPlayerIndex = 0;
            _initialPlayerCount = PlayerCount;
            Debug.Log("[GameBootstrapper] Offline mode");
        }

        // Tahta rotasyonu (pozisyon cache'lemeden ONCE)
        if (boardRotator != null && _localPlayerIndex > 0)
        {
            boardRotator.ApplyRotation(_localPlayerIndex);
            Canvas.ForceUpdateCanvases();
            Debug.Log($"[GameBootstrapper] Board rotated {_localPlayerIndex * 90f} for player {TurnName(_localPlayerIndex)}");
        }

        // Waypoint pozisyonlarini onbellege al (artik dondurumus pozisyonlari okur)
        if (positionManager != null)
        {
            positionManager.CacheWaypointPositions(boardWaypoints.MainPath);
            positionManager.CacheHomeLanePositions(0, boardWaypoints.HomeR);
            positionManager.CacheHomeLanePositions(1, boardWaypoints.HomeY); // 1 = Yellow
            positionManager.CacheHomeLanePositions(2, boardWaypoints.HomeG); // 2 = Green
            positionManager.CacheHomeLanePositions(3, boardWaypoints.HomeB);
        }

        // Oda kodunu goster
        if (txtInGameRoomCode != null && _bridge != null && _bridge.IsInRoom)
            txtInGameRoomCode.text = string.Format(LocalizationManager.Get("room_code_label"), _bridge.RoomCode);

        hudView.SetTurn(PlayerDisplayName(_state.CurrentTurnPlayerIndex), _state.CurrentTurnPlayerIndex, _localPlayerIndex);
        hudView.SetDice(-1);

        // Oyuncu kose panellerini kur
        SetupPlayerCornerPanels();

        if (chatView != null)
            chatView.Init(_localPlayerIndex, OnChatSend);

        if (quickChatView != null)
            quickChatView.Init(_localPlayerIndex, OnChatSend, OnLocalEmojiSend);

        pawnSpawner.enabled = true;

        _redPawns = pawnSpawner.SpawnColor(homeSlots.R, redPawnSprite, Color.white);
        _greenPawns = pawnSpawner.SpawnColor(homeSlots.G, greenPawnSprite, Color.white);
        _yellowPawns = pawnSpawner.SpawnColor(homeSlots.Y, yellowPawnSprite, Color.white);
        _bluePawns = pawnSpawner.SpawnColor(homeSlots.B, bluePawnSprite, Color.white);

        // Piyon sprite'larini ters dondur (dik kalsinlar)
        if (_localPlayerIndex > 0)
        {
            Quaternion counterRot = BoardRotator.GetCounterRotation(_localPlayerIndex);
            CounterRotatePawns(_redPawns, counterRot);
            CounterRotatePawns(_greenPawns, counterRot);
            CounterRotatePawns(_yellowPawns, counterRot);
            CounterRotatePawns(_bluePawns, counterRot);
        }

        RegisterPawns(_redPawns, 0);
        RegisterPawns(_yellowPawns, 1); // 1 = Yellow
        RegisterPawns(_greenPawns, 2); // 2 = Green
        RegisterPawns(_bluePawns, 3);

        HideUnusedColorPawns();

        if (winnerPanel != null)
            winnerPanel.SetActive(false);

        if (panelDisconnect != null)
            panelDisconnect.SetActive(false);

        if (btnDisconnectMainMenu != null)
            btnDisconnectMainMenu.onClick.AddListener(OnMainMenuClicked);

        if (btnReconnect != null)
        {
            btnReconnect.onClick.AddListener(OnReconnectClicked);
            btnReconnect.gameObject.SetActive(false);
        }

        if (btnTakeControl != null)
        {
            btnTakeControl.onClick.AddListener(OnTakeControlClicked);
            btnTakeControl.gameObject.SetActive(false);
        }

        InitScoreboard();

        if (btnRestart != null)
            btnRestart.onClick.AddListener(OnRestartClicked);

        foreach (var kv in _pawnStates)
            kv.Key.Clicked += OnPawnClicked;

        homeClickRed?.Init(0, OnHomeAreaClicked);
        homeClickYellow?.Init(1, OnHomeAreaClicked); // 1 = Yellow
        homeClickGreen?.Init(2, OnHomeAreaClicked); // 2 = Green
        homeClickBlue?.Init(3, OnHomeAreaClicked);
        boardAreaClick?.Init(OnBoardAreaClicked);

        btnRollDice.onClick.AddListener(OnRollDiceClicked);

        if (btnRollDice != null)
        {
            if (_bridge == null || !_bridge.IsInRoom)
            {
                // Offline mod: hemen etkinlestir
                bool isMyTurn = (_state.CurrentTurnPlayerIndex == _localPlayerIndex);
                btnRollDice.interactable = isMyTurn && !_gameOver;
                Debug.Log($"[Awake] Offline. FirstTurn={_state.CurrentTurnPlayerIndex}, MyTurn={isMyTurn}");
            }
            else if (_isSpectator)
            {
                // Spectator kesinlesti -> gizle
                btnRollDice.interactable = false;
                Debug.Log("[Awake] Spectator: dice button passive (visible but non-interactive).");
            }
            else
            {
                // Online oyuncu: siramizsa aktif, degilse pasif.
                bool isMyTurn = _state.CurrentTurnPlayerIndex == _localPlayerIndex;
                btnRollDice.interactable = isMyTurn && !_gameOver;
                Debug.Log($"[Awake] Online player: PlayerIndex={_localPlayerIndex}, MyTurn={isMyTurn}, BtnInteractable={isMyTurn && !_gameOver}");
            }
        }

        HighlightActivePlayerPawns();

        // Reconnect kontrolu: sahne yuklendiginde zaten odadaysak state'i sunucudan iste
        if (_bridge != null && _bridge.IsInRoom)
        {
            _waitingForReconnectState = true;
            _bridge.RequestGameState();
            // Restore gelene kadar timer/ses baslatma
            return;
        }

        // Oyun baslama sesi
        sfx?.PlayGameStart();

        // Ilk sira icin timer baslat -- spectator icin baslatma
        // Bot game'de ilk sira her zaman insan (index 0), timer normal baslar
        if (!_isSpectator)
            StartTurnTimer(rollTimeLimit);
    }

    // ========== TIMER NETWORK EVENT SUBSCRIPTIONS (Fix 1) ==========

    private void OnEnable()
    {
        if (_net != null)
        {
            _net.OnTimerStart += OnNetworkTimerStart;
            _net.OnTimerStop += OnNetworkTimerStop;
        }

        LocalizationManager.OnLanguageChanged += RefreshLocalization;
    }

    private void OnDisable()
    {
        if (_net != null)
        {
            _net.OnTimerStart -= OnNetworkTimerStart;
            _net.OnTimerStop -= OnNetworkTimerStop;
        }

        LocalizationManager.OnLanguageChanged -= RefreshLocalization;
    }

    // YENi metod
    private void OnNetworkRequestAdvanceTurn()
    {
        Debug.Log("[OnNetworkRequestAdvanceTurn] Received from client");

        // Extra turn varsa ayni oyuncu devam
        if (_extraTurnsEarned > 0)
        {
            _extraTurnsEarned--;
            Debug.Log($"[OnNetworkRequestAdvanceTurn] Extra turn! Remaining: {_extraTurnsEarned}");
            _net.BroadcastTurn(_state.CurrentTurnPlayerIndex);
            return;
        }

        AdvanceTurnInternalOnly();
    }

    private void InitializeGame()
    {
        // Event subscriptions are already done in Awake (with -= then +=).
        // Do NOT re-subscribe here — it causes double handler invocations.

        // Player index ve spectator -- InitializeGame 0.5s sonra calisir, property'ler artik kesinlikle sync olmustur.
        if (_isBotGame)
        {
            // Already set in Awake — do not override
            Debug.Log($"[GameBootstrapper] InitializeGame: Bot game, {_initialPlayerCount} players");
        }
        else if (_bridge != null && _bridge.IsInRoom)
        {
            _isSpectator = _bridge.IsSpectator;
            _localPlayerIndex = _bridge.LocalPlayerIndex;
            _initialPlayerCount = _bridge.PlayerCount;
            if (_isSpectator)
                Debug.Log("[GameBootstrapper] InitializeGame: Spectator mode");
            else
                Debug.Log($"[GameBootstrapper] InitializeGame: PlayerIndex={_localPlayerIndex}, Color={TurnName(_localPlayerIndex)}");
        }
        else
        {
            _localPlayerIndex = 0;
            Debug.Log("[GameBootstrapper] InitializeGame: Offline mode");
        }

        // Zar butonu: spectator tespit kesinlesti, butonu dogru state'e getir
        if (btnRollDice != null)
        {
            if (_isSpectator)
            {
                btnRollDice.interactable = false;
                Debug.Log("[InitializeGame] Spectator confirmed: dice button passive.");
            }
            else if (_bridge != null && _bridge.IsInRoom)
            {
                // Online oyuncu -- sira kontrolune gore enable et
                bool isMyTurn = (_state.CurrentTurnPlayerIndex == _localPlayerIndex);
                btnRollDice.interactable = isMyTurn && !_gameOver;
                Debug.Log($"[InitializeGame] Player confirmed. PlayerIndex={_localPlayerIndex}, MyTurn={isMyTurn}, BtnInteractable={btnRollDice.interactable}");
            }
        }

        // Tahta rotasyonu (pozisyon cache'lemeden ONCE)
        if (boardRotator != null && _localPlayerIndex > 0)
        {
            boardRotator.ApplyRotation(_localPlayerIndex);
            Canvas.ForceUpdateCanvases();
            Debug.Log($"[InitializeGame] Board rotated {_localPlayerIndex * 90f} for player {TurnName(_localPlayerIndex)}");
        }

        // Pozisyonlari yeniden cache'le (dondurumus haliyle)
        if (positionManager != null)
        {
            positionManager.CacheWaypointPositions(boardWaypoints.MainPath);
            positionManager.CacheHomeLanePositions(0, boardWaypoints.HomeR);
            positionManager.CacheHomeLanePositions(1, boardWaypoints.HomeY); // 1 = Yellow
            positionManager.CacheHomeLanePositions(2, boardWaypoints.HomeG); // 2 = Green
            positionManager.CacheHomeLanePositions(3, boardWaypoints.HomeB);
        }

        _initialPlayerCount = PlayerCount;

        hudView.SetTurn(PlayerDisplayName(_state.CurrentTurnPlayerIndex), _state.CurrentTurnPlayerIndex, _localPlayerIndex);
        hudView.SetDice(-1);

        pawnSpawner.enabled = true;

        _redPawns = pawnSpawner.SpawnColor(homeSlots.R, redPawnSprite, Color.white);
        _greenPawns = pawnSpawner.SpawnColor(homeSlots.G, greenPawnSprite, Color.white);
        _yellowPawns = pawnSpawner.SpawnColor(homeSlots.Y, yellowPawnSprite, Color.white);
        _bluePawns = pawnSpawner.SpawnColor(homeSlots.B, bluePawnSprite, Color.white);

        // Piyon sprite'larini ters dondur
        if (_localPlayerIndex > 0)
        {
            Quaternion counterRot = BoardRotator.GetCounterRotation(_localPlayerIndex);
            CounterRotatePawns(_redPawns, counterRot);
            CounterRotatePawns(_greenPawns, counterRot);
            CounterRotatePawns(_yellowPawns, counterRot);
            CounterRotatePawns(_bluePawns, counterRot);
        }

        RegisterPawns(_redPawns, 0);
        RegisterPawns(_yellowPawns, 1); // 1 = Yellow
        RegisterPawns(_greenPawns, 2); // 2 = Green
        RegisterPawns(_bluePawns, 3);

        HideUnusedColorPawns();

        if (winnerPanel != null)
            winnerPanel.SetActive(false);

        InitScoreboard();

        // btnRestart, pawn click & dice button subscriptions are already done in Start().
        // Do NOT re-subscribe here — double handlers cause duplicate processing.

        UpdateTurnUI();
        HighlightActivePlayerPawns();
    }

    private IEnumerator WaitForNetworkRoot()
    {
        // Host'un NetworkRoot'u spawn etmesini bekle
        yield return new WaitForSeconds(0.5f);

        _bridge = SocketIONetworkBridge.Instance;
        if (_bridge == null)
        {
            Debug.LogError("[GameBootstrapper] SocketIONetworkBridge not found!");
            yield break;
        }
        _net = _bridge;

        // Ensure event subscriptions exist (safe -= then +=)
        _net.OnRoll -= OnNetworkRoll;
        _net.OnMove -= OnNetworkMove;
        _net.OnTurn -= OnNetworkTurn;
        _net.OnMoveRequest -= OnNetworkMoveRequest;
        _net.OnRequestAdvanceTurn -= OnNetworkRequestAdvanceTurn;
        _net.OnChatMessage -= OnNetworkChatMessage;

        _net.OnRoll += OnNetworkRoll;
        _net.OnMove += OnNetworkMove;
        _net.OnTurn += OnNetworkTurn;
        _net.OnMoveRequest += OnNetworkMoveRequest;
        _net.OnRequestAdvanceTurn += OnNetworkRequestAdvanceTurn;
        _net.OnChatMessage += OnNetworkChatMessage;

        InitializeGame();
    }

    private void RefreshLocalization()
    {
        // HUD turn label
        if (hudView != null && _state != null)
            hudView.SetTurn(PlayerDisplayName(_state.CurrentTurnPlayerIndex), _state.CurrentTurnPlayerIndex, _localPlayerIndex);

        // Scoreboard title
        UpdateScoreboard();

        // Disconnect panel
        if (panelDisconnect != null && panelDisconnect.activeSelf)
        {
            switch (_disconnectStatus)
            {
                case DisconnectStatus.Disconnected:
                    if (txtDisconnectMessage != null)
                        txtDisconnectMessage.text = LocalizationManager.Get("disconnected");
                    break;
                case DisconnectStatus.Reconnecting:
                    if (txtDisconnectCountdown != null)
                        txtDisconnectCountdown.text = string.Format(LocalizationManager.Get("reconnecting"), Mathf.CeilToInt(_reconnectTimeLeft));
                    break;
                case DisconnectStatus.CouldNotConnect:
                    if (txtDisconnectCountdown != null)
                        txtDisconnectCountdown.text = LocalizationManager.Get("could_not_connect");
                    break;
                case DisconnectStatus.Connecting:
                    if (txtDisconnectCountdown != null)
                        txtDisconnectCountdown.text = LocalizationManager.Get("connecting_dots");
                    break;
                case DisconnectStatus.ReconnectFailed:
                    if (txtDisconnectCountdown != null)
                        txtDisconnectCountdown.text = LocalizationManager.Get("reconnect_failed");
                    break;
            }
        }

        // Corner panels (color names)
        SetupPlayerCornerPanels();
    }

    // UpdateTurnUI artik sadece local operasyonlarda kullanilacak
    private void SetupPlayerCornerPanels()
    {
        string[] cornerNames = new string[4];

        if (_bridge != null && _bridge.IsInRoom)
        {
            // Onca hepsini renk adiyla doldur (bos kalmasin)
            for (int i = 0; i < 4; i++)
                cornerNames[i] = TurnName(i);

            // Bridge'deki gercek NickName varsa uzerine yaz
            var players = _bridge.GetPlayers();
            if (players != null)
            {
                foreach (var player in players)
                {
                    int idx = player.playerIndex;
                    if (idx >= 0 && idx < 4)
                    {
                        string nick = player.nickname;
                        if (!string.IsNullOrEmpty(nick))
                            cornerNames[idx] = nick;
                    }
                }
            }
        }
        else
        {
            // Offline / bot mod: renk adlari + bot isimleri
            for (int i = 0; i < 4; i++)
                cornerNames[i] = _lobbyBots.Contains(i) ? $"Bot {i}" : TurnName(i);
        }

        hudView.SetupPlayerCorners(cornerNames, _localPlayerIndex, PlayerCount);
    }

    private void UpdateTurnUI()
    {
        hudView.SetTurn(PlayerDisplayName(_state.CurrentTurnPlayerIndex), _state.CurrentTurnPlayerIndex, _localPlayerIndex);

        if (btnRollDice != null)
            btnRollDice.interactable = !_isSpectator
                && (_state.CurrentTurnPlayerIndex == _localPlayerIndex)
                && !_isRollingDice && !_gameOver && !_isAnimating;
    }

    private void OnDestroy()
    {
        if (_net != null)
        {
            _net.OnRoll -= OnNetworkRoll;
            _net.OnMove -= OnNetworkMove;
            _net.OnTurn -= OnNetworkTurn;
            _net.OnMoveRequest -= OnNetworkMoveRequest;
            _net.OnChatMessage -= OnNetworkChatMessage;
            _net.OnRequestAdvanceTurn -= OnNetworkRequestAdvanceTurn;
            _net.OnTimerStart -= OnNetworkTimerStart;
            _net.OnTimerStop -= OnNetworkTimerStop;
        }

        if (_bridge != null)
        {
            _bridge.OnHostChanged -= OnHostChanged;
            _bridge.OnDisconnectedEvent -= OnBridgeDisconnected;
            _bridge.OnPlayerLeft -= OnBridgePlayerLeft;
            _bridge.OnPlayerJoined -= OnBridgePlayerJoined;
            _bridge.OnExitBot -= OnNetworkExitBot;
            _bridge.OnEnterBot -= OnNetworkEnterBot;
            _bridge.OnServerTimerExpired -= OnServerTimerExpired;
            _bridge.OnServerTimerExpiredDisconnected -= OnServerTimerExpiredDisconnected;
        }

        // Cleanup offline bridge if we created it
        if (_isBotGame && _net != null && _net is OfflineNetworkBridge offlineBridge)
        {
            if (offlineBridge.gameObject != null)
                Destroy(offlineBridge.gameObject);
        }

        if (btnRollDice != null)
            btnRollDice.onClick.RemoveListener(OnRollDiceClicked);

        foreach (var kv in _pawnStates)
            kv.Key.Clicked -= OnPawnClicked;

        if (btnRestart != null)
            btnRestart.onClick.RemoveListener(OnRestartClicked);

        if (btnScoreboardClose != null)
            btnScoreboardClose.onClick.RemoveListener(OnScoreboardClose);

        if (btnMainMenu != null)
            btnMainMenu.onClick.RemoveListener(OnMainMenuClicked);

        if (btnDisconnectMainMenu != null)
            btnDisconnectMainMenu.onClick.RemoveListener(OnMainMenuClicked);

        if (btnReconnect != null)
            btnReconnect.onClick.RemoveListener(OnReconnectClicked);

        if (btnTakeControl != null)
            btnTakeControl.onClick.RemoveListener(OnTakeControlClicked);

        // Reconnect event unsubscribe
        if (_bridge != null)
        {
            _bridge.OnIdentified -= OnBridgeIdentifiedInGame;
            _bridge.OnJoinedRoom -= OnBridgeJoinedRoomInGame;
            _bridge.OnGameStateReceived -= OnGameStateReceivedForReconnect;
            _bridge.OnGamePaused -= OnGamePaused;
            _bridge.OnGameResumed -= OnGameResumed;
            _bridge.OnPlayerPermanentlyLeft -= OnPlayerPermanentlyLeft;
        }
    }

    // ── Reconnect Handlers ──

    private void OnBridgeIdentifiedInGame(IdentifiedPayload data)
    {
        if (!data.success) return;
        if (string.IsNullOrEmpty(data.reconnectRoomCode)) return;

        Debug.Log($"[Reconnect] Re-joining room {data.reconnectRoomCode}");
        _bridge.JoinRoom(data.reconnectRoomCode);
    }

    private void OnBridgeJoinedRoomInGame(JoinedRoomPayload payload)
    {
        Debug.Log("[Reconnect] Joined room, requesting game state...");
        _waitingForReconnectState = true;
        _bridge.RequestGameState();
    }

    private void OnGameStateReceivedForReconnect(GameStatePayload data)
    {
        if (!_waitingForReconnectState) return;
        _waitingForReconnectState = false;

        // pawnStates bos ise yeni oyun - normal baslangic yap
        if (string.IsNullOrEmpty(data.pawnStates))
        {
            Debug.Log("[Reconnect] No pawn states (fresh game), starting normally...");
            if (sfx != null) sfx.PlayGameStart();
            if (!_isSpectator)
                StartTurnTimer(rollTimeLimit);
            return;
        }

        Debug.Log("[Reconnect] Game state received, restoring...");
        OnJoinedRoom();
    }

    private void OnJoinedRoom()
    {
        // Reconnect sonrasi: disconnect panelini kapat ve oyunu devam ettir
        if (_reconnectCoroutine != null)
        {
            StopCoroutine(_reconnectCoroutine);
            _reconnectCoroutine = null;
        }
        if (panelDisconnect != null) panelDisconnect.SetActive(false);
        if (btnReconnect != null)
        {
            btnReconnect.gameObject.SetActive(false);
            btnReconnect.interactable = true; // Sonraki disconnect icin hazir tut
        }

        // Spectator tespiti
        if (!_isSpectator && _bridge != null && _bridge.IsInRoom)
        {
            bool shouldBeSpectator = _bridge.IsSpectator;
            _initialPlayerCount = _bridge.PlayerCount;

            if (shouldBeSpectator)
            {
                _isSpectator = true;
                _localPlayerIndex = -1;
                if (btnRollDice != null) btnRollDice.interactable = false;
                Debug.Log($"[OnJoinedRoom] Spectator confirmed (timing fix).");
                // Spectator da state restore yapacak (asagida devam ediyor)
            }
        }
        else if (_isSpectator)
        {
            // Zaten spectator -- butonu gizli tut
            if (btnRollDice != null) btnRollDice.interactable = false;
        }

        _gameOver = false;

        // Only restore state for non-host players (host already has correct state)
        if (_bridge != null && _bridge.IsHost) return;

        // Try to restore game state from room properties
        if (_net != null && _net.TryGetGameState(
            out int turn, out int roll, out int phase, out int sixes, out int extraTurns))
        {
            Debug.Log($"[OnJoinedRoom] Restoring state: Turn={turn}, Roll={roll}, Phase={phase}");

            _state.CurrentTurnPlayerIndex = turn;
            _currentRoll = roll;
            _phase = (TurnPhase)phase;
            _consecutiveSixes = sixes;
            _extraTurnsEarned = extraTurns;

            // Update UI
            if (hudView != null)
            {
                hudView.SetTurn(PlayerDisplayName(turn), turn, _localPlayerIndex);
                if (roll > 0)
                    hudView.SetDice(roll);
                else
                    hudView.SetDice(-1);
            }

            // Update button state
            if (btnRollDice != null)
            {
                bool isMyTurn = (turn == _localPlayerIndex);
                btnRollDice.interactable = !_isSpectator && isMyTurn && _phase == TurnPhase.AwaitRoll && !_gameOver;
            }

            // Restore pawn states
            RestorePawnStatesFromNetwork();

            // Restore finish order (scoreboard)
            var savedFinishOrder = _net.GetFinishOrder();
            if (savedFinishOrder != null && savedFinishOrder.Length > 0)
            {
                _finishOrder.Clear();
                _finishOrder.AddRange(savedFinishOrder);

                if (_finishOrder.Count >= PlayerCount)
                    _gameOver = true;

                UpdateScoreboard();
            }

            // Restart timer if needed
            if (turn == _localPlayerIndex)
            {
                if (_phase == TurnPhase.AwaitRoll)
                    StartTurnTimer(rollTimeLimit);
                else if (_phase == TurnPhase.AwaitMove)
                {
                    // Calculate remaining time from persisted state
                    float timerDuration = moveTimeLimit;

                    if (_net != null && _net.TryGetTimerState(out double startTime, out float savedDuration))
                    {
                        double elapsed = _bridge.ServerTime - startTime;
                        float remaining = savedDuration - (float)elapsed;

                        // Add 2-second grace period for reconnection latency
                        remaining += 2f;

                        // Minimum 3 seconds to allow player interaction
                        timerDuration = Mathf.Max(3f, remaining);

                        Debug.Log($"[OnJoinedRoom] Calculated remaining time: {timerDuration:F1}s (elapsed: {elapsed:F1}s)");
                    }

                    StartTurnTimer(timerDuration);

                    // Highlight legal moves
                    var legal = GetLegalMoves(turn, roll);
                    HighlightLegalMoves(legal);
                    SetOnlyLegalClickable(legal);
                }
            }
        }
    }

    private void OnHostChanged(HostChangedPayload payload)
    {
        Debug.Log($"[OnHostChanged] New host. IsLocal={(_bridge != null && _bridge.IsHost)}");

        if (_gameOver) return;

        // Stuck state'leri temizle
        _isAnimating = false;
        _isRollingDice = false;
        _localRollPending = false;
        _botPlayers.Clear(); // Host migration sonrasi bot listesini sifirla

        // Yeni host ise, host sorumluluklarini devral
        if (_bridge != null && _bridge.IsHost)
        {
            Debug.Log("[OnHostChanged] I am the new host. Taking over responsibilities.");

            // Tum coroutine'leri durdur (eski host'un roll/move animasyonlari)
            StopAllCoroutines();

            // Disconnected oyunculari guncelle (odada olmayanlari bul)
            RefreshDisconnectedPlayers();

            // Phase ve state'i resetle
            _phase = TurnPhase.AwaitRoll;
            _currentRoll = -1;
            _consecutiveSixes = 0;
            _extraTurnsEarned = 0;

            // MoveId cakismasini onle: Yeni host'un moveId'leri
            // eski host'un broadcast ettigi moveId'lerle cakismamali
            _processedMoves.Clear();
            _nextMoveId = 1000 + UnityEngine.Random.Range(0, 1000);
            _lastProcessedPawnId = -1;

            // Mevcut siradaki oyuncu disconnected ise turu ilerlet
            if (_disconnectedPlayers.Contains(_state.CurrentTurnPlayerIndex)
                || _finishOrder.Contains(_state.CurrentTurnPlayerIndex))
            {
                AdvanceTurnAfterHostMigration();
            }
            else
            {
                // Siradaki oyuncu hala aktifse, UI guncelle ve timer baslat
                int currentTurn = _state.CurrentTurnPlayerIndex;
                hudView.SetTurn(PlayerDisplayName(currentTurn), currentTurn, _localPlayerIndex);
                hudView.SetDice(-1);

                if (btnRollDice != null)
                    btnRollDice.interactable = !_isSpectator && (currentTurn == _localPlayerIndex) && !_gameOver;

                HighlightActivePlayerPawns();
                StartTurnTimer(rollTimeLimit);

                // State'i kaydet
                _net?.SyncGameState(currentTurn, -1, (int)TurnPhase.AwaitRoll, 0, 0);
                SerializeAndSavePawnStates();
            }
        }
    }

    private void OnBridgeDisconnected()
    {
        Debug.LogWarning("[GameBootstrapper] Disconnected from server");

        // Kasitli cikis (Exit butonu) -> reconnect baslatma
        if (_isIntentionalDisconnect)
        {
            _isIntentionalDisconnect = false;
            return;
        }

        // Izleyici baglantisi kesildi -> reconnect yok, sadece bilgi goster
        if (_isSpectator)
        {
            if (panelDisconnect != null)
            {
                panelDisconnect.SetActive(true);
                if (txtDisconnectMessage != null)
                {
                    _disconnectStatus = DisconnectStatus.Disconnected;
                    txtDisconnectMessage.text = LocalizationManager.Get("disconnected");
                }
                if (txtDisconnectCountdown != null) txtDisconnectCountdown.text = "";
                if (btnReconnect != null) btnReconnect.gameObject.SetActive(false);
            }
            return; // Reconnect coroutine baslatma
        }

        _timerActive = false;

        if (panelDisconnect != null)
        {
            panelDisconnect.SetActive(true);
            if (txtDisconnectMessage != null)
            {
                _disconnectStatus = DisconnectStatus.Disconnected;
                txtDisconnectMessage.text = LocalizationManager.Get("disconnected");
            }
            if (btnReconnect != null)
            {
                btnReconnect.gameObject.SetActive(true);
                btnReconnect.interactable = true;
            }
        }

        _reconnectCoroutine = StartCoroutine(ReconnectCountdown());
    }

    private IEnumerator ReconnectCountdown()
    {
        // Socket.IO handles reconnection automatically, but we show UI feedback
        float timeLeft = 60f;
        var wait = new WaitForSeconds(1f);

        while (timeLeft > 0)
        {
            if (txtDisconnectCountdown != null)
            {
                _disconnectStatus = DisconnectStatus.Reconnecting;
                _reconnectTimeLeft = timeLeft;
                txtDisconnectCountdown.text = string.Format(LocalizationManager.Get("reconnecting"), Mathf.CeilToInt(timeLeft));
            }

            yield return wait;
            timeLeft -= 1f;
        }

        if (txtDisconnectCountdown != null)
        {
            _disconnectStatus = DisconnectStatus.CouldNotConnect;
            txtDisconnectCountdown.text = LocalizationManager.Get("could_not_connect");
        }
        if (btnReconnect != null)
            btnReconnect.gameObject.SetActive(false);
    }

    private void OnReconnectClicked()
    {
        if (txtDisconnectCountdown != null)
        {
            _disconnectStatus = DisconnectStatus.Connecting;
            txtDisconnectCountdown.text = LocalizationManager.Get("connecting_dots");
        }
        if (btnReconnect != null)
            btnReconnect.interactable = false;

        // Socket yok edilmis olabilir (Disconnect cagrildi) - yeniden baglan
        if (_bridge != null)
        {
            string nickname = PlayerPrefs.GetString("PlayerName", "Player");
            _bridge.Connect(nickname);
        }

        // 5 saniye icinde baglanilmazsa butonu tekrar aktif et
        StartCoroutine(ReconnectButtonTimeout());
    }

    private IEnumerator ReconnectButtonTimeout()
    {
        yield return new WaitForSeconds(5f);
        // Panel hala aciksa baglanti basarisiz olmus demektir
        if (panelDisconnect != null && panelDisconnect.activeSelf && btnReconnect != null)
        {
            btnReconnect.interactable = true;
            if (txtDisconnectCountdown != null)
                txtDisconnectCountdown.text = LocalizationManager.Get("reconnect_failed");
        }
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
            Debug.Log("[GameBootstrapper] App paused (background)");
        else
            Debug.Log("[GameBootstrapper] App resumed (foreground)");
    }

    // Reconnect failure handling (called by bridge events if needed)
    private void OnJoinRoomFailed(string message)
    {
        Debug.LogWarning($"[GameBootstrapper] OnJoinRoomFailed: {message}");

        // Reconnect baglaminda basarisiz: butonu tekrar aktif et
        if (panelDisconnect != null && panelDisconnect.activeSelf)
        {
            if (txtDisconnectCountdown != null)
                _disconnectStatus = DisconnectStatus.ReconnectFailed;
            txtDisconnectCountdown.text = LocalizationManager.Get("reconnect_failed");
            if (btnReconnect != null)
            {
                btnReconnect.gameObject.SetActive(true);
                btnReconnect.interactable = true;
            }
        }
    }

    /// <summary>
    /// Odadaki aktif oyunculari kontrol ederek _disconnectedPlayers set'ini gunceller.
    /// Host migration sonrasi cagrilir.
    /// </summary>
    private void RefreshDisconnectedPlayers()
    {
        HashSet<int> activePlayerIndices = new HashSet<int>();
        var players = _bridge?.GetPlayers();
        if (players != null)
        {
            foreach (var player in players)
            {
                activePlayerIndices.Add(player.playerIndex);
            }
        }

        for (int i = 0; i < _initialPlayerCount; i++)
        {
            if (!activePlayerIndices.Contains(i) && !_disconnectedPlayers.Contains(i)
                && !_tempDisconnectedPlayers.Contains(i))
            {
                _disconnectedPlayers.Add(i);
                if (!_finishOrder.Contains(i))
                {
                    _finishOrder.Add(i);
                }
                RemoveDisconnectedPlayerPawns(i);
                Debug.Log($"[RefreshDisconnectedPlayers] P{i} marked as disconnected");
            }
        }

        // Scoreboard ve finish order kaydet
        UpdateScoreboard();
        if (_bridge != null && _bridge.IsHost)
            _net?.SaveFinishOrder(_finishOrder.ToArray());
    }

    /// <summary>
    /// Host migration sonrasi turu guvenli sekilde ilerletir.
    /// </summary>
    private void AdvanceTurnAfterHostMigration()
    {
        StopTurnTimer();

        _state.NextTurn(_initialPlayerCount);

        int safetyCount = 0;
        while ((_finishOrder.Contains(_state.CurrentTurnPlayerIndex)
                || _tempDisconnectedPlayers.Contains(_state.CurrentTurnPlayerIndex))
               && safetyCount < _initialPlayerCount)
        {
            _state.NextTurn(_initialPlayerCount);
            safetyCount++;
        }

        int nextTurn = _state.CurrentTurnPlayerIndex;
        Debug.Log($"[AdvanceTurnAfterHostMigration] New turn: P{nextTurn}");

        hudView.SetTurn(PlayerDisplayName(nextTurn), nextTurn, _localPlayerIndex);
        hudView.SetDice(-1);

        if (btnRollDice != null)
            btnRollDice.interactable = (nextTurn == _localPlayerIndex) && !_gameOver;

        HighlightActivePlayerPawns();

        // Broadcast ve state kaydet
        if (_net != null)
        {
            _net.BroadcastTurn(nextTurn);
            _net.SyncGameState(nextTurn, -1, (int)TurnPhase.AwaitRoll, 0, 0);
            SerializeAndSavePawnStates();
        }

        StartTurnTimer(rollTimeLimit);
    }

    // Bug 3 fix: Handle player disconnects to prevent lockup
    private void OnBridgePlayerLeft(PlayerLeftPayload payload)
    {
        int actorNumber = payload.playerIndex + 1;

        // Spectator ayrildi -> oyun durumunu hic degistirme
        if (actorNumber > _initialPlayerCount)
        {
            Debug.Log($"[OnBridgePlayerLeft] Spectator {actorNumber} left. No game impact.");
            return;
        }

        int leftPlayerIndex = payload.playerIndex;
        Debug.Log($"[OnBridgePlayerLeft] Player {actorNumber} (Index={leftPlayerIndex}) left. isPermanent={payload.isPermanent}");

        if (_gameOver) return;

        // ── GECICI KOPUS (isPermanent = false) ──
        // Oyun sunucudan game_paused gelince duracak, burada sadece state guncelle
        if (!payload.isPermanent)
        {
            _tempDisconnectedPlayers.Add(leftPlayerIndex);
            _botPlayers.Remove(leftPlayerIndex);
            Debug.Log($"[OnBridgePlayerLeft] P{leftPlayerIndex} temporarily disconnected. Game will pause via server event.");

            // Stuck state'leri temizle
            _isAnimating = false;
            _isRollingDice = false;
            _localRollPending = false;

            return;
        }

        // ── KALICI AYRILMA (isPermanent = true) ──
        // Sunucu player_permanently_left event'i ile ayri bildirecek, burada sadece log
        Debug.Log($"[OnBridgePlayerLeft] P{leftPlayerIndex} permanent leave received. Handled by OnPlayerPermanentlyLeft.");
    }

    /// <summary>
    /// Oyuncu geri baglandi (reconnect). Gecici kopus listesinden cikar.
    /// </summary>
    private void OnBridgePlayerJoined(PlayerJoinedPayload payload)
    {
        int playerIndex = payload.playerIndex;
        if (_tempDisconnectedPlayers.Contains(playerIndex))
        {
            _tempDisconnectedPlayers.Remove(playerIndex);
            Debug.Log($"[OnBridgePlayerJoined] P{playerIndex} ({payload.nickname}) reconnected!");
            UpdateScoreboard();
        }
    }

    // ==================== GAME PAUSE/RESUME (DISCONNECT) ====================

    private void OnGamePaused(GamePausedPayload data)
    {
        if (_gameOver) return;
        _gamePaused = true;
        _paused = true; // Tum input'lari engelle

        Debug.Log($"[OnGamePaused] Game paused - waiting for P{data.disconnectedPlayerIndex} ({data.disconnectedNickname}), timeout={data.timeoutSeconds}s");

        // Timer'i durdur
        StopTurnTimer();

        // Zar ve animasyonlari durdur
        if (btnRollDice != null) btnRollDice.interactable = false;
        ClearAllHighlights();

        // Disconnect panelini goster - bekleme mesaji ile
        if (panelDisconnect != null)
        {
            panelDisconnect.SetActive(true);
            if (txtDisconnectMessage != null)
                txtDisconnectMessage.text = $"{data.disconnectedNickname} {LocalizationManager.Get("disconnected")}";
            if (btnReconnect != null)
                btnReconnect.gameObject.SetActive(false);
            if (btnDisconnectMainMenu != null)
                btnDisconnectMainMenu.gameObject.SetActive(true);
        }

        // Geri sayim baslat
        if (_pauseCountdownCoroutine != null) StopCoroutine(_pauseCountdownCoroutine);
        _pauseCountdownCoroutine = StartCoroutine(PauseCountdown(data.timeoutSeconds));
    }

    private IEnumerator PauseCountdown(int totalSeconds)
    {
        float timeLeft = totalSeconds;
        var wait = new WaitForSeconds(1f);

        while (timeLeft > 0 && _gamePaused)
        {
            if (txtDisconnectCountdown != null)
                txtDisconnectCountdown.text = $"{Mathf.CeilToInt(timeLeft)}s";

            yield return wait;
            timeLeft -= 1f;
        }
    }

    private void OnGameResumed(GameResumedPayload data)
    {
        Debug.Log($"[OnGameResumed] Game resumed - reason={data.reason}, playerIndex={data.playerIndex}");
        _gamePaused = false;
        _paused = false; // Input'lari tekrar ac

        // Countdown coroutine'i durdur
        if (_pauseCountdownCoroutine != null)
        {
            StopCoroutine(_pauseCountdownCoroutine);
            _pauseCountdownCoroutine = null;
        }

        // Disconnect panelini kapat
        if (panelDisconnect != null)
            panelDisconnect.SetActive(false);

        // Oyun devam ediyor - sira kimdeyse timer baslasin
        if (!_gameOver && _bridge != null && _bridge.IsHost)
        {
            float limit = (_phase == TurnPhase.AwaitRoll) ? rollTimeLimit : moveTimeLimit;
            StartTurnTimer(limit);
        }
    }

    private void OnPlayerPermanentlyLeft(PlayerPermanentlyLeftPayload data)
    {
        int leftPlayerIndex = data.playerIndex;
        Debug.Log($"[OnPlayerPermanentlyLeft] P{leftPlayerIndex} permanently removed (timeout)");

        if (_gameOver) return;

        _tempDisconnectedPlayers.Remove(leftPlayerIndex);

        bool alreadyLegitimatelyFinished = _finishOrder.Contains(leftPlayerIndex)
                                           && !_disconnectedPlayers.Contains(leftPlayerIndex);

        if (!_finishOrder.Contains(leftPlayerIndex))
            _finishOrder.Add(leftPlayerIndex);

        if (!alreadyLegitimatelyFinished)
        {
            _disconnectedPlayers.Add(leftPlayerIndex);
            _botPlayers.Remove(leftPlayerIndex);
            RemoveDisconnectedPlayerPawns(leftPlayerIndex);
        }
        else
        {
            _botPlayers.Remove(leftPlayerIndex);
        }

        // Kac aktif oyuncu kaldi?
        int remainingPlayers = 0;
        int lastRemainingIndex = -1;
        for (int i = 0; i < _initialPlayerCount; i++)
        {
            if (!_finishOrder.Contains(i))
            {
                remainingPlayers++;
                lastRemainingIndex = i;
            }
        }

        if (remainingPlayers <= 1 && lastRemainingIndex >= 0)
        {
            _finishOrder.Insert(0, lastRemainingIndex);
            _gameOver = true;
            if (sfx != null) sfx.PlayWin();
            if (btnRollDice != null) btnRollDice.interactable = false;
            StopTurnTimer();
            ClearAllHighlights();
        }

        UpdateScoreboard();

        if (_bridge != null && _bridge.IsHost)
            _net?.SaveFinishOrder(_finishOrder.ToArray());

        // Oyun devam ediyorsa ve cikan kisinin sirasiysa -> atla
        if (!_gameOver && _bridge != null && _bridge.IsHost)
        {
            if (_disconnectedPlayers.Contains(_state.CurrentTurnPlayerIndex)
                || _tempDisconnectedPlayers.Contains(_state.CurrentTurnPlayerIndex))
            {
                AdvanceTurnSkipDisconnected();
            }
        }
    }

    /// <summary>
    /// Host: Turu ilerletir, bitiren + gecici/kalici kopan oyunculari atlar.
    /// </summary>
    private void AdvanceTurnSkipDisconnected()
    {
        Debug.Log($"[AdvanceTurnSkipDisconnected] Current turn P{_state.CurrentTurnPlayerIndex} is disconnected. Advancing.");
        StopTurnTimer();
        _extraTurnsEarned = 0;

        _phase = TurnPhase.AwaitRoll;
        _currentRoll = -1;
        _consecutiveSixes = 0;

        _state.NextTurn(_initialPlayerCount);

        int safetyCount = 0;
        while ((_finishOrder.Contains(_state.CurrentTurnPlayerIndex)
                || _tempDisconnectedPlayers.Contains(_state.CurrentTurnPlayerIndex))
               && safetyCount < _initialPlayerCount)
        {
            _state.NextTurn(_initialPlayerCount);
            safetyCount++;
        }

        int nextTurn = _state.CurrentTurnPlayerIndex;
        Debug.Log($"[AdvanceTurnSkipDisconnected] New turn: P{nextTurn}");

        hudView.SetTurn(PlayerDisplayName(nextTurn), nextTurn, _localPlayerIndex);
        hudView.SetDice(-1);

        if (btnRollDice != null)
            btnRollDice.interactable = (nextTurn == _localPlayerIndex) && !_gameOver;

        HighlightActivePlayerPawns();

        if (_net != null)
        {
            _net.BroadcastTurn(nextTurn);
            _net.SyncGameState(nextTurn, -1, (int)TurnPhase.AwaitRoll, 0, _extraTurnsEarned);
            SerializeAndSavePawnStates();
        }

        if (nextTurn == _localPlayerIndex)
            StartTurnTimer(rollTimeLimit);
    }

    private void HideUnusedColorPawns()
    {
        for (int i = _initialPlayerCount; i < 4; i++)
        {
            var pawns = GetPawnsForTurn(i);
            foreach (var pawn in pawns)
            {
                pawn.gameObject.SetActive(false);
                _pawnStates[pawn].SetFinished();
            }
            Debug.Log($"[HideUnusedColorPawns] P{i} pawns hidden (no player).");
        }
    }

    private bool AllPawnsOnSameSquare(List<PawnView> pawns)
    {
        if (pawns.Count <= 1) return true;
        var first = _pawnStates[pawns[0]];
        for (int i = 1; i < pawns.Count; i++)
        {
            var s = _pawnStates[pawns[i]];
            if (s.Zone != first.Zone) return false;
            if (s.IsAtHome && first.IsAtHome) continue; // ikisi de evde = ayni yer
            if (s.IsInHomeLane && first.IsInHomeLane && s.HomeIndex == first.HomeIndex) continue;
            if (s.Zone == PawnZone.MainPath && s.MainIndex == first.MainIndex) continue;
            return false;
        }
        return true;
    }

    private void RemoveDisconnectedPlayerPawns(int playerIndex)
    {
        var pawns = GetPawnsForTurn(playerIndex);
        foreach (var pawn in pawns)
        {
            if (_pawnCurrentWaypoint.TryGetValue(pawn, out int wp))
            {
                positionManager?.UnregisterPawnFromWaypoint(pawn, wp);
                _pawnCurrentWaypoint.Remove(pawn);
            }

            pawnMover.StopMove(pawn);
            pawn.gameObject.SetActive(false);
            _pawnStates[pawn].SetFinished();
        }
        Debug.Log($"[RemoveDisconnectedPlayerPawns] P{playerIndex} pawns removed from board.");
    }

    // Zar atma: Sadece kendi siran ise atabilirsin
    private void OnRollDiceClicked()
    {
        if (_isSpectator) return;
        if (_paused) return;
        if (_gameOver) return;
        if (_phase != TurnPhase.AwaitRoll) return;
        if (_isRollingDice) return;
        if (_isAnimating) return;

        if (_state.CurrentTurnPlayerIndex != _localPlayerIndex)
        {
            Debug.Log("Not your turn!");
            return;
        }

        StartCoroutine(CoRollDiceAnimated());
    }

    private IEnumerator CoRollDiceAnimated()
    {
        // Safety guard: spectator veya sirasi olmayan oyuncu zar atamaz
        if (_isSpectator || _localPlayerIndex < 0) yield break;
        if (_state.CurrentTurnPlayerIndex != _localPlayerIndex)
        {
            Debug.LogWarning($"[CoRollDiceAnimated] Blocked unauthorized roll: turn={_state.CurrentTurnPlayerIndex}, me={_localPlayerIndex}, isSpectator={_isSpectator}");
            yield break;
        }

        // Timer hala aktifse ve bu local oyuncu ise: manuel zar atti -> bot modundan cikar
        if (_timerActive && _bridge != null && _bridge.IsHost
            && _botPlayers.Contains(_state.CurrentTurnPlayerIndex))
        {
            _botPlayers.Remove(_state.CurrentTurnPlayerIndex);
            Debug.Log($"[BotMode] P{_state.CurrentTurnPlayerIndex} woke up (manual roll)");
        }

        _isRollingDice = true;

        if (btnRollDice != null)
            btnRollDice.interactable = false;

        DisableAllPawnClicks();

        // Determine result IMMEDIATELY
        int roll = _dice.Roll();
        _currentRoll = roll;

        // Broadcast IMMEDIATELY
        if (_net != null && _bridge != null && _bridge.IsInRoom)
        {
            int turn = _state.CurrentTurnPlayerIndex;
            Debug.Log($"[CoRollDiceAnimated] Broadcasting Roll EARLY: P{turn} = {roll}");
            _localRollPending = true; // Sunucudan geri donene kadar CoRemoteDiceAnimation baslatma
            _net.BroadcastRoll(turn, roll);

            // Host saves state immediately
            if (_bridge.IsHost)
            {
                _net.SyncGameState(turn, roll, (int)_phase, _consecutiveSixes, _extraTurnsEarned);
            }
        }
        else if (_bridge == null || !_bridge.IsInRoom)
        {
            // Offline / bot mode: track consecutive sixes locally (OnNetworkRoll won't fire)
            if (roll == 6)
            {
                _consecutiveSixes++;
                if (_consecutiveSixes < 3 && HasPawnOutsideHomeLane(_state.CurrentTurnPlayerIndex))
                    _extraTurnsEarned++;
            }
            else
            {
                _consecutiveSixes = 0;
            }
        }

        hudView.SetTurn(PlayerDisplayName(_state.CurrentTurnPlayerIndex), _state.CurrentTurnPlayerIndex, _localPlayerIndex);
        sfx?.PlayDice();

        hudView.StartDiceRollAnimation();
        yield return new WaitForSeconds(diceRollDuration);

        // Finalize visual
        hudView.SetDice(roll);

        _isRollingDice = false;

        yield return new WaitForSeconds(0.5f); // Wait for players to see the result

        int turn2 = _state.CurrentTurnPlayerIndex;

        // 3 ARDISIK 6 KONTROLU - EN ONCE!
        if (_consecutiveSixes >= 3)
        {
            Debug.Log($"[CoRollDiceAnimated] 3 consecutive sixes! Penalty for P{turn2}");

            _consecutiveSixes = 0;

            if (_bridge != null && _bridge.IsInRoom)
            {
                if (_bridge.IsHost)
                {
                    _extraTurnsEarned = 0;
                    AdvanceTurnInternalOnly();
                }
                else
                {
                    _currentRoll = -1;
                    hudView.SetDice(-1);
                    _net?.RequestAdvanceTurn();
                    if (btnRollDice != null)
                        btnRollDice.interactable = false;
                }
            }
            else
            {
                _extraTurnsEarned = 0;
                AdvanceTurnInternalOnly();
            }

            yield break;
        }

        var legal = GetLegalMoves(turn2, roll);

        if (legal.Count == 0)
        {
            Debug.Log($"[CoRollDiceAnimated] No legal moves for P{turn2}");

            if (_bridge != null && _bridge.IsInRoom)
            {
                if (_bridge.IsHost)
                {
                    if (_extraTurnsEarned > 0)
                    {
                        _extraTurnsEarned--;
                        _currentRoll = -1;
                        hudView.SetDice(-1);
                        _net.BroadcastTurn(_state.CurrentTurnPlayerIndex);
                    }
                    else
                    {
                        AdvanceTurnInternalOnly();
                    }
                }
                else
                {
                    _currentRoll = -1;
                    hudView.SetDice(-1);
                    _net?.RequestAdvanceTurn();
                    if (btnRollDice != null)
                        btnRollDice.interactable = false;
                }
            }
            else
            {
                if (_extraTurnsEarned > 0)
                {
                    _extraTurnsEarned--;
                    _currentRoll = -1;
                    hudView.SetDice(-1);
                    if (btnRollDice != null)
                        btnRollDice.interactable = !_isSpectator && !_gameOver
                            && (_state.CurrentTurnPlayerIndex == _localPlayerIndex);
                    HighlightActivePlayerPawns();
                    StartTurnTimer(rollTimeLimit);
                }
                else
                {
                    AdvanceTurnInternalOnly();
                }
            }

            yield break;
        }

        if (legal.Count == 1)
        {
            Debug.Log($"[CoRollDiceAnimated] Single legal move, auto-moving");
            int pawnId = _pawnToId[legal[0]];
            _net?.SendMoveRequest(turn2, pawnId, roll);
            yield break;
        }

        // Tum legal piyonlar ayni karedeyse secim gereksiz - otomatik oyna
        if (legal.Count > 1 && AllPawnsOnSameSquare(legal))
        {
            Debug.Log($"[CoRollDiceAnimated] All {legal.Count} legal pawns on same square, auto-moving first");
            int pawnId = _pawnToId[legal[0]];
            _net?.SendMoveRequest(turn2, pawnId, roll);
            yield break;
        }

        HighlightLegalMoves(legal);
        SetOnlyLegalClickable(legal);
        _phase = TurnPhase.AwaitMove;

        // Piyon secim timer'i baslat
        StartTurnTimer(moveTimeLimit);
    }


    private void OnPawnClicked(PawnView pawn)
    {
        if (_paused) return;
        if (_gameOver) return;
        if (_phase != TurnPhase.AwaitMove) return;
        if (_currentRoll < 1) return;
        if (_isAnimating) return;
        if (_localBotMode) return; // Bot taking over — player must press Take Control first

        // Bug 3 fix: Rapid click protection
        float timeSinceLastRequest = Time.time - _lastMoveRequestTime;
        if (timeSinceLastRequest < MIN_MOVE_REQUEST_INTERVAL)
        {
            Debug.Log($"[OnPawnClicked] Too fast! Wait {MIN_MOVE_REQUEST_INTERVAL - timeSinceLastRequest:F2}s");
            return;
        }

        int turn = _state.CurrentTurnPlayerIndex;
        if (turn != _localPlayerIndex) return;

        var pawnsThisTurn = GetPawnsForTurn(turn);
        if (!pawnsThisTurn.Contains(pawn)) return;

        var legal = GetLegalMoves(turn, _currentRoll);
        if (!legal.Contains(pawn)) return;

        // Bug 3 fix: Set cooldown timestamp
        _lastMoveRequestTime = Time.time;

        // Bug 3 fix: Immediately disable clicks to prevent rapid fire
        DisableAllPawnClicks();
        if (btnRollDice != null) btnRollDice.interactable = false;

        int pawnId = _pawnToId[pawn];
        _net?.SendMoveRequest(turn, pawnId, _currentRoll);
    }

    private void OnNetworkMoveRequest(int playerIndex, int pawnId, int roll)
    {
        // Sadece host karar versin
        if (_net == null || !_net.IsHost) return;

        if (playerIndex != _state.CurrentTurnPlayerIndex) return;

        // Bug 3 fix: Prevent duplicate requests for same pawn while animating
        if (_lastProcessedPawnId == pawnId && _isAnimating)
        {
            Debug.LogWarning($"[OnNetworkMoveRequest] Duplicate request for pawn {pawnId} ignored");
            return;
        }

        if (!_idToPawn.TryGetValue(pawnId, out var pawn)) return;

        // Race condition fix: Client'in gonderdigi roll degerini kullan
        if (roll > 0 && roll <= 6 && roll != _currentRoll)
        {
            Debug.LogWarning($"[OnNetworkMoveRequest] Roll mismatch! Host={_currentRoll}, Client={roll}. Using client roll.");
            _currentRoll = roll;
        }

        var legal = GetLegalMoves(playerIndex, _currentRoll);
        if (!legal.Contains(pawn)) return;

        // Bug 3 fix: Track this pawn as processed
        _lastProcessedPawnId = pawnId;

        // Bug 2 fix: Generate unique move ID
        int moveId = _nextMoveId++;

        // Host hamleyi broadcast eder with moveId
        _net.BroadcastMove(playerIndex, pawnId, _currentRoll, moveId);

        // State sync (pawn states FinishMove'da kaydedilecek - hamle uygulandiktan sonra)
        _net.SyncGameState(_state.CurrentTurnPlayerIndex, _currentRoll, (int)_phase, _consecutiveSixes, _extraTurnsEarned);
    }

    private void OnNetworkRoll(int playerIndex, int roll)
    {
        Debug.Log($"[OnNetworkRoll] P{playerIndex} rolled {roll}, LocalPlayer={_localPlayerIndex}");

        // Timer'i HERKESTE durdur (senkronizasyon duzeltmesi)
        StopTurnTimer();

        if (roll == 6)
        {
            _consecutiveSixes++;
            Debug.Log($"[OnNetworkRoll] Consecutive sixes: {_consecutiveSixes}");

            if (_consecutiveSixes >= 3)
            {
                Debug.Log($"[OnNetworkRoll] 3 consecutive sixes! Penalty for P{playerIndex}");

                if (!(_bridge != null && _bridge.IsInRoom) || (_bridge != null && _bridge.IsHost))
                {
                    _extraTurnsEarned = 0;
                }
            }
            else
            {
                if (!(_bridge != null && _bridge.IsInRoom) || (_bridge != null && _bridge.IsHost))
                {
                    // Sadece evde veya main path'te piyon varsa extra turn ver
                    if (HasPawnOutsideHomeLane(playerIndex))
                    {
                        _extraTurnsEarned++;
                        Debug.Log($"[OnNetworkRoll] Extra turns: {_extraTurnsEarned}");
                    }
                    else
                    {
                        Debug.Log($"[OnNetworkRoll] 6 rolled but all pawns in home lane, no extra turn");
                    }
                }
            }
        }
        else
        {
            _consecutiveSixes = 0;
        }

        // Zar ve UI'i TUM oyuncular icin guncelle
        _currentRoll = roll;
        hudView.SetTurn(PlayerDisplayName(playerIndex), playerIndex, _localPlayerIndex);

        // Cift zar atmayi engelle
        if (btnRollDice != null)
            btnRollDice.interactable = false;

        // If we are not the one who initiated the local roll, play animation
        bool amIRollingLocally = (playerIndex == _localPlayerIndex && _localRollPending);

        Debug.Log($"[OnNetworkRoll] P{playerIndex} rolled {roll}, amIRollingLocally={amIRollingLocally}, _localRollPending={_localRollPending}, _isRollingDice={_isRollingDice}, localPlayer={_localPlayerIndex}");

        // Kendi broadcast'imizi consume et
        if (playerIndex == _localPlayerIndex && _localRollPending)
            _localRollPending = false;

        if (!amIRollingLocally)
        {
            // Cancel any existing remote animation to prevent overlap/desync
            StopCoroutine("CoRemoteDiceAnimation");
            StartCoroutine(CoRemoteDiceAnimation(playerIndex, roll));
            if (playerIndex == _localPlayerIndex)
                Debug.LogWarning($"[OnNetworkRoll] WARNING: Starting CoRemoteDiceAnimation for LOCAL player! This should not happen during manual roll.");
        }

        // Host: uzak oyuncu icin hamle timer'i baslat
        if (_bridge != null && _bridge.IsInRoom && _bridge.IsHost && playerIndex != _localPlayerIndex && _consecutiveSixes < 3)
        {
            // Add delay for animation
            if (_timerDelayCoroutine != null) StopCoroutine(_timerDelayCoroutine);
            _timerDelayCoroutine = StartCoroutine(StartTimerAfterDelay(diceRollDuration + 0.5f, playerIndex, roll));
        }
    }

    private IEnumerator StartTimerAfterDelay(float delay, int playerIndex, int roll)
    {
        yield return new WaitForSeconds(delay);
        _timerDelayCoroutine = null; // Coroutine bitti, ref temizle

        // Check if state is still valid
        if (_state.CurrentTurnPlayerIndex == playerIndex && _currentRoll == roll)
        {
            var legal = GetLegalMoves(playerIndex, roll);
            if (legal.Count > 1)
            {
                _phase = TurnPhase.AwaitMove;
                StartTurnTimer(moveTimeLimit);
                Debug.Log($"[StartTimerAfterDelay] Host: starting move timer for P{playerIndex}");
            }
        }
    }

    // Uzaktan gelen zar atisi icin gorsel animasyon
    private IEnumerator CoRemoteDiceAnimation(int playerIndex, int finalRoll)
    {
        sfx?.PlayDice();

        hudView.StartDiceRollAnimation();
        yield return new WaitForSeconds(diceRollDuration);

        hudView.SetDice(finalRoll);

        yield return new WaitForSeconds(0.3f);

        // Eger benim icin oto-atildiysa, simdi AwaitMove fazini kur
        if (playerIndex == _localPlayerIndex && _consecutiveSixes < 3)
        {
            // Guard: Sadece hala bizim turumuzsa ve phase AwaitRoll ise AwaitMove'a gec
            if (_state.CurrentTurnPlayerIndex != _localPlayerIndex)
            {
                Debug.LogWarning($"[CoRemoteDiceAnimation] Skipping AwaitMove setup - turn already changed to P{_state.CurrentTurnPlayerIndex}");
                yield break;
            }

            var legal = GetLegalMoves(playerIndex, finalRoll);
            if (legal.Count > 1)
            {
                _phase = TurnPhase.AwaitMove;
                HighlightLegalMoves(legal);
                SetOnlyLegalClickable(legal);
                StartTurnTimer(moveTimeLimit);
                Debug.Log($"[CoRemoteDiceAnimation] Auto-rolled for me, entering AwaitMove with {legal.Count} legal moves");
            }
        }
    }

    private void OnNetworkMove(int playerIndex, int pawnId, int roll, int moveId) // Bug 2: moveId parametresi eklendi
    {
        StopTurnTimer(); // Timer durdur (senkronizasyon guvenligi)

        // FIX: Onceki animasyon sikismissa temizle (yeni move geldigine gore onceki tamamlanmis olmali)
        _isAnimating = false;

        Debug.Log($"[RPC RECEIVED] Move: P{playerIndex} pawn {pawnId} with roll {roll}, moveId={moveId}");

        // Bug 2 fix: Deduplication check
        if (_processedMoves.ContainsKey(moveId))
        {
            Debug.LogWarning($"[OnNetworkMove] Duplicate move {moveId} ignored");
            return;
        }

        _processedMoves[moveId] = true;

        // Clean old entries (keep last 100)
        if (_processedMoves.Count > 100)
        {
            var oldest = _processedMoves.Keys.OrderBy(k => k).Take(50).ToList();
            foreach (var k in oldest)
                _processedMoves.Remove(k);
        }

        if (!_idToPawn.TryGetValue(pawnId, out var pawn))
        {
            Debug.LogError($"[OnNetworkMove] Pawn {pawnId} not found!");
            return;
        }

        _currentRoll = roll;

        ApplyMove(playerIndex, pawn, roll);

        CheckWinAndEndIfNeeded(playerIndex);
    }

    private void OnNetworkTurn(int nextPlayerIndex)
    {
        Debug.Log($"[RPC RECEIVED] Turn: Now P{nextPlayerIndex} ({TurnName(nextPlayerIndex)})");

        // FIX: Animasyon flag'lerini sifirla (host animasyonu client'tan once bitebilir, race condition)
        _isAnimating = false;

        // Gercek sira degisimi mi, yoksa extra turn mi?
        if (nextPlayerIndex != _state.CurrentTurnPlayerIndex)
        {
            _consecutiveSixes = 0; // Sira degisti, sifirla
        }

        _state.CurrentTurnPlayerIndex = nextPlayerIndex;
        _phase = TurnPhase.AwaitRoll;
        _isRollingDice = false;
        _localRollPending = false;
        _currentRoll = -1;

        hudView.SetDice(-1);
        hudView.SetTurn(PlayerDisplayName(nextPlayerIndex), nextPlayerIndex, _localPlayerIndex);

        if (btnRollDice != null)
        {
            bool isMyTurn = (nextPlayerIndex == _localPlayerIndex);
            btnRollDice.interactable = isMyTurn && !_gameOver;
            Debug.Log($"[OnNetworkTurn] Dice interactable={btnRollDice.interactable}, isMyTurn={isMyTurn}, gameOver={_gameOver}, phase={_phase}, localPlayer={_localPlayerIndex}");
        }
        // Sira sende ise ses cal + titresim
        if (nextPlayerIndex == _localPlayerIndex && !_gameOver)
        {
            sfx?.PlayYourTurn();

            // Mobilde titresim (Android)
#if UNITY_ANDROID && !UNITY_EDITOR
        Handheld.Vibrate();
#endif
        }
        HighlightActivePlayerPawns();

        // Zar atma timer'i baslat
        StartTurnTimer(rollTimeLimit);

    }

    // ========== TIMER NETWORK HANDLERS (Fix 1) ==========

    private void OnNetworkTimerStart(float duration)
    {
        // Sunucu dogru sureyi gonderiyor (bot override dahil), herkese uygula
        _timerActive = true;
        _turnTimer = duration;
        _clockPlayed = false;
        hudView?.SetTimer(_turnTimer);
        Debug.Log($"[Timer] Network timer start: {duration}s for P{_state.CurrentTurnPlayerIndex}");
    }



    private void FinishMove()
    {
        StopTurnTimer(); // Timer durdur
        _phase = TurnPhase.AwaitRoll;
        _isRollingDice = false;
        _localRollPending = false;

        // Bug 3 fix: Reset move tracking for next turn
        _lastProcessedPawnId = -1;

        foreach (var kv in _pawnStates)
            kv.Key.SetClickable(false);

        // ==================== ONLINE ====================
        if (_bridge != null && _bridge.IsInRoom)
        {
            // Hamle uygulandiktan sonra pawn state'leri kaydet
            if (_bridge.IsHost)
                SerializeAndSavePawnStates();

            if (_bridge.IsHost)
            {
                // Bitiren oyuncuya extra turn verme
                if (_finishOrder.Contains(_state.CurrentTurnPlayerIndex))
                {
                    _extraTurnsEarned = 0;
                    AdvanceTurnInternalOnly();
                    return;
                }

                // Host: extra turn varsa ayni oyuncu devam
                if (_extraTurnsEarned > 0)
                {
                    _extraTurnsEarned--;
                    _currentRoll = -1;
                    hudView.SetDice(-1);
                    Debug.Log($"[FinishMove] Host: Extra turn! Remaining: {_extraTurnsEarned}");
                    _net.BroadcastTurn(_state.CurrentTurnPlayerIndex); // Ayni oyuncu
                    return;
                }

                // Host: sira ilerlet
                AdvanceTurnInternalOnly();
            }
            else
            {
                // Client: UI temizle
                _currentRoll = -1;
                hudView.SetDice(-1);

                // Bitiren oyuncu extra turn almasin
                if (_finishOrder.Contains(_localPlayerIndex))
                    _extraTurnsEarned = 0;

                // Dice butonuna DOKUNMA! OnNetworkTurn zaten yonetiyor.
                // ApplyMove ve OnNetworkRoll zaten disabled yapiyor.
                // Burada tekrar false yapmak, animasyon host'tan once bittiginde
                // OnNetworkTurn'un enable ettigi butonu yanlis sekilde kapatir.
            }
            return;
        }

        // ==================== OFFLINE ====================
        // Bitiren oyuncuya extra turn verme
        if (_finishOrder.Contains(_state.CurrentTurnPlayerIndex))
        {
            _extraTurnsEarned = 0;
            AdvanceTurnInternalOnly();
            return;
        }

        if (_extraTurnsEarned > 0)
        {
            _extraTurnsEarned--;
            _currentRoll = -1;
            hudView.SetDice(-1);
            Debug.Log($"[FinishMove] Offline: Extra turn! Remaining: {_extraTurnsEarned}");

            if (_lobbyBots.Contains(_state.CurrentTurnPlayerIndex))
            {
                if (btnRollDice != null) btnRollDice.interactable = false;
                ScheduleBotTurn();
            }
            else
            {
                if (btnRollDice != null)
                    btnRollDice.interactable = !_gameOver;
                HighlightActivePlayerPawns();
                StartTurnTimer(rollTimeLimit);
            }
            return;
        }

        AdvanceTurnInternalOnly();
    }

    private void AdvanceTurnInternalOnly()
    {
        StopTurnTimer(); // Timer durdur
        Debug.Log($"[AdvanceTurnInternalOnly] Advancing from P{_state.CurrentTurnPlayerIndex}");

        _phase = TurnPhase.AwaitRoll;
        _currentRoll = -1;
        _consecutiveSixes = 0;

        _state.NextTurn(_initialPlayerCount);

        // Bitiren/cikan oyunculari atla (sonsuz dongu korumali)
        int safetyCount = 0;
        while ((_finishOrder.Contains(_state.CurrentTurnPlayerIndex)
                || _tempDisconnectedPlayers.Contains(_state.CurrentTurnPlayerIndex))
               && safetyCount < _initialPlayerCount)
        {
            _state.NextTurn(_initialPlayerCount);
            safetyCount++;
        }

        Debug.Log($"[AdvanceTurnInternalOnly] New turn: P{_state.CurrentTurnPlayerIndex}");

        // SADECE HOST BROADCAST EDER
        if (_net != null && _bridge != null && _bridge.IsInRoom && _bridge.IsHost)
        {
            Debug.Log($"[AdvanceTurnInternalOnly] Broadcasting Turn: P{_state.CurrentTurnPlayerIndex}");
            _net.BroadcastTurn(_state.CurrentTurnPlayerIndex);

            // Bug 1 fix: Persist state after turn change
            _net.SyncGameState(_state.CurrentTurnPlayerIndex, -1, (int)TurnPhase.AwaitRoll, 0, _extraTurnsEarned);
            SerializeAndSavePawnStates();
        }
        else if (_bridge == null || !_bridge.IsInRoom)
        {
            hudView.SetDice(-1);
            hudView.SetTurn(PlayerDisplayName(_state.CurrentTurnPlayerIndex), _state.CurrentTurnPlayerIndex, _localPlayerIndex);

            if (_lobbyBots.Contains(_state.CurrentTurnPlayerIndex))
            {
                // Bot's turn — disable dice, schedule bot play
                if (btnRollDice != null) btnRollDice.interactable = false;
                ScheduleBotTurn();
            }
            else
            {
                // Singleplayer'da sayaç sıfırlanınca bot modu kalıcı kalmasın
                if (_isBotGame) SetLocalBotMode(false);
                if (btnRollDice != null)
                    btnRollDice.interactable = !_gameOver;
                StartTurnTimer(rollTimeLimit);
            }
        }

        HighlightActivePlayerPawns();
    }

    // Bu metodlari GameBootstrapper.cs dosyasinin sonuna ekle

    private void CounterRotatePawns(List<PawnView> pawns, Quaternion counterRotation)
    {
        foreach (var pawn in pawns)
            pawn.Rect.localRotation = counterRotation;
    }

    private void RegisterPawns(List<PawnView> pawns, int ownerIndex)
    {
        for (int i = 0; i < pawns.Count; i++)
        {
            var p = pawns[i];
            _pawnStates[p] = new PawnState();
            _pawnOwner[p] = ownerIndex;
            RegisterPawnId(p);
            p.CacheHomeUI();
        }
    }

    private List<PawnView> GetPawnsForTurn(int playerIndex)
    {
        return playerIndex switch
        {
            0 => _redPawns,
            1 => _yellowPawns,
            2 => _greenPawns,
            3 => _bluePawns,
            _ => _redPawns
        };
    }

    private bool TryGetStartIndexForPlayer(int playerIndex, out int startIndex)
    {
        switch (playerIndex)
        {
            case 0: startIndex = 0; return true;   // Red: WP_00
            case 1: startIndex = 26; return true;  // Yellow: WP_26
            case 2: startIndex = 13; return true;  // Green: WP_13
            case 3: startIndex = 39; return true;  // Blue: WP_39
            default: startIndex = -1; return false;
        }
    }

    private void ResolveCaptures(PawnView movedPawn)
    {
        if (movedPawn == null) return;

        var movedState = _pawnStates[movedPawn];
        if (movedState.IsAtHome) return;
        if (movedState.IsInHomeLane) return;
        if (movedState.IsFinished) return;

        int landingIndex = movedState.MainIndex;

        Debug.Log($"[ResolveCaptures] Checking at MainIndex={landingIndex} for P{_pawnOwner[movedPawn]}");

        if (safeSquares != null && safeSquares.IsSafeIndex(landingIndex))
            return;

        int moverOwner = _pawnOwner[movedPawn];

        // Blok kontrolu
        int enemyCountOnTile = 0;
        foreach (var kv in _pawnStates)
        {
            var p = kv.Key;
            if (p == movedPawn) continue;

            var st = kv.Value;
            if (st.IsAtHome || st.IsInHomeLane || st.IsFinished) continue;
            if (st.MainIndex != landingIndex) continue;

            int owner = _pawnOwner[p];
            if (owner == moverOwner) continue;

            enemyCountOnTile++;
            if (enemyCountOnTile >= 2)
                return;
        }

        // Capture
        foreach (var kv in _pawnStates)
        {
            var otherPawn = kv.Key;
            if (otherPawn == movedPawn) continue;

            var otherState = kv.Value;
            if (otherState.IsAtHome || otherState.IsInHomeLane || otherState.IsFinished) continue;
            if (otherState.MainIndex != landingIndex) continue;

            int otherOwner = _pawnOwner[otherPawn];
            if (otherOwner == moverOwner) continue;

            if (!(_bridge != null && _bridge.IsInRoom) || (_bridge != null && _bridge.IsHost))
            {
                _extraTurnsEarned++;
                Debug.Log($"[ResolveCaptures] Capture! Extra turns: {_extraTurnsEarned}");
            }

            int capturedIndex = otherState.MainIndex;

            if (!TryGetStartIndexForPlayer(otherOwner, out int capturedStartIndex))
            {
                Debug.LogError($"[ResolveCaptures] Cannot find start index for player {otherOwner}");
                capturedStartIndex = 0;
            }

            otherState.ReturnHome();

            sfx?.PlayCapture();

            if (_pawnCurrentWaypoint.TryGetValue(otherPawn, out int capturedWp))
            {
                positionManager?.UnregisterPawnFromWaypoint(otherPawn, capturedWp);
                _pawnCurrentWaypoint.Remove(otherPawn);
            }

            otherPawn.SetStackScale(1f);

            Vector3 homePos = GetHomePawnPosition(otherPawn);
            pawnMover.MoveBackwardsToHome(
                otherPawn,
                boardWaypoints.MainPath,
                capturedIndex,
                capturedStartIndex,
                homePos,
                () =>
                {
                    otherPawn.ReturnHomeUI();
                }
            );

            break;
        }
    }


    /// <summary>
    /// Pawn'un home slot pozisyonunu bul
    /// </summary>
    private Vector3 GetHomePawnPosition(PawnView pawn)
    {
        // Pawn'un sahibini bul
        if (!_pawnOwner.TryGetValue(pawn, out int ownerIndex))
            return pawn.Rect.position; // Fallback

        // Renk gruplarina gore home slot'lari al
        IReadOnlyList<RectTransform> homeSlotsForColor = ownerIndex switch
        {
            0 => homeSlots.R,
            1 => homeSlots.Y,
            2 => homeSlots.G,
            3 => homeSlots.B,
            _ => null
        };

        if (homeSlotsForColor == null || homeSlotsForColor.Count == 0)
            return pawn.Rect.position;

        // Bu pawn'un hangi slot'ta oldugunu bul
        var pawnsOfColor = GetPawnsForTurn(ownerIndex);
        int pawnIndexInColor = pawnsOfColor.IndexOf(pawn);

        if (pawnIndexInColor >= 0 && pawnIndexInColor < homeSlotsForColor.Count)
            return homeSlotsForColor[pawnIndexInColor].position;

        // Fallback: ilk slot
        return homeSlotsForColor[0].position;
    }

    private int GetHomeEntryIndex(int playerIndex)
    {
        return playerIndex switch
        {
            0 => 50,
            1 => 24,
            2 => 11,
            3 => 37,
            _ => 50
        };
    }

    private IReadOnlyList<RectTransform> GetHomePath(int playerIndex)
    {
        return playerIndex switch
        {
            0 => boardWaypoints.HomeR,
            1 => boardWaypoints.HomeY,
            2 => boardWaypoints.HomeG,
            3 => boardWaypoints.HomeB,
            _ => boardWaypoints.HomeR
        };
    }

    private void ClearAllHighlights()
    {
        foreach (var kv in _pawnStates)
            kv.Key.SetHighlightNone();
    }

    private void HighlightActivePlayerPawns()
    {
        ClearAllHighlights();

        int turn = _state.CurrentTurnPlayerIndex;
        var pawns = GetPawnsForTurn(turn);

        foreach (var p in pawns)
        {
            if (_pawnStates[p].IsFinished) continue;
            p.SetHighlightActive();
        }
    }

    private void HighlightLegalMoves(List<PawnView> legal)
    {
        HighlightActivePlayerPawns();

        foreach (var p in legal)
            p.SetHighlightLegal();
    }

    private int CountFinishedPawns(int playerIndex)
    {
        int count = 0;
        var pawns = GetPawnsForTurn(playerIndex);

        foreach (var p in pawns)
        {
            if (_pawnStates[p].IsFinished)
                count++;
        }

        return count;
    }

    private void CheckWinAndEndIfNeeded(int playerIndex)
    {
        int finished = CountFinishedPawns(playerIndex);
        if (finished < 4) return;

        // Zaten siralamada varsa tekrar ekleme
        if (_finishOrder.Contains(playerIndex)) return;

        _finishOrder.Add(playerIndex);
        sfx?.PlayWin();

        // Online: finishOrder'u kaydet (late joiner icin)
        if (_net != null && _bridge != null && _bridge.IsInRoom && _bridge.IsHost)
            _net.SaveFinishOrder(_finishOrder.ToArray());

        int activePlayers = _initialPlayerCount;

        // Oyun bitmeden önce local oyuncu bitirdiyse bireysel reklam göster (1. ve 2. sıra)
        if (_finishOrder.Count < activePlayers - 1 && playerIndex == _localPlayerIndex)
        {
            AdManager.Instance?.ShowInterstitial(() => UpdateScoreboard(openPanel: true));
            return;
        }

        // Son kalan oyuncuyu otomatik ekle
        if (_finishOrder.Count >= activePlayers - 1)
        {
            for (int i = 0; i < activePlayers; i++)
            {
                if (!_finishOrder.Contains(i))
                {
                    _finishOrder.Add(i);
                    break;
                }
            }

            _gameOver = true;

            if (btnRollDice != null)
                btnRollDice.interactable = false;

            ClearAllHighlights();

            // GPGS: Oyun bitti, skorları raporla
            ReportGameToGPGS();

            // Oyun bitti: tüm cihazlarda reklam tetikle.
            // 1. ve 2. biten oyuncular zaten bireysel reklamlarını gördü,
            // AdManager'daki 60s cooldown onlarda tekrar çıkmasını engeller.
            AdManager.Instance?.ShowInterstitial(() => UpdateScoreboard());
            return;
        }

        UpdateScoreboard();
    }

    private void OnRestartClicked()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ==================== SCOREBOARD ====================

    private void InitScoreboard()
    {
        if (scoreboardPanel != null)
            scoreboardPanel.SetActive(false);

        if (btnScoreboardClose != null)
            btnScoreboardClose.onClick.AddListener(OnScoreboardClose);

        if (btnMainMenu != null)
        {
            btnMainMenu.onClick.AddListener(OnMainMenuClicked);
            btnMainMenu.gameObject.SetActive(false);
        }
    }

    private void UpdateScoreboard(bool openPanel = false)
    {
        if (scoreboardPanel == null) return;

        // Gosterim sirasini olustur: 1) Mesru bitirenler, 2) ???, 3) Disconnect olanlar
        var displayEntries = new List<string>();

        // 1. Mesru bitirenler (disconnect olmayan, _finishOrder sirasiyla)
        foreach (int idx in _finishOrder)
        {
            if (!_disconnectedPlayers.Contains(idx))
                displayEntries.Add(PlayerDisplayName(idx));
        }

        // 2. Hala oynayan oyuncular -> "???"
        for (int i = 0; i < _initialPlayerCount; i++)
        {
            if (!_finishOrder.Contains(i))
                displayEntries.Add("???");
        }

        // 3. Disconnect olanlar (en sona)
        foreach (int idx in _finishOrder)
        {
            if (_disconnectedPlayers.Contains(idx))
                displayEntries.Add(PlayerDisplayName(idx));
        }

        // Goster
        for (int i = 0; i < scoreboardTexts.Length; i++)
        {
            if (i < displayEntries.Count)
                scoreboardTexts[i].text = $"{i + 1}. {displayEntries[i]}";
            else
                scoreboardTexts[i].text = "";
        }

        if (txtScoreboardTitle != null)
            txtScoreboardTitle.text = _gameOver ? LocalizationManager.Get("game_over") : LocalizationManager.Get("rankings");

        // X butonu: oyun devam ediyorsa goster, bittiyse gizle
        if (btnScoreboardClose != null)
            btnScoreboardClose.gameObject.SetActive(!_gameOver);

        // Ana Menu butonu: sadece oyun bittiyse VE yerel oyuncu da bitirmisse (ya da spectator) goster
        if (btnMainMenu != null)
        {
            bool localPlayerFinished = _isSpectator || _finishOrder.Contains(_localPlayerIndex);
            btnMainMenu.gameObject.SetActive(_gameOver && localPlayerFinished);
        }

        // Paneli sadece açıkça istendiğinde veya oyun bittiyse aç
        if (openPanel || _gameOver)
            if (!_isSpectator || _gameOver)
                scoreboardPanel.SetActive(true);
    }

    private void OnScoreboardClose()
    {
        if (scoreboardPanel != null)
            scoreboardPanel.SetActive(false);
    }

    private void OnMainMenuClicked() => ExitToMainMenu();

    public void ExitToMainMenu()
    {
        // Bot game cleanup
        BotGameConfig.Reset();

        if (_bridge != null && _bridge.IsInRoom)
        {
            _isLeavingToMainMenu = true;
            _isIntentionalDisconnect = true;
            // Bilinçli çıkış: permanent=true gonder → sunucu hemen cikarir, 60s bekleme yok
            _bridge.LeaveRoom(true);
            SceneManager.LoadScene(0);
            return;
        }

        // Odada degilsek direkt sahne yukle
        SceneManager.LoadScene(0);
    }

    // ==================== LOBBY BOT (singleplayer) ====================

    private void ScheduleBotTurn()
    {
        if (_botTurnCoroutine != null)
            StopCoroutine(_botTurnCoroutine);
        _botTurnCoroutine = StartCoroutine(CoBotPlayTurn());
    }

    private IEnumerator CoBotPlayTurn()
    {
        int botIndex = _state.CurrentTurnPlayerIndex;
        if (!_lobbyBots.Contains(botIndex) || _gameOver) yield break;

        // Wait before acting (feel more natural)
        yield return new WaitForSeconds(BotAutoDelay);
        if (_gameOver || _state.CurrentTurnPlayerIndex != botIndex) yield break;

        // ── Roll dice ──
        _isRollingDice = true;
        DisableAllPawnClicks();
        if (btnRollDice != null) btnRollDice.interactable = false;

        int roll = _dice.Roll();
        _currentRoll = roll;

        // Track consecutive sixes (same logic as OnNetworkRoll)
        if (roll == 6)
        {
            _consecutiveSixes++;
            if (_consecutiveSixes < 3 && HasPawnOutsideHomeLane(botIndex))
                _extraTurnsEarned++;
        }
        else
        {
            _consecutiveSixes = 0;
        }

        // Play dice animation
        hudView.SetTurn(PlayerDisplayName(botIndex), botIndex, _localPlayerIndex);
        sfx?.PlayDice();
        hudView.StartDiceRollAnimation();
        yield return new WaitForSeconds(diceRollDuration);
        hudView.SetDice(roll);
        _isRollingDice = false;

        yield return new WaitForSeconds(0.5f);

        // ── 3 consecutive sixes penalty ──
        if (_consecutiveSixes >= 3)
        {
            Debug.Log($"[CoBotPlayTurn] Bot P{botIndex} rolled 3 consecutive sixes — penalty!");
            _consecutiveSixes = 0;
            _extraTurnsEarned = 0;
            AdvanceTurnInternalOnly();
            yield break;
        }

        // ── Check legal moves ──
        var legal = GetLegalMoves(botIndex, roll);

        if (legal.Count == 0)
        {
            Debug.Log($"[CoBotPlayTurn] Bot P{botIndex} has no legal moves");
            if (_extraTurnsEarned > 0)
            {
                _extraTurnsEarned--;
                _currentRoll = -1;
                hudView.SetDice(-1);
                ScheduleBotTurn(); // bot gets extra turn
            }
            else
            {
                AdvanceTurnInternalOnly();
            }
            yield break;
        }

        // ── Pick a move (priority-based AI) ──
        yield return new WaitForSeconds(0.5f); // "thinking" delay

        if (_gameOver || _state.CurrentTurnPlayerIndex != botIndex) yield break;

        PawnView chosen = BotPickMove(legal, botIndex, roll);
        int pawnId = _pawnToId[chosen];

        Debug.Log($"[CoBotPlayTurn] Bot P{botIndex} moving pawn {pawnId} with roll {roll}");

        // Process through the standard move pipeline:
        // SendMoveRequest → OnNetworkMoveRequest (host validates) → BroadcastMove → OnNetworkMove → ApplyMove → FinishMove
        _phase = TurnPhase.AwaitMove;
        _net.SendMoveRequest(botIndex, pawnId, roll);
        // FinishMove will handle extra turns and turn advancement,
        // which will call ScheduleBotTurn again if next player is also a bot.
    }

    /// <summary>
    /// Priority-based bot move selection:
    /// 1. Finish a pawn (HomeIndex + roll == 5)
    /// 2. Capture an enemy pawn on the main path
    /// 3. Move a threatened pawn to a safe square or into the home lane
    /// 4. Enter a new pawn from home base if 0–1 active pawns on board
    /// 5. Advance the most progressed pawn (closest to finish)
    /// 6. Random fallback
    /// </summary>
    private PawnView BotPickMove(List<PawnView> legal, int botIndex, int roll)
    {
        if (legal.Count == 1) return legal[0];

        const int pathCount = 52;
        TryGetStartIndexForPlayer(botIndex, out int botStart);
        int homeEntry = GetHomeEntryIndex(botIndex);

        // ── 1. Finish a pawn ──
        foreach (var pawn in legal)
        {
            var st = _pawnStates[pawn];
            if (st.IsInHomeLane && st.HomeIndex + roll == 5)
                return pawn;
        }

        // ── 2. Capture an enemy pawn ──
        foreach (var pawn in legal)
        {
            var st = _pawnStates[pawn];
            if (st.IsAtHome || st.IsInHomeLane) continue;

            int from = st.MainIndex;
            int distToEntry = (homeEntry - from + pathCount) % pathCount;
            if (roll > distToEntry) continue; // will enter home lane, no capture

            int landing = (from + roll) % pathCount;
            if (safeSquares != null && safeSquares.IsSafeIndex(landing)) continue;

            foreach (var kv in _pawnStates)
            {
                if (_pawnOwner[kv.Key] == botIndex) continue;
                var enemySt = kv.Value;
                if (enemySt.IsAtHome || enemySt.IsInHomeLane || enemySt.IsFinished) continue;
                if (enemySt.MainIndex == landing)
                    return pawn;
            }
        }

        // ── 3. Move threatened pawn to safety ──
        foreach (var pawn in legal)
        {
            var st = _pawnStates[pawn];
            if (st.IsAtHome || st.IsInHomeLane) continue;
            if (!IsBotPawnThreatened(pawn, botIndex)) continue;

            int from = st.MainIndex;
            int distToEntry = (homeEntry - from + pathCount) % pathCount;
            if (roll >= distToEntry) return pawn; // enters home lane = safe

            int landing = (from + roll) % pathCount;
            if (safeSquares != null && safeSquares.IsSafeIndex(landing)) return pawn;
        }

        // ── 4. Enter new pawn on any 6 (priorities 1-3 already passed) ──
        if (roll == 6)
        {
            foreach (var pawn in legal)
                if (_pawnStates[pawn].IsAtHome) return pawn;
        }

        // ── 5. Advance the most progressed pawn ──
        // Urgency order: threatened (needs to escape) > normal > already on safe square
        PawnView best = null;
        int bestProgress = -1;

        // Pass A: threatened pawns — move them away from danger (IsBotPawnThreatened already returns false for safe-square pawns)
        foreach (var pawn in legal)
        {
            if (!IsBotPawnThreatened(pawn, botIndex)) continue;
            int progress = GetBotPawnProgress(pawn, botIndex);
            if (progress > bestProgress) { bestProgress = progress; best = pawn; }
        }

        // Pass B: non-threatened pawns that are NOT already on a safe square
        if (best == null)
        {
            foreach (var pawn in legal)
            {
                if (IsBotPawnThreatened(pawn, botIndex)) continue;
                var st = _pawnStates[pawn];
                bool onSafe = !st.IsAtHome && !st.IsInHomeLane
                              && safeSquares != null && safeSquares.IsSafeIndex(st.MainIndex);
                if (onSafe) continue;
                int progress = GetBotPawnProgress(pawn, botIndex);
                if (progress > bestProgress) { bestProgress = progress; best = pawn; }
            }
        }

        // Pass C: fallback — safe-square pawns or whatever is left
        if (best == null)
        {
            foreach (var pawn in legal)
            {
                int progress = GetBotPawnProgress(pawn, botIndex);
                if (progress > bestProgress) { bestProgress = progress; best = pawn; }
            }
        }

        return best ?? legal[UnityEngine.Random.Range(0, legal.Count)];
    }

    // Returns how far a pawn has traveled from its start (higher = closer to finish)
    private int GetBotPawnProgress(PawnView pawn, int playerIndex)
    {
        var st = _pawnStates[pawn];
        if (st.IsAtHome) return -1;
        if (st.IsFinished) return 100;
        if (st.IsInHomeLane) return 52 + st.HomeIndex;
        TryGetStartIndexForPlayer(playerIndex, out int start);
        return (st.MainIndex - start + 52) % 52;
    }

    // Returns true if an enemy pawn can reach this pawn's position in 1–6 rolls
    private bool IsBotPawnThreatened(PawnView pawn, int botIndex)
    {
        var st = _pawnStates[pawn];
        if (safeSquares != null && safeSquares.IsSafeIndex(st.MainIndex)) return false;

        int myPos = st.MainIndex;
        const int pathCount = 52;

        foreach (var kv in _pawnStates)
        {
            if (_pawnOwner[kv.Key] == botIndex) continue;
            var enemySt = kv.Value;
            if (enemySt.IsAtHome || enemySt.IsInHomeLane || enemySt.IsFinished) continue;

            int dist = (myPos - enemySt.MainIndex + pathCount) % pathCount;
            if (dist >= 1 && dist <= 6) return true;
        }
        return false;
    }

    // ==================== BOT MODE (AFK recovery) ====================

    private void SetLocalBotMode(bool active)
    {
        _localBotMode = active;
        // Don't show TakeControl button in bot games — lobby bots can't be "taken over"
        if (btnTakeControl != null && !_isBotGame)
            btnTakeControl.gameObject.SetActive(active);
    }

    private void OnTakeControlClicked()
    {
        SetLocalBotMode(false);
        _botPlayers.Remove(_localPlayerIndex);

        // Bot'un devam eden coroutine'lerini durdur (FinishMove timer'i ezmesini engelle)
        StopCoroutine("CoRollDiceAnimated");
        CancelAnimationSafetyTimer();
        _isRollingDice = false;
        _localRollPending = false;
        _isAnimating = false;

        // Bekleyen timer delay coroutine'ini iptal et (bot timer'in ustune yazmasini engelle)
        if (_timerDelayCoroutine != null)
        {
            StopCoroutine(_timerDelayCoroutine);
            _timerDelayCoroutine = null;
        }

        // Timer'i durdur ve normal sureli yeniden baslat
        StopTurnTimer(false);
        if (_state.CurrentTurnPlayerIndex == _localPlayerIndex && !_gameOver)
        {
            if (_phase == TurnPhase.AwaitRoll)
            {
                if (btnRollDice != null) btnRollDice.interactable = true;
                StartTurnTimer(rollTimeLimit);
            }
            else if (_phase == TurnPhase.AwaitMove)
            {
                var legal = GetLegalMoves(_localPlayerIndex, _currentRoll);
                if (legal.Count > 0)
                {
                    HighlightLegalMoves(legal);
                    SetOnlyLegalClickable(legal);
                }
                StartTurnTimer(moveTimeLimit);
            }
        }

        // Host'a bildir (host _botPlayers'dan cikarmasi icin)
        if (_bridge != null && _bridge.IsInRoom)
        {
            _bridge.SendExitBot(_localPlayerIndex);
        }

        Debug.Log($"[BotMode] P{_localPlayerIndex} took manual control via button.");
    }

    private void OnNetworkEnterBot(int playerIndex)
    {
        _botPlayers.Add(playerIndex);
        Debug.Log($"[BotMode] P{playerIndex} entered bot mode (network).");
    }

    private void OnNetworkExitBot(int playerIndex)
    {
        _botPlayers.Remove(playerIndex);
        Debug.Log($"[BotMode] P{playerIndex} exited bot mode (network).");

        // Bekleyen timer delay coroutine'ini iptal et (bot timer'in ustune yazmasini engelle)
        if (_timerDelayCoroutine != null)
        {
            StopCoroutine(_timerDelayCoroutine);
            _timerDelayCoroutine = null;
        }

        // Host: timer'i normal sureyle yeniden baslat ve broadcast et
        // ANCAK kendi exit_bot'umuzsa (Take Control), OnTakeControlClicked zaten halletti
        if (_bridge != null && _bridge.IsHost && _state.CurrentTurnPlayerIndex == playerIndex
            && playerIndex != _localPlayerIndex)
        {
            StopTurnTimer();
            if (_phase == TurnPhase.AwaitRoll)
                StartTurnTimer(rollTimeLimit);
            else if (_phase == TurnPhase.AwaitMove)
                StartTurnTimer(moveTimeLimit);
        }
    }

    // NOTE: OnRoomPropertiesUpdate was a Photon callback.
    // Bot exit detection via room properties is now handled by the bridge internally
    // or can be polled. If _bridge provides an OnRoomPropertyChanged event, subscribe to it.
    // For now, bot exit is handled through SetRoomProperty + bridge event system.

    private void DisableAllPawnClicks()
    {
        foreach (var kv in _pawnStates)
            kv.Key.SetClickable(false);
    }

    private void SetOnlyLegalClickable(List<PawnView> legal)
    {
        DisableAllPawnClicks();

        for (int i = 0; i < legal.Count; i++)
            legal[i].SetClickable(true);
    }

    private List<PawnView> GetLegalMoves(int playerIndex, int roll)
    {
        var pawns = GetPawnsForTurn(playerIndex);
        var legal = new List<PawnView>(4);

        foreach (var p in pawns)
        {
            var st = _pawnStates[p];

            if (st.IsAtHome)
            {
                if (roll == 6 && TryGetStartIndexForPlayer(playerIndex, out _))
                    legal.Add(p);
            }
            else
            {
                if (st.IsFinished) continue;

                if (st.IsInHomeLane)
                {
                    if (st.HomeIndex + roll <= 5)
                        legal.Add(p);
                    continue;
                }

                legal.Add(p);
            }
        }

        return legal;
    }

    private void ApplyMove(int playerIndex, PawnView pawn, int roll)
    {
        // Bug 2 & 3 fix: Set animation flag IMMEDIATELY at method start
        _isAnimating = true;
        DisableAllPawnClicks();
        if (btnRollDice != null) btnRollDice.interactable = false;

        // FIX: Animasyon guvenlik zaman asimi (sikisma onleme)
        if (_animationSafetyTimer != null) StopCoroutine(_animationSafetyTimer);
        _animationSafetyTimer = StartCoroutine(AnimationSafetyTimeout(5f));

        var st = _pawnStates[pawn];

        // ==================== EVDEN CIKIS ====================
        if (st.IsAtHome)
        {
            if (roll != 6)
            {
                _isAnimating = false;
                return;
            }
            if (!TryGetStartIndexForPlayer(playerIndex, out int startIndex))
            {
                _isAnimating = false;
                return;
            }

            st.EnterMainAt(startIndex);
            sfx?.PlayHomeExit();

            if (_pawnCurrentWaypoint.TryGetValue(pawn, out int oldWp))
                positionManager?.UnregisterPawnFromWaypoint(pawn, oldWp);

            // Evden cikis instant kalabilir (tek kareye spawn)
            pawn.SetPosition(boardWaypoints.MainPath[startIndex].position);
            _pawnCurrentWaypoint[pawn] = startIndex;
            positionManager?.RegisterPawnAtWaypoint(pawn, startIndex);

            ResolveCaptures(pawn);
            _isAnimating = false; // No animation for home exit, reset flag
            CancelAnimationSafetyTimer();
            FinishMove();
            return;
        }

        if (st.IsFinished)
        {
            _isAnimating = false;
            return;
        }

        // ==================== HOME LANE ====================
        if (st.IsInHomeLane)
        {
            if (st.HomeIndex + roll > 5)
            {
                _isAnimating = false;
                return;
            }

            var homePath = GetHomePath(playerIndex);
            int fromHome = st.HomeIndex;
            int newHomeIndex = fromHome + roll;

            // Eski home lane pozisyonundan unregister
            int oldKey = GetHomeLaneKey(playerIndex, fromHome);
            if (_pawnCurrentWaypoint.ContainsKey(pawn))
                positionManager?.UnregisterPawnFromWaypoint(pawn, oldKey);

            var positions = new List<Vector3>();
            for (int i = fromHome + 1; i <= newHomeIndex; i++)
                positions.Add(homePath[i].position);

            st.AdvanceHome(roll);

            if (newHomeIndex == 5)
            {
                if (!(_bridge != null && _bridge.IsInRoom) || (_bridge != null && _bridge.IsHost))
                {
                    _extraTurnsEarned++;
                    Debug.Log($"[ApplyMove] Pawn finished! Extra turns: {_extraTurnsEarned}");
                }
                sfx?.PlayFinish();
            }

            pawnMover.MoveAlongPositions(pawn, positions, () =>
        {
            // Yeni home lane pozisyonuna register
            int newKey = GetHomeLaneKey(playerIndex, newHomeIndex);
            _pawnCurrentWaypoint[pawn] = newKey;
            positionManager?.RegisterPawnAtWaypoint(pawn, newKey);

            _isAnimating = false;
            CancelAnimationSafetyTimer();
            FinishMove();
        });
            return;
        }

        // ==================== MAIN PATH ====================
        int entry = GetHomeEntryIndex(playerIndex);
        int from = st.MainIndex;
        int pathCount = boardWaypoints.MainPath.Count;
        int distToEntry = (entry - from + pathCount) % pathCount;

        // Normal main path hareketi (entry'e kadar)
        if (roll <= distToEntry)
        {
            if (_pawnCurrentWaypoint.TryGetValue(pawn, out int oldWp))
                positionManager?.UnregisterPawnFromWaypoint(pawn, oldWp);

            // Pozisyon listesi olustur
            var positions = new List<Vector3>();
            int cur = from;
            for (int i = 0; i < roll; i++)
            {
                cur = (cur + 1) % pathCount;
                positions.Add(boardWaypoints.MainPath[cur].position);
            }

            // State'i hemen guncelle
            st.AdvanceMain(roll, pathCount);
            int targetIndex = st.MainIndex;

            Debug.Log($"[ApplyMove] P{playerIndex} MainPath: from={from}, roll={roll}, target={targetIndex}, distToEntry={distToEntry}, steps={positions.Count}");

            pawnMover.MoveAlongPositions(pawn, positions, () =>
            {
                _pawnCurrentWaypoint[pawn] = targetIndex;
                positionManager?.RegisterPawnAtWaypoint(pawn, targetIndex);
                ResolveCaptures(pawn);
                _isAnimating = false;
                CancelAnimationSafetyTimer();
                FinishMove();
            });
            return;
        }

        // ==================== HOME'A GIRIS ====================
        int intoHome = roll - distToEntry - 1;
        if (intoHome == 5)
        {
            if (!(_bridge != null && _bridge.IsInRoom) || (_bridge != null && _bridge.IsHost))
            {
                _extraTurnsEarned++;
                Debug.Log($"[ApplyMove] Pawn finished! Extra turns: {_extraTurnsEarned}");
            }
            sfx?.PlayFinish();
        }
        if (_pawnOwner[pawn] != playerIndex)
        {
            _isAnimating = false;
            return;
        }

        if (_pawnCurrentWaypoint.TryGetValue(pawn, out int oldWp2))
        {
            positionManager?.UnregisterPawnFromWaypoint(pawn, oldWp2);
            _pawnCurrentWaypoint.Remove(pawn);
        }

        // Pozisyon listesi: once main path (entry'e kadar), sonra home lane
        var positions2 = new List<Vector3>();
        int cur2 = from;
        for (int i = 0; i < distToEntry; i++)
        {
            cur2 = (cur2 + 1) % pathCount;
            positions2.Add(boardWaypoints.MainPath[cur2].position);
        }

        var homePath2 = GetHomePath(playerIndex);
        for (int i = 0; i <= intoHome; i++)
            positions2.Add(homePath2[i].position);

        // State'i hemen guncelle
        if (distToEntry > 0)
            st.AdvanceMain(distToEntry, pathCount);
        st.EnterHomeLane();
        st.AdvanceHome(intoHome);

        if (intoHome == 5)
        {
            _extraTurnsEarned++;
            Debug.Log($"[ApplyMove] Pawn finished! Extra turns: {_extraTurnsEarned}");
        }

        pawnMover.MoveAlongPositions(pawn, positions2, () =>
    {
        // Home lane pozisyonuna register
        int homeKey = GetHomeLaneKey(playerIndex, intoHome);
        _pawnCurrentWaypoint[pawn] = homeKey;
        positionManager?.RegisterPawnAtWaypoint(pawn, homeKey);

        _isAnimating = false;
        CancelAnimationSafetyTimer();
        FinishMove();
    });
    }

    private void CancelAnimationSafetyTimer()
    {
        if (_animationSafetyTimer != null)
        {
            StopCoroutine(_animationSafetyTimer);
            _animationSafetyTimer = null;
        }
    }

    private IEnumerator AnimationSafetyTimeout(float maxDuration)
    {
        yield return new WaitForSeconds(maxDuration);
        if (_isAnimating)
        {
            Debug.LogWarning("[AnimationSafetyTimeout] Animation stuck! Force resetting.");
            _isAnimating = false;
            _isRollingDice = false;
            _animationSafetyTimer = null;
        }
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;

        if (paused)
        {
            if (btnRollDice != null)
                btnRollDice.interactable = false;

            foreach (var kv in _pawnStates)
                kv.Key.SetClickable(false);

            return;
        }

        if (_gameOver)
        {
            if (btnRollDice != null)
                btnRollDice.interactable = false;

            foreach (var kv in _pawnStates)
                kv.Key.SetClickable(false);

            return;
        }

        if (_phase == TurnPhase.AwaitRoll)
        {
            if (btnRollDice != null)
                btnRollDice.interactable = !_isSpectator && !_isRollingDice && (_state.CurrentTurnPlayerIndex == _localPlayerIndex);

            foreach (var kv in _pawnStates)
                kv.Key.SetClickable(false);

            HighlightActivePlayerPawns();
            return;
        }

        if (_phase == TurnPhase.AwaitMove)
        {
            if (btnRollDice != null)
                btnRollDice.interactable = !_isSpectator && !_isRollingDice && !_isAnimating
                    && (_state.CurrentTurnPlayerIndex == _localPlayerIndex);

            foreach (var kv in _pawnStates)
                kv.Key.SetClickable(false);

            int turn = _state.CurrentTurnPlayerIndex;

            if (turn == _localPlayerIndex)
            {
                var legal = GetLegalMoves(turn, _currentRoll);
                for (int i = 0; i < legal.Count; i++)
                    legal[i].SetClickable(true);

                HighlightLegalMoves(legal);
            }

            return;
        }
    }
    private void Update()
    {
        // Alt+Enter: Fullscreen toggle
        if (Input.GetKey(KeyCode.LeftAlt) && Input.GetKeyDown(KeyCode.Return))
        {
            Screen.fullScreen = !Screen.fullScreen;
        }

        // Turn Timer Tick -- _paused sadece interaction'i engeller, timer her zaman akar
        if (_timerActive && !_gameOver)
        {
            _turnTimer -= Time.deltaTime;
            hudView?.SetTimer(_turnTimer);

            // 3 saniye kala clock sesi cal (bot oyuncular icin calma)
            if (_turnTimer <= 3f && !_clockPlayed && !_botPlayers.Contains(_state.CurrentTurnPlayerIndex))
            {
                _clockPlayed = true;
                sfx?.PlayClock();
            }

            if (_turnTimer <= 0f)
            {
                _timerActive = false;
                hudView?.HideTimer();
                OnTurnTimerExpired();
            }
        }
    }

    // Timer yardimci metotlari
    private void StartTurnTimer(float duration)
    {
        // Bot override KALDIRILDI - sunucu hallediyor (server-side timer)

        _turnTimer = duration;
        _timerActive = true;
        _clockPlayed = false; // Her yeni timer baslatildiginda sifirla
        hudView?.SetTimer(_turnTimer);
        Debug.Log($"[Timer] Started: {duration}s for phase {_phase}");

        // Broadcast to all clients (host only) - sunucu bot override uygular
        if (_net != null && _net.IsHost)
        {
            _net.BroadcastTimerStart(duration, _state.CurrentTurnPlayerIndex);

            // Save timer state with synchronized timestamp
            _net.SaveTimerState(_bridge.ServerTime, duration);
        }
    }

    private void StopTurnTimer(bool broadcast = true)
    {
        _timerActive = false;
        _turnTimer = 0f;
        hudView?.HideTimer();
        sfx?.StopClock(); // Saat sesini durdur

        // Broadcast to all clients (host only)
        if (broadcast && _net != null && _net.IsHost)
        {
            _net.BroadcastTimerStop();

            // Clear timer state from room properties
            _net.ClearTimerState();
        }
    }

    private void OnNetworkTimerStop()
    {
        // Sunucu timer_stop gonderiyor, herkese uygula
        StopTurnTimer(false);
    }

    private void OnTurnTimerExpired()
    {
        if (_isSpectator) return;
        Debug.Log($"[Timer] Expired! Phase={_phase}, Turn=P{_state.CurrentTurnPlayerIndex}");

        // ONLINE modda sunucu server_timer_expired gonderiyor - burada sadece OFFLINE icin
        if (_bridge != null && _bridge.IsInRoom) return;

        // OFFLINE mod (local oyun) - eski mantik aynen kalir
        bool isMyTurn = (_state.CurrentTurnPlayerIndex == _localPlayerIndex);
        if (isMyTurn)
        {
            SetLocalBotMode(true);
            _botPlayers.Add(_state.CurrentTurnPlayerIndex);
            if (_phase == TurnPhase.AwaitRoll) AutoRollDice();
            else if (_phase == TurnPhase.AwaitMove) AutoMovePawn();
            return;
        }

        // Offline disconnected (guvenlik icin)
        _botPlayers.Add(_state.CurrentTurnPlayerIndex);
        if (_phase == TurnPhase.AwaitRoll) AutoRollDice();
        else if (_phase == TurnPhase.AwaitMove) AutoMovePawn();
    }

    // Sunucu timer suresi doldu - connected oyuncu icin
    private void OnServerTimerExpired(int playerIndex)
    {
        if (_gamePaused) return; // Oyun duraklatildiysa bot baslatma
        if (playerIndex != _localPlayerIndex) return;
        if (_isSpectator) return;

        Debug.Log($"[Timer] Server says timer expired for P{playerIndex}, phase={_phase}");
        SetLocalBotMode(true);
        _botPlayers.Add(playerIndex);

        if (_bridge != null && _bridge.IsInRoom)
            _bridge.SendEnterBot(playerIndex);

        if (_phase == TurnPhase.AwaitRoll)
            AutoRollDice();
        else if (_phase == TurnPhase.AwaitMove)
            AutoMovePawn();
    }

    // Sunucu timer suresi doldu - disconnected oyuncu icin (HOST alir)
    private void OnServerTimerExpiredDisconnected(int playerIndex)
    {
        if (_gamePaused) return; // Oyun duraklatildiysa bot baslatma
        if (_bridge == null || !_bridge.IsHost) return;
        if (!_tempDisconnectedPlayers.Contains(playerIndex)) return;

        Debug.Log($"[Timer] Server says timer expired for disconnected P{playerIndex}");
        _botPlayers.Add(playerIndex);
        _bridge.SendEnterBot(playerIndex); // Sunucuya bildir - sonraki turlar 1.5s olsun

        if (_phase == TurnPhase.AwaitRoll) AutoRollDice();
        else if (_phase == TurnPhase.AwaitMove) AutoMovePawn();
    }

    private void AutoRollDice()
    {
        if (_gamePaused) return;
        if (_isRollingDice || _isAnimating) return;
        Debug.Log($"[Timer] Auto-rolling dice for P{_state.CurrentTurnPlayerIndex}");
        StartCoroutine(CoRollDiceAnimated());
    }

    private void AutoMovePawn()
    {
        if (_gamePaused) return;
        if (_isAnimating || _isRollingDice) return;
        int turn = _state.CurrentTurnPlayerIndex;
        var legal = GetLegalMoves(turn, _currentRoll);
        if (legal.Count == 0) return;

        // Rastgele bir legal piyon sec
        PawnView chosen = legal[Random.Range(0, legal.Count)];
        int pawnId = _pawnToId[chosen];
        Debug.Log($"[Timer] Auto-moving pawn {pawnId} for P{turn}");
        _net?.SendMoveRequest(turn, pawnId, _currentRoll);
    }
    private int GetHomeLaneKey(int playerIndex, int homeIndex)
    {
        return PawnPositionManager.GetHomeLaneKey(playerIndex, homeIndex);
    }

    /// <summary>
    /// Oyuncunun evde veya main path'te piyonu var mi?
    /// (Home lane ve finished haric)
    /// </summary>
    private bool HasPawnOutsideHomeLane(int playerIndex)
    {
        var pawns = GetPawnsForTurn(playerIndex);
        foreach (var p in pawns)
        {
            var st = _pawnStates[p];
            if (st.IsAtHome) return true;      // Evde piyon var, 6 ile cikabilir
            if (!st.IsInHomeLane && !st.IsFinished) return true; // Main path'te piyon var
        }
        return false; // Hepsi home lane'de veya bitmis
    }

    private void OnHomeAreaClicked(int playerIndex)
    {
        if (_paused) return;
        if (_gameOver) return;
        if (_currentRoll < 1) return;
        if (_localBotMode) return; // Bot taking over — player must press Take Control first

        // Sadece kendi siran ve kendi rengin
        int turn = _state.CurrentTurnPlayerIndex;
        if (turn != _localPlayerIndex) return;
        if (turn != playerIndex) return;

        // 6 degilse evden cikamaz
        if (_currentRoll != 6) return;

        // AwaitMove fazinda olmali
        if (_phase != TurnPhase.AwaitMove) return;

        // Evdeki ilk legal piyonu bul
        var pawns = GetPawnsForTurn(playerIndex);
        PawnView homePawn = null;

        foreach (var p in pawns)
        {
            if (_pawnStates[p].IsAtHome)
            {
                homePawn = p;
                break;
            }
        }

        if (homePawn == null) return;

        // Legal mi kontrol et
        var legal = GetLegalMoves(turn, _currentRoll);
        if (!legal.Contains(homePawn)) return;

        // Hamleyi gonder
        int pawnId = _pawnToId[homePawn];
        _net?.SendMoveRequest(turn, pawnId, _currentRoll);
    }

    private void OnBoardAreaClicked(Vector2 screenPos)
    {
        if (_paused || _gameOver || _phase != TurnPhase.AwaitMove) return;
        if (_currentRoll < 1 || _isAnimating) return;
        if (_localBotMode) return; // Bot taking over — player must press Take Control first
        if (_state.CurrentTurnPlayerIndex != _localPlayerIndex) return;

        var legal = GetLegalMoves(_state.CurrentTurnPlayerIndex, _currentRoll);
        if (legal.Count == 0) return;

        // En yakin legal piyonu bul (evdekiler haric - HomeAreaClick hallediyor)
        PawnView nearest = null;
        float minDist = float.MaxValue;

        foreach (var pawn in legal)
        {
            if (_pawnStates[pawn].IsAtHome) continue;
            Vector2 pawnScreenPos = RectTransformUtility.WorldToScreenPoint(null, pawn.transform.position);
            float dist = Vector2.Distance(screenPos, pawnScreenPos);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = pawn;
            }
        }

        float maxDist = Screen.height * 0.08f;
        if (nearest != null && minDist < maxDist)
        {
            OnPawnClicked(nearest);
        }
    }

    // ========== BUG 1 FIX: PAWN STATE SERIALIZATION METHODS ==========

    /// <summary>
    /// Serialize all pawn states and save to room properties (host only)
    /// </summary>
    private void SerializeAndSavePawnStates()
    {
        if (_net == null || !(_bridge != null && _bridge.IsHost)) return;

        // Format: "pawnId:zone:mainIndex:homeIndex:isInHomeLane:isFinished;"
        var sb = new System.Text.StringBuilder();

        foreach (var kvp in _pawnToId)
        {
            var pawn = kvp.Key;
            var id = kvp.Value;
            if (!_pawnStates.TryGetValue(pawn, out var state)) continue;

            sb.Append(id).Append(":")
              .Append((int)state.Zone).Append(":")
              .Append(state.MainIndex).Append(":")
              .Append(state.HomeIndex).Append(":")
              .Append(state.IsInHomeLane ? 1 : 0).Append(":")
              .Append(state.IsFinished ? 1 : 0).Append(";");
        }

        _net.SavePawnStates(sb.ToString());
        Debug.Log($"[SerializePawnStates] Saved {_pawnToId.Count} pawns");
    }

    /// <summary>
    /// Restore pawn states from room properties (client only)
    /// </summary>
    private void RestorePawnStatesFromNetwork()
    {
        if (_net == null) return;

        string data = _net.GetPawnStates();
        if (string.IsNullOrEmpty(data))
        {
            Debug.Log("[RestorePawnStates] No pawn state data found");
            return;
        }

        var entries = data.Split(';');
        int restored = 0;

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry)) continue;

            var parts = entry.Split(':');
            if (parts.Length < 6) continue;

            int id = int.Parse(parts[0]);
            if (!_idToPawn.TryGetValue(id, out var pawn)) continue;
            if (!_pawnStates.TryGetValue(pawn, out var state)) continue;

            var zone = (PawnZone)int.Parse(parts[1]);
            int mainIndex = int.Parse(parts[2]);
            int homeIndex = int.Parse(parts[3]);
            bool isInHomeLane = parts[4] == "1";
            bool isFinished = parts[5] == "1";

            // Restore state based on zone
            if (zone == PawnZone.Home)
            {
                state.ReturnHome();
            }
            else if (isFinished || zone == PawnZone.Finished)
            {
                // Pawn is in home lane and finished
                state.EnterHomeLane();
                for (int i = 0; i < 5; i++)
                    state.AdvanceHome(1);
            }
            else if (isInHomeLane || zone == PawnZone.HomeLane)
            {
                // Pawn is in home lane
                state.EnterHomeLane();
                for (int i = 0; i < homeIndex; i++)
                    state.AdvanceHome(1);
            }
            else if (zone == PawnZone.MainPath)
            {
                // Pawn is on main path
                state.EnterMainAt(mainIndex);
            }

            // Update _pawnCurrentWaypoint for correct movement tracking
            int ownerIdx = _pawnOwner[pawn];
            if (_pawnCurrentWaypoint.TryGetValue(pawn, out int oldWp))
                positionManager?.UnregisterPawnFromWaypoint(pawn, oldWp);

            if (zone == PawnZone.MainPath)
            {
                _pawnCurrentWaypoint[pawn] = mainIndex;
                positionManager?.RegisterPawnAtWaypoint(pawn, mainIndex);
            }
            else if (zone == PawnZone.HomeLane && !isFinished)
            {
                int homeKey = GetHomeLaneKey(ownerIdx, homeIndex);
                _pawnCurrentWaypoint[pawn] = homeKey;
                positionManager?.RegisterPawnAtWaypoint(pawn, homeKey);
            }
            else
            {
                _pawnCurrentWaypoint.Remove(pawn);
            }

            // Restore visual position
            Vector3 pos = GetPawnVisualPosition(pawn, state, ownerIdx);
            pawn.SetPosition(pos);
            restored++;
        }

        Debug.Log($"[RestorePawnStates] Restored {restored} pawns from network");
        if (positionManager != null) positionManager.RefreshAllStacks();
    }

    /// <summary>
    /// Calculate visual position for a pawn based on its state
    /// </summary>
    private Vector3 GetPawnVisualPosition(PawnView pawn, PawnState state, int playerIndex)
    {
        if (state.IsAtHome)
        {
            // Get home slot position
            return GetHomePawnPosition(pawn);
        }

        if (state.IsInHomeLane)
        {
            // Get home lane position
            var homePath = GetHomePath(playerIndex);
            if (homePath != null && state.HomeIndex >= 0 && state.HomeIndex < homePath.Count)
                return homePath[state.HomeIndex].position;
        }
        else if (state.Zone == PawnZone.MainPath)
        {
            // Get main path position
            if (state.MainIndex >= 0 && state.MainIndex < boardWaypoints.MainPath.Count)
                return boardWaypoints.MainPath[state.MainIndex].position;
        }

        // Fallback: return home position
        return GetHomePawnPosition(pawn);
    }

    // ========== CHAT ==========

    // Lokal emoji: QuickChatView'dan index gelir, hemen animasyon oynat (aga gitmeden once)
    private void OnLocalEmojiSend(int index)
    {
        var frames = quickChatView != null ? quickChatView.GetFrames(index) : null;
        var localPanel = hudView.GetCornerPanelForPlayer(_localPlayerIndex, _localPlayerIndex);
        if (localPanel != null && frames != null && frames.Length > 0)
            localPanel.ShowAnimatedEmoji(frames);
    }

    private void OnChatSend(string message)
    {
        // Emoji ise lokal animasyon OnLocalEmojiSend'den zaten tetiklendi,
        // burada sadece aga gonderim yapilir.
        if (message.StartsWith("__EMOJI__"))
        {
            // sadece network'e gonder, lokal animasyon zaten OnLocalEmojiSend'de tetiklendi
        }
        else if (message.StartsWith(LudoFriends.Presentation.QuickChatView.QuickPrefix))
        {
            // Quick chat: kendi dilinde float göster
            string indexStr = message[LudoFriends.Presentation.QuickChatView.QuickPrefix.Length..];
            if (int.TryParse(indexStr, out int qIndex))
            {
                string localText = LocalizationManager.GetQuickChat(qIndex);
                var localPanel = hudView.GetCornerPanelForPlayer(_localPlayerIndex, _localPlayerIndex);
                if (localPanel != null) localPanel.ShowEmojiFloat(localText);
            }
        }
        else
        {
            // Serbest metin mesajı
            var localPanel = hudView.GetCornerPanelForPlayer(_localPlayerIndex, _localPlayerIndex);
            if (localPanel != null) localPanel.ShowEmojiFloat(message);
        }
        _net.BroadcastChatMessage(message, _localPlayerIndex);
    }

    private void OnNetworkChatMessage(string message, int senderPlayerIndex)
    {
        var senderPanel = hudView.GetCornerPanelForPlayer(senderPlayerIndex, _localPlayerIndex);
        if (message.StartsWith("__EMOJI__"))
        {
            // Index'i coz, animasyonu oynat
            string indexStr = message["__EMOJI__".Length..].Trim();
            if (int.TryParse(indexStr, out int index))
            {
                var frames = quickChatView != null ? quickChatView.GetFrames(index) : null;
                if (sfx != null && quickChatView != null)
                {
                    var clip = quickChatView.GetAudioClip(index);
                    if (clip != null) sfx.PlayClip(clip);
                }
                if (senderPanel != null)
                {
                    if (frames != null && frames.Length > 0)
                        senderPanel.ShowAnimatedEmoji(frames);
                    else
                        senderPanel.ShowEmojiFloat("🎉"); // frames yüklenemezse fallback
                }
            }
            return; // __EMOJI__ mesajı kesinlikle text branch'e düşmesin
        }
        else if (message.StartsWith(LudoFriends.Presentation.QuickChatView.QuickPrefix))
        {
            // Quick chat: alıcının kendi dilinde göster
            string indexStr = message[LudoFriends.Presentation.QuickChatView.QuickPrefix.Length..];
            if (int.TryParse(indexStr, out int qIndex))
            {
                string localText = LocalizationManager.GetQuickChat(qIndex);
                if (senderPanel != null) senderPanel.ShowEmojiFloat(localText);
                if (chatView != null) chatView.AddMessage(localText, senderPlayerIndex);
            }
        }
        else
        {
            if (senderPanel != null) senderPanel.ShowEmojiFloat(message);
            if (chatView != null) chatView.AddMessage(message, senderPlayerIndex);
        }
    }

    // ==================== GPGS REPORTING ====================

    private void ReportGameToGPGS()
    {
        var gpgs = GPGSManager.Instance;
        if (gpgs == null) return;

        // Herkes için: oynanan oyun sayısını raporla
        gpgs.ReportGamePlayed();

        // 1. sırada bitiren yerel oyuncu mu?
        if (_finishOrder.Count > 0 && _finishOrder[0] == _localPlayerIndex)
        {
            gpgs.ReportWin();
        }
    }
}
