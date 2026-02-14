using UnityEngine;

namespace LudoFriends.Gameplay
{
    public class DiceService
    {
        public int Roll()
        {
            return Random.Range(1, 7); // 1..6
        }
    }
}
