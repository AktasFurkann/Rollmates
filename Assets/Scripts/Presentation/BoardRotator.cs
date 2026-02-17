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
            float angle = 0f;
            switch (localPlayerIndex)
            {
                case 0: angle = 0f; break;   // Red -> 0
                case 1: angle = 180f; break; // Yellow -> 180
                case 2: angle = 90f; break;  // Green -> 90
                case 3: angle = 270f; break; // Blue -> 270
            }
            Rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>
        /// Piyon sprite'larının dik kalması için ters rotasyon döndürür.
        /// </summary>
        public static Quaternion GetCounterRotation(int localPlayerIndex)
        {
            float angle = 0f;
            switch (localPlayerIndex)
            {
                case 0: angle = 0f; break;
                case 1: angle = 180f; break;
                case 2: angle = 90f; break;
                case 3: angle = 270f; break;
            }
            return Quaternion.Euler(0f, 0f, -angle);
        }
    }
}
