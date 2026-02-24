using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton that manages Turkish/English localization.
/// Auto-creates itself via RuntimeInitializeOnLoadMethod — no scene setup needed.
/// Survives scene loads via DontDestroyOnLoad.
/// </summary>
public class LocalizationManager : MonoBehaviour
{
    public static LocalizationManager Instance { get; private set; }

    public enum Language { Turkish = 0, English = 1 }

    public static Language CurrentLanguage { get; private set; } = Language.Turkish;

    /// <summary>Fired after CurrentLanguage changes. Subscribe to refresh UI text.</summary>
    public static event Action OnLanguageChanged;

    private const string PrefKey = "Language";

    private static readonly Dictionary<string, string> _tr = new()
    {
        { "connected",           "Bağlandı!" },
        { "connecting",          "Sunucuya bağlanılıyor..." },
        { "ready",               "Hazır!" },
        { "room_created",        "Oda oluşturuldu!" },
        { "searching",           "Oyun aranıyor..." },
        { "room_not_found",      "Oda bulunamadı!" },
        { "room_not_found_full", "Oda bulunamadı veya dolu!" },
        { "disconnected",        "Bağlantı kesildi!" },
        { "invite_copied",       "Davet linki kopyalandı!" },
        { "not_enough_players",  "Yeterli oyuncu yok, bekleniyor..." },
        { "starting_in",         "Başlamaya {0} saniye..." },
        { "waiting_for_players", "Oyuncular bekleniyor..." },
        { "loading_game",        "Oyun yükleniyor..." },
        { "waiting_for_host",    "Waiting for host..." },
        { "reconnecting",        "Yeniden bağlanılıyor... ({0}s)" },
        { "could_not_connect",   "Bağlantı kurulamadı." },
        { "connecting_dots",     "Bağlanılıyor..." },
        { "reconnect_failed",    "Yeniden bağlanılamadı. Tekrar dene." },
        { "game_over",           "Oyun Bitti!" },
        { "rankings",            "Sıralama" },
        { "color_0",             "Kırmızı" },
        { "color_1",             "Sarı" },
        { "color_2",             "Yeşil" },
        { "color_3",             "Mavi" },
        { "become_spectator",    "İzleyici Ol" },
        { "become_player",       "Oyuncu Ol" },
    };

    private static readonly Dictionary<string, string> _en = new()
    {
        { "connected",           "Connected!" },
        { "connecting",          "Connecting to server..." },
        { "ready",               "Ready!" },
        { "room_created",        "Room created!" },
        { "searching",           "Searching for game..." },
        { "room_not_found",      "Room not found!" },
        { "room_not_found_full", "Room not found or full!" },
        { "disconnected",        "Connection lost!" },
        { "invite_copied",       "Invite link copied!" },
        { "not_enough_players",  "Not enough players, waiting..." },
        { "starting_in",         "Starting in {0} seconds..." },
        { "waiting_for_players", "Waiting for players..." },
        { "loading_game",        "Loading game..." },
        { "waiting_for_host",    "Waiting for host..." },
        { "reconnecting",        "Reconnecting... ({0}s)" },
        { "could_not_connect",   "Could not connect." },
        { "connecting_dots",     "Connecting..." },
        { "reconnect_failed",    "Reconnection failed. Try again." },
        { "game_over",           "Game Over!" },
        { "rankings",            "Rankings" },
        { "color_0",             "Red" },
        { "color_1",             "Yellow" },
        { "color_2",             "Green" },
        { "color_3",             "Blue" },
        { "become_spectator",    "Spectate" },
        { "become_player",       "Play" },
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateInstance()
    {
        if (Instance != null) return;
        var go = new GameObject("LocalizationManager");
        go.AddComponent<LocalizationManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        CurrentLanguage = (Language)PlayerPrefs.GetInt(PrefKey, (int)Language.Turkish);
    }

    /// <summary>Returns the localized string for the given key in the current language.</summary>
    public static string Get(string key)
    {
        var dict = CurrentLanguage == Language.English ? _en : _tr;
        return dict.TryGetValue(key, out string val) ? val : $"[{key}]";
    }

    /// <summary>Returns the player color name at index (0=Red/Kırmızı … 3=Blue/Mavi).</summary>
    public static string GetColorName(int index) => Get($"color_{index}");

    // Instance methods so Unity Inspector Button.onClick can bind them directly.

    public void SetLanguageTR()
    {
        if (CurrentLanguage == Language.Turkish) return;
        CurrentLanguage = Language.Turkish;
        PlayerPrefs.SetInt(PrefKey, (int)Language.Turkish);
        PlayerPrefs.Save();
        OnLanguageChanged?.Invoke();
    }

    public void SetLanguageEN()
    {
        if (CurrentLanguage == Language.English) return;
        CurrentLanguage = Language.English;
        PlayerPrefs.SetInt(PrefKey, (int)Language.English);
        PlayerPrefs.Save();
        OnLanguageChanged?.Invoke();
    }
}
