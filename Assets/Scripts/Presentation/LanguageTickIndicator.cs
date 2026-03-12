using UnityEngine;

namespace LudoFriends.Presentation
{
    public class LanguageTickIndicator : MonoBehaviour
    {
        [SerializeField] private RectTransform tickMark;
        [SerializeField] private RectTransform anchorTR;
        [SerializeField] private RectTransform anchorEN;

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
            if (tickMark == null) return;

            bool isTR = LocalizationManager.CurrentLanguage == LocalizationManager.Language.Turkish;
            RectTransform target = isTR ? anchorTR : anchorEN;

            if (target != null)
                tickMark.position = target.position;
        }
    }
}
