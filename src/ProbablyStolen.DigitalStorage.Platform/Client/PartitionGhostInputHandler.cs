using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using ProbablyStolen.DigitalStorage.Client.Terminal.Interaction;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Captures a partition ghost before the native release and shields underlying inventory placement.</summary>
internal sealed class PartitionGhostInputHandler : EmptyActionHandler
{
    private CustomUIWindow? _window;
    private TreeNodeRender? _shield;
    private PartitionWindowController? _controller;
    private Func<ItemKey?>? _capture;
    private Action? _refresh;
    private InputActionManager? _manager;
    private ItemMouseDragHandler? _quarantinedDrag;
    private bool _consumedRelease;

    public override List<KeyCode> keyListeners => new List<KeyCode> { ItemMouseDragHandler.current?.key ?? KeyCode.Mouse0 };

    internal void Initialize(CustomUIWindow window, TreeNodeRender shield,
        PartitionWindowController controller, Func<ItemKey?> capture, Action refresh)
    {
        _window = window;
        _shield = shield;
        _controller = controller;
        _capture = capture;
        _refresh = refresh;
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
        if (_quarantinedDrag != null)
        {
            var drag = _quarantinedDrag;
            var ended = !drag.IsDraggingItem;
            if (!ended)
            {
                try { ended = NativeTerminalDragAccess.CancelWithoutPlacement(drag); }
                catch { }
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
        var key = drag.key;
        if (!keysReleased.Contains(key) || !drag.IsDraggingItem) return;
        var sourceValidated = _controller!.ValidateSourceForGesture();
        if (!IsPointerOverWindow(sourceValidated)) return;
        _consumedRelease = true;
        PartitionGhostGestureResult result;
        try
        {
            result = _controller!.HandleGhostGesture(true, true, _capture!,
                () => NativeTerminalDragAccess.CancelWithoutPlacement(drag));
        }
        catch
        {
            var cancelled = false;
            try { cancelled = NativeTerminalDragAccess.CancelWithoutPlacement(drag); }
            catch { }
            if (!cancelled) Quarantine(drag);
            return;
        }
        if (result.Accepted)
        {
            try { _refresh?.Invoke(); }
            catch { }
        }
        if (!result.CancellationConfirmed) Quarantine(drag);
    }

    protected override bool OnEventUpdate()
    {
        if (_consumedRelease)
        {
            _consumedRelease = false;
            return true;
        }
        if (!IsPointerOverWindow(false) || ItemMouseDragHandler.current?.IsDraggingItem == true) return false;
        OverlayHandler.current?.HideTooltip();
        return true;
    }

    private void Quarantine(ItemMouseDragHandler drag)
    {
        // Release handlers all run before InputActionManager checks update consumption.
        InputActionManager.current?.actionHandlers.Remove(drag);
        _quarantinedDrag = drag;
    }

    private bool IsPointerOverWindow(bool sourceValidated)
    {
        var window = _window;
        var shield = _shield;
        var eventSystem = EventSystem.current;
        if (window == null || shield == null || !window.IsVisible || eventSystem == null) return false;

        var dragItem = sourceValidated
            ? ItemMouseDragHandler.current?.GetCurrentItem() as GameItemElement
            : null;
        var activeDrag = ItemMouseDragHandler.current?.IsDraggingItem == true;
        var hits = new List<RaycastResult>();
        eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = Input.mousePosition }, hits);
        foreach (var hit in hits)
        {
            if (activeDrag && (dragItem != null
                    ? hit.gameObject.transform.IsChildOf(dragItem.fakeMiddle.rectTransform)
                    : hit.gameObject.GetComponentInParent<GameItemElement>() != null)) continue;
            var transform = hit.gameObject.transform;
            return ReferenceEquals(hit.gameObject, shield.gameObject) ||
                transform.IsChildOf(shield.transform) || transform.IsChildOf(window.root.transform);
        }
        return false;
    }

    private void OnDisable()
    {
        if (_manager != null) _manager.actionHandlers.Remove(this);
    }

    private void OnDestroy()
    {
        OnDisable();
        if (_quarantinedDrag != null)
        {
            var drag = _quarantinedDrag;
            try
            {
                if (!drag.IsDraggingItem || NativeTerminalDragAccess.CancelWithoutPlacement(drag))
                    _manager?.Add(drag);
            }
            catch { }
        }
        _quarantinedDrag = null;
        _manager?.actionHandlers.Remove(this);
    }
}
