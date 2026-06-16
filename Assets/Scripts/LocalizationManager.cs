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
        { "select_room_type",    "Oda Türü Seç" },
        { "room_code_input",     "Oda kodu gir..." },
        { "room_label",          "Oda: {0}" },
        { "share",               "Paylaş" },
        { "room_code_label",     "Oda Kodu: {0}" },
        { "qc_0",                "Selam" },
        { "qc_1",                "İyi Şanslar" },
        { "qc_2",                "Teşekkürler!" },
        { "qc_3",                "Hoşçakal" },
        { "qc_4",                "Haha" },
        { "qc_5",                "Eyvah" },
        { "qc_6",                "Evet!" },
        { "qc_7",                "İyi oyun" },
        { "qc_8",                "Şanslı zar!" },
        { "qc_9",                "Hızlan lütfen" },
        { "qc_10",               "İyi oynadın" },
        { "qc_11",               "Yeme lütfen" },
        { "play_with_bots",      "Botlarla Oyna" },
        { "add_bot",             "Bot Ekle +" },
        { "remove_bot",          "Bot Çıkar -" },
        { "take_control",        "Kontrolü Al" },
        { "inventory",           "Envanter" },
        { "coins_earned",        "+{0} coin" },
        { "not_enough_coins",    "Yetersiz coin" },
        { "selected",            "Seçili" },
        { "update_available",    "Güncelleme Mevcut" },
        { "update_now",          "Güncelle" },
        { "update_later",        "Sonra" },
        { "update_force_msg",    "Devam etmek için lütfen güncelleyin." },
        { "update_default_msg",  "Yeni bir sürüm hazır." },
        { "version_label",       "Sürüm {0}" },
        { "version_check_latest","En son sürüm ✓" },
        { "settings_title",      "AYARLAR" },
        { "music_label",         "Müzik" },
        { "sfx_label",           "Ses Efektleri" },
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
        { "select_room_type",    "Select Room Type" },
        { "room_code_input",     "Enter room code..." },
        { "room_label",          "Room: {0}" },
        { "share",               "Share" },
        { "room_code_label",     "Room Code: {0}" },
        { "qc_0",                "Hello" },
        { "qc_1",                "Good Luck" },
        { "qc_2",                "Thank you!" },
        { "qc_3",                "Bye" },
        { "qc_4",                "Haha" },
        { "qc_5",                "Oops" },
        { "qc_6",                "Yes!" },
        { "qc_7",                "Good game" },
        { "qc_8",                "Lucky roll!" },
        { "qc_9",                "Faster Please" },
        { "qc_10",               "Well Played" },
        { "qc_11",               "Please don't Kill" },
        { "play_with_bots",      "Play vs Bots" },
        { "add_bot",             "Add Bot +" },
        { "remove_bot",          "Remove Bot -" },
        { "take_control",        "Take Control" },
        { "inventory",           "Inventory" },
        { "coins_earned",        "+{0} coins" },
        { "not_enough_coins",    "Not enough coins" },
        { "selected",            "Selected" },
        { "update_available",    "Update Available" },
        { "update_now",          "Update" },
        { "update_later",        "Later" },
        { "update_force_msg",    "Please update to continue." },
        { "update_default_msg",  "A new version is available." },
        { "version_label",       "Version {0}" },
        { "version_check_latest","Latest version ✓" },
        { "settings_title",      "SETTINGS" },
        { "music_label",         "Music" },
        { "sfx_label",           "Sound Effects" },
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

        // İlk kez açılıyorsa cihaz diline göre belirle, sonraki açılışlarda kullanıcı tercihini koru
        int defaultLang = Application.systemLanguage == SystemLanguage.Turkish
            ? (int)Language.Turkish
            : (int)Language.English;
        CurrentLanguage = (Language)PlayerPrefs.GetInt(PrefKey, defaultLang);
    }

    /// <summary>Returns the localized string for the given key in the current language.</summary>
    public static string Get(string key)
    {
        var dict = CurrentLanguage == Language.English ? _en : _tr;
        return dict.TryGetValue(key, out string val) ? val : $"[{key}]";
    }

    /// <summary>Returns the player color name at index (0=Red/Kırmızı … 3=Blue/Mavi).</summary>
    public static string GetColorName(int index) => Get($"color_{index}");

    /// <summary>Returns the localized quick chat message at index.</summary>
    public static string GetQuickChat(int index) => Get($"qc_{index}");

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
