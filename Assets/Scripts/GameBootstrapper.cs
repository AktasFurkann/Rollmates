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

    [Header("Home Click Areas")]
[SerializeField] private HomeAreaClick homeClickRed;
[SerializeField] private HomeAreaClick homeClickGreen;
[SerializeField] private HomeAreaClick homeClickYellow;
[SerializeField] private HomeAreaClick homeClickBlue;

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

    [Header("Pawn Sprites")]
    [SerializeField] private Sprite redPawnSprite;
    [SerializeField] private Sprite greenPawnSprite;
    [SerializeField] private Sprite yellowPawnSprite;
    [SerializeField] private Sprite bluePawnSprite;

    [Header("Audio")]
    [SerializeField] private SfxPlayer sfx;

    [Header("Board Rotation")]
    [SerializeField] private BoardRotator boardRotator;

    [Header("Turn Timer")]
    [SerializeField] private float rollTimeLimit = 15f;
    [SerializeField] private float moveTimeLimit = 10f;
    private float _turnTimer = 0f;
    private bool _timerActive = false;
    private bool _clockPlayed = false; // ✅ 3 saniye sesi tekrar çalmasın diye flag

    private readonly string[] _turnNames = { "Kırmızı", "Yeşil", "Sarı", "Mavi" };

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
            positionManager.CacheHomeLanePositions(1, boardWaypoints.HomeG);
            positionManager.CacheHomeLanePositions(2, boardWaypoints.HomeY);
            positionManager.CacheHomeLanePositions(3, boardWaypoints.HomeB);
        }

        hudView.SetTurn(_turnNames[_state.CurrentTurnPlayerIndex], _state.CurrentTurnPlayerIndex);
        hudView.SetDice(-1);

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
        RegisterPawns(_greenPawns, 1);
        RegisterPawns(_yellowPawns, 2);
        RegisterPawns(_bluePawns, 3);

        if (winnerPanel != null)
            winnerPanel.SetActive(false);

        if (btnRestart != null)
            btnRestart.onClick.AddListener(OnRestartClicked);

        foreach (var kv in _pawnStates)
            kv.Key.Clicked += OnPawnClicked;

        homeClickRed?.Init(0, OnHomeAreaClicked);
        homeClickGreen?.Init(1, OnHomeAreaClicked);
        homeClickYellow?.Init(2, OnHomeAreaClicked);
        homeClickBlue?.Init(3, OnHomeAreaClicked);

        btnRollDice.onClick.AddListener(OnRollDiceClicked);

        if (btnRollDice != null)
        {
            bool isMyTurn = (_state.CurrentTurnPlayerIndex == _localPlayerIndex);
            btnRollDice.interactable = isMyTurn && !_gameOver;
            Debug.Log($"[Awake] FirstTurn={_state.CurrentTurnPlayerIndex}, MyTurn={isMyTurn}, ButtonActive={btnRollDice.interactable}");
        }

        HighlightActivePlayerPawns();

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
            positionManager.CacheHomeLanePositions(1, boardWaypoints.HomeG);
            positionManager.CacheHomeLanePositions(2, boardWaypoints.HomeY);
            positionManager.CacheHomeLanePositions(3, boardWaypoints.HomeB);
        }

        hudView.SetTurn(_turnNames[_state.CurrentTurnPlayerIndex], _state.CurrentTurnPlayerIndex);
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
        RegisterPawns(_greenPawns, 1);
        RegisterPawns(_yellowPawns, 2);
        RegisterPawns(_bluePawns, 3);

        if (winnerPanel != null)
            winnerPanel.SetActive(false);

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
    private void UpdateTurnUI()
    {
        hudView.SetTurn(_turnNames[_state.CurrentTurnPlayerIndex], _state.CurrentTurnPlayerIndex);

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
        }

        if (btnRollDice != null)
            btnRollDice.onClick.RemoveListener(OnRollDiceClicked);

        foreach (var kv in _pawnStates)
            kv.Key.Clicked -= OnPawnClicked;

        if (btnRestart != null)
            btnRestart.onClick.RemoveListener(OnRestartClicked);
    }

    // ✅ Bug 1 fix: Reconnection support - sync state when player joins/rejoins
    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();

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
                hudView.SetTurn(_turnNames[turn], turn);
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

    // ✅ Bug 3 fix: Handle player disconnects to prevent lockup
    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        base.OnPlayerLeftRoom(otherPlayer);

        Debug.Log($"[OnPlayerLeftRoom] Player {otherPlayer.ActorNumber} left.");

        if (PhotonNetwork.IsMasterClient)
        {
            // If the player who left was the current turn player
            // ActorNumber is usually 1-indexed, but our logic maps 0..3 to ActorNumbers.
            // We need to check if the left player corresponds to _state.CurrentTurnPlayerIndex.

            // Simplified check: Just ensure the game keeps moving.
            // If it was their turn, we need to skip them.

            // Note: properly mapping ActorNumber to PlayerIndex depends on how rooms are set up.
            // Assuming 4 player max room, actors 1,2,3,4.
            // If we use simple mapping (ActorNumber - 1), then:
            int leftPlayerIndex = otherPlayer.ActorNumber - 1;

            if (leftPlayerIndex == _state.CurrentTurnPlayerIndex)
            {
                 Debug.Log($"[OnPlayerLeftRoom] Current turn player {leftPlayerIndex} disconnected. Advancing turn.");
                 StopTurnTimer(); // Stop their timer
                 AdvanceTurnInternalOnly(); // Skip to next
            }
        }
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

        hudView.SetTurn(_turnNames[_state.CurrentTurnPlayerIndex], _state.CurrentTurnPlayerIndex);
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
        _photon?.SendMoveRequest(turn2, pawnId);
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
        _photon?.SendMoveRequest(turn, pawnId);
    }

    private void OnNetworkMoveRequest(int playerIndex, int pawnId)
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
    hudView.SetTurn(_turnNames[playerIndex], playerIndex);

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
    hudView.SetTurn(_turnNames[nextPlayerIndex], nextPlayerIndex);

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

                // ✅ Eğer Turn RPC zaten geldi ve sıra bizdeyse, butonu açık bırak
                if (btnRollDice != null)
                {
                    bool isMyTurn = (_state.CurrentTurnPlayerIndex == _localPlayerIndex);
                    btnRollDice.interactable = isMyTurn && !_gameOver;
                    Debug.Log($"[FinishMove] Client: turn={_state.CurrentTurnPlayerIndex}, myTurn={isMyTurn}, button={btnRollDice.interactable}");
                }
            }
            return;
        }

        // ==================== OFFLINE ====================
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

        _state.NextTurn(PlayerCount);

        Debug.Log($"[AdvanceTurnInternalOnly] New turn: P{_state.CurrentTurnPlayerIndex}");

        if (PhotonNetwork.InRoom)
        {
            int maxPlayerIndex = PhotonNetwork.CurrentRoom.PlayerCount - 1;

            while (_state.CurrentTurnPlayerIndex > maxPlayerIndex)
            {
                _state.NextTurn(PlayerCount);
            }
        }

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
            hudView.SetTurn(_turnNames[_state.CurrentTurnPlayerIndex], _state.CurrentTurnPlayerIndex);

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
            1 => _greenPawns,
            2 => _yellowPawns,
            3 => _bluePawns,
            _ => _redPawns
        };
    }

    private bool TryGetStartIndexForPlayer(int playerIndex, out int startIndex)
    {
        switch (playerIndex)
        {
            case 0: startIndex = 0; return true;   // Red: WP_00
            case 1: startIndex = 13; return true;  // Green: WP_13
            case 2: startIndex = 26; return true;  // Yellow: WP_26
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
            1 => homeSlots.G,
            2 => homeSlots.Y,
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
            1 => 11,
            2 => 24,
            3 => 37,
            _ => 50
        };
    }

    private IReadOnlyList<RectTransform> GetHomePath(int playerIndex)
    {
        return playerIndex switch
        {
            0 => boardWaypoints.HomeR,
            1 => boardWaypoints.HomeG,
            2 => boardWaypoints.HomeY,
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

        _gameOver = true;

        if (btnRollDice != null)
            btnRollDice.interactable = false;

        if (winnerPanel != null)
        {
            winnerPanel.SetActive(true);
            sfx?.PlayWin();
        }

        if (txtWinner != null)
        {
            string winner = _turnNames[playerIndex];
            txtWinner.text = $"{winner} kazandı!";
        }

        ClearAllHighlights();
    }

    private void OnRestartClicked()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
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

            if (_pawnCurrentWaypoint.TryGetValue(pawn, out int oldWp))
                positionManager?.UnregisterPawnFromWaypoint(pawn, oldWp);

            // Evden çıkış instant kalabilir (tek kareye spawn)
            pawn.SetPosition(boardWaypoints.MainPath[startIndex].position);
            _pawnCurrentWaypoint[pawn] = startIndex;
            positionManager?.RegisterPawnAtWaypoint(pawn, startIndex);

            ResolveCaptures(pawn);
            _isAnimating = false; // ✅ No animation for home exit, reset flag
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

            // ✅ _isAnimating already set at method start, removed redundant lines

            pawnMover.MoveAlongPositions(pawn, positions, () =>
            {
                _pawnCurrentWaypoint[pawn] = targetIndex;
                positionManager?.RegisterPawnAtWaypoint(pawn, targetIndex);
                ResolveCaptures(pawn);
                _isAnimating = false;
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
        FinishMove();
    });
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

        // Online: sadece host timeout kararını alır
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) return;

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
        _photon?.SendMoveRequest(turn, pawnId);
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
    _photon?.SendMoveRequest(turn, pawnId);
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


}