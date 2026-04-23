using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LudoFriends.Presentation
{
    public class MutePlayerRow : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI txtName;
        [SerializeField] private Button muteButton;
        [SerializeField] private GameObject mutedIcon;
        [SerializeField] private GameObject unmutedIcon;

        private PlayerCornerPanel _target;

        public void Bind(string playerName, PlayerCornerPanel target)
        {
            _target = target;

            if (txtName != null) txtName.text = playerName;

            if (muteButton != null)
            {
                muteButton.onClick.RemoveAllListeners();
                muteButton.onClick.AddListener(OnMuteClicked);
            }

            RefreshIcons();
        }

        private void OnMuteClicked()
        {
            if (_target == null) return;
            _target.ToggleMute();
            RefreshIcons();
        }

        private void RefreshIcons()
        {
            bool muted = _target != null && _target.IsMuted;
            if (mutedIcon != null) mutedIcon.SetActive(muted);
            if (unmutedIcon != null) unmutedIcon.SetActive(!muted);
        }
    }
}
