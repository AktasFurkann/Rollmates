using System.Collections;
using System.Collections.Generic;
using System.Linq; // ✅ Bug 2 fix: LINQ for deduplication cleanup
using LudoFriends.Core;
using LudoFriends.Gameplay;
using LudoFriends.Presentation;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using LudoFriends.Networking;
using Photon.Pun;

public class GameBootstrapper : MonoBehaviourPunCallbacks // ✅ Bug 1 fix: reconnection support
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
    [SerializeField] private TMPro.TextMeshProUGUI[] scoreboardTexts; // 4 elemanlı
    [SerializeField] private Button btnScoreboardClose;   // X butonu
    [SerializeField] private Button btnMainMenu;          // Ana Menü butonu

    [Header("Disconnect UI")]
    [SerializeField] private GameObject panelDisconnect;
    [SerializeField] private TMPro.TextMeshProUGUI txtDisconnectMessage;
    [SerializeField] private TMPro.TextMeshProUGUI txtDisconnectCountdown;
    [SerializeField] private Button btnDisconnectMainMenu;
    [SerializeField] private Button btnReconnect;

    [Header("Bot Mode UI")]
    [SerializeField] private Button btnTakeControl;

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
    [SerializeField] private MonoBehaviour networkBehaviour;
    private IGameNetwork _net;
    private PhotonNetworkBridge _photon;

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
            if (PhotonNetwork.InRoom)
                return PhotonNetwork.CurrentRoom.PlayerCount;
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
    private readonly List<int> _finishOrder = new List<int>();
    private readonly HashSet<int> _disconnectedPlayers = new HashSet<int>();
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
    private bool _clockPlayed = false; // ✅ 3 saniye sesi tekrar çalmasın diye flag

    private readonly string[] _turnNames = { "Kırmızı", "Sarı", "Yeşil", "Mavi" };

    private readonly Dictionary<int, PawnView> _idToPawn = new Dictionary<int, PawnView>();
    private readonly Dictionary<PawnView, int> _pawnToId = new Dictionary<PawnView, int>();
    private int _nextPawnId = 1;

    // ✅ Bug 2 & 3 fixes: Move deduplication and rapid click protection
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

    // ✅ Yeni: Her oyuncunun hangi player index'i olduğunu tutan map
    private int _localPlayerIndex = -1;
    private int _initialPlayerCount;

    private void Awake()
    {
        _state = new GameState();
        _dice = new DiceService();

        _net = networkBehaviour as IGameNetwork;
        _photon = networkBehaviour as PhotonNetworkBridge;

        if (_photon != null)
        {
            _photon.OnRoll -= OnNetworkRoll;
            _photon.OnMove -= OnNetworkMove;
            _photon.OnTurn -= OnNetworkTurn;
            _photon.OnMoveRequest -= OnNetworkMoveRequest;
            _photon.OnRequestAdvanceTurn -= OnNetworkRequestAdvanceTurn;

            _photon.OnRoll += OnNetworkRoll;
            _photon.OnMove += OnNetworkMove;
            _photon.OnTurn += OnNetworkTurn;
            _photon.OnMoveRequest += OnNetworkMoveRequest;
            _photon.OnRequestAdvanceTurn += OnNetworkRequestAdvanceTurn;
            _photon.OnChatMessage += OnNetworkChatMessage;
        }

        // Player index belirleme (rotasyondan ÖNCE gerekli)
        if (PhotonNetwork.InRoom)
        {
            _localPlayerIndex = PhotonNetwork.LocalPlayer.ActorNumber - 1;

            if (_localPlayerIndex >= PlayerCount)
                _localPlayerIndex = PlayerCount - 1;

            Debug.Log($"[GameBootstrapper] ActorNumber={PhotonNetwork.LocalPlayer.ActorNumber}, PlayerIndex={_localPlayerIndex}, Color={_turnNames[_localPlayerIndex]}");
        }
        else
        {
            _localPlayerIndex = 0;
            Debug.Log("[GameBootstrapper] Offline mode");
        }

        // Tahta rotasyonu (pozisyon cache'lemeden ÖNCE)
        if (boardRotator != null && _localPlayerIndex > 0)
        {
            boardRotator.ApplyRotation(_localPlayerIndex);
            Canvas.ForceUpdateCanvases();
            Debug.Log($"[GameBootstrapper] Board rotated {_localPlayerIndex * 90f}° for player {_turnNames[_localPlayerIndex]}");
        }

        // Waypoint pozisyonlarını önbelleğe al (artık döndürülmüş pozisyonları okur)
        if (positionManager != null)
        {
            positionManager.CacheWaypointPositions(boardWaypoints.MainPath);
            positionManager.CacheHomeLanePositions(0, boardWaypoints.HomeR);
            positionManager.CacheHomeLanePositions(1, boardWaypoints.HomeY); // 1 = Yellow
            positionManager.CacheHomeLanePositions(2, boardWaypoints.HomeG); // 2 = Green
            positionManager.CacheHomeLanePositions(3, boardWaypoints.HomeB);
        }

        _initialPlayerCount = PlayerCount;

        hudView.SetTurn(_turnNames[_state.CurrentTurnPlayerIndex], _state.CurrentTurnPlayerIndex, _localPlayerIndex);
        hudView.SetDice(-1);

        // Oyuncu köşe panellerini kur
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

        // Piyon sprite'larını ters döndür (dik kalsınlar)
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
            bool isMyTurn = (_state.CurrentTurnPlayerIndex == _localPlayerIndex);
            btnRollDice.interactable = isMyTurn && !_gameOver;
            Debug.Log($"[Awake] FirstTurn={_state.CurrentTurnPlayerIndex}, MyTurn={isMyTurn}, ButtonActive={btnRollDice.interactable}");
        }

        HighlightActivePlayerPawns();

        // ✅ Oyun başlama sesi
        sfx?.PlayGameStart();

        // ✅ İlk sıra için timer başlat (online + offline)
        StartTurnTimer(rollTimeLimit);
    }

    // ========== TIMER NETWORK EVENT SUBSCRIPTIONS (Fix 1) ==========

    public override void OnEnable()
    {
        base.OnEnable();

        if (_photon != null)
        {
            _photon.OnTimerStart += OnNetworkTimerStart;
            _photon.OnTimerStop += OnNetworkTimerStop;
        }
    }

    public override void OnDisable()
    {
        base.OnDisable();

        if (_photon != null)
        {
            _photon.OnTimerStart -= OnNetworkTimerStart;
            _photon.OnTimerStop -= OnNetworkTimerStop;
        }
    }

    // ✅ YENİ metod
    private void OnNetworkRequestAdvanceTurn()
    {
        Debug.Log("[OnNetworkRequestAdvanceTurn] Received from client");

        // ✅ Extra turn varsa aynı oyuncu devam
        if (_extraTurnsEarned > 0)
        {
            _extraTurnsEarned--;
            Debug.Log($"[OnNetworkRequestAdvanceTurn] Extra turn! Remaining: {_extraTurnsEarned}");
            _photon.BroadcastTurn(_state.CurrentTurnPlayerIndex);
            return;
        }

        AdvanceTurnInternalOnly();
    }

    private void InitializeGame()
    {
        if (_photon != null)
        {
            _photon.OnRoll += OnNetworkRoll;
            _photon.OnMove += OnNetworkMove;
            _photon.OnTurn += OnNetworkTurn;
            _photon.OnMoveRequest += OnNetworkMoveRequest;
        }

        // Player index belirleme
        if (PhotonNetwork.InRoom)
        {
            _localPlayerIndex = PhotonNetwork.LocalPlayer.ActorNumber - 1;

            if (_localPlayerIndex >= PlayerCount)
                _localPlayerIndex = PlayerCount - 1;

            Debug.Log($"[GameBootstrapper] ActorNumber={PhotonNetwork.LocalPlayer.ActorNumber}, PlayerIndex={_localPlayerIndex}, Color={_turnNames[_localPlayerIndex]}");
        }
        else
        {
            _localPlayerIndex = 0;
            Debug.Log("[GameBootstrapper] Offline mode");
        }

        // Tahta rotasyonu (pozisyon cache'lemeden ÖNCE)
        if (boardRotator != null && _localPlayerIndex > 0)
        {
            boardRotator.ApplyRotation(_localPlayerIndex);
            Canvas.ForceUpdateCanvases();
            Debug.Log($"[InitializeGame] Board rotated {_localPlayerIndex * 90f}° for player {_turnNames[_localPlayerIndex]}");
        }

        // Pozisyonları yeniden cache'le (döndürülmüş haliyle)
        if (positionManager != null)
        {
            positionManager.CacheWaypointPositions(boardWaypoints.MainPath);
            positionManager.CacheHomeLanePositions(0, boardWaypoints.HomeR);
            positionManager.CacheHomeLanePositions(1, boardWaypoints.HomeY); // 1 = Yellow
            positionManager.CacheHomeLanePositions(2, boardWaypoints.HomeG); // 2 = Green
            positionManager.CacheHomeLanePositions(3, boardWaypoints.HomeB);
        }

        _initialPlayerCount = PlayerCount;

        hudView.SetTurn(_turnNames[_state.CurrentTurnPlayerIndex], _state.CurrentTurnPlayerIndex, _localPlayerIndex);
        hudView.SetDice(-1);

        pawnSpawner.enabled = true;

        _redPawns = pawnSpawner.SpawnColor(homeSlots.R, redPawnSprite, Color.white);
        _greenPawns = pawnSpawner.SpawnColor(homeSlots.G, greenPawnSprite, Color.white);
        _yellowPawns = pawnSpawner.SpawnColor(homeSlots.Y, yellowPawnSprite, Color.white);
        _bluePawns = pawnSpawner.SpawnColor(homeSlots.B, bluePawnSprite, Color.white);

        // Piyon sprite'larını ters döndür
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

        // Sahnede PhotonNetworkBridge'i bul
        _photon = FindObjectOfType<PhotonNetworkBridge>();

        if (_photon == null)
        {
            Debug.LogError("[GameBootstrapper] NetworkRoot not found!");
            yield break;
        }

        _net = _photon;
        InitializeGame();
    }

    // ✅ UpdateTurnUI artık sadece local operasyonlarda kullanılacak
    private void SetupPlayerCornerPanels()
    {
        string[] cornerNames = new string[4];

        if (PhotonNetwork.InRoom)
        {
            // Önce hepsini renk adıyla doldur (boş kalmasın)
            for (int i = 0; i < 4; i++)
                cornerNames[i] = _turnNames[i];

            // Photon'daki gerçek NickName varsa üzerine yaz
            foreach (var kv in PhotonNetwork.CurrentRoom.Players)
            {
                int idx = kv.Value.ActorNumber - 1;
                if (idx >= 0 && idx < 4)
                {
                    string nick = kv.Value.NickName;
                    if (!string.IsNullOrEmpty(nick))
                        cornerNames[idx] = nick;
                }
            }
        }
        else
        {
            // Offline mod: renk adları
            for (int i = 0; i < 4; i++)
                cornerNames[i] = _turnNames[i];
        }

        hudView.SetupPlayerCorners(cornerNames, _localPlayerIndex, PlayerCount);
    }

    private void UpdateTurnUI()
    {
        hudView.SetTurn(_turnNames[_state.CurrentTurnPlayerIndex], _state.CurrentTurnPlayerIndex, _localPlayerIndex);

        if (btnRollDice != null)
            btnRollDice.interactable = (_state.CurrentTurnPlayerIndex == _localPlayerIndex)
                && !_isRollingDice && !_gameOver && !_isAnimating; // ✅
    }

    private void OnDestroy()
    {
        if (_photon != null)
        {
            _photon.OnRoll -= OnNetworkRoll; // ✅ Düzeltildi (+ değil -)
            _photon.OnMove -= OnNetworkMove;
            _photon.OnTurn -= OnNetworkTurn;
            _photon.OnMoveRequest -= OnNetworkMoveRequest;
            _photon.OnChatMessage -= OnNetworkChatMessage;
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

    // ✅ Bug 1 fix: Reconnection support - sync state when player joins/rejoins
    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();

        // Reconnect sonrası: disconnect panelini kapat ve oyunu devam ettir
        if (_reconnectCoroutine != null)
        {
            StopCoroutine(_reconnectCoroutine);
            _reconnectCoroutine = null;
        }
        if (panelDisconnect != null) panelDisconnect.SetActive(false);
        if (btnReconnect != null) btnReconnect.gameObject.SetActive(false);
        _gameOver = false;

        // Only restore state for non-host players (host already has correct state)
        if (PhotonNetwork.IsMasterClient) return;

        // Try to restore game state from room properties
        if (_photon != null && _photon.TryGetGameState(
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
                hudView.SetTurn(_turnNames[turn], turn, _localPlayerIndex);
                if (roll > 0)
                    hudView.SetDice(roll);
                else
                    hudView.SetDice(-1);
            }

            // Update button state
            if (btnRollDice != null)
            {
                bool isMyTurn = (turn == _localPlayerIndex);
                btnRollDice.interactable = isMyTurn && _phase == TurnPhase.AwaitRoll && !_gameOver;
            }

            // Restore pawn states
            RestorePawnStatesFromNetwork();

            // Restore finish order (scoreboard)
            var savedFinishOrder = _photon.GetFinishOrder();
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
                    // ✅ MODIFIED (Fix 2): Calculate remaining time from persisted state
                    float timerDuration = moveTimeLimit;

                    if (_photon != null && _photon.TryGetTimerState(out double startTime, out float savedDuration))
                    {
                        double elapsed = PhotonNetwork.Time - startTime;
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
                    HighlightLegalMoves(legal);      // ✅ Enhancement 2: Visual pulse animation for reconnection
                    SetOnlyLegalClickable(legal);
                }
            }
        }
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        base.OnMasterClientSwitched(newMasterClient);
        Debug.Log($"[OnMasterClientSwitched] New host: {newMasterClient.ActorNumber}, IsLocal={newMasterClient.IsLocal}");

        if (_gameOver) return;

        // Stuck state'leri temizle
        _isAnimating = false;
        _isRollingDice = false;
        _botPlayers.Clear(); // Host migration sonrası bot listesini sıfırla

        // Yeni host ise, host sorumluluklarını devral
        if (newMasterClient.IsLocal)
        {
            Debug.Log("[OnMasterClientSwitched] I am the new host. Taking over responsibilities.");

            // Tüm coroutine'leri durdur (eski host'un roll/move animasyonları)
            StopAllCoroutines();

            // Disconnected oyuncuları güncelle (odada olmayanları bul)
            RefreshDisconnectedPlayers();

            // Phase ve state'i resetle
            _phase = TurnPhase.AwaitRoll;
            _currentRoll = -1;
            _consecutiveSixes = 0;
            _extraTurnsEarned = 0;

            // ✅ MoveId çakışmasını önle: Yeni host'un moveId'leri
            // eski host'un broadcast ettiği moveId'lerle çakışmamalı
            _processedMoves.Clear();
            _nextMoveId = 1000 + UnityEngine.Random.Range(0, 1000);
            _lastProcessedPawnId = -1;

            // Mevcut sıradaki oyuncu disconnected ise turu ilerlet
            if (_disconnectedPlayers.Contains(_state.CurrentTurnPlayerIndex)
                || _finishOrder.Contains(_state.CurrentTurnPlayerIndex))
            {
                AdvanceTurnAfterHostMigration();
            }
            else
            {
                // Sıradaki oyuncu hâlâ aktifse, UI güncelle ve timer başlat
                int currentTurn = _state.CurrentTurnPlayerIndex;
                hudView.SetTurn(_turnNames[currentTurn], currentTurn, _localPlayerIndex);
                hudView.SetDice(-1);

                if (btnRollDice != null)
                    btnRollDice.interactable = (currentTurn == _localPlayerIndex) && !_gameOver;

                HighlightActivePlayerPawns();
                StartTurnTimer(rollTimeLimit);

                // State'i kaydet
                _photon?.SyncGameState(currentTurn, -1, (int)TurnPhase.AwaitRoll, 0, 0);
                SerializeAndSavePawnStates();
            }
        }
    }

    public override void OnDisconnected(Photon.Realtime.DisconnectCause cause)
    {
        Debug.LogWarning($"[GameBootstrapper] Disconnected: {cause}");

        // Kasıtlı çıkış (Exit butonu) → reconnect başlatma
        if (_isIntentionalDisconnect)
        {
            _isIntentionalDisconnect = false;
            return;
        }

        _timerActive = false;
        _gameOver = true;

        if (panelDisconnect != null)
        {
            panelDisconnect.SetActive(true);
            if (txtDisconnectMessage != null)
                txtDisconnectMessage.text = "Bağlantı kesildi!";
            if (btnReconnect != null)
                btnReconnect.gameObject.SetActive(true);
        }

        _reconnectCoroutine = StartCoroutine(ReconnectCountdown());
    }

    private IEnumerator ReconnectCountdown()
    {
        float timeLeft = 60f;
        var wait = new WaitForSeconds(1f);
        PhotonNetwork.ReconnectAndRejoin();

        while (timeLeft > 0)
        {
            if (txtDisconnectCountdown != null)
                txtDisconnectCountdown.text = $"Yeniden bağlanılıyor... ({Mathf.CeilToInt(timeLeft)}s)";

            yield return wait;
            timeLeft -= 1f;
        }

        if (txtDisconnectCountdown != null)
            txtDisconnectCountdown.text = "Bağlantı kurulamadı.";
        if (btnReconnect != null)
            btnReconnect.gameObject.SetActive(false);
    }

    private void OnReconnectClicked()
    {
        if (txtDisconnectCountdown != null)
            txtDisconnectCountdown.text = "Bağlanılıyor...";
        if (btnReconnect != null)
            btnReconnect.interactable = false;
        PhotonNetwork.ReconnectAndRejoin();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
            Debug.Log("[GameBootstrapper] App paused (background)");
        else
            Debug.Log("[GameBootstrapper] App resumed (foreground)");
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        base.OnJoinRoomFailed(returnCode, message);
        Debug.LogWarning($"[GameBootstrapper] OnJoinRoomFailed: {returnCode} - {message}");

        // Reconnect bağlamında başarısız: butonu tekrar aktif et
        if (panelDisconnect != null && panelDisconnect.activeSelf)
        {
            if (txtDisconnectCountdown != null)
                txtDisconnectCountdown.text = "Yeniden bağlanılamadı. Tekrar dene.";
            if (btnReconnect != null)
            {
                btnReconnect.gameObject.SetActive(true);
                btnReconnect.interactable = true;
            }
        }
    }

    /// <summary>
    /// Odadaki aktif oyuncuları kontrol ederek _disconnectedPlayers set'ini günceller.
    /// Host migration sonrası çağrılır.
    /// </summary>
    private void RefreshDisconnectedPlayers()
    {
        HashSet<int> activeActors = new HashSet<int>();
        foreach (var player in PhotonNetwork.PlayerList)
        {
            activeActors.Add(player.ActorNumber - 1);
        }

        for (int i = 0; i < _initialPlayerCount; i++)
        {
            if (!activeActors.Contains(i) && !_disconnectedPlayers.Contains(i))
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
        if (PhotonNetwork.IsMasterClient)
            _photon?.SaveFinishOrder(_finishOrder.ToArray());
    }

    /// <summary>
    /// Host migration sonrası turu güvenli şekilde ilerletir.
    /// </summary>
    private void AdvanceTurnAfterHostMigration()
    {
        StopTurnTimer();

        _state.NextTurn(_initialPlayerCount);

        int safetyCount = 0;
        while (_finishOrder.Contains(_state.CurrentTurnPlayerIndex) && safetyCount < _initialPlayerCount)
        {
            _state.NextTurn(_initialPlayerCount);
            safetyCount++;
        }

        int nextTurn = _state.CurrentTurnPlayerIndex;
        Debug.Log($"[AdvanceTurnAfterHostMigration] New turn: P{nextTurn}");

        hudView.SetTurn(_turnNames[nextTurn], nextTurn, _localPlayerIndex);
        hudView.SetDice(-1);

        if (btnRollDice != null)
            btnRollDice.interactable = (nextTurn == _localPlayerIndex) && !_gameOver;

        HighlightActivePlayerPawns();

        // Broadcast ve state kaydet
        if (_photon != null)
        {
            _photon.BroadcastTurn(nextTurn);
            _photon.SyncGameState(nextTurn, -1, (int)TurnPhase.AwaitRoll, 0, 0);
            SerializeAndSavePawnStates();
        }

        StartTurnTimer(rollTimeLimit);
    }

    // ✅ Bug 3 fix: Handle player disconnects to prevent lockup
    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        base.OnPlayerLeftRoom(otherPlayer);

        // PlayerTtl > 0 ise geçici disconnect: oyuncu geri dönebilir, işlem yapma
        if (otherPlayer.IsInactive)
        {
            Debug.Log($"[OnPlayerLeftRoom] Player {otherPlayer.ActorNumber} is inactive (temporary disconnect). Waiting for reconnect.");
            return;
        }

        if (_gameOver) return;

        int leftPlayerIndex = otherPlayer.ActorNumber - 1;
        Debug.Log($"[OnPlayerLeftRoom] Player {otherPlayer.ActorNumber} (Index={leftPlayerIndex}) left.");

        // Çıkan oyuncuyu henüz sıralamada değilse sonuncu yap
        if (!_finishOrder.Contains(leftPlayerIndex))
        {
            _finishOrder.Add(leftPlayerIndex);
            Debug.Log($"[OnPlayerLeftRoom] P{leftPlayerIndex} added to finish order as #{_finishOrder.Count}");
        }
        _disconnectedPlayers.Add(leftPlayerIndex);
        _botPlayers.Remove(leftPlayerIndex);

        // Çıkan oyuncunun piyonlarını tahtadan kaldır
        RemoveDisconnectedPlayerPawns(leftPlayerIndex);

        // Kaç aktif (bitmemiş) oyuncu kaldı?
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

        // Sadece 1 kişi kaldıysa → o 1., oyun biter
        if (remainingPlayers <= 1 && lastRemainingIndex >= 0)
        {
            // Kalan oyuncuyu 1. sıraya koy (listenin başına)
            _finishOrder.Insert(0, lastRemainingIndex);
            _gameOver = true;
            if (sfx != null) sfx.PlayWin();

            if (btnRollDice != null)
                btnRollDice.interactable = false;

            StopTurnTimer();
            ClearAllHighlights();
            Debug.Log($"[OnPlayerLeftRoom] Only P{lastRemainingIndex} remains, game over!");
        }

        // Scoreboard güncelle
        UpdateScoreboard();

        // Photon state kaydet
        if (PhotonNetwork.IsMasterClient)
            _photon?.SaveFinishOrder(_finishOrder.ToArray());

        // Stuck state'leri temizle
        _isAnimating = false;
        _isRollingDice = false;

        // Oyun devam ediyorsa ve çıkan kişinin sırasıysa → atla
        if (!_gameOver)
        {
            if (PhotonNetwork.IsMasterClient && _disconnectedPlayers.Contains(_state.CurrentTurnPlayerIndex))
            {
                Debug.Log($"[OnPlayerLeftRoom] Current turn player P{_state.CurrentTurnPlayerIndex} is disconnected. Advancing turn.");
                StopTurnTimer();
                _extraTurnsEarned = 0;

                // Turu ilerlet ve lokal state'i de güncelle (RPC kendine ulaşmayabilir)
                _phase = TurnPhase.AwaitRoll;
                _currentRoll = -1;
                _consecutiveSixes = 0;

                _state.NextTurn(_initialPlayerCount);

                int safetyCount = 0;
                while (_finishOrder.Contains(_state.CurrentTurnPlayerIndex) && safetyCount < _initialPlayerCount)
                {
                    _state.NextTurn(_initialPlayerCount);
                    safetyCount++;
                }

                int nextTurn = _state.CurrentTurnPlayerIndex;
                Debug.Log($"[OnPlayerLeftRoom] New turn: P{nextTurn}");

                // UI'ı güncelle
                hudView.SetTurn(_turnNames[nextTurn], nextTurn, _localPlayerIndex);
                hudView.SetDice(-1);

                if (btnRollDice != null)
                    btnRollDice.interactable = (nextTurn == _localPlayerIndex) && !_gameOver;

                HighlightActivePlayerPawns();

                // Diğer client'lara da broadcast et
                if (_photon != null)
                {
                    _photon.BroadcastTurn(nextTurn);
                    _photon.SyncGameState(nextTurn, -1, (int)TurnPhase.AwaitRoll, 0, _extraTurnsEarned);
                    SerializeAndSavePawnStates();
                }

                // Sıra bizdeyse timer başlat
                if (nextTurn == _localPlayerIndex)
                    StartTurnTimer(rollTimeLimit);
            }
            else if (!PhotonNetwork.IsMasterClient)
            {
                // Non-host: Çıkan oyuncu sıradaki ise, host'un BroadcastTurn göndermesini bekle
                // Ama UI'ı kilitlenme durumundan kurtarmak için flag'leri temizle
                _isAnimating = false;
                _isRollingDice = false;
                Debug.Log($"[OnPlayerLeftRoom] Non-host: cleared stuck flags, waiting for host BroadcastTurn");
            }
        }
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

    // ✅ Zar atma: Sadece kendi sıran ise atabilirsin
    private void OnRollDiceClicked()
    {
        if (_paused) return;
        if (_gameOver) return;
        if (_phase != TurnPhase.AwaitRoll) return;
        if (_isRollingDice) return;
        if (_isAnimating) return; // ✅ YENİ

        if (_state.CurrentTurnPlayerIndex != _localPlayerIndex)
        {
            Debug.Log("Not your turn!");
            return;
        }

        StartCoroutine(CoRollDiceAnimated());
    }

    private IEnumerator CoRollDiceAnimated()
    {
        // Timer hâlâ aktifse ve bu local oyuncu ise: manuel zar attı → bot modundan çıkar
        if (_timerActive && PhotonNetwork.IsMasterClient
            && _botPlayers.Contains(_state.CurrentTurnPlayerIndex))
        {
            _botPlayers.Remove(_state.CurrentTurnPlayerIndex);
            Debug.Log($"[BotMode] P{_state.CurrentTurnPlayerIndex} woke up (manual roll)");
        }

        _isRollingDice = true;

        if (btnRollDice != null)
            btnRollDice.interactable = false;

        DisableAllPawnClicks();

        // ✅ IMPACT FIX 1: Determine result IMMEDIATELY
        int roll = _dice.Roll();
        _currentRoll = roll;

        // ✅ IMPACT FIX 1: Broadcast IMMEDIATELY
        if (_photon != null && PhotonNetwork.InRoom)
        {
            int turn = _state.CurrentTurnPlayerIndex;
            Debug.Log($"[CoRollDiceAnimated] Broadcasting Roll EARLY: P{turn} = {roll}");
            _photon.BroadcastRoll(turn, roll);

            // Host saves state immediately
            if (PhotonNetwork.IsMasterClient)
            {
                _photon.SyncGameState(turn, roll, (int)_phase, _consecutiveSixes, _extraTurnsEarned);
            }
        }

        hudView.SetTurn(_turnNames[_state.CurrentTurnPlayerIndex], _state.CurrentTurnPlayerIndex, _localPlayerIndex);
        sfx?.PlayDice();

        float elapsed = 0f;
        while (elapsed < diceRollDuration)
        {
            int fakeValue = Random.Range(1, 7);
            hudView.SetDice(fakeValue);
            elapsed += diceTickInterval;
            yield return new WaitForSeconds(diceTickInterval);
        }

        // ✅ Finalize visual
        hudView.SetDice(roll);

        _isRollingDice = false;

        yield return new WaitForSeconds(0.5f); // Wait for players to see the result

    int turn2 = _state.CurrentTurnPlayerIndex;

    // ✅ 3 ARDIŞIK 6 KONTROLÜ - EN ÖNCE!
    if (_consecutiveSixes >= 3)
    {
        Debug.Log($"[CoRollDiceAnimated] 3 consecutive sixes! Penalty for P{turn2}");

        _consecutiveSixes = 0; // ✅ Sıfırla

        if (PhotonNetwork.InRoom)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                _extraTurnsEarned = 0;
                AdvanceTurnInternalOnly();
            }
            else
            {
                _currentRoll = -1;
                hudView.SetDice(-1);
                _photon?.RequestAdvanceTurn();
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

        if (PhotonNetwork.InRoom)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                if (_extraTurnsEarned > 0)
                {
                    _extraTurnsEarned--;
                    _currentRoll = -1;
                    hudView.SetDice(-1);
                    _photon.BroadcastTurn(_state.CurrentTurnPlayerIndex);
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
                _photon?.RequestAdvanceTurn();
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
                if (btnRollDice != null) btnRollDice.interactable = !_gameOver;
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
        _photon?.SendMoveRequest(turn2, pawnId, roll);
        yield break;
    }

    HighlightLegalMoves(legal);
    SetOnlyLegalClickable(legal);
    _phase = TurnPhase.AwaitMove;

    // ✅ Piyon seçim timer'ı başlat
    StartTurnTimer(moveTimeLimit);
}


    private void OnPawnClicked(PawnView pawn)
    {
        if (_paused) return;
        if (_gameOver) return;
        if (_phase != TurnPhase.AwaitMove) return;
        if (_currentRoll < 1) return;
        if (_isAnimating) return; // ✅ YENİ

        // ✅ Bug 3 fix: Rapid click protection
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

        // ✅ Bug 3 fix: Set cooldown timestamp
        _lastMoveRequestTime = Time.time;

        // ✅ Bug 3 fix: Immediately disable clicks to prevent rapid fire
        DisableAllPawnClicks();
        if (btnRollDice != null) btnRollDice.interactable = false;

        int pawnId = _pawnToId[pawn];
        _photon?.SendMoveRequest(turn, pawnId, _currentRoll);
    }

    private void OnNetworkMoveRequest(int playerIndex, int pawnId, int roll)
    {
        // ✅ Sadece host karar versin
        if (_photon == null || !_photon.IsHost) return;

        if (playerIndex != _state.CurrentTurnPlayerIndex) return;

        // ✅ Bug 3 fix: Prevent duplicate requests for same pawn while animating
        if (_lastProcessedPawnId == pawnId && _isAnimating)
        {
            Debug.LogWarning($"[OnNetworkMoveRequest] Duplicate request for pawn {pawnId} ignored");
            return;
        }

        if (!_idToPawn.TryGetValue(pawnId, out var pawn)) return;

        // ✅ Race condition fix: Client'ın gönderdiği roll değerini kullan
        // (BroadcastRoll All RPC'si, MoveRequest MasterClient RPC'sinden geç gelebilir)
        if (roll > 0 && roll <= 6 && roll != _currentRoll)
        {
            Debug.LogWarning($"[OnNetworkMoveRequest] Roll mismatch! Host={_currentRoll}, Client={roll}. Using client roll.");
            _currentRoll = roll;
        }

        var legal = GetLegalMoves(playerIndex, _currentRoll);
        if (!legal.Contains(pawn)) return;

        // ✅ Bug 3 fix: Track this pawn as processed
        _lastProcessedPawnId = pawnId;

        // ✅ Bug 2 fix: Generate unique move ID
        int moveId = _nextMoveId++;

        // ✅ Host hamleyi broadcast eder with moveId
        _photon.BroadcastMove(playerIndex, pawnId, _currentRoll, moveId);

        // ✅ Bug 1 fix: Persist state after move validation
        _photon.SyncGameState(_state.CurrentTurnPlayerIndex, _currentRoll, (int)_phase, _consecutiveSixes, _extraTurnsEarned);
        SerializeAndSavePawnStates();
    }

    private void OnNetworkRoll(int playerIndex, int roll)
{
    Debug.Log($"[OnNetworkRoll] P{playerIndex} rolled {roll}, LocalPlayer={_localPlayerIndex}");

    // ✅ Timer'ı HERKESTE durdur (senkronizasyon düzeltmesi)
    StopTurnTimer();

    if (roll == 6)
    {
        _consecutiveSixes++;
        Debug.Log($"[OnNetworkRoll] Consecutive sixes: {_consecutiveSixes}");

        if (_consecutiveSixes >= 3)
        {
            Debug.Log($"[OnNetworkRoll] 3 consecutive sixes! Penalty for P{playerIndex}");

            if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
            {
                _extraTurnsEarned = 0;
            }
        }
        else
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
            {
                // ✅ Sadece evde veya main path'te piyon varsa extra turn ver
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

    // ✅ Zar ve UI'ı TÜM oyuncular için güncelle
    _currentRoll = roll;
    hudView.SetTurn(_turnNames[playerIndex], playerIndex, _localPlayerIndex);

    // ✅ Çift zar atmayı engelle
    if (btnRollDice != null)
        btnRollDice.interactable = false;

    // ✅ IMPACT FIX 2: If we are not the one who initiated the local roll, play animation
    // If we initiated it (CoRollDiceAnimated), we are already playing it.
    bool amIRollingLocally = (playerIndex == _localPlayerIndex && _isRollingDice);

    if (!amIRollingLocally)
    {
        // Cancel any existing remote animation to prevent overlap/desync
        StopCoroutine("CoRemoteDiceAnimation");
        StartCoroutine(CoRemoteDiceAnimation(playerIndex, roll));
    }

    // ✅ Eğer Host değilsek ve sıra bizdeyse, ama biz atmadıysak (Timeout vs)
    if (playerIndex == _localPlayerIndex && !amIRollingLocally && _consecutiveSixes < 3)
    {
         // Logic handled in CoRemoteDiceAnimation
    }

    // ✅ Host: uzak oyuncu için hamle timer'ı başlat
    // (Wait for animation to finish effectively using the same duration logic)
    if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && playerIndex != _localPlayerIndex && _consecutiveSixes < 3)
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

    // ✅ Uzaktan gelen zar atışı için görsel animasyon
    private IEnumerator CoRemoteDiceAnimation(int playerIndex, int finalRoll)
    {
        sfx?.PlayDice();

        float elapsed = 0f;
        while (elapsed < diceRollDuration)
        {
            int fakeValue = Random.Range(1, 7);
            hudView.SetDice(fakeValue);
            elapsed += diceTickInterval;
            yield return new WaitForSeconds(diceTickInterval);
        }

        hudView.SetDice(finalRoll);

        yield return new WaitForSeconds(0.3f);

        // ✅ Eğer benim için oto-atıldıysa, şimdi AwaitMove fazını kur
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

    private void OnNetworkMove(int playerIndex, int pawnId, int roll, int moveId) // ✅ Bug 2: moveId parametresi eklendi
    {
        StopTurnTimer(); // ✅ Timer durdur (senkronizasyon güvenliği)

        // ✅ FIX: Önceki animasyon sıkışmışsa temizle (yeni move geldiğine göre önceki tamamlanmış olmalı)
        _isAnimating = false;

        Debug.Log($"[RPC RECEIVED] Move: P{playerIndex} pawn {pawnId} with roll {roll}, moveId={moveId}");

        // ✅ Bug 2 fix: Deduplication check
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
    Debug.Log($"[RPC RECEIVED] Turn: Now P{nextPlayerIndex} ({_turnNames[nextPlayerIndex]})");

    // ✅ FIX: Animasyon flag'lerini sıfırla (host animasyonu client'tan önce bitebilir, race condition)
    _isAnimating = false;

    // ✅ Gerçek sıra değişimi mi, yoksa extra turn mı?
    if (nextPlayerIndex != _state.CurrentTurnPlayerIndex)
    {
        _consecutiveSixes = 0; // Sıra değişti, sıfırla
    }

    _state.CurrentTurnPlayerIndex = nextPlayerIndex;
    _phase = TurnPhase.AwaitRoll;
    _isRollingDice = false;
    _currentRoll = -1;

    hudView.SetDice(-1);
    hudView.SetTurn(_turnNames[nextPlayerIndex], nextPlayerIndex, _localPlayerIndex);

    if (btnRollDice != null)
    {
        bool isMyTurn = (nextPlayerIndex == _localPlayerIndex);
        btnRollDice.interactable = isMyTurn && !_gameOver;
    }
    // ✅ Sıra sende ise ses çal + titreşim
    if (nextPlayerIndex == _localPlayerIndex && !_gameOver)
    {
        sfx?.PlayYourTurn();

        // ✅ Mobilde titreşim (Android)
        #if UNITY_ANDROID && !UNITY_EDITOR
        Handheld.Vibrate();
        #endif
    }
    HighlightActivePlayerPawns();

    // ✅ Zar atma timer'ı başlat
    StartTurnTimer(rollTimeLimit);

    // ✅ FIX: Client'lar her sıra değişiminde host state'ini doğrula (desync önleme)
    if (!PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom)
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
        // Start local timer display for all clients
        _timerActive = true;
        _turnTimer = duration;
    }



    private void FinishMove()
    {
        StopTurnTimer(); // ✅ Timer durdur
        _phase = TurnPhase.AwaitRoll;
        _isRollingDice = false;

        // ✅ Bug 3 fix: Reset move tracking for next turn
        _lastProcessedPawnId = -1;

        foreach (var kv in _pawnStates)
            kv.Key.SetClickable(false);

        // ==================== ONLINE ====================
        if (PhotonNetwork.InRoom)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                // Bitiren oyuncuya extra turn verme
                if (_finishOrder.Contains(_state.CurrentTurnPlayerIndex))
                {
                    _extraTurnsEarned = 0;
                    AdvanceTurnInternalOnly();
                    return;
                }

                // ✅ Host: extra turn varsa aynı oyuncu devam
                if (_extraTurnsEarned > 0)
                {
                    _extraTurnsEarned--;
                    _currentRoll = -1;
                    hudView.SetDice(-1);
                    Debug.Log($"[FinishMove] Host: Extra turn! Remaining: {_extraTurnsEarned}");
                    _photon.BroadcastTurn(_state.CurrentTurnPlayerIndex); // Aynı oyuncu
                    return;
                }

                // ✅ Host: sıra ilerlet
                AdvanceTurnInternalOnly();
            }
            else
            {
                // ✅ Client: UI temizle, ama butonu körü körüne kapatma
                _currentRoll = -1;
                hudView.SetDice(-1);

                // Bitiren oyuncu extra turn almasın
                if (_finishOrder.Contains(_localPlayerIndex))
                    _extraTurnsEarned = 0;

                // ✅ Eğer Turn RPC zaten geldi ve sıra bizdeyse, butonu açık bırak
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
            StartTurnTimer(rollTimeLimit); // ✅ Extra turn için timer yeniden başlat
            return;
        }

        AdvanceTurnInternalOnly();
    }

    private void AdvanceTurnInternalOnly()
    {
        StopTurnTimer(); // ✅ Timer durdur
        Debug.Log($"[AdvanceTurnInternalOnly] Advancing from P{_state.CurrentTurnPlayerIndex}");

        _phase = TurnPhase.AwaitRoll;
        _currentRoll = -1;
        _consecutiveSixes = 0; // ✅ YENİ

        _state.NextTurn(_initialPlayerCount);

        // Bitiren/çıkan oyuncuları atla (sonsuz döngü korumalı)
        int safetyCount = 0;
        while (_finishOrder.Contains(_state.CurrentTurnPlayerIndex) && safetyCount < _initialPlayerCount)
        {
            _state.NextTurn(_initialPlayerCount);
            safetyCount++;
        }

        Debug.Log($"[AdvanceTurnInternalOnly] New turn: P{_state.CurrentTurnPlayerIndex}");

        // ✅ SADECE HOST BROADCAST EDER
        if (_photon != null && PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
        {
            Debug.Log($"[AdvanceTurnInternalOnly] Broadcasting Turn: P{_state.CurrentTurnPlayerIndex}");
            _photon.BroadcastTurn(_state.CurrentTurnPlayerIndex);

            // ✅ Bug 1 fix: Persist state after turn change
            _photon.SyncGameState(_state.CurrentTurnPlayerIndex, -1, (int)TurnPhase.AwaitRoll, 0, _extraTurnsEarned);
            SerializeAndSavePawnStates();
        }
        else if (!PhotonNetwork.InRoom)
        {
            hudView.SetDice(-1);
            hudView.SetTurn(_turnNames[_state.CurrentTurnPlayerIndex], _state.CurrentTurnPlayerIndex, _localPlayerIndex);

            if (btnRollDice != null)
                btnRollDice.interactable = !_gameOver;

            StartTurnTimer(rollTimeLimit); // ✅ Offline modda sıra değişiminde timer başlat
        }

        HighlightActivePlayerPawns();
    }

    // ✅ Bu metodları GameBootstrapper.cs dosyasının sonuna ekle

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

        // Blok kontrolü
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

            if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
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
    /// ✅ Pawn'un home slot pozisyonunu bul
    /// </summary>
    private Vector3 GetHomePawnPosition(PawnView pawn)
    {
        // Pawn'un sahibini bul
        if (!_pawnOwner.TryGetValue(pawn, out int ownerIndex))
            return pawn.Rect.position; // Fallback

        // ✅ Renk gruplarına göre home slot'ları al (değişken adı değiştirildi)
        IReadOnlyList<RectTransform> homeSlotsForColor = ownerIndex switch
        {
            0 => homeSlots.R,  // homeSlots burada field (GameBootstrapper'daki)
            1 => homeSlots.Y,
            2 => homeSlots.G,
            3 => homeSlots.B,
            _ => null
        };

        if (homeSlotsForColor == null || homeSlotsForColor.Count == 0)
            return pawn.Rect.position;

        // Bu pawn'un hangi slot'ta olduğunu bul
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

        // Zaten sıralamada varsa tekrar ekleme
        if (_finishOrder.Contains(playerIndex)) return;

        _finishOrder.Add(playerIndex);
        sfx?.PlayWin();

        // Online: finishOrder'u room properties'e kaydet (late joiner için)
        if (_photon != null && PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
            _photon.SaveFinishOrder(_finishOrder.ToArray());

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

        // Gösterim sırasını oluştur: 1) Meşru bitirenler, 2) ???, 3) Disconnect olanlar
        var displayEntries = new List<string>();

        // 1. Meşru bitirenler (disconnect olmayan, _finishOrder sırasıyla)
        foreach (int idx in _finishOrder)
        {
            if (!_disconnectedPlayers.Contains(idx))
                displayEntries.Add(_turnNames[idx]);
        }

        // 2. Hala oynayan oyuncular → "???"
        for (int i = 0; i < _initialPlayerCount; i++)
        {
            if (!_finishOrder.Contains(i))
                displayEntries.Add("???");
        }

        // 3. Disconnect olanlar (en sona)
        foreach (int idx in _finishOrder)
        {
            if (_disconnectedPlayers.Contains(idx))
                displayEntries.Add(_turnNames[idx]);
        }

        // Göster
        for (int i = 0; i < scoreboardTexts.Length; i++)
        {
            if (i < displayEntries.Count)
                scoreboardTexts[i].text = $"{i + 1}. {displayEntries[i]}";
            else
                scoreboardTexts[i].text = "";
        }

        if (txtScoreboardTitle != null)
            txtScoreboardTitle.text = _gameOver ? "Oyun Bitti!" : "Sıralama";

        // X butonu: oyun devam ediyorsa göster, bittiyse gizle
        if (btnScoreboardClose != null)
            btnScoreboardClose.gameObject.SetActive(!_gameOver);

        // Ana Menü butonu: sadece oyun bittiyse VE yerel oyuncu da bitirmişse göster
        if (btnMainMenu != null)
        {
            bool localPlayerFinished = _finishOrder.Contains(_localPlayerIndex);
            btnMainMenu.gameObject.SetActive(_gameOver && localPlayerFinished);
        }

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
        if (PhotonNetwork.InRoom)
        {
            _isLeavingToMainMenu = true;
            PhotonNetwork.LeaveRoom(false); // becomeInactive: false → hemen kalıcı çıkış, 60s bekleme yok
            return; // OnLeftRoom callback'ini bekle
        }

        // Odada değilsek direkt sahne yükle — Disconnect yok
        SceneManager.LoadScene(0);
    }

    public override void OnLeftRoom()
    {
        base.OnLeftRoom();
        if (_isLeavingToMainMenu)
        {
            _isLeavingToMainMenu = false;
            // Disconnect ÇAĞIRMA — Photon master server'a bağlı kalır.
            // LobbyManager.Start() IsConnectedAndReady görür → JoinLobby çağırır.
            // Disconnect() çağırınca OnDisconnected tetiklenip reconnect başlıyordu.
            SceneManager.LoadScene(0);
        }
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

        if (PhotonNetwork.IsMasterClient)
        {
            // Host kendi bot modundan çıkıyor
            _botPlayers.Remove(_localPlayerIndex);
        }
        else if (PhotonNetwork.InRoom)
        {
            // Client → host'a bildir (room property üzerinden)
            var props = new ExitGames.Client.Photon.Hashtable();
            props[$"exitbot_{_localPlayerIndex}"] = PhotonNetwork.ServerTimestamp;
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }

        Debug.Log($"[BotMode] P{_localPlayerIndex} took manual control via button.");
    }

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        base.OnRoomPropertiesUpdate(propertiesThatChanged);
        if (!PhotonNetwork.IsMasterClient) return;

        for (int i = 0; i < 4; i++)
        {
            string key = $"exitbot_{i}";
            if (propertiesThatChanged.ContainsKey(key))
            {
                _botPlayers.Remove(i);
                Debug.Log($"[BotMode] P{i} exited bot mode via button (room property).");
            }
        }
    }

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
        // ✅ Bug 2 & 3 fix: Set animation flag IMMEDIATELY at method start
        _isAnimating = true;
        DisableAllPawnClicks();
        if (btnRollDice != null) btnRollDice.interactable = false;

        // ✅ FIX: Animasyon güvenlik zaman aşımı (sıkışma önleme)
        if (_animationSafetyTimer != null) StopCoroutine(_animationSafetyTimer);
        _animationSafetyTimer = StartCoroutine(AnimationSafetyTimeout(5f));

        var st = _pawnStates[pawn];

        // ==================== EVDEN ÇIKIŞ ====================
        if (st.IsAtHome)
        {
            if (roll != 6)
            {
                _isAnimating = false; // ✅ Reset on early exit
                return;
            }
            if (!TryGetStartIndexForPlayer(playerIndex, out int startIndex))
            {
                _isAnimating = false; // ✅ Reset on early exit
                return;
            }

            st.EnterMainAt(startIndex);
            sfx?.PlayHomeExit();

            if (_pawnCurrentWaypoint.TryGetValue(pawn, out int oldWp))
                positionManager?.UnregisterPawnFromWaypoint(pawn, oldWp);

            // Evden çıkış instant kalabilir (tek kareye spawn)
            pawn.SetPosition(boardWaypoints.MainPath[startIndex].position);
            _pawnCurrentWaypoint[pawn] = startIndex;
            positionManager?.RegisterPawnAtWaypoint(pawn, startIndex);

            ResolveCaptures(pawn);
            _isAnimating = false; // ✅ No animation for home exit, reset flag
            CancelAnimationSafetyTimer();
            FinishMove();
            return;
        }

        if (st.IsFinished)
        {
            _isAnimating = false; // ✅ Reset on early exit
            return;
        }

        // ==================== HOME LANE ====================
         if (st.IsInHomeLane)
    {
        if (st.HomeIndex + roll > 5)
        {
            _isAnimating = false; // ✅ Reset on early exit
            return;
        }

        var homePath = GetHomePath(playerIndex);
        int fromHome = st.HomeIndex;
        int newHomeIndex = fromHome + roll;

        // ✅ Eski home lane pozisyonundan unregister
        int oldKey = GetHomeLaneKey(playerIndex, fromHome);
        if (_pawnCurrentWaypoint.ContainsKey(pawn))
            positionManager?.UnregisterPawnFromWaypoint(pawn, oldKey);

        var positions = new List<Vector3>();
        for (int i = fromHome + 1; i <= newHomeIndex; i++)
            positions.Add(homePath[i].position);

        st.AdvanceHome(roll);

            if (newHomeIndex == 5)
            {
                if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
                {
                    _extraTurnsEarned++;
                    Debug.Log($"[ApplyMove] Pawn finished! Extra turns: {_extraTurnsEarned}");
                }
                sfx?.PlayFinish();  // ✅ YENİ
            }

            // ✅ _isAnimating already set at method start, removed redundant lines

            pawnMover.MoveAlongPositions(pawn, positions, () =>
        {
            // ✅ Yeni home lane pozisyonuna register
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

            // Pozisyon listesi oluştur
            var positions = new List<Vector3>();
            int cur = from;
            for (int i = 0; i < roll; i++)
            {
                cur = (cur + 1) % pathCount;
                positions.Add(boardWaypoints.MainPath[cur].position);
            }

            // State'i hemen güncelle
            st.AdvanceMain(roll, pathCount);
            int targetIndex = st.MainIndex;

            Debug.Log($"[ApplyMove] P{playerIndex} MainPath: from={from}, roll={roll}, target={targetIndex}, distToEntry={distToEntry}, steps={positions.Count}");

            // ✅ _isAnimating already set at method start, removed redundant lines

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

        // ==================== HOME'A GİRİŞ ====================
        int intoHome = roll - distToEntry - 1;
        if (intoHome == 5)
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
            {
                _extraTurnsEarned++;
                Debug.Log($"[ApplyMove] Pawn finished! Extra turns: {_extraTurnsEarned}");
            }
            sfx?.PlayFinish();  // ✅ YENİ
        }
        if (_pawnOwner[pawn] != playerIndex)
        {
            _isAnimating = false; // ✅ Reset on early exit
            return;
        }

        if (_pawnCurrentWaypoint.TryGetValue(pawn, out int oldWp2))
        {
            positionManager?.UnregisterPawnFromWaypoint(pawn, oldWp2);
            _pawnCurrentWaypoint.Remove(pawn);
        }

        // Pozisyon listesi: önce main path (entry'e kadar), sonra home lane
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

        // State'i hemen güncelle
        if (distToEntry > 0)
            st.AdvanceMain(distToEntry, pathCount);
        st.EnterHomeLane();
        st.AdvanceHome(intoHome);

        if (intoHome == 5)
        {
            _extraTurnsEarned++;
            Debug.Log($"[ApplyMove] Pawn finished! Extra turns: {_extraTurnsEarned}");
        }

        // ✅ _isAnimating already set at method start, removed redundant lines

        pawnMover.MoveAlongPositions(pawn, positions2, () =>
    {
        // ✅ Home lane pozisyonuna register
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
                btnRollDice.interactable = !_isRollingDice && (_state.CurrentTurnPlayerIndex == _localPlayerIndex);

            foreach (var kv in _pawnStates)
                kv.Key.SetClickable(false);

            HighlightActivePlayerPawns();
            return;
        }

        if (_phase == TurnPhase.AwaitMove)
        {
            if (btnRollDice != null)
                btnRollDice.interactable = !_isRollingDice && !_isAnimating
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

        // ✅ Turn Timer Tick
        if (_timerActive && !_gameOver && !_paused)
        {
            _turnTimer -= Time.deltaTime;
            hudView?.SetTimer(_turnTimer);

            // ✅ 3 saniye kala clock sesi çal
            if (_turnTimer <= 3f && !_clockPlayed)
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

    // ✅ Timer yardımcı metotları
    private void StartTurnTimer(float duration)
    {
        // Bot oyuncu için kısa auto-play süresi
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient
            && _botPlayers.Contains(_state.CurrentTurnPlayerIndex))
        {
            duration = BotAutoDelay;
        }

        _turnTimer = duration;
        _timerActive = true;
        _clockPlayed = false; // ✅ Her yeni timer başlatıldığında sıfırla
        hudView?.SetTimer(_turnTimer);
        Debug.Log($"[Timer] Started: {duration}s for phase {_phase}");

        // ✅ NEW (Fix 1): Broadcast to all clients (host only)
        if (_photon != null && _photon.IsHost)
        {
            _photon.BroadcastTimerStart(duration);

            // ✅ NEW (Fix 2): Save timer state with synchronized timestamp
            _photon.SaveTimerState(PhotonNetwork.Time, duration);
        }
    }

    private void StopTurnTimer(bool broadcast = true)
    {
        _timerActive = false;
        _turnTimer = 0f;
        hudView?.HideTimer();
        sfx?.StopClock(); // ✅ Saat sesini durdur

        // ✅ NEW (Fix 1): Broadcast to all clients (host only)
        if (broadcast && _photon != null && _photon.IsHost)
        {
            _photon.BroadcastTimerStop();

            // ✅ NEW (Fix 2): Clear timer state from room properties
            _photon.ClearTimerState();
        }
    }

    private void OnNetworkTimerStop()
    {
        // Stop local timer display for all clients
        StopTurnTimer(false);
    }

    private void OnTurnTimerExpired()
    {
        Debug.Log($"[Timer] Expired! Phase={_phase}, Turn=P{_state.CurrentTurnPlayerIndex}");

        // Tüm clientlarda: kendi sıramsa lokal bot moduna gir
        if (_state.CurrentTurnPlayerIndex == _localPlayerIndex)
            SetLocalBotMode(true);

        // Online: sadece host timeout kararını alır
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) return;

        // Bot moduna al
        _botPlayers.Add(_state.CurrentTurnPlayerIndex);

        if (_phase == TurnPhase.AwaitRoll)
        {
            AutoRollDice();
        }
        else if (_phase == TurnPhase.AwaitMove)
        {
            AutoMovePawn();
        }
    }

    private void AutoRollDice()
    {
        if (_isRollingDice || _isAnimating) return;
        Debug.Log($"[Timer] Auto-rolling dice for P{_state.CurrentTurnPlayerIndex}");
        StartCoroutine(CoRollDiceAnimated());
    }

    private void AutoMovePawn()
    {
        int turn = _state.CurrentTurnPlayerIndex;
        var legal = GetLegalMoves(turn, _currentRoll);
        if (legal.Count == 0) return;

        // Rastgele bir legal piyon seç
        PawnView chosen = legal[Random.Range(0, legal.Count)];
        int pawnId = _pawnToId[chosen];
        Debug.Log($"[Timer] Auto-moving pawn {pawnId} for P{turn}");
        _photon?.SendMoveRequest(turn, pawnId, _currentRoll);
    }
private int GetHomeLaneKey(int playerIndex, int homeIndex)
{
    return PawnPositionManager.GetHomeLaneKey(playerIndex, homeIndex);
}

/// <summary>
/// Oyuncunun evde veya main path'te piyonu var mı?
/// (Home lane ve finished hariç)
/// </summary>
private bool HasPawnOutsideHomeLane(int playerIndex)
{
    var pawns = GetPawnsForTurn(playerIndex);
    foreach (var p in pawns)
    {
        var st = _pawnStates[p];
        if (st.IsAtHome) return true;      // Evde piyon var, 6 ile çıkabilir
        if (!st.IsInHomeLane && !st.IsFinished) return true; // Main path'te piyon var
    }
    return false; // Hepsi home lane'de veya bitmiş
}

private void OnHomeAreaClicked(int playerIndex)
{
    if (_paused) return;
    if (_gameOver) return;
    if (_currentRoll < 1) return;

    // Sadece kendi sıran ve kendi rengin
    int turn = _state.CurrentTurnPlayerIndex;
    if (turn != _localPlayerIndex) return;
    if (turn != playerIndex) return;

    // 6 değilse evden çıkamaz
    if (_currentRoll != 6) return;

    // AwaitMove fazında olmalı
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

    // ✅ Hamleyi gönder
    int pawnId = _pawnToId[homePawn];
    _photon?.SendMoveRequest(turn, pawnId, _currentRoll);
}

private void OnBoardAreaClicked(Vector2 screenPos)
{
    if (_paused || _gameOver || _phase != TurnPhase.AwaitMove) return;
    if (_currentRoll < 1 || _isAnimating) return;
    if (_state.CurrentTurnPlayerIndex != _localPlayerIndex) return;

    var legal = GetLegalMoves(_state.CurrentTurnPlayerIndex, _currentRoll);
    if (legal.Count == 0) return;

    // En yakın legal piyonu bul (evdekiler hariç - HomeAreaClick hallediyor)
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

    // ✅ ========== BUG 1 FIX: PAWN STATE SERIALIZATION METHODS ==========

    /// <summary>
    /// Serialize all pawn states and save to room properties (host only)
    /// </summary>
    private void SerializeAndSavePawnStates()
    {
        if (_photon == null || !PhotonNetwork.IsMasterClient) return;

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

        _photon.SavePawnStates(sb.ToString());
        Debug.Log($"[SerializePawnStates] Saved {_pawnToId.Count} pawns");
    }

    /// <summary>
    /// Restore pawn states from room properties (client only)
    /// </summary>
    private void RestorePawnStatesFromNetwork()
    {
        if (_photon == null) return;

        string data = _photon.GetPawnStates();
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

            // Restore visual position
            Vector3 pos = GetPawnVisualPosition(pawn, state, _pawnOwner[pawn]);
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

    // Lokal emoji: QuickChatView'dan index gelir, hemen animasyon oynat (ağa gitmeden önce)
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
        // burada sadece ağa gönderim yapılır.
        // Metin ise lokal panelde float göster.
        if (!message.StartsWith("__EMOJI__"))
        {
            var localPanel = hudView.GetCornerPanelForPlayer(_localPlayerIndex, _localPlayerIndex);
            if (localPanel != null) localPanel.ShowEmojiFloat(message);
        }
        _photon.BroadcastChatMessage(message, _localPlayerIndex);
    }

    private void OnNetworkChatMessage(string message, int senderPlayerIndex)
    {
        var senderPanel = hudView.GetCornerPanelForPlayer(senderPlayerIndex, _localPlayerIndex);
        if (message.StartsWith("__EMOJI__"))
        {
            // Index'i çöz, animasyonu oynat
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