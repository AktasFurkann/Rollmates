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
    private bool _tookManualControl = false; // Take Control sonrasi host timer broadcast'ini yoksay
    private bool _isSpectator = false;
    private readonly List<int> _finishOrder = new List<int>();
    private readonly HashSet<int> _disconnectedPlayers = new HashSet<int>();
    private readonly HashSet<int> _tempDisconnectedPlayers = new HashSet<int>();
    private readonly HashSet<int> _botPlayers = new HashSet<int>();
    private const float BotAutoDelay = 1.5f;

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

    private string TurnName(int index) => LocalizationManager.GetColorName(index);

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

        _bridge = SocketIONetworkBridge.Instance;
        _net = _bridge;

        if (_net != null)
        {
            _net.OnRoll -= OnNetworkRoll;
            _net.OnMove -= OnNetworkMove;
            _net.OnTurn -= OnNetworkTurn;
            _net.OnMoveRequest -= OnNetworkMoveRequest;
            _net.OnRequestAdvanceTurn -= OnNetworkRequestAdvanceTurn;

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
        }

        // Player index + spectator tespiti + initialPlayerCount
        if (_bridge != null && _bridge.IsInRoom)
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
            txtInGameRoomCode.text = _bridge.RoomCode;

        hudView.SetTurn(TurnName(_state.CurrentTurnPlayerIndex), _state.CurrentTurnPlayerIndex, _localPlayerIndex);
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

        // Oyun baslama sesi
        sfx?.PlayGameStart();

        // Ilk sira icin timer baslat -- spectator icin baslatma
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
        if (_net != null)
        {
            _net.OnRoll += OnNetworkRoll;
            _net.OnMove += OnNetworkMove;
            _net.OnTurn += OnNetworkTurn;
            _net.OnMoveRequest += OnNetworkMoveRequest;
        }

        // Player index ve spectator -- InitializeGame 0.5s sonra calisir, property'ler artik kesinlikle sync olmustur.
        if (_bridge != null && _bridge.IsInRoom)
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

        hudView.SetTurn(TurnName(_state.CurrentTurnPlayerIndex), _state.CurrentTurnPlayerIndex, _localPlayerIndex);
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

        if (btnRestart != null)
            btnRestart.onClick.AddListener(OnRestartClicked);

        foreach (var kv in _pawnStates)
            kv.Key.Clicked += OnPawnClicked;

        btnRollDice.onClick.AddListener(OnRollDiceClicked);

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
        InitializeGame();
    }

    private void RefreshLocalization()
    {
        // HUD turn label
        if (hudView != null && _state != null)
            hudView.SetTurn(TurnName(_state.CurrentTurnPlayerIndex), _state.CurrentTurnPlayerIndex, _localPlayerIndex);

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
            // Offline mod: renk adlari
            for (int i = 0; i < 4; i++)
                cornerNames[i] = TurnName(i);
        }

        hudView.SetupPlayerCorners(cornerNames, _localPlayerIndex, PlayerCount);
    }

    private void UpdateTurnUI()
    {
        hudView.SetTurn(TurnName(_state.CurrentTurnPlayerIndex), _state.CurrentTurnPlayerIndex, _localPlayerIndex);

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
        }

        if (_bridge != null)
        {
            _bridge.OnHostChanged -= OnHostChanged;
            _bridge.OnDisconnectedEvent -= OnBridgeDisconnected;
            _bridge.OnPlayerLeft -= OnBridgePlayerLeft;
            _bridge.OnPlayerJoined -= OnBridgePlayerJoined;
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
    }

    // Bug 1 fix: Reconnection support - sync state when player joins/rejoins
    // NOTE: This is now called by bridge reconnection events or can be called manually
    private void OnJoinedRoom()
    {
        // Reconnect sonrasi: disconnect panelini kapat ve oyunu devam ettir
        if (_reconnectCoroutine != null)
        {
            StopCoroutine(_reconnectCoroutine);
            _reconnectCoroutine = null;
        }
        if (panelDisconnect != null) panelDisconnect.SetActive(false);
        if (btnReconnect != null) btnReconnect.gameObject.SetActive(false);

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
                hudView.SetTurn(TurnName(turn), turn, _localPlayerIndex);
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
                hudView.SetTurn(TurnName(currentTurn), currentTurn, _localPlayerIndex);
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
        _gameOver = true;

        if (panelDisconnect != null)
        {
            panelDisconnect.SetActive(true);
            if (txtDisconnectMessage != null)
            {
                _disconnectStatus = DisconnectStatus.Disconnected;
                txtDisconnectMessage.text = LocalizationManager.Get("disconnected");
            }
            if (btnReconnect != null)
                btnReconnect.gameObject.SetActive(true);
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
        // Socket.IO handles reconnection automatically
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

        hudView.SetTurn(TurnName(nextTurn), nextTurn, _localPlayerIndex);
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
        // Piyonlari kaldirma, finishOrder'a ekleme, game over kontrolu yapma
        // Sadece turunu atla ve reconnect'i bekle
        if (!payload.isPermanent)
        {
            _tempDisconnectedPlayers.Add(leftPlayerIndex);
            _botPlayers.Remove(leftPlayerIndex);
            Debug.Log($"[OnBridgePlayerLeft] P{leftPlayerIndex} temporarily disconnected. Waiting for reconnect...");

            // Stuck state'leri temizle
            _isAnimating = false;
            _isRollingDice = false;

            // Sirasi gelmis oyuncuysa -> turu atla
            if (_bridge != null && _bridge.IsHost
                && (_state.CurrentTurnPlayerIndex == leftPlayerIndex
                    || _tempDisconnectedPlayers.Contains(_state.CurrentTurnPlayerIndex)))
            {
                AdvanceTurnSkipDisconnected();
            }

            UpdateScoreboard();
            return;
        }

        // ── KALICI AYRILMA (isPermanent = true) ──
        // 60s doldu veya lobby'den cikti: piyonlari kaldir, game over kontrol et

        // Gecici listeden cikar (onceden eklenmisse)
        _tempDisconnectedPlayers.Remove(leftPlayerIndex);

        // Oyunu mesru sekilde bitirmis oyuncu mu? (finishOrder'da ama disconnectedPlayers'da degil)
        bool alreadyLegitimatelyFinished = _finishOrder.Contains(leftPlayerIndex)
                                           && !_disconnectedPlayers.Contains(leftPlayerIndex);

        // Cikan oyuncuyu henuz siralamada degilse sonuncu yap
        if (!_finishOrder.Contains(leftPlayerIndex))
            _finishOrder.Add(leftPlayerIndex);

        // Bug 2 Fix: Mesru finisher'i _disconnectedPlayers'a ekleme -> scoreboard korunsun
        if (!alreadyLegitimatelyFinished)
        {
            _disconnectedPlayers.Add(leftPlayerIndex);
            _botPlayers.Remove(leftPlayerIndex);
            // Cikan oyuncunun piyonlarini tahtadan kaldir
            RemoveDisconnectedPlayerPawns(leftPlayerIndex);
        }
        else
        {
            Debug.Log($"[OnBridgePlayerLeft] P{leftPlayerIndex} already legitimately finished. Preserving rank.");
            _botPlayers.Remove(leftPlayerIndex);
        }

        // Kac aktif (bitmemis) oyuncu kaldi?
        int totalPlayers = _initialPlayerCount;
        int remainingPlayers = 0;
        int lastRemainingIndex = -1;
        for (int i = 0; i < totalPlayers; i++)
        {
            if (!_finishOrder.Contains(i))
            {
                remainingPlayers++;
                lastRemainingIndex = i;
            }
        }

        // Sadece 1 kisi kaldiysa -> o 1., oyun biter
        if (remainingPlayers <= 1 && lastRemainingIndex >= 0)
        {
            // Kalan oyuncuyu 1. siraya koy (listenin basina)
            _finishOrder.Insert(0, lastRemainingIndex);
            _gameOver = true;
            if (sfx != null) sfx.PlayWin();

            if (btnRollDice != null)
                btnRollDice.interactable = false;

            StopTurnTimer();
            ClearAllHighlights();
            Debug.Log($"[OnBridgePlayerLeft] Only P{lastRemainingIndex} remains, game over!");
        }

        // Scoreboard guncelle
        UpdateScoreboard();

        // State kaydet
        if (_bridge != null && _bridge.IsHost)
            _net?.SaveFinishOrder(_finishOrder.ToArray());

        // Stuck state'leri temizle
        _isAnimating = false;
        _isRollingDice = false;

        // Oyun devam ediyorsa ve cikan kisinin sirasiysa -> atla
        if (!_gameOver)
        {
            if (_bridge != null && _bridge.IsHost
                && (_disconnectedPlayers.Contains(_state.CurrentTurnPlayerIndex)
                    || _tempDisconnectedPlayers.Contains(_state.CurrentTurnPlayerIndex)))
            {
                AdvanceTurnSkipDisconnected();
            }
            else if (_bridge == null || !_bridge.IsHost)
            {
                // Non-host: Cikan oyuncu siradaki ise, host'un BroadcastTurn gondermesini bekle
                _isAnimating = false;
                _isRollingDice = false;
                Debug.Log($"[OnBridgePlayerLeft] Non-host: cleared stuck flags, waiting for host BroadcastTurn");
            }
        }
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

        hudView.SetTurn(TurnName(nextTurn), nextTurn, _localPlayerIndex);
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
            _net.BroadcastRoll(turn, roll);

            // Host saves state immediately
            if (_bridge.IsHost)
            {
                _net.SyncGameState(turn, roll, (int)_phase, _consecutiveSixes, _extraTurnsEarned);
            }
        }

        hudView.SetTurn(TurnName(_state.CurrentTurnPlayerIndex), _state.CurrentTurnPlayerIndex, _localPlayerIndex);
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
                {
                    bool isMyTurn = (_state.CurrentTurnPlayerIndex == _localPlayerIndex);
                    btnRollDice.interactable = isMyTurn && !_gameOver;
                }
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
                {
                    bool isMyTurn = (_state.CurrentTurnPlayerIndex == _localPlayerIndex);
                    btnRollDice.interactable = isMyTurn && !_gameOver;
                }
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

        // Bug 1 fix: Persist state after move validation
        _net.SyncGameState(_state.CurrentTurnPlayerIndex, _currentRoll, (int)_phase, _consecutiveSixes, _extraTurnsEarned);
        SerializeAndSavePawnStates();
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
    hudView.SetTurn(TurnName(playerIndex), playerIndex, _localPlayerIndex);

    // Cift zar atmayi engelle
    if (btnRollDice != null)
        btnRollDice.interactable = false;

    // If we are not the one who initiated the local roll, play animation
    bool amIRollingLocally = (playerIndex == _localPlayerIndex && _isRollingDice);

    if (!amIRollingLocally)
    {
        // Cancel any existing remote animation to prevent overlap/desync
        StopCoroutine("CoRemoteDiceAnimation");
        StartCoroutine(CoRemoteDiceAnimation(playerIndex, roll));
    }

    // Eger Host degilsek ve sira bizdeyse, ama biz atmadiysa (Timeout vs)
    if (playerIndex == _localPlayerIndex && !amIRollingLocally && _consecutiveSixes < 3)
    {
         // Logic handled in CoRemoteDiceAnimation
    }

    // Host: uzak oyuncu icin hamle timer'i baslat
    if (_bridge != null && _bridge.IsInRoom && _bridge.IsHost && playerIndex != _localPlayerIndex && _consecutiveSixes < 3)
    {
        // Add delay for animation
        StartCoroutine(StartTimerAfterDelay(diceRollDuration + 0.5f, playerIndex, roll));
    }
}

