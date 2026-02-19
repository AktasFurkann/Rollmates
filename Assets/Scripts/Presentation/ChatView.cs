using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LudoFriends.Presentation
{
    public class ChatView : MonoBehaviour
    {
        [Header("Toggle Button")]
        [SerializeField] private Button btnToggleChat;
        [SerializeField] private GameObject notificationDot;

        [Header("Chat Panel")]
        [SerializeField] private GameObject chatPanel;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private Transform messageContainer;
        [SerializeField] private GameObject messageItemPrefab;

        [Header("Input")]
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private Button btnSend;

        [Header("Audio")]
        [SerializeField] private SfxPlayer sfx;

        private static readonly string[] PlayerNames = { "Kırmızı", "Sarı", "Yeşil", "Mavi" };
        private static readonly Color[] PlayerColors =
        {
            new Color(0.9f, 0.15f, 0.15f),
            new Color(0.95f, 0.85f, 0f),
            new Color(0.1f, 0.75f, 0.1f),
            new Color(0.15f, 0.35f, 0.9f)
        };

        private int _localPlayerIndex;
        private Action<string> _onSend;
        private bool _isOpen;
        private GameObject _overlayGO;

        public void Init(int localPlayerIndex, Action<string> onSend)
        {
            _localPlayerIndex = localPlayerIndex;
            _onSend = onSend;

            // Mesajların alt alta dizilmesi için Content'e layout ekle
            var vlg = messageContainer.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = messageContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(6, 6, 6, 6);

            var csf = messageContainer.GetComponent<ContentSizeFitter>();
            if (csf == null) csf = messageContainer.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Dışarı tıklayınca paneli kapatmak için şeffaf overlay
            var canvas = chatPanel.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                _overlayGO = new GameObject("ChatOverlay");
                _overlayGO.transform.SetParent(canvas.transform, false);
                _overlayGO.transform.SetSiblingIndex(chatPanel.transform.GetSiblingIndex());
                var rt = _overlayGO.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                var img = _overlayGO.AddComponent<Image>();
                img.color = new Color(0, 0, 0, 0);
                var btn = _overlayGO.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(ToggleChat);
                _overlayGO.SetActive(false);
            }

            chatPanel.SetActive(false);
            if (notificationDot != null) notificationDot.SetActive(false);

            btnToggleChat.onClick.AddListener(ToggleChat);
            btnSend.onClick.AddListener(SendMessage);
            inputField.onSubmit.AddListener(_ => SendMessage());
        }

        public void AddMessage(string message, int senderPlayerIndex)
        {
            var item = Instantiate(messageItemPrefab, messageContainer);
            var txt = item.GetComponentInChildren<TextMeshProUGUI>();
            if (txt != null)
            {
                string name = (senderPlayerIndex >= 0 && senderPlayerIndex < PlayerNames.Length)
                    ? PlayerNames[senderPlayerIndex] : "?";
                Color c = (senderPlayerIndex >= 0 && senderPlayerIndex < PlayerColors.Length)
                    ? PlayerColors[senderPlayerIndex] : Color.white;
                txt.color = c;
                txt.outlineWidth = 0f;
                txt.richText = true;
                txt.text = $"<b>{name}:</b> {message}";
            }

            // Kendi mesajın değilse bildirim sesi çal
            if (senderPlayerIndex != _localPlayerIndex)
                sfx?.PlayChatNotification();

            if (!_isOpen && notificationDot != null)
                notificationDot.SetActive(true);

            StartCoroutine(ScrollToBottom());
        }

        private void ToggleChat()
        {
            _isOpen = !_isOpen;
            chatPanel.SetActive(_isOpen);
            if (_overlayGO != null) _overlayGO.SetActive(_isOpen);
            if (_isOpen)
            {
                if (notificationDot != null) notificationDot.SetActive(false);
                StartCoroutine(ScrollToBottom());
                inputField.ActivateInputField();
            }
            else
            {
                inputField.DeactivateInputField();
            }
        }

        private void SendMessage()
        {
            string text = inputField.text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            inputField.text = "";
            inputField.DeactivateInputField();
            _onSend?.Invoke(text);
        }

        private IEnumerator ScrollToBottom()
        {
            yield return null;
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }
}
