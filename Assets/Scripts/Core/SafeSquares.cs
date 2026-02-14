using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LudoFriends.Core
{
    public class SafeSquares : MonoBehaviour
    {
        [SerializeField] private BoardWaypoints boardWaypoints;
        [SerializeField] private List<RectTransform> starMarkers = new();

        private readonly HashSet<int> _safeIndices = new HashSet<int>();

        public bool IsSafeIndex(int mainIndex) => _safeIndices.Contains(mainIndex);

        private IEnumerator Start()
        {
            // BoardWaypoints MainPath'i kendisi oluşturuyorsa, 1 frame beklemek güvenli.
            yield return null;

            _safeIndices.Clear();

            if (boardWaypoints == null || boardWaypoints.MainPath == null || boardWaypoints.MainPath.Count == 0)
            {
                Debug.LogError("[SafeSquares] BoardWaypoints/MainPath missing.");
                yield break;
            }

            foreach (var m in starMarkers)
            {
                if (m == null) continue;
                int idx = FindClosestWaypointIndex(m.position);
                _safeIndices.Add(idx);
            }

            var list = new List<int>(_safeIndices);
            list.Sort();
            Debug.Log("[SafeSquares] Safe indices: " + string.Join(", ", list));
        }

        private int FindClosestWaypointIndex(Vector3 worldPos)
        {
            float best = float.MaxValue;
            int bestIdx = 0;

            var path = boardWaypoints.MainPath;
            for (int i = 0; i < path.Count; i++)
            {
                float d = (path[i].position - worldPos).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    bestIdx = i;
                }
            }

            return bestIdx;
        }
    }
}
