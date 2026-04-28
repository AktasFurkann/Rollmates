using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using LudoFriends.Services;

namespace LudoFriends.Presentation
{
    /// <summary>
    /// Zar sprite animasyonunu yönetir.
    /// Roll sırasında rollFrames döngüsü oynatılır, sonuçta ilgili yüz gösterilir.
    /// </summary>
    public class DiceAnimator : MonoBehaviour
    {
        [SerializeField] private Image targetImage;
        [SerializeField] private float fps = 12f;

        [Tooltip("Animasyon kareleri – sprite sheet'in alt 2 satırı")]
        [SerializeField] private Sprite[] rollFrames;

        [Tooltip("Sonuç yüzleri – sprite sheet'in üst satırı (index 0 = değer 1, ..., index 5 = değer 6)")]
        [SerializeField] private Sprite[] faceSprites;

        [Tooltip("Zara basılmadan önceki varsayılan görüntü (boşsa faceSprites[0] kullanılır)")]
        [SerializeField] private Sprite idleSprite;

        [Tooltip("True ise Awake'de oyuncunun seçili skin'ini otomatik uygular (lokal). " +
                 "Multiplayer roll için ApplySkin manuel çağrılmalı.")]
        [SerializeField] private bool autoApplyLocalSkin = true;

        private Coroutine _coroutine;

        private void Awake()
        {
            if (autoApplyLocalSkin)
            {
                var skin = DiceSkinManager.GetSelected();
                if (skin != null) ApplySkin(skin);
            }
        }

        /// <summary>
        /// Skin sprite'larını runtime'da uygular. Idle varsa Hide() ile yeniden gösterilir.
        /// </summary>
        public void ApplySkin(DiceSkin skin)
        {
            if (skin == null) return;
            if (skin.rollFrames != null && skin.rollFrames.Length > 0) rollFrames = skin.rollFrames;
            if (skin.faceSprites != null && skin.faceSprites.Length > 0) faceSprites = skin.faceSprites;
            idleSprite = skin.idleSprite;
            // Idle gösteriliyorsa yeni skin'in idle'ına geç. Roll sırasında çağrılırsa görsel
            // animasyon zaten sürdüğü için bir sonraki frame yeni rollFrames'i kullanacak.
            if (_coroutine == null) Hide();
        }

        // -----------------------------------------------

        /// <summary>
        /// rollFrames üzerinde sonsuz döngü animasyonu başlatır.
        /// </summary>
        public void PlayRolling()
        {
            if (_coroutine != null) StopCoroutine(_coroutine);
            if (rollFrames == null || rollFrames.Length == 0) return;

            if (targetImage != null)
            {
                targetImage.sprite = rollFrames[0];
                targetImage.enabled = true;
            }
            _coroutine = StartCoroutine(AnimateRoll());
        }

        /// <summary>
        /// Animasyonu durdurur ve verilen değerin yüz sprite'ını gösterir (1–6).
        /// </summary>
        public void ShowResult(int value)
        {
            if (_coroutine != null)
            {
                StopCoroutine(_coroutine);
                _coroutine = null;
            }

            if (faceSprites == null || faceSprites.Length == 0) return;

            int idx = Mathf.Clamp(value - 1, 0, faceSprites.Length - 1);
            if (targetImage != null)
            {
                targetImage.sprite = faceSprites[idx];
                targetImage.enabled = true;
            }
        }

        /// <summary>
        /// Animasyonu durdurur. faceSprites varsa ilk yüzü (idle) gösterir, yoksa image'ı gizler.
        /// </summary>
        public void Hide()
        {
            if (_coroutine != null)
            {
                StopCoroutine(_coroutine);
                _coroutine = null;
            }

            if (targetImage == null) return;

            if (idleSprite != null)
            {
                targetImage.sprite = idleSprite;
                targetImage.enabled = true;
            }
            else if (faceSprites != null && faceSprites.Length > 0)
            {
                targetImage.sprite = faceSprites[0];
                targetImage.enabled = true;
            }
            else
            {
                targetImage.enabled = false;
            }
        }

        // -----------------------------------------------

        private IEnumerator AnimateRoll()
        {
            float interval = 1f / Mathf.Max(fps, 1f);
            while (true)
            {
                foreach (var frame in rollFrames)
                {
                    if (targetImage != null) targetImage.sprite = frame;
                    yield return new WaitForSeconds(interval);
                }
            }
        }
    }
}
