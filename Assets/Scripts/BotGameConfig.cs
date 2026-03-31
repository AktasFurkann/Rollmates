namespace LudoFriends.Networking
{
    /// <summary>
    /// Static configuration for single-player bot games.
    /// Set before loading GameScene, read by GameBootstrapper on Awake.
    /// </summary>
    public static class BotGameConfig
    {
        /// <summary>True when the next game should be an offline bot game.</summary>
        public static bool IsActive { get; set; }

        /// <summary>Total number of players including the human (2, 3, or 4).</summary>
        public static int TotalPlayers { get; set; }

        /// <summary>Set by MainMenu to tell LobbyManager to skip server connection and enter bot lobby.</summary>
        public static bool PendingBotLobby { get; set; }

        /// <summary>Reset after the game starts to avoid leaking into future sessions.</summary>
        public static void Reset()
        {
            IsActive = false;
            TotalPlayers = 0;
            PendingBotLobby = false;
        }
    }
}
