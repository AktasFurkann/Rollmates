using UnityEngine;

namespace LudoFriends.Networking
{
    public static class NetworkConfig
    {
        // Geliştirme: Bilgisayarının yerel IP adresi (aynı WiFi'daki telefonlar erişebilir)
        // Yayın: Gerçek sunucu adresiyle değiştir
        public static string ServerUrl = "http://34.244.199.87:3000";

        private const string PLAYER_ID_KEY = "rm_player_id";

        public static string PlayerId
        {
            get
            {
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
