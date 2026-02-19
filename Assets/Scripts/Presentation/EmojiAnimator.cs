using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace LudoFriends.Presentation
{
    /// <summary>
    /// UI Image üzerinde sprite frame animasyonu oynatır.
    /// EmojiPopup objesine ekle, targetImage'ı bağla.
    /// </summary>
    public class EmojiAnimator : MonoBehaviour
    {
        [SerializeField] private Image targetImage;
        [SerializeField] private float fps = 12f;

        private Coroutine _coroutine;

        // -----------------------------------------------

        /// <summary>
        /// Verilen frame dizisiyle animasyon başlatır.
        /// loop = true → animasyon bitince başa döner.
        /// </summary>
        public void Play(Sprite[] frames, bool loop = true)
        {
            if (_coroutine != null) StopCoroutine(_coroutine);
            if (frames == null || frames.Length == 0) return;

            if (targetImage != null) targetImage.enabled = true;
            _coroutine = StartCoroutine(Animate(frames, loop));
        }

        public void Stop()
        {
            if (_coroutine != null)
            {
                StopCoroutine(_coroutine);
                _coroutine = null;
            }
            if (targetImage != null) targetImage.enabled = false;
        }

        // -----------------------------------------------

        private IEnumerator Animate(Sprite[] frames, bool loop)
        {
            float interval = 1f / Mathf.Max(fps, 1f);
            do
            {
                foreach (var frame in frames)
                {
                    if (targetImage != null) targetImage.sprite = frame;
                    yield return new WaitForSeconds(interval);
                }
            }
            while (loop);
        }
    }
}
