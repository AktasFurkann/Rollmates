using System.Collections;
using UnityEngine;
using UnityEngine.UI;

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

        private Coroutine _coroutine;

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

            if (faceSprites != null && faceSprites.Length > 0)
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
