using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal enum TerminalDropHighlight { Hidden, Ready, Hover, Rejected }

/// <summary>Receives native single/group releases before their ordinary inventory placement.</summary>
internal sealed class TerminalDropInputHandler : EmptyActionHandler
{
    private CustomUIWindow? _window;
    private InputActionManager? _manager;
    private Func<bool>? _isCurrentSession;
    private Action<IReadOnlyList<GameItem>>? _storeSelection;
    private Action<GameItem, int>? _storeSingle;
    private Func<GameItem, int, bool>? _canStore;
    private Action<TerminalDropHighlight>? _setHighlight;
    private Func<bool>? _isOverDropArea;
    private TerminalDiagnostics? _diagnostics;
    private Action<int>? _showHint;
    private bool _hintVisible;
    private bool _consumedRelease;
    private bool _previewAccepted;
    private GameItem[] _previewItems = Array.Empty<GameItem>();
    private int[] _previewCounts = Array.Empty<int>();
    private int _previewAmount;
    private bool _previewGroup;
    private int _releaseFrame = -1;
    private float _nextObservation;
    private string _lastObservation = string.Empty;
    private string _hitDetails = "none";

    public override List<KeyCode> keyListeners => new[]
    {
        ItemMultiSelectHandler.current?.key ?? KeyCode.Mouse0,
        ItemMouseDragHandler.current?.key ?? KeyCode.Mouse0
    }.Distinct().ToList();

    internal void Initialize(CustomUIWindow window, Func<bool> isCurrentSession,
        Action<IReadOnlyList<GameItem>> storeSelection, TerminalDiagnostics? diagnostics = null,
        Action<int>? showHint = null, Action<GameItem, int>? storeSingle = null,
        Func<GameItem, int, bool>? canStore = null, Action<TerminalDropHighlight>? setHighlight = null,
        Func<bool>? isOverDropArea = null)
    {
        ResetPreview();
        _window = window;
        _isCurrentSession = isCurrentSession;
        _storeSelection = storeSelection;
        _storeSingle = storeSingle;
        _canStore = canStore;
        _setHighlight = setHighlight;
        _isOverDropArea = isOverDropArea;
        _diagnostics = diagnostics;
        _showHint = showHint;
        _diagnostics?.Record("input_created", "awaiting_registration=true");
    }

