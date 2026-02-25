using System;
using System.Collections.Generic;
using UnityEngine;
using SocketIOClient;
using SocketIOClient.Newtonsoft.Json;

namespace LudoFriends.Networking
{
    public class SocketIONetworkBridge : MonoBehaviour, IGameNetwork
    {
        public static SocketIONetworkBridge Instance { get; private set; }

        // ── IGameNetwork Events ──
        public event Action<int, int> OnRoll;
        public event Action<int, int, int, int> OnMove;
        public event Action<int> OnTurn;
        public event Action<int, int, int> OnMoveRequest;
        public event Action OnRequestAdvanceTurn;
        public event Action<int> OnRollRequest;
        public event Action<float> OnTimerStart;
        public event Action OnTimerStop;
        public event Action<string, int> OnChatMessage;
        public event Action<int> OnExitBot;

        // ── Lobby/Room Events (for LobbyManager & GameBootstrapper) ──
        public event Action<JoinedRoomPayload> OnJoinedRoom;
        public event Action<PlayerJoinedPayload> OnPlayerJoined;
        public event Action<PlayerLeftPayload> OnPlayerLeft;
        public event Action<HostChangedPayload> OnHostChanged;
        public event Action<JoinFailedPayload> OnJoinFailed;
        public event Action<GameStartedPayload> OnGameStarted;
        public event Action<CountdownTickPayload> OnCountdownTick;
        public event Action<RoomCreatedPayload> OnRoomCreated;
        public event Action OnDisconnectedEvent;
        public event Action OnReconnected;
        public event Action<GameStatePayload> OnGameStateReceived;
        public event Action<IdentifiedPayload> OnIdentified;

        // ── State ──
        private SocketIOUnity _socket;
        private bool _isHost;
        private bool _isSpectator;
        private bool _isInRoom;
        private bool _isConnected;
        private int _localPlayerIndex = -1;
        private string _roomCode = "";
        private int _playerCount;
        private double _serverTimeOffset; // local - server
        private List<RoomPlayerInfo> _players = new List<RoomPlayerInfo>();

        // Cached state for TryGet methods
        private GameStatePayload _cachedState;
        private bool _hasGameState;

        // Pending actions (to dispatch from main thread — background thread cannot touch Unity API)
        private volatile bool _pendingIdentify;
        private volatile bool _pendingDisconnect;
        private volatile bool _pendingReconnect;
        private string _nickname;

        // ── Public Properties ──
        public bool IsHost => _isHost;
        public bool IsSpectator => _isSpectator;
        public bool IsInRoom => _isInRoom;
        public bool IsConnected => _isConnected;
        public int LocalPlayerIndex => _localPlayerIndex;
        public string RoomCode => _roomCode;
        public int PlayerCount => _playerCount;
        public double ServerTime => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _serverTimeOffset;
        public List<RoomPlayerInfo> Players => _players;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            Disconnect();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_pendingIdentify && _socket != null)
            {
                _pendingIdentify = false;
                Debug.Log($"[SocketIO] Sending identify from main thread: {_nickname}");
                _socket.Emit("identify", new
                {
                    playerId = NetworkConfig.PlayerId,
                    nickname = _nickname
                });
            }

            if (_pendingDisconnect)
            {
                _pendingDisconnect = false;
                OnDisconnectedEvent?.Invoke();
            }

