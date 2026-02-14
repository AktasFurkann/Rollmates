using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LudoFriends.Presentation
{
    public class PawnView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Image bodyImage;
        [SerializeField] private Outline uiOutline;

        public RectTransform Rect => (RectTransform)transform;

        public event Action<PawnView> Clicked;

        private Transform _homeParent;
        private Vector2 _homeAnchoredPos;
        private Vector3 _homeLocalScale;
        private Quaternion _homeLocalRotation;

        // ---- HIGHLIGHT (SCALE) ----
        [SerializeField] private float normalScale = 1f;
        [SerializeField] private float activeScale = 1.08f;
        [SerializeField] private float legalScale = 1.15f;

        // ✅ Pulse animasyonu
        [SerializeField] private float pulseMin = 1.1f;
        [SerializeField] private float pulseMax = 1.3f;
        [SerializeField] private float pulseSpeed = 3f;

        // ✅ Tıklama alanı büyütücü
        [SerializeField] private float tapAreaMultiplier = 1.8f;

        private Vector3 _baseScale;
        private bool _scaleCached = false;
        private float _stackScale = 1f;
        private bool _isPulsing = false;
        private Coroutine _pulseCoroutine;

        // ✅ Bug 3 fix: Click debouncing
        private float _lastClickTime = -999f;
        private const float CLICK_DEBOUNCE_INTERVAL = 0.3f; // 300ms

        [SerializeField] private CanvasGroup canvasGroup;

        // ✅ Tıklama alanı için invisible collider
        private Image _tapArea;

        public void SetClickable(bool canClick)
        {
            if (canvasGroup == null) return;
            canvasGroup.interactable = canClick;
            canvasGroup.blocksRaycasts = canClick;
            // ✅ Alpha'yı değiştirme, pulse ile gösteriyoruz
        }

        private void CacheBaseScaleIfNeeded()
        {
            if (_scaleCached) return;
            _baseScale = Rect.localScale;
            _scaleCached = true;
            normalScale = _baseScale.x;
        }

        public void SetStackScale(float stackScale)
        {
            _stackScale = stackScale;
            RefreshScale();
        }

        private void RefreshScale()
        {
            CacheBaseScaleIfNeeded();
            Rect.localScale = _baseScale * _stackScale;
        }

        public void SetHighlightNone()
        {
            CacheBaseScaleIfNeeded();
            StopPulse();
            Rect.localScale = _baseScale * _stackScale;
        }

        public void SetHighlightActive()
        {
            CacheBaseScaleIfNeeded();
            StopPulse();
            Rect.localScale = _baseScale * _stackScale * activeScale;
        }

        public void SetHighlightLegal()
        {
            CacheBaseScaleIfNeeded();
            // ✅ Legal piyonlar pulse yapsın
            StartPulse();
        }

        // ✅ Pulse başlat
        private void StartPulse()
        {
            if (_isPulsing) return;
            _isPulsing = true;
            _pulseCoroutine = StartCoroutine(CoPulse());
        }

        // ✅ Pulse durdur
        private void StopPulse()
        {
            if (!_isPulsing) return;
            _isPulsing = false;
            if (_pulseCoroutine != null)
            {
                StopCoroutine(_pulseCoroutine);
                _pulseCoroutine = null;
            }
        }

        // ✅ Pulse coroutine
        private IEnumerator CoPulse()
        {
            CacheBaseScaleIfNeeded();
            float t = 0f;

            while (_isPulsing)
            {
                t += Time.deltaTime * pulseSpeed;
                float scale = Mathf.Lerp(pulseMin, pulseMax, (Mathf.Sin(t) + 1f) / 2f);
                Rect.localScale = _baseScale * _stackScale * scale;
                yield return null;
            }
        }

        private void Awake()
        {
            if (bodyImage == null)
                bodyImage = GetComponentInChildren<Image>(true);

            if (uiOutline == null && bodyImage != null)
                uiOutline = bodyImage.GetComponent<Outline>();

            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();

            if (uiOutline != null)
                uiOutline.enabled = false;

            // ✅ Tıklama alanını büyüt
            SetupExpandedTapArea();
        }

        // ✅ Görünmez büyük tıklama alanı oluştur
        private void SetupExpandedTapArea()
        {
            // Mevcut RectTransform'un sizeDelta'sını büyüt
            // (Pawn görüntüsü aynı kalır, tıklama alanı büyür)
            Vector2 currentSize = Rect.sizeDelta;
            Vector2 expandedSize = currentSize * tapAreaMultiplier;

            // Eğer zaten yeterince büyükse dokunma
            if (currentSize.x >= 80) return;

            // RectTransform'u büyüt
            Rect.sizeDelta = expandedSize;

            // Body image'ı küçült (görüntü aynı kalsın)
            if (bodyImage != null)
            {
                var bodyRect = bodyImage.rectTransform;
                bodyRect.sizeDelta = currentSize;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // ✅ Bug 3 fix: Debounce rapid clicks
            float timeSinceLastClick = Time.time - _lastClickTime;
            if (timeSinceLastClick < CLICK_DEBOUNCE_INTERVAL)
            {
                Debug.Log($"[PawnView] Click debounced ({timeSinceLastClick:F2}s)");
                return;
            }

            _lastClickTime = Time.time;
            Clicked?.Invoke(this);
        }

        public void SetColor(Color c)
        {
            if (bodyImage != null) bodyImage.color = c;
        }

        public void SetSprite(Sprite sprite, bool setNativeSize = false)
        {
            if (bodyImage == null) return;
            bodyImage.sprite = sprite;
            if (setNativeSize) bodyImage.SetNativeSize();
        }

        public void SetOutlineVisible(bool visible)
        {
            if (uiOutline != null)
                uiOutline.enabled = visible;
        }

        public void SetOutlineColor(Color c)
        {
            if (uiOutline != null)
                uiOutline.effectColor = c;
        }

        public void SetOutlineDistance(Vector2 dist)
        {
            if (uiOutline != null)
                uiOutline.effectDistance = dist;
        }

        public void SetPosition(Vector3 worldPos)
        {
            Rect.position = worldPos;
        }

        public void CacheHomeUI()
        {
            _homeParent = Rect.parent;
            _homeAnchoredPos = Rect.anchoredPosition;
            _homeLocalScale = Vector3.one;
            _homeLocalRotation = Rect.localRotation;
        }

        public void ReturnHomeUI()
        {
            StopPulse();

            if (_homeParent != null)
                Rect.SetParent(_homeParent, false);

            Rect.anchoredPosition = _homeAnchoredPos;
            Rect.localScale = _homeLocalScale;
            Rect.localRotation = _homeLocalRotation;
            _stackScale = 1f;
        }
    }
}