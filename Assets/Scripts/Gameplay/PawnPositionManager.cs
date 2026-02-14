using System.Collections.Generic;
using UnityEngine;
using LudoFriends.Presentation;

namespace LudoFriends.Gameplay
{
    /// <summary>
    /// Aynı waypoint'teki pawnları akıllı stack'ler (ölçeklendirme + grid)
    /// </summary>
    public class PawnPositionManager : MonoBehaviour
    {
        [Header("Stack Config")]
        [SerializeField] private float baseStackOffsetX = 20f;  // Yan mesafe
        [SerializeField] private float baseStackOffsetY = 20f;  // Dikey mesafe

        [Header("Scaling")]
        [SerializeField] private float normalScale = 1f;        // Tek pawn
        [SerializeField] private float twoScale = 0.85f;        // 2 pawn
        [SerializeField] private float threeScale = 0.75f;      // 3 pawn
        [SerializeField] private float fourPlusScale = 0.65f;   // 4+ pawn

        // Key: Waypoint index, Value: Bu waypoint'teki pawnlar
        private readonly Dictionary<int, List<PawnView>> _pawnsByWaypoint = new Dictionary<int, List<PawnView>>();

        // Key: Waypoint index, Value: Waypoint pozisyonu
        private readonly Dictionary<int, Vector3> _waypointPositions = new Dictionary<int, Vector3>();

        /// <summary>
        /// Waypoint pozisyonlarını önbelleğe al
        /// </summary>
        public void CacheWaypointPositions(IReadOnlyList<Transform> waypoints)
        {
            _waypointPositions.Clear();

            for (int i = 0; i < waypoints.Count; i++)
            {
                _waypointPositions[i] = waypoints[i].position;
            }
        }

/// <summary>
/// Home lane pozisyonlarını da önbelleğe al
/// Key format: 1000 + playerIndex * 10 + homeIndex (örn: P0 home[2] = 1002)
/// </summary>
public void CacheHomeLanePositions(int playerIndex, IReadOnlyList<RectTransform> homeLane)
{
    for (int i = 0; i < homeLane.Count; i++)
    {
        int key = GetHomeLaneKey(playerIndex, i);
        _waypointPositions[key] = homeLane[i].position;
    }
}

public static int GetHomeLaneKey(int playerIndex, int homeIndex)
{
    return 1000 + playerIndex * 10 + homeIndex;
}

        /// <summary>
        /// Pawn bir waypoint'e geldiğinde çağrılır
        /// </summary>
        public void RegisterPawnAtWaypoint(PawnView pawn, int waypointIndex)
        {
            if (!_pawnsByWaypoint.ContainsKey(waypointIndex))
                _pawnsByWaypoint[waypointIndex] = new List<PawnView>();

            var list = _pawnsByWaypoint[waypointIndex];

            if (!list.Contains(pawn))
                list.Add(pawn);

            UpdateStackAtWaypoint(waypointIndex);
        }

        /// <summary>
        /// Pawn bir waypoint'ten ayrıldığında çağrılır
        /// </summary>
        public void UnregisterPawnFromWaypoint(PawnView pawn, int waypointIndex)
        {
            if (!_pawnsByWaypoint.ContainsKey(waypointIndex))
                return;

            var list = _pawnsByWaypoint[waypointIndex];
            list.Remove(pawn);

            if (list.Count == 0)
                _pawnsByWaypoint.Remove(waypointIndex);
            else
                UpdateStackAtWaypoint(waypointIndex);
        }

        /// <summary>
        /// Pawn eve döndüğünde tüm waypoint'lerden kaldır
        /// </summary>
        public void UnregisterPawnFromAll(PawnView pawn)
        {
            var keysToUpdate = new List<int>();

            foreach (var kvp in _pawnsByWaypoint)
            {
                if (kvp.Value.Remove(pawn))
                    keysToUpdate.Add(kvp.Key);
            }

            foreach (var key in keysToUpdate)
            {
                if (_pawnsByWaypoint[key].Count == 0)
                    _pawnsByWaypoint.Remove(key);
                else
                    UpdateStackAtWaypoint(key);
            }
        }

