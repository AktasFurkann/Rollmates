using System.Collections.Generic;
using UnityEngine;

namespace LudoFriends.Presentation
{
    /// <summary>
    /// Pause menu icindeki rakip oyuncu mute listesi.
    /// PauseMenu.Open() -> Populate() cagrilir, her rakip icin bir MutePlayerRow olusturulur.
    /// PauseMenu.Resume() -> Clear() ile satirlar yok edilir.
    /// </summary>
    public class PlayerMuteListController : MonoBehaviour
    {
        [SerializeField] private Transform rowContainer;
        [SerializeField] private MutePlayerRow rowPrefab;
        [SerializeField] private HudView hud;
        [SerializeField] private GameBootstrapper game;

        private readonly List<MutePlayerRow> _rows = new();

        public void Populate()
        {
            Clear();

            if (hud == null || game == null || rowPrefab == null || rowContainer == null)
                return;

            var opponents = game.GetOpponentInfos();
            int localIdx = game.LocalPlayerIndexPublic;

            foreach (var (playerIndex, displayName) in opponents)
            {
                var cornerPanel = hud.GetCornerPanelForPlayer(playerIndex, localIdx);
                if (cornerPanel == null) continue;

                var row = Instantiate(rowPrefab, rowContainer);
                row.Bind(displayName, cornerPanel);
                _rows.Add(row);
            }
        }

        public void Clear()
        {
            foreach (var row in _rows)
            {
                if (row != null) Destroy(row.gameObject);
            }
            _rows.Clear();
        }
    }
}
