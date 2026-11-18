using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal interface IDropdownOptionHover : IDisposable
{
    void Show(RectTransform popup, int optionIndex);
    bool TryScroll(bool altHeld, float delta, int frameId);
    void Hide();
}

internal sealed class PopupDropdownHover : MonoBehaviour
{
    private readonly List<RaycastResult> _hits = new();
    private RectTransform? _popup;
    private ScrollRect? _scroll;
    private IDropdownOptionHover? _detail;
    private Action? _close;
    private PopupDropdownRowHover? _hovered;
    private int? _refreshAfterFrame;

    internal void Initialize(RectTransform popup, ScrollRect? scroll, IDropdownOptionHover detail, Action close)
    {
        _popup = popup;
        _scroll = scroll;
        _detail = detail;
        _close = close;
        if (_scroll != null) _scroll.onValueChanged.AddListener(OnListMoved);
    }

    internal void Bind(GameObject root, int index) =>
        root.AddComponent<PopupDropdownRowHover>().Initialize(this, index);

    internal void Enter(PopupDropdownRowHover row)
    {
        if (_popup == null || _detail == null || _refreshAfterFrame.HasValue || ReferenceEquals(_hovered, row)) return;
        _hovered = row;
        _detail.Show(_popup, row.Index);
    }

    internal void Exit(PopupDropdownRowHover row)
    {
        if (ReferenceEquals(_hovered, row)) Clear();
    }

    private void Clear()
    {
        _hovered = null;
        _detail?.Hide();
    }

    internal void InvalidateHover()
    {
        Clear();
        _refreshAfterFrame = Time.frameCount;
    }

    internal void Scroll(PopupDropdownRowHover row, PointerEventData eventData)
    {
        var altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        if (ReferenceEquals(_hovered, row) && _detail?.TryScroll(altHeld, eventData.scrollDelta.y, Time.frameCount) == true)
        {
            eventData.Use();
            return;
        }

        // Unity sends scroll to the nearest handler. Forward ordinary wheel input to the list.
        if (_scroll != null) _scroll.OnScroll(eventData);
    }

    internal void Release()
    {
        _close = null;
        _hovered = null;
        _refreshAfterFrame = null;
        if (_scroll != null) _scroll.onValueChanged.RemoveListener(OnListMoved);
        _scroll = null;
        _detail?.Dispose();
        _detail = null;
        _popup = null;
    }

    private void OnListMoved(Vector2 _) => InvalidateHover();

    private void LateUpdate()
    {
        // The active row can change after layout without a matching pointer-enter event.
        if (_refreshAfterFrame is not { } frame || frame == Time.frameCount) return;
        _refreshAfterFrame = null;
        var events = EventSystem.current;
        if (events == null) return;
        events.RaycastAll(new PointerEventData(events) { position = Input.mousePosition }, _hits);
        var row = _hits.Count > 0 ? _hits[0].gameObject.GetComponentInParent<PopupDropdownRowHover>() : null;
        _hits.Clear();
        if (row != null && row.isActiveAndEnabled && row.Owner == this) Enter(row);
    }

    private void OnDisable() => _close?.Invoke();
}

internal sealed class PopupDropdownRowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IScrollHandler
{
    internal PopupDropdownHover? Owner { get; private set; }
    internal int Index { get; private set; }

    internal void Initialize(PopupDropdownHover popup, int index)
    {
        Owner = popup;
        Index = index;
    }

    void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
    {
        if (isActiveAndEnabled) Owner?.Enter(this);
    }

    void IPointerExitHandler.OnPointerExit(PointerEventData eventData) => Owner?.Exit(this);

    void IScrollHandler.OnScroll(PointerEventData eventData) => Owner?.Scroll(this, eventData);

    private void OnDisable() => Owner?.Exit(this);
}
