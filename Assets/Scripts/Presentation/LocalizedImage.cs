using UnityEngine;
using UnityEngine.UI;

namespace LudoFriends.Presentation
{
    [RequireComponent(typeof(Image))]
    public class LocalizedImage : MonoBehaviour
    {
        [SerializeField] private Sprite spriteTR;
        [SerializeField] private Sprite spriteEN;

        private Image _image;

        private void Awake()
        {
            _image = GetComponent<Image>();
        }

        private void OnEnable()
        {
            LocalizationManager.OnLanguageChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            LocalizationManager.OnLanguageChanged -= Refresh;
        }

        private void Refresh()
        {
            if (_image == null) return;
            _image.sprite = LocalizationManager.CurrentLanguage == LocalizationManager.Language.Turkish
                ? spriteTR
                : spriteEN;
        }
    }
}
