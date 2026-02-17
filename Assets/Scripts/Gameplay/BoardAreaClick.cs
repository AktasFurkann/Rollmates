using UnityEngine;
using UnityEngine.EventSystems;

public class BoardAreaClick : MonoBehaviour, IPointerClickHandler
{
    private System.Action<Vector2> _onBoardClicked;

    public void Init(System.Action<Vector2> callback)
    {
        _onBoardClicked = callback;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        _onBoardClicked?.Invoke(eventData.position);
    }
}
