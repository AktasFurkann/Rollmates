using System.Collections.Generic;
using UnityEngine;

namespace LudoFriends.Core
{
    public class BoardWaypoints : MonoBehaviour
    {
        [Header("Assign in Inspector")]
        [SerializeField] private RectTransform waypointsRoot;

        public IReadOnlyList<RectTransform> MainPath => _mainPath;
        private readonly List<RectTransform> _mainPath = new();

        public IReadOnlyList<RectTransform> HomeR => homeR;
public IReadOnlyList<RectTransform> HomeG => homeG;
public IReadOnlyList<RectTransform> HomeY => homeY;
public IReadOnlyList<RectTransform> HomeB => homeB;

[SerializeField] private List<RectTransform> homeR = new List<RectTransform>(6);
[SerializeField] private List<RectTransform> homeG = new List<RectTransform>(6);
[SerializeField] private List<RectTransform> homeY = new List<RectTransform>(6);
[SerializeField] private List<RectTransform> homeB = new List<RectTransform>(6);


        private void Awake()
        {
            BuildMainPath();
        }

        private void BuildMainPath()
        {
            _mainPath.Clear();

            // WP_00..WP_51 isimlerine göre bul
            for (int i = 0; i < 52; i++)
            {
                string name = $"WP_{i:00}";
                var t = waypointsRoot.Find(name) as RectTransform;
                if (t == null)
                {
                    Debug.LogError($"[BoardWaypoints] Missing child: {name} under WaypointsRoot");
                    continue;
                }
                _mainPath.Add(t);
            }

            Debug.Log($"[BoardWaypoints] MainPath count: {_mainPath.Count}");
        }
    }
}
