using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Draws and drives an AE2-style scrollbar (<see cref="ScrollbarModel"/>): a fixed-size handle that can be dragged,
/// and a track that pages toward the pointer, repeating after 250 ms and then every 150 ms while held.
/// </summary>
internal sealed class TerminalScrollbar : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private const float FirstRepeatDelay = 0.25f;
    private const float RepeatDelay = 0.15f;
    private static readonly Color32 TrackColor = new(35, 32, 45, 255);
    private static readonly Color32 HandleColor = new(132, 120, 156, 255);
    private static readonly Color32 DisabledHandleColor = new(70, 64, 82, 255);
    private readonly ScrollbarModel _model = new();
    private RectTransform? _track;
    private Image? _handle;
    private float _handleHeight;
    private Action<int>? _changed;
    private bool _dragging;
    private float _dragOffset;
    private int _repeatDirection;
    private float _nextRepeat;
    private Camera? _pressCamera;
    private float _laidOutHeight = -1f;

    /// <summary>Adds a scrollbar as a layout child of <paramref name="parent"/>.</summary>
    internal static TerminalScrollbar Create(RectTransform parent, float width, float height, float handleHeight,
        Action<int> changed)
    {
        var root = new GameObject("terminal_scrollbar", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var layout = root.AddComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = width;
        layout.minHeight = layout.preferredHeight = height;
        layout.flexibleWidth = layout.flexibleHeight = 0f;
        var track = root.AddComponent<Image>();
        track.color = TrackColor;
        track.raycastTarget = true;

        var handle = new GameObject("terminal_scrollbar_handle", typeof(RectTransform)).AddComponent<Image>();
        handle.transform.SetParent(root.transform, false);
        handle.raycastTarget = false;
        var rect = handle.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, handleHeight);

        var scrollbar = root.AddComponent<TerminalScrollbar>();
        scrollbar._track = (RectTransform)root.transform;
        scrollbar._handle = handle;
        scrollbar._handleHeight = handleHeight;
        scrollbar._changed = changed ?? throw new ArgumentNullException(nameof(changed));
        scrollbar.Layout();
        return scrollbar;
    }

    /// <summary>Applies the screen model's range and row without raising <c>changed</c>.</summary>
    internal void SetRange(int maxScroll, int pageSize, int current)
    {
        _model.SetRange(0, maxScroll, pageSize);
        _model.SetCurrent(current);
        if (_model.IsDisabled) StopInput();
        Layout();
    }

    /// <summary>Applies one mouse wheel event; returns whether the scroll row changed.</summary>
    internal bool Wheel(float delta) => Apply(_model.Wheel(delta));

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || _model.IsDisabled ||
            !TryPointerFromTop(eventData.position, eventData.pressEventCamera, out var pointer))
        {
            return;
        }

        _pressCamera = eventData.pressEventCamera;
        var handleTop = HandleOffset();
        if (pointer >= handleTop && pointer <= handleTop + _handleHeight)
        {
            _dragging = true;
            _dragOffset = pointer - handleTop;
            return;
        }

        _repeatDirection = pointer < handleTop ? -1 : 1;
        Apply(_model.ClickTrack(_repeatDirection < 0));
        _nextRepeat = Time.unscaledTime + FirstRepeatDelay;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging || _track == null ||
            !TryPointerFromTop(eventData.position, eventData.pressEventCamera, out var pointer))
        {
            return;
        }

        Apply(_model.DragTo(pointer, _dragOffset, _track.rect.height, _handleHeight));
    }

    public void OnPointerUp(PointerEventData eventData) => StopInput();

    private void Update()
    {
        // Unity sizes the track in a later layout pass than the one that created it.
        if (_track != null && !Mathf.Approximately(_track.rect.height, _laidOutHeight)) Layout();
        if (_repeatDirection == 0 || Time.unscaledTime < _nextRepeat) return;
        if (!Input.GetMouseButton(0))
        {
            StopInput();
            return;
        }

        _nextRepeat = Time.unscaledTime + RepeatDelay;
        // Stop once the handle reaches the held pointer, so holding the track never overshoots it.
        if (!TryPointerFromTop(Input.mousePosition, _pressCamera, out var pointer)) return;
        var handleTop = HandleOffset();
        var stillAway = _repeatDirection < 0 ? pointer < handleTop : pointer > handleTop + _handleHeight;
        if (stillAway) Apply(_model.ClickTrack(_repeatDirection < 0));
    }

    private void OnDisable() => StopInput();

    private bool Apply(bool changed)
    {
        if (!changed) return false;
        Layout();
        _changed?.Invoke(_model.Current);
        return true;
    }

    private void StopInput()
    {
        _dragging = false;
        _repeatDirection = 0;
    }

    private float HandleOffset() => _track == null ? 0f : _model.GetHandleOffset(_track.rect.height, _handleHeight);

    private void Layout()
    {
        if (_handle == null) return;
        _laidOutHeight = _track == null ? -1f : _track.rect.height;
        _handle.rectTransform.anchoredPosition = new Vector2(0f, -HandleOffset());
        _handle.color = _model.IsDisabled ? DisabledHandleColor : HandleColor;
    }

    private bool TryPointerFromTop(Vector2 screen, Camera? camera, out float pointer)
    {
        pointer = 0f;
        if (_track == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_track, screen, camera, out var local))
        {
            return false;
        }

        pointer = _track.rect.yMax - local.y;
        return true;
    }
}
