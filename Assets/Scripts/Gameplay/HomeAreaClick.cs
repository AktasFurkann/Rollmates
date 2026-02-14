using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;
using LudoFriends.Presentation;

public class HomeAreaClick : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private int ownerPlayerIndex; // 0=Red, 1=Green, 2=Yellow, 3=Blue

    // GameBootstrapper'dan set edilecek
    private System.Action<int> _onHomeClicked;

    public void Init(int playerIndex, System.Action<int> onHomeClicked)
    {
        ownerPlayerIndex = playerIndex;
        _onHomeClicked = onHomeClicked;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        _onHomeClicked?.Invoke(ownerPlayerIndex);
    }
}