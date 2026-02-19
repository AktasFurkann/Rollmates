using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LudoFriends.Presentation
{
    /// <summary>
    /// Oyun tahtasının bir köşesindeki oyuncu bilgi paneli.
    /// Şimdilik: renkli daire + baş harf + isim.
    /// İleride: SetPhoto(Texture2D) ile Google Play / Facebook fotoğrafı takılabilir.
    /// </summary>
    public class PlayerCornerPanel : MonoBehaviour
    {
        [Header("Avatar")]
        [SerializeField] private Image avatarBackground;    // Daire arka plan (oyuncu rengi)
        [SerializeField] private Image avatarFrame;         // Daire kenarlığı (daha parlak)
        [SerializeField] private TextMeshProUGUI txtInitial; // Baş harf: "K", "S", "Y", "M"

        [Header("Photo (ileride gerçek foto için)")]
        [SerializeField] private RawImage photoImage;       // Gerçek profil fotoğrafı (başta gizli)

        [Header("Name")]
        [SerializeField] private TextMeshProUGUI txtName;   // Oyuncu adı

        [Header("Root")]
        [SerializeField] private GameObject root;           // Panelin kökü (Show/Hide için)

        // ------------------------------------------------

        /// <summary>
        /// Paneli göster ve verilen isim + renge göre ayarla.
        /// </summary>
        public void Show(string playerName, Color playerColor)
        {
            if (root != null) root.SetActive(true);

            // Daire arka planı: yarı şeffaf oyuncu rengi
            if (avatarBackground != null)
            {
                Color bg = playerColor;
                bg.a = 0.9f;
                avatarBackground.color = bg;
            }

            // Daire kenarlığı: parlak tam dolu
            if (avatarFrame != null)
                avatarFrame.color = playerColor;

            // Baş harf
            if (txtInitial != null)
            {
                txtInitial.gameObject.SetActive(true);
                txtInitial.text = playerName.Length > 0
                    ? playerName[0].ToString().ToUpper()
                    : "?";
            }

            // Gerçek fotoğraf varsa gizle (SetPhoto çağrılana kadar)
            if (photoImage != null) photoImage.gameObject.SetActive(false);

            // Oyuncu adı
            if (txtName != null)
                txtName.text = playerName;
        }

        /// <summary>
        /// Paneli gizle (bu renkte aktif oyuncu yok).
        /// </summary>
        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }

        /// <summary>
        /// Google Play Games / Facebook entegrasyonu için.
        /// Gerçek profil fotoğrafı geldiğinde bu metodu çağır.
        /// txtInitial gizlenir, photoImage gösterilir.
        /// </summary>
        public void SetPhoto(Texture2D photo)
        {
            if (photoImage == null) return;

            photoImage.texture = photo;
            photoImage.gameObject.SetActive(true);

            if (txtInitial != null)
                txtInitial.gameObject.SetActive(false);
        }

        /// <summary>
        /// Fotoğraf URL'sinden indir ve göster (Coroutine ile).
        /// GameBootstrapper veya bir ProfileManager buraya Coroutine başlatır.
        /// </summary>
        public void SetPhotoFromUrl(string url, MonoBehaviour coroutineHost)
        {
            if (string.IsNullOrEmpty(url) || coroutineHost == null) return;
            coroutineHost.StartCoroutine(DownloadPhoto(url));
        }

        private System.Collections.IEnumerator DownloadPhoto(string url)
        {
            using var request = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(url);
            yield return request.SendWebRequest();

            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Texture2D tex = UnityEngine.Networking.DownloadHandlerTexture.GetContent(request);
                SetPhoto(tex);
            }
        }
    }
}