        /// <summary>
        /// Belirli bir waypoint'teki pawnları akıllı dizle
        /// </summary>
        private void UpdateStackAtWaypoint(int waypointIndex)
{
    if (!_pawnsByWaypoint.TryGetValue(waypointIndex, out var pawns))
        return;

    if (!_waypointPositions.TryGetValue(waypointIndex, out var basePos))
        return;

    int count = pawns.Count;

    // ✅ Ölçek belirle
    float scale = GetScaleForCount(count);

    // ✅ Layout belirle
    var layout = GetLayoutForCount(count);

    // ✅ Her pawn için pozisyon + stack scale uygula
    for (int i = 0; i < count && i < layout.Count; i++)
    {
        Vector3 offset = layout[i];
        Vector3 finalPos = basePos + offset;

        pawns[i].SetPosition(finalPos);

        // ✅ Stack scale'i pawn'a bildir (highlight sistem bunu kullanacak)
        pawns[i].SetStackScale(scale);
    }
}

        /// <summary>
        /// Pawn sayısına göre scale döndür
        /// </summary>
        private float GetScaleForCount(int count)
        {
            return count switch
            {
                1 => normalScale,
                2 => twoScale,
                3 => threeScale,
                _ => fourPlusScale
            };
        }

        /// <summary>
        /// Pawn sayısına göre layout (offset listesi) döndür
        /// </summary>
        private List<Vector3> GetLayoutForCount(int count)
        {
            switch (count)
            {
                case 1:
                    // Merkez
                    return new List<Vector3> { Vector3.zero };

                case 2:
                    // Yan yana (sol-sağ)
                    return new List<Vector3>
                    {
                        new Vector3(-baseStackOffsetX / 2f, 0, 0),  // Sol
                        new Vector3(baseStackOffsetX / 2f, 0, 0)    // Sağ
                    };

                case 3:
                    // Üstte 2, altta 1 (sol)
                    return new List<Vector3>
                    {
                        new Vector3(-baseStackOffsetX / 2f, baseStackOffsetY / 2f, 0),  // Sol üst
                        new Vector3(baseStackOffsetX / 2f, baseStackOffsetY / 2f, 0),   // Sağ üst
                        new Vector3(-baseStackOffsetX / 2f, -baseStackOffsetY / 2f, 0)  // Sol alt
                    };

                case 4:
                    // 2x2 grid
                    return new List<Vector3>
                    {
                        new Vector3(-baseStackOffsetX / 2f, baseStackOffsetY / 2f, 0),   // Sol üst
                        new Vector3(baseStackOffsetX / 2f, baseStackOffsetY / 2f, 0),    // Sağ üst
                        new Vector3(-baseStackOffsetX / 2f, -baseStackOffsetY / 2f, 0),  // Sol alt
                        new Vector3(baseStackOffsetX / 2f, -baseStackOffsetY / 2f, 0)    // Sağ alt
                    };

                case 5:
                    // 3x2: Sol üst, orta üst, sağ üst, sol alt, orta alt
                    return new List<Vector3>
                    {
                        new Vector3(-baseStackOffsetX, baseStackOffsetY / 2f, 0),   // Sol üst
                        new Vector3(0, baseStackOffsetY / 2f, 0),                   // Orta üst
                        new Vector3(baseStackOffsetX, baseStackOffsetY / 2f, 0),    // Sağ üst
                        new Vector3(-baseStackOffsetX / 2f, -baseStackOffsetY / 2f, 0), // Sol alt
                        new Vector3(baseStackOffsetX / 2f, -baseStackOffsetY / 2f, 0)   // Sağ alt
                    };

                case 6:
                default:
                    // 3x2: 3 üst, 3 alt
                    return new List<Vector3>
                    {
                        new Vector3(-baseStackOffsetX, baseStackOffsetY / 2f, 0),   // Sol üst
                        new Vector3(0, baseStackOffsetY / 2f, 0),                   // Orta üst
                        new Vector3(baseStackOffsetX, baseStackOffsetY / 2f, 0),    // Sağ üst
                        new Vector3(-baseStackOffsetX, -baseStackOffsetY / 2f, 0),  // Sol alt
                        new Vector3(0, -baseStackOffsetY / 2f, 0),                  // Orta alt
                        new Vector3(baseStackOffsetX, -baseStackOffsetY / 2f, 0)    // Sağ alt
                    };
            }
        }

        /// <summary>
        /// Debug: Waypoint durumu
        /// </summary>
        public void DebugPrint()
        {
            foreach (var kvp in _pawnsByWaypoint)
            {
                Debug.Log($"Waypoint {kvp.Key}: {kvp.Value.Count} pawns");
            }
        }
    }
}