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

[Header("Dice Positions")]
[SerializeField] private RectTransform diceContainer;  // Zar elementlerinin parent'ı
[SerializeField] private RectTransform dicePosBottom;   // Kendi sıran (alt)
[SerializeField] private RectTransform dicePosLeft;     // +1 oyuncu (sol)
[SerializeField] private RectTransform dicePosTop;      // +2 oyuncu (üst)
[SerializeField] private RectTransform dicePosRight;    // +3 oyuncu (sağ)

[Header("Timer")]
[SerializeField] private TextMeshProUGUI txtTimer;  // ✅ Geri sayım göstergesi

        private readonly Color[] _turnColors = new Color[]
        {
            new Color(0.9f, 0.15f, 0.15f),  // Kırmızı
            new Color(0.95f, 0.85f, 0.0f),  // Sarı (Was Green)
            new Color(0.1f, 0.75f, 0.1f),   // Yeşil (Was Yellow)
            new Color(0.15f, 0.35f, 0.9f)   // Mavi
        };

        public void SetTurn(string turnName, int playerIndex = -1, int localPlayerIndex = -1)
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

        // Zar pozisyonunu sıradaki oyuncunun bölgesine taşı
        if (localPlayerIndex >= 0)
        {
            // ✅ Açıya göre pozisyon belirleme (turn sırası artık dairesel değil)
            float playerAngle = GetPlayerAngle(playerIndex);
            float localAngle = GetPlayerAngle(localPlayerIndex);

            // Farkı hesapla (0, 90, 180, 270)
            float diff = (playerAngle - localAngle + 360f) % 360f;

            // Diff -> Relative Position mapping
            // 0 -> Me (Bottom) -> 0
            // 90 -> Left -> 1
            // 180 -> Top -> 2
            // 270 -> Right -> 3

            int relativePos = 0;
            if (Mathf.Approximately(diff, 90f)) relativePos = 1;
            else if (Mathf.Approximately(diff, 180f)) relativePos = 2;
            else if (Mathf.Approximately(diff, 270f)) relativePos = 3;

            SetDicePosition(relativePos);
        }
    }
}

        public void SetDicePosition(int relativePosition)
        {
            if (diceContainer == null) return;

            RectTransform target = relativePosition switch
            {
                1 => dicePosLeft,
                2 => dicePosTop,
                3 => dicePosRight,
                _ => dicePosBottom
            };

            if (target != null)
                diceContainer.position = target.position;
        }

        public void SetDice(int value)
        {
            txtDice.text = value < 0 ? "-" : value.ToString();
        }

        // ✅ Timer UI metotları
        public void SetTimer(float seconds)
        {
            if (txtTimer == null) return;
            int sec = Mathf.CeilToInt(seconds);
            txtTimer.text = sec > 0 ? sec.ToString() : "";

            // Son 5 saniyede kırmızıya dön
            if (sec <= 5)
                txtTimer.color = new Color(0.9f, 0.15f, 0.15f);
            else
                txtTimer.color = Color.white;
        }

        public void HideTimer()
        {
            if (txtTimer != null)
                txtTimer.text = "";
        }

        private float GetPlayerAngle(int playerIndex)
        {
            // 0: Red (0)
            // 1: Yellow (180 - Opposite)
            // 2: Green (90 - Left)
            // 3: Blue (270 - Right)
            if (playerIndex == 1) return 180f;
            if (playerIndex == 2) return 90f;
            return playerIndex * 90f;
        }
    }
}