using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Samples a dragged item released over a window without moving it. The native drag is cancelled without placing
/// the item, so it stays exactly where it was; only its identity and picture are read. When the cancellation cannot
/// be confirmed, the native drag handler is held back until the drag has ended, so the release can never also drop
/// the item into the window.
/// </summary>
internal sealed class SampleDropHandler : EmptyActionHandler
{
    private CustomUIWindow? _window;
    private TreeNodeRender? _shield;
    private bool _consumedRelease;
    private Func<bool>? _ownsWheel;
    private ItemMouseDragHandler? _wheelDrag;
    private InputActionManager? _manager;
    private ItemMouseDragHandler? _quarantinedDrag;
    private Action<GameItem>? _sample;
    private Func<Vector2, bool>? _isSampleTarget;

    public override List<KeyCode> keyListeners => new List<KeyCode> { ItemMouseDragHandler.current?.key ?? KeyCode.Mouse0 };

    /// <summary>Binds the handler; <paramref name="sample"/> receives the dragged item while it is still held.</summary>
    /// <param name="window">The window that takes samples.</param>
    /// <param name="shield">The window's native input shield.</param>
    /// <param name="sample">Receives the released item.</param>
    /// <param name="isSampleTarget">Checks that the release landed on the rule grid.</param>
    /// <param name="ownsWheel">Whether the window uses the wheel while the pointer is over its grid.</param>
    internal void Initialize(CustomUIWindow window, TreeNodeRender shield, Action<GameItem> sample,
        Func<Vector2, bool> isSampleTarget, Func<bool>? ownsWheel = null)
    {
        _window = window;
        _shield = shield;
        _sample = sample;
        _isSampleTarget = isSampleTarget;
        _ownsWheel = ownsWheel;
    }

    // This handler outranks the native drag (RefreshPriority) and InputActionManager hands every handler the same
    // wheel delta in priority order, so the drag reads the scroll suppression set here. It is restored after the
    // frame, so outside the window the wheel works as usual.
    protected override void OnEventMouseScroll(Vector2 scrollDelta)
    {
        var drag = ItemMouseDragHandler.current;
        if (drag == null || !drag.IsDraggingItem || _wheelDrag != null || _ownsWheel?.Invoke() != true) return;
        if (NativeTerminalDragAccess.SetScrollIgnore(drag, true) == false) _wheelDrag = drag;
    }

    private void RestoreWheel()
    {
        var drag = _wheelDrag;
        _wheelDrag = null;
        // Leave the native suppression enabled while the player holds its key.
        if (drag != null && !Input.GetKey(drag.scrollIgnoreKey)) NativeTerminalDragAccess.SetScrollIgnore(drag, false);
    }

    private void Start() => EnsureRegistered();

    private void EnsureRegistered()
    {
        var manager = InputActionManager.current;
        if (manager == null) return;
        RefreshPriority();
        if (!ReferenceEquals(_manager, manager))
        {
            _manager?.actionHandlers.Remove(this);
            _manager = manager;
        }

        Check();
        manager.Add(this);
    }

    private void RefreshPriority()
    {
        var nativePriority = ItemMouseDragHandler.current?.priority ?? int.MinValue;
        var tooltipPriority = ItemTooltipHandler.current?.priority ?? int.MinValue;
        var highest = Math.Max(nativePriority, tooltipPriority);
        priority = highest == int.MaxValue ? int.MaxValue : highest + 1;
    }

    private void LateUpdate()
    {
        RestoreWheel();
        if (_quarantinedDrag != null)
        {
            var drag = _quarantinedDrag;
            var ended = !drag.IsDraggingItem;
            if (!ended)
            {
                try { ended = NativeTerminalDragAccess.CancelWithoutPlacement(drag); }
                catch { /* Retried next frame; the drag handler stays held back meanwhile. */ }
            }

            if (ended && InputActionManager.current != null)
            {
                InputActionManager.current.Add(drag);
                _quarantinedDrag = null;
            }

            return;
        }

        EnsureRegistered();
        RefreshPriority();
    }

    protected override void OnEventRelease(ISet<KeyCode> keysReleased)
    {
        var drag = ItemMouseDragHandler.current;
        if (drag == null) return;
        if (!keysReleased.Contains(drag.key) || !drag.IsDraggingItem || !IsPointerOverWindow()) return;
        _consumedRelease = true;
        var item = drag.GetCurrentItem() as GameItem;
        try
        {
            if (item != null && _isSampleTarget?.Invoke(Input.mousePosition) == true) _sample?.Invoke(item);
        }
        catch
        {
            // A failed sample read leaves the draft unchanged; the item is still released below without placing it.
        }

        var cancelled = false;
        try { cancelled = NativeTerminalDragAccess.CancelWithoutPlacement(drag); }
        catch { /* Treated as unconfirmed below. */ }
        if (!cancelled) Quarantine(drag);
    }

    protected override bool OnEventUpdate()
    {
        if (_consumedRelease)
        {
            _consumedRelease = false;
            return true;
        }

        return false;
    }

    private void Quarantine(ItemMouseDragHandler drag)
    {
        // Release handlers all run before InputActionManager checks update consumption.
        InputActionManager.current?.actionHandlers.Remove(drag);
        _quarantinedDrag = drag;
    }

    private bool IsPointerOverWindow() => _window != null && _shield != null && IsPointerOverWindow(_window, _shield);

    /// <summary>
    /// Gets whether the topmost hit under the pointer, past a dragged item's own ghost, belongs to the window or its
    /// shield. Another window lying over it, such as a focused native window, wins the hit.
    /// </summary>
    internal static bool IsPointerOverWindow(CustomUIWindow window, TreeNodeRender shield)
    {
        var eventSystem = EventSystem.current;
        if (window == null || shield == null || !window.IsVisible || eventSystem == null) return false;
        var dragItem = ItemMouseDragHandler.current?.GetCurrentItem() as GameItemElement;
        var hits = new List<RaycastResult>();
        eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = Input.mousePosition }, hits);
        foreach (var hit in hits)
        {
            if (dragItem != null ? hit.gameObject.transform.IsChildOf(dragItem.fakeMiddle.rectTransform)
                    : hit.gameObject.GetComponentInParent<GameItemElement>() != null) continue;
            var transform = hit.gameObject.transform;
            return ReferenceEquals(hit.gameObject, shield.gameObject) ||
                transform.IsChildOf(shield.transform) || transform.IsChildOf(window.root.transform);
        }

        return false;
    }

    private void OnDisable()
    {
        RestoreWheel();
        _manager?.actionHandlers.Remove(this);
    }

    private void OnDestroy()
    {
        OnDisable();
        if (_quarantinedDrag != null)
        {
            var drag = _quarantinedDrag;
            try
            {
                if (!drag.IsDraggingItem || NativeTerminalDragAccess.CancelWithoutPlacement(drag)) _manager?.Add(drag);
            }
            catch { /* The native handler is lost only if the game itself is shutting down. */ }
        }

        _quarantinedDrag = null;
    }
}
