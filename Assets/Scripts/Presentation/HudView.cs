using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LudoFriends.Presentation
{
    public class HudView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI txtTurnInfo;
        [SerializeField] private TextMeshProUGUI txtDice;

        [Header("Turn Indicator")]
        [SerializeField] private Image turnColorIndicator;  // ✅ Renk göstergesi
        [SerializeField] private Image turnBackground;      // ✅ Arka plan

[Header("Dice")]
[SerializeField] private Image diceBG;             // ✅ YENİ: Zar arka plan çerçevesi

[Header("Roll Button")]
[SerializeField] private Image rollButtonImage;     // ✅ YENİ: Roll butonunun Image'ı

        private readonly Color[] _turnColors = new Color[]
        {
            new Color(0.9f, 0.15f, 0.15f),  // Kırmızı
            new Color(0.1f, 0.75f, 0.1f),   // Yeşil
            new Color(0.95f, 0.85f, 0.0f),  // Sarı
            new Color(0.15f, 0.35f, 0.9f)   // Mavi
        };

        public void SetTurn(string turnName, int playerIndex = -1)
{
    txtTurnInfo.text = turnName;

    if (playerIndex >= 0 && playerIndex < _turnColors.Length)
    {
        Color c = _turnColors[playerIndex];

        if (turnColorIndicator != null)
            turnColorIndicator.color = c;

        if (turnBackground != null)
        {
            Color bg = c;
            bg.a = 0.3f;
            turnBackground.color = bg;
        }

        txtTurnInfo.color = c;

        // ✅ Zar outline rengini değiştir
        if (diceBG != null)
        {
            var outline = diceBG.GetComponent<Outline>();
            if (outline != null)
                outline.effectColor = c;
        }

        // ✅ Zar yazı outline rengini değiştir
        if (txtDice != null)
        {
            txtDice.outlineColor = c;
            txtDice.outlineWidth = 0.25f;
        }

        // ✅ Roll buton rengini değiştir
        if (rollButtonImage != null)
            rollButtonImage.color = c;
    }
}

        public void SetDice(int value)
        {
            txtDice.text = value < 0 ? "-" : value.ToString();
        }
    }
}