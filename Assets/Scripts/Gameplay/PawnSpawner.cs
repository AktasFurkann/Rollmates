using System.Collections.Generic;
using LudoFriends.Presentation;
using UnityEngine;

namespace LudoFriends.Gameplay
{
    public class PawnSpawner : MonoBehaviour
    {
        [SerializeField] private PawnView pawnPrefab;
        [SerializeField] private RectTransform pawnsRoot;

        // ✅ Sprite ile spawn (renkli sprite kullanıyorsan tintColor'ı beyaz geç)
        public List<PawnView> SpawnColor(IReadOnlyList<RectTransform> slots, Sprite pawnSprite, Color tintColor)
        {
            var list = new List<PawnView>(4);

            for (int i = 0; i < slots.Count; i++)
            {
                var pawn = Instantiate(pawnPrefab, pawnsRoot);
                pawn.name = $"Pawn_{i}";

                // Sprite varsa onu bas
                if (pawnSprite != null)
                    pawn.SetSprite(pawnSprite);

                // Tint istersen uygula (renkli sprite'ta beyaz kullan)
                pawn.SetColor(tintColor);

                // Slot pozisyonuna koy
                pawn.SetPosition(slots[i].position);

                list.Add(pawn);
            }

            return list;
        }
    }
}
