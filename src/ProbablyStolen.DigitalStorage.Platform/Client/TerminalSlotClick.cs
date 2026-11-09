using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Adds right-click to a terminal slot; Unity's Button only reports the left button.</summary>
internal sealed class TerminalSlotClick : MonoBehaviour, IPointerClickHandler
{
    private Action? _rightClick;

    internal void Initialize(Action rightClick) => _rightClick = rightClick ?? throw new ArgumentNullException(nameof(rightClick));

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right) _rightClick?.Invoke();
    }

    private void OnDestroy() => _rightClick = null;
}
