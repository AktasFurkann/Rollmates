using UnityEngine;

namespace LudoFriends.Networking
{
    public static class NetworkConfig
    {
        // Geliştirme: Bilgisayarının yerel IP adresi (aynı WiFi'daki telefonlar erişebilir)
        // Yayın: Gerçek sunucu adresiyle değiştir
        public static string ServerUrl = "http://176.34.150.252:3000";

        private const string PLAYER_ID_KEY = "rm_player_id";

        public static string PlayerId
        {
            get
            {
                // GPGS giriş yapılmışsa Google Play ID kullan (cihazlar arası tutarlı)
                var gpgs = Services.GPGSManager.Instance;
                if (gpgs != null && gpgs.IsAuthenticated && !string.IsNullOrEmpty(gpgs.PlayerId))
                    return gpgs.PlayerId;

                // Fallback: yerel UUID
                var id = PlayerPrefs.GetString(PLAYER_ID_KEY, "");
                if (string.IsNullOrEmpty(id))
                {
                    id = System.Guid.NewGuid().ToString();
                    PlayerPrefs.SetString(PLAYER_ID_KEY, id);
                    PlayerPrefs.Save();
                }
                return id;
            }
        }
    }
}
