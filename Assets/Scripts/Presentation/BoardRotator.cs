using UnityEngine;

namespace LudoFriends.Presentation
{
    /// <summary>
    /// Tahtayı yerel oyuncunun rengi sol altta olacak şekilde döndürür.
    /// CacheWaypointPositions'dan ÖNCE çağrılmalıdır.
    /// </summary>
    public class BoardRotator : MonoBehaviour
    {
        public RectTransform Rect => (RectTransform)transform;

        /// <summary>
        /// Player 0 = 0°, Player 1 = 90°, Player 2 = 180°, Player 3 = 270°
        /// </summary>
        public void ApplyRotation(int localPlayerIndex)
        {
            float angle = localPlayerIndex * 90f;
            Rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>
        /// Piyon sprite'larının dik kalması için ters rotasyon döndürür.
        /// </summary>
        public static Quaternion GetCounterRotation(int localPlayerIndex)
        {
            return Quaternion.Euler(0f, 0f, -(localPlayerIndex * 90f));
        }
    }
}
