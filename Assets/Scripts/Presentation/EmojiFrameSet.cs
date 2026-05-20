using UnityEngine;

namespace LudoFriends.Presentation
{
    /// <summary>
    /// Bir emoji'nin frame animasyon verisi. Her emoji klasoru icin tek bir asset.
    /// EmojiBatchImporter tarafindan otomatik olusturulur.
    /// </summary>
    [CreateAssetMenu(fileName = "EmojiFrameSet", menuName = "Rollmates/Emoji Frame Set", order = 10)]
    public class EmojiFrameSet : ScriptableObject
    {
        [Tooltip("Klasor adi ile esit olmasi onerilir (Clap, Heart, Joy gibi)")]
        public string emojiName;

        [Tooltip("Sirali sprite frame'leri")]
        public Sprite[] frames;

        [Tooltip("Bu emojiye ozel ses (opsiyonel)")]
        public AudioClip audioClip;
    }
}
