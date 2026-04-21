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

        [Header("Mute")]
        [SerializeField] private Button muteButton;         // Karşı oyuncuyu sustur butonu
        [SerializeField] private GameObject mutedIcon;      // Mute olduğunda gösterilecek ikon (🔇)
        [SerializeField] private GameObject unmutedIcon;    // Mute değilken gösterilecek ikon (🔊)

        /// <summary>Bu oyuncunun sohbeti/emojisi susturuldu mu?</summary>
        public bool IsMuted { get; private set; }

        private void Awake()
        {
            if (muteButton != null)
                muteButton.onClick.AddListener(ToggleMute);
        }

        private void ToggleMute()
        {
            IsMuted = !IsMuted;
            RefreshMuteIcons();
        }

        private void RefreshMuteIcons()
        {
            if (mutedIcon != null)   mutedIcon.SetActive(IsMuted);
            if (unmutedIcon != null) unmutedIcon.SetActive(!IsMuted);
        }

        /// <summary>
        /// Yerel oyuncu paneli için mute butonunu gizler.
        /// Karşı oyuncu panelleri için muteButton aktif kalır.
        /// </summary>
        public void SetLocalPlayer(bool isLocal)
        {
            if (muteButton != null)
                muteButton.gameObject.SetActive(!isLocal);
        }

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

        private Vector2 _emojiPopupOrigin;
        private bool _emojiPopupOriginCached;

        private Vector2 GetEmojiPopupOrigin()
        {
            if (!_emojiPopupOriginCached && emojiPopup != null)
            {
                _emojiPopupOrigin = emojiPopup.GetComponent<RectTransform>().anchoredPosition;
                _emojiPopupOriginCached = true;
            }
            return _emojiPopupOrigin;
        }

        /// <summary>
        /// Profil üstünde kayan metin mesajı gösterir (quick chat veya normal chat).
        /// </summary>
        public void ShowEmojiFloat(string text)
        {
            if (emojiPopup == null) return;
            StopAllCoroutines();

            emojiPopup.SetActive(true);

            // Tüm Image bileşenlerini aç, sadece emojiImage'ı kapat
            // (component.enabled kullan — gameObject.SetActive değil, yoksa children kaybolur)
            foreach (var img in emojiPopup.GetComponentsInChildren<Image>(true))
            {
                img.gameObject.SetActive(true);
                img.enabled = (img != emojiImage);
            }

            if (emojiAnimator != null) emojiAnimator.Stop();

            // txtEmoji atanmamışsa dinamik bul
            var label = txtEmoji != null
                ? txtEmoji
                : emojiPopup.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (label != null) { label.gameObject.SetActive(true); label.text = text; }

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

            emojiPopup.SetActive(true);

            // Tüm Image bileşenlerini kapat, sadece emojiImage'ı aç
            // (component.enabled kullan — baloncuk arka planı dahil hepsi gizlenir)
            foreach (var img in emojiPopup.GetComponentsInChildren<Image>(true))
            {
                img.gameObject.SetActive(true);
                img.enabled = (img == emojiImage);
            }

            // Text içeriklerini gizle
            foreach (var tmp in emojiPopup.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                tmp.gameObject.SetActive(false);

            if (emojiImage != null) emojiImage.gameObject.SetActive(true);
            if (emojiAnimator != null) emojiAnimator.Play(frames, loop: true);

            StartCoroutine(FloatUpAndFade());
        }

        private IEnumerator FloatAndFade()
        {
            emojiPopup.SetActive(true);

            var cg = emojiPopup.GetComponent<CanvasGroup>();
            if (cg == null) cg = emojiPopup.AddComponent<CanvasGroup>();

            cg.alpha = 1f;

            // 3 saniye gorunur bekle
            yield return new WaitForSeconds(3f);

            // 1 saniyede fade out
            float fadeDuration = 1f;
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                cg.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
                yield return null;
            }

            if (emojiAnimator != null) emojiAnimator.Stop();
            emojiPopup.SetActive(false);
        }

        // Emoji için: baloncuksuz, yukarı kayarak ve fade out
        private IEnumerator FloatUpAndFade()
        {
            emojiPopup.SetActive(true);

            var cg = emojiPopup.GetComponent<CanvasGroup>();
            if (cg == null) cg = emojiPopup.AddComponent<CanvasGroup>();
            cg.alpha = 1f;

            var rt = emojiPopup.GetComponent<RectTransform>();
            Vector2 startPos = GetEmojiPopupOrigin();
            if (rt != null) rt.anchoredPosition = startPos;

            float duration = 2.5f;
            float floatDistance = 80f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                cg.alpha = Mathf.Lerp(1f, 0f, t);
                if (rt != null)
                    rt.anchoredPosition = Vector2.Lerp(startPos, startPos + Vector2.up * floatDistance, t);
                yield return null;
            }

            if (rt != null) rt.anchoredPosition = startPos;
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