            if (_pendingReconnect)
            {
                _pendingReconnect = false;
                OnReconnected?.Invoke();
            }
        }

        // ══════════════════════════════════════
        // CONNECTION
        // ══════════════════════════════════════

        public void Connect(string nickname)
        {
            if (_socket != null)
            {
                _socket.Disconnect();
                _socket.Dispose();
            }

            _socket = new SocketIOUnity(NetworkConfig.ServerUrl, new SocketIOOptions
            {
                Reconnection = true,
                ReconnectionAttempts = 10,
                ReconnectionDelay = 1000,
            });

            // Newtonsoft.Json kullan (System.Text.Json public field'ları deserialize edemez)
            _socket.JsonSerializer = new NewtonsoftJsonSerializer();

            _nickname = nickname;

            // Register OnConnected BEFORE Connect() to avoid race condition
            _socket.OnConnected += (sender, e) =>
            {
                _isConnected = true;
                Debug.Log("[SocketIO] Connected to server, will send identify on main thread...");
                _pendingIdentify = true; // Will be sent from Update() on main thread
            };

            RegisterEventListeners();

            Debug.Log("[SocketIO] Connecting to server...");
            _socket.Connect();
        }

        public void Disconnect()
        {
            if (_socket != null)
            {
                _isInRoom = false;
                _isConnected = false;
                try { _socket.Disconnect(); } catch { }
                _socket.Dispose();
                _socket = null;
            }
        }

        private void RegisterEventListeners()
        {
            _socket.OnDisconnected += (sender, reason) =>
            {
                Debug.Log($"[SocketIO] Disconnected: {reason}");
                _isConnected = false;
                _pendingDisconnect = true; // Main thread'de invoke edilecek (Unity API guvenli)
            };

            _socket.OnReconnected += (sender, attempts) =>
            {
                Debug.Log($"[SocketIO] Reconnected after {attempts} attempts");
                _isConnected = true;
                _pendingIdentify = true; // Re-identify on main thread
                _pendingReconnect = true; // Main thread'de invoke edilecek
            };

            // ── Identity ──
            _socket.OnUnityThread("identified", response =>
            {
                Debug.Log($"[SocketIO] RAW identified response received");
                try
                {
                    var data = response.GetValue<IdentifiedPayload>();
                    Debug.Log($"[SocketIO] Identified: success={data.success}, reconnect={data.reconnectRoomCode}");
                    OnIdentified?.Invoke(data);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[SocketIO] Error parsing identified: {ex.Message}");
                }
            });

            // ── Lobby ──
            _socket.OnUnityThread("room_created", response =>
            {
                var data = response.GetValue<RoomCreatedPayload>();
                _roomCode = data.code;
                OnRoomCreated?.Invoke(data);
            });

            _socket.OnUnityThread("joined_room", response =>
            {
                var data = response.GetValue<JoinedRoomPayload>();
                _roomCode = data.code;
                _localPlayerIndex = data.yourPlayerIndex;
                _isHost = data.isHost;
                _isSpectator = data.isSpectator;
                _isInRoom = true;
                _players = new List<RoomPlayerInfo>(data.players);
                _playerCount = _players.Count;
                Debug.Log($"[SocketIO] Joined room {data.code}, idx={data.yourPlayerIndex}, host={data.isHost}, spectator={data.isSpectator}");
                OnJoinedRoom?.Invoke(data);
            });

            _socket.OnUnityThread("player_joined", response =>
            {
                var data = response.GetValue<PlayerJoinedPayload>();
                _players.Add(new RoomPlayerInfo
                {
                    playerId = data.playerId,
                    playerIndex = data.playerIndex,
                    nickname = data.nickname,
                    isConnected = true
                });
                _playerCount = _players.Count;
                OnPlayerJoined?.Invoke(data);
            });

            _socket.OnUnityThread("player_left", response =>
            {
                var data = response.GetValue<PlayerLeftPayload>();
                if (data.isPermanent)
                    _players.RemoveAll(p => p.playerId == data.playerId);
                else
                {
                    var p = _players.Find(x => x.playerId == data.playerId);
                    if (p != null) p.isConnected = false;
                }
                _playerCount = _players.Count;
                OnPlayerLeft?.Invoke(data);
            });

            _socket.OnUnityThread("join_failed", response =>
            {
                var data = response.GetValue<JoinFailedPayload>();
                Debug.LogWarning($"[SocketIO] Join failed: {data.reason}");
                OnJoinFailed?.Invoke(data);
            });

            _socket.OnUnityThread("host_changed", response =>
            {
                var data = response.GetValue<HostChangedPayload>();
                _isHost = data.newHostPlayerId == NetworkConfig.PlayerId;
                Debug.Log($"[SocketIO] Host changed: idx={data.newHostPlayerIndex}, amIHost={_isHost}");
                OnHostChanged?.Invoke(data);
            });

            _socket.OnUnityThread("game_started", response =>
            {
                var data = response.GetValue<GameStartedPayload>();
                Debug.Log($"[SocketIO] Game started with {data.initialPlayerCount} players");
                OnGameStarted?.Invoke(data);
            });

            _socket.OnUnityThread("countdown_tick", response =>
            {
                var data = response.GetValue<CountdownTickPayload>();
                OnCountdownTick?.Invoke(data);
            });

            // ── Game RPCs ──

            _socket.OnUnityThread("roll", response =>
            {
                var data = response.GetValue<RollPayload>();
                OnRoll?.Invoke(data.playerIndex, data.roll);
            });

            _socket.OnUnityThread("move", response =>
            {
                var data = response.GetValue<MovePayload>();
                OnMove?.Invoke(data.playerIndex, data.pawnId, data.roll, data.moveId);
            });

            _socket.OnUnityThread("turn", response =>
            {
                var data = response.GetValue<TurnPayload>();
                OnTurn?.Invoke(data.nextPlayerIndex);
            });

            // Host receives forwarded requests
            _socket.OnUnityThread("roll_request_fwd", response =>
            {
                var data = response.GetValue<RollRequestPayload>();
                OnRollRequest?.Invoke(data.playerIndex);
            });

            _socket.OnUnityThread("move_request_fwd", response =>
            {
                var data = response.GetValue<MoveRequestPayload>();
                OnMoveRequest?.Invoke(data.playerIndex, data.pawnId, data.roll);
            });

            _socket.OnUnityThread("request_advance_turn_fwd", response =>
            {
                OnRequestAdvanceTurn?.Invoke();
            });

            _socket.OnUnityThread("exit_bot_fwd", response =>
            {
                var data = response.GetValue<ExitBotPayload>();
                OnExitBot?.Invoke(data.playerIndex);
            });

            _socket.OnUnityThread("timer_start", response =>
            {
                var data = response.GetValue<TimerStartPayload>();
                OnTimerStart?.Invoke(data.duration);
            });

            _socket.OnUnityThread("timer_stop", response =>
            {
                OnTimerStop?.Invoke();
            });

            _socket.OnUnityThread("chat", response =>
            {
                var data = response.GetValue<ChatPayload>();
                OnChatMessage?.Invoke(data.message, data.senderPlayerIndex);
            });

            // ── State Retrieval ──

            _socket.OnUnityThread("game_state", response =>
            {
                var data = response.GetValue<GameStatePayload>();
                _cachedState = data;
                _hasGameState = true;
                _serverTimeOffset = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - data.serverTime;
                OnGameStateReceived?.Invoke(data);
            });

            _socket.OnUnityThread("server_time", response =>
            {
                var data = response.GetValue<ServerTimePayload>();
                _serverTimeOffset = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - data.time;
            });
        }

        // ══════════════════════════════════════
        // LOBBY METHODS
        // ══════════════════════════════════════

        public void CreateRoom(bool isPrivate)
        {
            _socket?.Emit("create_room", new { isPrivate });
        }

        public void JoinRoom(string code)
        {
            _socket?.Emit("join_room", new { code });
        }

        public void JoinRandom()
        {
            _socket?.Emit("join_random");
        }

        public void LeaveRoom(bool permanent = true)
        {
            _socket?.Emit("leave_room", new { permanent });
            _isInRoom = false;
            _roomCode = "";
            _players.Clear();
            _playerCount = 0;
        }

        public void StartGame()
        {
            _socket?.Emit("start_game");
        }

        // ══════════════════════════════════════
        // IGameNetwork - SEND METHODS
        // ══════════════════════════════════════

        public void SendRollRequest(int playerIndex)
        {
            if (_isHost)
                OnRollRequest?.Invoke(playerIndex);
            else
                _socket?.Emit("roll_request", new { playerIndex });
        }

        public void BroadcastRoll(int playerIndex, int roll)
        {
            _socket?.Emit("broadcast_roll", new { playerIndex, roll });
        }

        public void BroadcastMove(int playerIndex, int pawnId, int roll, int moveId)
        {
            _socket?.Emit("broadcast_move", new { playerIndex, pawnId, roll, moveId });
        }

        public void BroadcastTurn(int nextPlayerIndex)
        {
            _socket?.Emit("broadcast_turn", new { nextPlayerIndex });
        }

        public void SendMoveRequest(int playerIndex, int pawnId, int roll)
        {
            if (_isHost)
                OnMoveRequest?.Invoke(playerIndex, pawnId, roll);
            else
                _socket?.Emit("move_request", new { playerIndex, pawnId, roll });
        }

        public void RequestAdvanceTurn()
        {
            if (_isHost)
                OnRequestAdvanceTurn?.Invoke();
            else
                _socket?.Emit("request_advance_turn");
        }

        public void SendExitBot(int playerIndex)
        {
            if (_isHost)
                OnExitBot?.Invoke(playerIndex);
            else
                _socket?.Emit("exit_bot", new { playerIndex });
        }

        public void BroadcastTimerStart(float duration)
        {
            _socket?.Emit("broadcast_timer_start", new { duration });
        }

        public void BroadcastTimerStop()
        {
            _socket?.Emit("broadcast_timer_stop");
        }

        public void BroadcastChatMessage(string message, int senderPlayerIndex)
        {
            _socket?.Emit("broadcast_chat", new { message, senderPlayerIndex });
        }

        // ══════════════════════════════════════
        // IGameNetwork - STATE PERSISTENCE
        // ══════════════════════════════════════

        public void SyncGameState(int turn, int roll, int phase, int sixes, int extraTurns)
        {
            if (!_isHost) return;
            _socket?.Emit("sync_game_state", new { turn, roll, phase, sixes, extraTurns });
        }

        public bool TryGetGameState(out int turn, out int roll, out int phase, out int sixes, out int extraTurns)
        {
            if (_hasGameState && _cachedState != null)
            {
                turn = _cachedState.turn;
                roll = _cachedState.roll;
                phase = _cachedState.phase;
                sixes = _cachedState.sixes;
                extraTurns = _cachedState.extraTurns;
                return true;
            }
            turn = roll = phase = sixes = extraTurns = 0;
            return false;
        }

        public void SavePawnStates(string serializedStates)
        {
            if (!_isHost) return;
            _socket?.Emit("save_pawn_states", new { pawnStates = serializedStates });
        }

        public string GetPawnStates()
        {
            return _cachedState?.pawnStates;
        }

        public void SaveTimerState(double startTime, float duration)
        {
            if (!_isHost) return;
            _socket?.Emit("save_timer_state", new { startTime, duration });
        }

        public bool TryGetTimerState(out double startTime, out float duration)
        {
            if (_hasGameState && _cachedState != null &&
                _cachedState.timerStartTime > 0 && _cachedState.timerDuration > 0)
            {
                startTime = _cachedState.timerStartTime;
                duration = _cachedState.timerDuration;
                return true;
            }
            startTime = 0;
            duration = 0;
            return false;
        }

        public void ClearTimerState()
        {
            if (!_isHost) return;
            _socket?.Emit("clear_timer_state");
        }

        public void SaveFinishOrder(int[] finishOrder)
        {
            if (!_isHost) return;
            _socket?.Emit("save_finish_order", new { finishOrder });
        }

        public int[] GetFinishOrder()
        {
            return _cachedState?.finishOrder;
        }

        // ══════════════════════════════════════
        // UTILITY
        // ══════════════════════════════════════

        public void RequestGameState()
        {
            _socket?.Emit("get_game_state");
        }

        public void RequestServerTime()
        {
            _socket?.Emit("get_server_time");
        }

        public void SetSpectator(bool isSpectator)
        {
            _isSpectator = isSpectator;
            _socket?.Emit("set_spectator", new { isSpectator });
        }

        public void SetRoomProperty(string key, object value)
        {
            if (!_isHost) return;
            _socket?.Emit("set_room_property", new { key, value });
        }

        public List<RoomPlayerInfo> GetPlayers()
        {
            return _players;
        }
    }
}