    private bool IsCurrent => _window != null && _window.IsVisible && _isCurrentSession?.Invoke() == true;

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
        if (manager.Add(this))
        {
            _diagnostics?.Record("input_registered", $"priority={priority} handlers={manager.actionHandlers.Count}");
        }
    }

    protected override bool OnEventInit()
    {
        var group = ItemMultiSelectHandler.current;
        var single = ItemMouseDragHandler.current;
        if (group == null && single == null) return false;
        var nativePriority = Math.Max(group?.priority ?? int.MinValue, single?.priority ?? int.MinValue);
        var tooltipPriority = ItemTooltipHandler.current?.priority ?? int.MinValue;
        priority = Math.Max(nativePriority, tooltipPriority) == int.MaxValue
            ? int.MaxValue : Math.Max(nativePriority, tooltipPriority) + 1;
        _diagnostics?.Record("input_initialized", $"priority={priority} group_priority={group?.priority} single_priority={single?.priority} tooltip_priority={tooltipPriority}");
        return true;
    }

    private void RefreshPriority()
    {
        var groupPriority = ItemMultiSelectHandler.current?.priority ?? int.MinValue;
        var singlePriority = ItemMouseDragHandler.current?.priority ?? int.MinValue;
        var tooltipPriority = ItemTooltipHandler.current?.priority ?? int.MinValue;
        var max = Math.Max(Math.Max(groupPriority, singlePriority), tooltipPriority);
        priority = max == int.MaxValue ? int.MaxValue : max + 1;
    }

    private static GameItem[] DraggedItems(out bool groupDrag, out int amount)
    {
        var group = ItemMultiSelectHandler.current;
        groupDrag = group?.IsGroupDragging == true;
        amount = 0;
        if (groupDrag)
        {
            return EmporiumEntry.Instance?.GetAllItems().Where(group!.IsGroupDraggingItem).ToArray()
                ?? Array.Empty<GameItem>();
        }

        var single = ItemMouseDragHandler.current;
        if (single?.IsDraggingItem == true && single.GetCurrentItem() is GameItem item)
        {
            amount = NativeTerminalDragAccess.GetAmount(single);
            return new[] { item };
        }

        return Array.Empty<GameItem>();
    }

    protected override void OnEventRelease(ISet<KeyCode> keysReleased)
    {
        var items = DraggedItems(out var groupDrag, out var amount);
        var group = ItemMultiSelectHandler.current;
        var single = ItemMouseDragHandler.current;
        var key = groupDrag ? group!.key : single?.key ?? group?.key ?? KeyCode.Mouse0;
        if (!keysReleased.Contains(key)) return;

        _releaseFrame = Time.frameCount;
        var target = PointerTarget(items);
        _diagnostics?.Record("release",
            $"mode={(groupDrag ? "group" : items.Length > 0 ? "single" : "idle")} selected={items.Length} amount={amount} target={target} current={IsCurrent} hit={_hitDetails}");
        if (!IsCurrent || items.Length == 0 || target != "terminal" && target != "frame") return;

        _consumedRelease = true;
        ClearHint();
        ResetPreview();
        _setHighlight?.Invoke(TerminalDropHighlight.Hidden);
        try
        {
            // Group cancellation restores ghosts; the single bridge also clears stale native placement.
            var cancelled = groupDrag ? group!.Cancel() : single != null && NativeTerminalDragAccess.CancelWithoutPlacement(single);
            _diagnostics?.Record("drop_dispatch", $"mode={(groupDrag ? "group" : "single")} selected={items.Length} amount={amount} native_cancelled={cancelled} target={target}");
            if (!cancelled || target != "terminal") return;
            if (groupDrag) _storeSelection?.Invoke(items);
            else _storeSingle?.Invoke(items[0], amount);
        }
        catch
        {
            // Do not let a failed terminal release fall through into a second native placement.
            _diagnostics?.Record("drop_failed", "native_fallback=false");
        }
    }

    private string PointerTarget(IReadOnlyList<GameItem> dragged)
    {
        _hitDetails = "none";
        if (_window == null || EventSystem.current == null) return "no_event_system";
        var canvas = _window.root.GetComponentInParent<Canvas>();
        var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (!RectTransformUtility.RectangleContainsScreenPoint(_window.Rect, Input.mousePosition, camera)) return "outside";
        var skippedGhosts = 0;
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Input.mousePosition }, hits);
        foreach (var hit in hits)
        {
            if (dragged.OfType<GameItemElement>().Any(item =>
                hit.gameObject.transform.IsChildOf(item.fakeMiddle.rectTransform)))
            {
                skippedGhosts++;
                continue;
            }

            _hitDetails = $"module:{hit.module?.GetType().Name},order:{hit.sortingOrder},layer:{hit.sortingLayer},depth:{hit.depth},ghosts:{skippedGhosts}";
            if (!ReferenceEquals(hit.gameObject.GetComponentInParent<TerminalDropInputHandler>(), this)) return "blocked";
            return (_isOverDropArea?.Invoke() ??
                RectTransformUtility.RectangleContainsScreenPoint(_window.ContentRoot, Input.mousePosition, camera))
                ? "terminal" : "frame";
        }

        return "no_hit";
    }

    private void LateUpdate()
    {
        EnsureRegistered();
        RefreshPriority();
        if (!IsCurrent)
        {
            ResetPreview();
            ClearHint();
            _setHighlight?.Invoke(TerminalDropHighlight.Hidden);
            return;
        }

        var items = DraggedItems(out var groupDrag, out var amount);
        var key = groupDrag ? ItemMultiSelectHandler.current!.key : ItemMouseDragHandler.current?.key ?? KeyCode.Mouse0;
        if (Input.GetKeyUp(key) && _releaseFrame != Time.frameCount)
        {
            _diagnostics?.Record("release_unobserved", $"registered={_manager?.actionHandlers.Contains(this)} priority={priority}");
        }

        if (Time.unscaledTime < _nextObservation)
        {
            ApplyFeedback(items.Length, items.Length > 0 ? PointerTarget(items) : "idle", _previewAccepted);
            return;
        }

        _nextObservation = Time.unscaledTime + 0.15f;
        var target = items.Length > 0 ? PointerTarget(items) : "idle";
        var observation = $"mode={(groupDrag ? "group" : items.Length > 0 ? "single" : "idle")} selected={items.Length} amount={amount} target={target}";
        if (observation != _lastObservation)
        {
            _diagnostics?.Record("hover", $"{observation} hit={_hitDetails}");
            _lastObservation = observation;
        }

        if (items.Length == 0)
        {
            ResetPreview();
            ClearHint();
            _setHighlight?.Invoke(TerminalDropHighlight.Hidden);
            return;
        }

        if (_previewGroup != groupDrag || _previewAmount != amount || items.Length != _previewItems.Length ||
            items.Where((item, index) => !ReferenceEquals(item, _previewItems[index]) || item.unitCount != _previewCounts[index]).Any())
        {
            // Native reconstruction can create render objects. Cache only this gesture's hint, never its commit.
            _previewItems = items;
            _previewCounts = items.Select(item => item.unitCount).ToArray();
            _previewGroup = groupDrag;
            _previewAmount = amount;
            _previewAccepted = false;
            try
            {
                _previewAccepted = items.Any(item => _canStore?.Invoke(item, groupDrag ? item.unitCount : amount) == true);
            }
            catch
            {
                _diagnostics?.Record("preview_failed", "inventory_unchanged=true");
            }
        }

        ApplyFeedback(items.Length, target, _previewAccepted);
    }

    private void ApplyFeedback(int count, string target, bool canStore)
    {
        if (count == 0)
        {
            ResetPreview();
            ClearHint();
            _setHighlight?.Invoke(TerminalDropHighlight.Hidden);
            return;
        }

        _setHighlight?.Invoke(!canStore ? TerminalDropHighlight.Rejected :
            target == "terminal" ? TerminalDropHighlight.Hover : TerminalDropHighlight.Ready);
        if (target == "terminal")
        {
            _showHint?.Invoke(canStore ? count : -count);
            _hintVisible = true;
        }
        else ClearHint();
    }

    private void ResetPreview()
    {
        _previewItems = Array.Empty<GameItem>();
        _previewCounts = Array.Empty<int>();
        _previewAccepted = false;
    }

    private void ClearHint()
    {
        if (_hintVisible)
        {
            _showHint?.Invoke(0);
            _hintVisible = false;
        }
    }

    protected override bool OnEventUpdate()
    {
        if (_consumedRelease)
        {
            _consumedRelease = false;
            return true;
        }

        // Unity controls use EventSystem independently. Suppress idle native clicks/hover behind this window.
        // Active native drags keep their updates so their ghosts, rotation and quantity controls still work.
        var activeDrag = DraggedItems(out _, out _).Length > 0 ||
            ItemMouseDragHandler.current?.GetCurrentItem() != null || ItemMultiSelectHandler.current?.HasSelection == true;
        if (!IsCurrent) return false;
        var pointerTarget = PointerTarget(Array.Empty<GameItem>());
        if (!activeDrag && !_hintVisible && (pointerTarget is "terminal" or "frame"))
        {
            OverlayHandler.current?.HideTooltip();
        }
        return IsCurrent && !activeDrag && (pointerTarget is "terminal" or "frame");
    }

    private void OnDisable()
    {
        ResetPreview();
        ClearHint();
        _setHighlight?.Invoke(TerminalDropHighlight.Hidden);
    }

    private void OnDestroy()
    {
        OnDisable();
        _manager?.actionHandlers.Remove(this);
        _diagnostics?.Record("input_destroyed", "registered=false");
    }
}
