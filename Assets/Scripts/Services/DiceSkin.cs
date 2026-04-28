using UnityEngine;

namespace LudoFriends.Services
{
    public enum DiceSkinUnlockType
    {
        Free = 0,
        Coin = 1,
        Ad = 2,
        Iap = 3,
    }

    [CreateAssetMenu(fileName = "DiceSkin", menuName = "Rollmates/Dice Skin", order = 0)]
    public class DiceSkin : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Network-safe unique id (lowercase, ASCII). Örn: default, gold, fire")]
        public string id = "default";

        [Tooltip("Görünen ad (UI). Sonra localization key'e dönüşebilir.")]
        public string displayName = "Default";

        [Tooltip("Inventory grid'inde gösterilecek küçük preview sprite.")]
        public Sprite previewIcon;

        [Header("Sprites")]
        [Tooltip("Roll animasyonu kareleri.")]
        public Sprite[] rollFrames;

        [Tooltip("1-6 yüzleri. Sıra: index 0 = 1, index 5 = 6.")]
        public Sprite[] faceSprites = new Sprite[6];

        [Tooltip("Zara basılmadan önceki varsayılan görüntü. Boşsa faceSprites[0] kullanılır.")]
        public Sprite idleSprite;

        [Header("Unlock")]
        public DiceSkinUnlockType unlockType = DiceSkinUnlockType.Free;

        [Tooltip("Coin türü için coin miktarı, Ad türü için izlenmesi gereken reklam sayısı, IAP için kullanılmaz.")]
        public int unlockCost;

        [Tooltip("IAP türü için Google Play product id.")]
        public string iapProductId = "";
    }
}