private IEnumerator StartTimerAfterDelay(float delay, int playerIndex, int roll)
{
    yield return new WaitForSeconds(delay);

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
    _currentRoll = -1;
    _tookManualControl = false; // Sira degisiminde manual control flag sifirla

    hudView.SetDice(-1);
    hudView.SetTurn(TurnName(nextPlayerIndex), nextPlayerIndex, _localPlayerIndex);

    if (btnRollDice != null)
    {
        bool isMyTurn = (nextPlayerIndex == _localPlayerIndex);
        btnRollDice.interactable = isMyTurn && !_gameOver;
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

    // FIX: Client'lar her sira degisiminde host state'ini dogrula (desync onleme)
    if (_bridge != null && !_bridge.IsHost && _bridge.IsInRoom)
    {
        StartCoroutine(VerifyPawnStatesAfterDelay(0.5f));
    }
}

    private IEnumerator VerifyPawnStatesAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        RestorePawnStatesFromNetwork();
    }

    // ========== TIMER NETWORK HANDLERS (Fix 1) ==========

    private void OnNetworkTimerStart(float duration)
    {
        // Sira bizdeyse ve bot degilsek, kendi lokal timer'imizi kullan
        if (_state.CurrentTurnPlayerIndex == _localPlayerIndex && !_localBotMode)
        {
            Debug.Log($"[Timer] Ignoring host timer ({duration}s) - local player is active (not bot)");
            return;
        }

        // Start local timer display for all clients
        _timerActive = true;
        _turnTimer = duration;
    }



    private void FinishMove()
    {
        StopTurnTimer(); // Timer durdur
        _phase = TurnPhase.AwaitRoll;
        _isRollingDice = false;

        // Bug 3 fix: Reset move tracking for next turn
        _lastProcessedPawnId = -1;

        foreach (var kv in _pawnStates)
            kv.Key.SetClickable(false);

        // ==================== ONLINE ====================
        if (_bridge != null && _bridge.IsInRoom)
        {
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
                // Client: UI temizle, ama butonu koru korune kapatma
                _currentRoll = -1;
                hudView.SetDice(-1);

                // Bitiren oyuncu extra turn almasin
                if (_finishOrder.Contains(_localPlayerIndex))
                    _extraTurnsEarned = 0;

                // Eger Turn RPC zaten geldi ve sira bizdeyse, butonu acik birak
                if (btnRollDice != null)
                {
                    bool isMyTurn = (_state.CurrentTurnPlayerIndex == _localPlayerIndex);
                    btnRollDice.interactable = isMyTurn && !_gameOver && !_finishOrder.Contains(_localPlayerIndex);
                    Debug.Log($"[FinishMove] Client: turn={_state.CurrentTurnPlayerIndex}, myTurn={isMyTurn}, button={btnRollDice.interactable}");
                }
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

            if (btnRollDice != null)
                btnRollDice.interactable = !_gameOver;

            HighlightActivePlayerPawns();
            StartTurnTimer(rollTimeLimit);
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
            hudView.SetTurn(TurnName(_state.CurrentTurnPlayerIndex), _state.CurrentTurnPlayerIndex, _localPlayerIndex);

            if (btnRollDice != null)
                btnRollDice.interactable = !_gameOver;

            StartTurnTimer(rollTimeLimit); // Offline modda sira degisiminde timer baslat
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

    private void UpdateScoreboard()
    {
        if (scoreboardPanel == null) return;

        // Gosterim sirasini olustur: 1) Mesru bitirenler, 2) ???, 3) Disconnect olanlar
        var displayEntries = new List<string>();

        // 1. Mesru bitirenler (disconnect olmayan, _finishOrder sirasiyla)
        foreach (int idx in _finishOrder)
        {
            if (!_disconnectedPlayers.Contains(idx))
                displayEntries.Add(TurnName(idx));
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
                displayEntries.Add(TurnName(idx));
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

        // Spectator icin oyun bitmeden paneli acma
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
        if (_bridge != null && _bridge.IsInRoom)
        {
            _isLeavingToMainMenu = true;
            _isIntentionalDisconnect = true;
            _bridge.LeaveRoom(true);
            SceneManager.LoadScene(0);
            return;
        }

        // Odada degilsek direkt sahne yukle
        SceneManager.LoadScene(0);
    }

    // ==================== BOT MODE ====================

    private void SetLocalBotMode(bool active)
    {
        _localBotMode = active;
        if (btnTakeControl != null)
            btnTakeControl.gameObject.SetActive(active);
    }

    private void OnTakeControlClicked()
    {
        SetLocalBotMode(false);
        _botPlayers.Remove(_localPlayerIndex);
        _tookManualControl = true;

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

    private void OnNetworkExitBot(int playerIndex)
    {
        _botPlayers.Remove(playerIndex);
        Debug.Log($"[BotMode] P{playerIndex} exited bot mode (network).");

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
        // Bot oyuncu icin kisa auto-play suresi
        if (_bridge != null && _bridge.IsInRoom && _bridge.IsHost
            && _botPlayers.Contains(_state.CurrentTurnPlayerIndex))
        {
            duration = BotAutoDelay;
        }

        _turnTimer = duration;
        _timerActive = true;
        _clockPlayed = false; // Her yeni timer baslatildiginda sifirla
        hudView?.SetTimer(_turnTimer);
        Debug.Log($"[Timer] Started: {duration}s for phase {_phase}");

        // Broadcast to all clients (host only)
        if (_net != null && _net.IsHost)
        {
            _net.BroadcastTimerStart(duration);

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
        // Sira bizdeyse ve bot degilsek, kendi lokal timer'imizi kullan
        if (_state.CurrentTurnPlayerIndex == _localPlayerIndex && !_localBotMode)
        {
            Debug.Log("[Timer] Ignoring host timer stop - local player is active (not bot)");
            return;
        }

        // Stop local timer display for all clients
        StopTurnTimer(false);
    }

    private void OnTurnTimerExpired()
    {
        if (_isSpectator) return;
        Debug.Log($"[Timer] Expired! Phase={_phase}, Turn=P{_state.CurrentTurnPlayerIndex}");

        bool isMyTurn = (_state.CurrentTurnPlayerIndex == _localPlayerIndex);

        // Kendi siramsa: lokal bot modu + otomatik oyna (host veya client fark etmez)
        if (isMyTurn)
        {
            SetLocalBotMode(true);
            _botPlayers.Add(_state.CurrentTurnPlayerIndex);

            if (_phase == TurnPhase.AwaitRoll)
                AutoRollDice();
            else if (_phase == TurnPhase.AwaitMove)
                AutoMovePawn();
            return;
        }

        // Sira bizde degil -> sadece host devam eder (bot/disconnected oyuncu yonetimi)
        if (_bridge != null && _bridge.IsInRoom && !_bridge.IsHost) return;

        // Host: siradaki oyuncuyu bot olarak isaretle (kisa timer icin)
        _botPlayers.Add(_state.CurrentTurnPlayerIndex);

        // Bagli oyuncular kendi bot modunu kendileri yonetiyor (yukaridaki isMyTurn blogu).
        // Host sadece gecici kopuk (disconnected) oyuncular icin auto-play yapar.
        if (!_tempDisconnectedPlayers.Contains(_state.CurrentTurnPlayerIndex)) return;

        if (_phase == TurnPhase.AwaitRoll)
            AutoRollDice();
        else if (_phase == TurnPhase.AwaitMove)
            AutoMovePawn();
    }

    private void AutoRollDice()
    {
        if (_isRollingDice || _isAnimating) return;
        Debug.Log($"[Timer] Auto-rolling dice for P{_state.CurrentTurnPlayerIndex}");
        StartCoroutine(CoRollDiceAnimated());
    }

    private void AutoMovePawn()
    {
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
        // Metin ise lokal panelde float goster.
        if (!message.StartsWith("__EMOJI__"))
        {
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
            string indexStr = message["__EMOJI__".Length..];
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
                }
            }
        }
        else
        {
            if (senderPanel != null) senderPanel.ShowEmojiFloat(message);
            if (chatView != null) chatView.AddMessage(message, senderPlayerIndex);
        }
    }
}
