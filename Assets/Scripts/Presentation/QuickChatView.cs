using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LudoFriends.Presentation
{
    /// <summary>
    /// Hazır mesaj ve emoji seçimi için Quick Chat paneli.
    /// Emojiler index tabanlı iletilir: "__EMOJI__0", "__EMOJI__1" ...
    /// GameBootstrapper bu index'i kullanarak animasyon frame'lerini çeker.
    /// </summary>
    public class QuickChatView : MonoBehaviour
    {
        [Header("Toggle Button")]
        [SerializeField] private Button btnToggle;

        [Header("Panel")]
        [SerializeField] private GameObject panel;

        [Header("Quick Message Buttons (text Inspector'da ayarlanır)")]
        [SerializeField] private Button[] quickMessageButtons;

        [Header("Emoji Entries (sıra önemli – network index bu listeye göre)")]
        [SerializeField] private EmojiEntry[] emojiEntries;

        private Action<string> _onSend;
        private Action<int> _onEmojiSend; // index tabanlı lokal callback
        private bool _isOpen;
        private GameObject _overlayGO;

        // -----------------------------------------------

        [Serializable]
        public class EmojiEntry
        {
            public Button button;
            [Tooltip("Animasyon frame'leri – sırayla oynatılır (ör: 8-12 sprite)")]
            public Sprite[] frames;
        }

        // -----------------------------------------------

        /// <summary>
        /// onSend  : metin/emoji network gönderimi için (GameBootstrapper)
        /// onEmoji : lokal animasyon için index callback (GameBootstrapper)
        /// </summary>
        public void Init(int localPlayerIndex, Action<string> onSend, Action<int> onEmoji = null)
        {
            _onSend      = onSend;
            _onEmojiSend = onEmoji;

            panel.SetActive(false);
            btnToggle.onClick.AddListener(Toggle);

            // Click-outside overlay
            var canvas = panel.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                _overlayGO = new GameObject("QuickChatOverlay");
                _overlayGO.transform.SetParent(canvas.transform, false);
                _overlayGO.transform.SetSiblingIndex(panel.transform.GetSiblingIndex());
                var rt = _overlayGO.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                var img = _overlayGO.AddComponent<Image>();
                img.color = new Color(0, 0, 0, 0);
                var btn = _overlayGO.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(Close);
                _overlayGO.SetActive(false);
            }

            // Mesaj butonları
            foreach (var btn in quickMessageButtons)
            {
                if (btn == null) continue;
                var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp == null) continue;
                string capturedText = tmp.text;
                btn.onClick.AddListener(() => SendMessage(capturedText));
            }

            // Emoji butonları – index tabanlı
            for (int i = 0; i < emojiEntries.Length; i++)
            {
                var entry = emojiEntries[i];
                if (entry?.button == null) continue;
                int capturedIndex = i;
                entry.button.onClick.AddListener(() => SendEmoji(capturedIndex));
            }
        }

        /// <summary>
        /// Verilen index'e ait sprite frame dizisini döndürür.
        /// GameBootstrapper, network'ten gelen emoji index'ini çözmek için kullanır.
        /// </summary>
        public Sprite[] GetFrames(int index)
        {
            if (index < 0 || index >= emojiEntries.Length) return null;
            return emojiEntries[index].frames;
        }

        // -----------------------------------------------

        private void Toggle() { if (_isOpen) Close(); else Open(); }

        private void Open()
        {
            _isOpen = true;
            panel.SetActive(true);
            if (_overlayGO != null) _overlayGO.SetActive(true);
        }

        private void Close()
        {
            _isOpen = false;
            panel.SetActive(false);
            if (_overlayGO != null) _overlayGO.SetActive(false);
        }

        private void SendMessage(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _onSend?.Invoke(text);
            Close();
        }

        private void SendEmoji(int index)
        {
            // Lokal: hemen animasyonu tetikle
            _onEmojiSend?.Invoke(index);
            // Network: index string olarak gönder
            _onSend?.Invoke("__EMOJI__" + index);
            Close();
        }
    }
}
