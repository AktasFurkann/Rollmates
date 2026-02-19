using System.Collections;
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

        [Header("Emoji Float")]
        [SerializeField] private GameObject emojiPopup;    // Child obje – avatar üstünde, başta inactive
        [SerializeField] private TextMeshProUGUI txtEmoji;  // Metin mesajları için TMP
        [SerializeField] private Image emojiImage;          // Animasyonlu emoji için Image
        [SerializeField] private EmojiAnimator emojiAnimator; // Frame animator

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

        // ------------------------------------------------
        // Emoji Float Animasyonu
        // ------------------------------------------------

        /// <summary>
        /// Profil üstünde kayan metin mesajı gösterir (quick chat veya normal chat).
        /// </summary>
        public void ShowEmojiFloat(string text)
        {
            if (emojiPopup == null) return;
            StopAllCoroutines();

            // Metin moduna geç
            if (txtEmoji != null)  { txtEmoji.gameObject.SetActive(true);  txtEmoji.text = text; }
            if (emojiImage != null) emojiImage.gameObject.SetActive(false);
            if (emojiAnimator != null) emojiAnimator.Stop();

            StartCoroutine(FloatAndFade());
        }

        /// <summary>
        /// Profil üstünde animasyonlu sprite emoji gösterir.
        /// frames: EmojiAnimator'a verilecek sprite dizisi.
        /// </summary>
        public void ShowAnimatedEmoji(Sprite[] frames)
        {
            if (emojiPopup == null || frames == null || frames.Length == 0) return;
            StopAllCoroutines();

            // Animasyon moduna geç
            if (txtEmoji != null)   txtEmoji.gameObject.SetActive(false);
            if (emojiImage != null) emojiImage.gameObject.SetActive(true);
            if (emojiAnimator != null) emojiAnimator.Play(frames, loop: true);

            StartCoroutine(FloatAndFade());
        }

        private IEnumerator FloatAndFade()
        {
            emojiPopup.SetActive(true);

            var rt = emojiPopup.GetComponent<RectTransform>();
            var cg = emojiPopup.GetComponent<CanvasGroup>();
            if (cg == null) cg = emojiPopup.AddComponent<CanvasGroup>();

            Vector2 startPos = Vector2.zero;
            Vector2 endPos   = new Vector2(0f, 80f);
            rt.anchoredPosition = startPos;
            cg.alpha = 1f;

            float duration = 5f;
            float elapsed  = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                rt.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
                cg.alpha = Mathf.Lerp(1f, 0f, t * t);
                yield return null;
            }

            if (emojiAnimator != null) emojiAnimator.Stop();
            emojiPopup.SetActive(false);
        }

        // ------------------------------------------------

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
