using System.Collections.Generic;
using UnityEngine;

namespace LudoFriends.Core
{
    public class HomeSlots : MonoBehaviour
    {
        [SerializeField] private RectTransform waypointsRoot;

        public IReadOnlyList<RectTransform> R => _r;
        public IReadOnlyList<RectTransform> Y => _y;
        public IReadOnlyList<RectTransform> B => _b;
        public IReadOnlyList<RectTransform> G => _g;

        private readonly List<RectTransform> _r = new();
        private readonly List<RectTransform> _y = new();
        private readonly List<RectTransform> _b = new();
        private readonly List<RectTransform> _g = new();

        private void Awake()
        {
            _r.Clear(); _y.Clear(); _b.Clear(); _g.Clear();

            Fill("Home_R", "R_S", _r);
            Fill("Home_Y", "Y_S", _y);
            Fill("Home_B", "B_S", _b);
            Fill("Home_G", "G_S", _g);

            Debug.Log($"[HomeSlots] R:{_r.Count} Y:{_y.Count} B:{_b.Count} G:{_g.Count}");
        }

        private void Fill(string homeRootName, string prefix, List<RectTransform> list)
        {
            var homeRoot = waypointsRoot.Find(homeRootName) as RectTransform;
            if (homeRoot == null)
            {
                Debug.LogError($"[HomeSlots] Missing: {homeRootName} under WaypointsRoot");
                return;
            }

            for (int i = 0; i < 4; i++)
            {
                var t = homeRoot.Find($"{prefix}{i}") as RectTransform;
                if (t == null)
                {
                    Debug.LogError($"[HomeSlots] Missing: {homeRootName}/{prefix}{i}");
                    continue;
                }
                list.Add(t);
            }
        }
    }
}
