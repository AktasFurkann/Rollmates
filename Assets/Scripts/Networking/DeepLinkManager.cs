using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Singleton that listens for deep links in the format rollmates://join/{roomCode}
/// and stores the pending room code for LobbyManager to consume.
/// Survives scene loads via DontDestroyOnLoad.
/// </summary>
public class DeepLinkManager : MonoBehaviour
{
    public static DeepLinkManager Instance { get; private set; }

    /// <summary>
    /// The room code extracted from the most recent deep link.
    /// LobbyManager reads this on Start() and clears it after consuming.
    /// </summary>
    public string PendingRoomCode { get; private set; } = "";

    /// <summary>
    /// Fired whenever a new pending code is set.
    /// LobbyManager subscribes to this for the warm-start-on-LobbyScene case.
    /// </summary>
    public static event System.Action<string> OnPendingCodeChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateInstance()
    {
        if (Instance != null) return;
        var go = new GameObject("DeepLinkManager");
        go.AddComponent<DeepLinkManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Soguk baslama: uygulama kapali, link tiklandiysa URL burada gelir
        if (!string.IsNullOrEmpty(Application.absoluteURL))
            ProcessURL(Application.absoluteURL);

        Application.deepLinkActivated += OnDeepLinkActivated;
    }

    private void OnDestroy()
    {
        Application.deepLinkActivated -= OnDeepLinkActivated;
    }

    private void OnDeepLinkActivated(string url)
    {
        Debug.Log($"[DeepLink] Link alindi: {url}");
        ProcessURL(url);

        // MainMenu'deyse LobbyScene'e gec; LobbyManager.Start() orada kodu okur
        if (SceneManager.GetActiveScene().name == "MainMenu")
            SceneManager.LoadScene("LobbyScene");

        // LobbyScene zaten aktifse eventi tetikle
        OnPendingCodeChanged?.Invoke(PendingRoomCode);
    }

    private void ProcessURL(string url)
    {
        try
        {
            var uri = new System.Uri(url);

            if (uri.Scheme != "rollmates")
            {
                Debug.LogWarning($"[DeepLink] Bilinmeyen scheme: {uri.Scheme}");
                return;
            }

            // rollmates://join/123456 → AbsolutePath = "/123456"
            string code = uri.AbsolutePath.TrimStart('/');

            if (code.Length == 6 && System.Text.RegularExpressions.Regex.IsMatch(code, @"^\d{6}$"))
            {
                PendingRoomCode = code;
                Debug.Log($"[DeepLink] Oda kodu alindi: {PendingRoomCode}");
            }
            else
            {
                Debug.LogWarning($"[DeepLink] Gecersiz oda kodu: '{code}'");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[DeepLink] URL parse hatasi: {e.Message}");
        }
    }

    /// <summary>
    /// Kodu tukettikten sonra temizle. LobbyManager bunu JoinRoomByCode'dan once cagirır.
    /// </summary>
    public void ConsumePendingCode() => PendingRoomCode = "";
}
