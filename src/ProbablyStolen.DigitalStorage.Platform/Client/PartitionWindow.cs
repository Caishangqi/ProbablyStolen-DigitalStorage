using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Partition;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Client.Workbench;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;
using ProbablyStolen.DigitalStorage.Platform.Client.Drag;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Storage;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Edits the workbench cell's partition. Overview changes save immediately; detail edits save when valid.
/// The rule limit follows module changes while the editor is open.
/// </summary>
internal sealed partial class PartitionWindow : TerminalWindowHost
{
    private new const string WindowId = "digital_storage_partition";
    private const int Columns = TerminalWindowMetrics.Columns;
    private const float CellPixels = TerminalWindowMetrics.CellPixels;
    private const float Gap = 3f;
    private static readonly float GridWidth = TerminalWindowMetrics.GridWidth;
    private static readonly float BodyWidth = GridWidth + TerminalWindowMetrics.ScrollbarGap +
        TerminalWindowMetrics.ScrollbarWidth;
    private const float WindowWidth = TerminalWindowMetrics.Width;
    private const float CountWidth = 110f;
    private static PartitionWindow? _current;
    private readonly ModText _text;
    private readonly ITerminalViewSettings _view;
    private readonly TerminalSearchService _search;
    private readonly GameItem _cell;
    private readonly Action<string> _log;
    private readonly PartitionSession _session;
    private readonly PartitionDraft _draft;
    private readonly PartitionReadResult _opened;
    private readonly ItemPictureCapture _pictures;
    private readonly Dictionary<ItemKey, ItemPicture> _pictureCache = new();
    private readonly Dictionary<ItemKey, GameItem> _dropped = new();
    private readonly GridGestureArbiter _gestures = new();
    private readonly PartitionPointerArbiter _pointer = new();
    private readonly PartitionPointerBackCoordinator _pointerBack;
    private readonly PartitionHoverHintCoordinator _hoverHint;
    private readonly PartitionRuleTooltip _ruleTooltip = new();
    private readonly Dictionary<int, RichText> _ruleHints = new();
    private readonly Dictionary<int, RichTextBuilder> _ruleHintBuilders = new();
    private CustomUIWindow? _window;
    private RectTransform? _body;
    private CustomUIElement? _status;
    private CustomUIElement? _count;
    private ItemSlotGrid? _grid;
    private TerminalScrollbar? _scrollbar;
    private Image? _dropHighlight;
    private TerminalDropHighlight _dropState = TerminalDropHighlight.Hidden;
    private DragGhostView? _ghost;
    private RuleDraft? _detail;
    private ItemAttributeSet? _detailSample;
    private int _scrollRow;
    private bool _rendering;
    private int _rows = TerminalWindowMetrics.Rows;
    private PixelWindow? _workbenchWindow;
    private readonly GameItem? _workbench;
    private int _selected = -1;
    private int _target = -1;
    private int _dragSource = -1;
    private int _generation;
    private bool _showSample;
    private string? _messageKey;
    private object[] _messageArgs = Array.Empty<object>();

    private PartitionWindow(TerminalRegistry registry, ModText text, GameItem cell, ITerminalViewSettings view,
        TerminalSearchService search, Action<string> log, ButtonIcons icons) : base(registry, text, view, () => false, () => false, log, null, icons)
    {
        _text = text;
        _view = view;
        _search = search;
        _cell = cell;
        _log = log;
        _session = new PartitionSession(cell);
        _opened = new CellPartitionTags(cell).Capture().Interpret();
        _draft = new PartitionDraft(_opened.Config, CellTypeLimitSource.EffectiveOrBase(cell), Columns,
            () => Guid.NewGuid().ToString("N"));
        _pictures = new ItemPictureCapture(new PictureSourceChain<GameItem>(
            key => _dropped.TryGetValue(key, out var item) ? item : null, FindInCell), log, "partition");
        _pointerBack = new PartitionPointerBackCoordinator(_gestures, _pointer);
        _hoverHint = new PartitionHoverHintCoordinator(ShowRuleHint, HideRuleHint);
        _draft.Changed += Save;
        _workbench = cell.parentInventory?.GetParentItem();
        _workbenchWindow = _workbench?.contentWindow;
    }

    /// <summary>Opens the editor for a cell, closing any other partition editor first.</summary>
    internal static void Open(ModText text, GameItem cell, ITerminalViewSettings view, TerminalSearchService search,
        Action<string> log, ButtonIcons icons)
    {
        if (CustomUIManager.Instance == null) return;
        if (!icons.Ready)
        {
            icons.ShowUnavailable(text);
            return;
        }
        CloseCurrent();
        var registry = PartitionTerminalFeature.CreateRegistry((IConfigManager)view, log,
            owner => new PartitionTerminalView((PartitionWindow)owner),
            context => ((PartitionWindow)context.Window).DraftMode,
            context => ((PartitionWindow)context.Window).ToggleMode(),
            context => ((PartitionWindow)context.Window).ClearPartitions());
        var window = new PartitionWindow(registry, text, cell, view, search, log, icons);
        _current = window;
        window.Open();
    }

    /// <summary>Closes the editor when it edits this cell, discarding its draft; used when the cell leaves the workbench.</summary>
    internal static void CloseFor(GameItem cell)
    {
        if (_current != null && _current._session.Accepts(cell)) _current.Close();
    }

    /// <summary>Closes any open editor, discarding its draft.</summary>
    internal static new void CloseCurrent() => _current?.Close();

    internal static void InvalidateSamples()
    {
        if (_current == null) return;
        // The open RuleDraft may contain invalid or incomplete edits that have not been applied to the cell. Keep those
        // rows and values, while ensuring any future row uses no stale sample data.
        _current._detailSample = null;
        _current._detail?.ClearSample();
        _current.Render();
    }

    /// <summary>
    /// Closes the editor when its workbench window hides (PixelWindow.Hide, the single path of the close button,
    /// WindowsHandler, a drag of the workbench and Destroy; reported by a Harmony postfix).
    /// </summary>
    internal static void OnWindowHidden(PixelWindow window)
    {
        if (_current is { } current && current._session.IsActive && ReferenceEquals(current._workbenchWindow, window))
            current.Close();
    }

    /// <summary>
    /// Closes the editor when its cell is picked up. A native drag keeps the item in its slot until it is dropped
    /// (ItemMouseDragHandler.StartDrag), so the slot's removal callback fires only then; reported by a Harmony postfix.
    /// </summary>
    internal static void OnDragStarted(ItemMouseDragHandler drag)
    {
        if (_current is { } current && current._session.IsActive && drag.IsDraggingItem &&
            ReferenceEquals(drag.GetCurrentItem(), current._cell))
            current.Close();
    }

    private void Close()
    {
        if (_window != null && _window.IsAlive) _window.Close();
        else if (_session.IsActive)
        {
            _session.End();
            if (ReferenceEquals(_current, this)) _current = null;
        }
    }

    private void Render()
    {
        if (_window == null || _rendering) return;
        PartitionViewRefreshCoordinator.Refresh(_detail != null, HasSubScreen,
            RenderOverview, RefreshDetailScreen);
    }

    private void RenderOverview()
    {
        if (_window == null || _rendering) return;
        _rendering = true;
        _generation++;
        _pointer.Cancel();
        try { RenderBody(); }
        finally { _rendering = false; }
    }

    private void RenderBody()
    {
        if (_window == null || _body == null) return;
        // The list belongs to a control that the redraw replaces; so do the picture and the summary box.
        _dropdown?.Close();
        _detailLayers.Clear();
        _summaryBox = null;
        EndRuleDrag();
        CustomUIBuilder.ClearContainer(_body);
        _grid = null;
        _scrollbar = null;
        _dropHighlight = null;
        _dropState = TerminalDropHighlight.Hidden;
        var builder = new CustomUIBuilder(CustomUIManager.Instance, _window, _body);
        BuildOverview(builder);
        RefreshStatus();
    }

    private void FitHeight()
    {
        var content = _window?.ContentRoot;
        if (_window == null || content == null) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        var frame = _window.Rect.rect.height - content.rect.height;
        var height = Mathf.Ceil(LayoutUtility.GetPreferredHeight(content) + frame);
        if (height > 0f) _window.SetSize(TerminalWindowMetrics.Width, height);
    }

    private void RefreshStatus()
    {
        _count?.SetText(_text.Get("partition_status", _draft.Rules.Count, _draft.RuleLimit));
        _status?.SetText(_messageKey != null ? _text.Get(_messageKey, _messageArgs)
            : _detail == null && _draft.Rules.Count == 0 ? _text.Get("partition_empty_list_note")
            : string.Empty);
    }

    private static void SetWidth(CustomUIElement control, float width)
    {
        var layout = control.root.GetComponent<LayoutElement>() ?? control.root.AddComponent<LayoutElement>();
        // Outranks a text's own preferred width (TMP layout priority 1), so long text wraps or is cut instead.
        layout.layoutPriority = 2;
        layout.minWidth = layout.preferredWidth = width;
        layout.flexibleWidth = 0f;
    }

    private void Message(string key, params object[] args)
    {
        _messageKey = key;
        _messageArgs = args;
        RefreshStatus();
    }

    private int VisibleRows => _rows;

    private int Visible => Columns * _rows;

    private int TotalRows => Math.Max(VisibleRows, (_draft.Capacity + Columns - 1) / Columns);

    private int MaxScroll => TotalRows - VisibleRows;

    // Rows follow the independent partition size setting; the dock sits outside the main frame.
    private int CountRows()
    {
        var canvas = RenderHandler.current?.windowRoot?.rectTransform.GetComponentInParent<Canvas>();
        return TerminalWindowMetrics.GetRows(_view.PartitionSize, LetterboxManager.GameAreaHeight,
            canvas == null ? 0f : canvas.scaleFactor);
    }

    private void BuildOverview(CustomUIBuilder builder)
    {
        _rows = CountRows();
        _scrollRow = Math.Max(0, Math.Min(MaxScroll, _scrollRow));
        builder.BeginRow(Gap);

        builder.BeginGrid(Columns, CellPixels, CellPixels, spacing: TerminalWindowMetrics.CellSpacing);
        _grid = new ItemSlotGrid(_pictures, showBadge: false, CellPixels);
        // Presses, drags and releases are read every frame (Tick); a button click alone cannot tell a drag apart.
        _grid.Build(builder, Visible, _ => { });
        // Slot drags must not reach the window's drag handler. Disable the button's press tint because slot tones
        // already show selection and drop targets.
        for (var index = 0; index < Visible; index++)
            if (_grid.Button(index) is { } button) NativeWindowLayer.BlockWindowDrag(button.root);
        _grid.DisableClickTint();
        builder.End();
        _scrollbar = TerminalScrollbar.Create(builder.CurrentContainer, TerminalWindowMetrics.ScrollbarWidth,
            TerminalWindowMetrics.GetGridHeight(VisibleRows), TerminalWindowMetrics.ScrollbarHandle, row =>
            {
                _pointer.Cancel();
                EndRuleDrag();
                _scrollRow = row;
                FillGrid();
            });
        builder.End();
        _dropHighlight = _grid.CreateDropHighlight(GridWidth, TerminalWindowMetrics.GetGridHeight(VisibleRows));
        FillGrid();
    }

    private void FillGrid()
    {
        if (_grid == null) return;
        _hoverHint.Hide();
        _ruleHints.Clear();
        _scrollbar?.SetRange(MaxScroll, VisibleRows, _scrollRow);
        var warnings = PartitionLint.Check(_draft.Rules).ToLookup(warning => warning.RuleId);
        for (var index = 0; index < Visible; index++)
        {
            var slot = SlotOf(index);
            var rule = _draft.At(slot);
            if (rule == null)
            {
                _grid.SetEmpty(index, interactable: slot < _draft.Capacity);
            }
            else
            {
                var picture = Picture(rule.DisplayKey);
                _grid.SetEntry(index, picture, showPicture: picture != null, interactable: true, badgeText: null);
                var tip = new RichTextBuilder(40)
                    .AddLine(rule.Name ?? Summary(rule), ignoreEmpty: true, RenderHandler.ColorPalette.White, bold: true);
                foreach (var warning in warnings[rule.RuleId])
                    tip.AddLine(_text.Get("partition_warning_" + warning.Kind.ToString().ToLowerInvariant()),
                        ignoreEmpty: true, RenderHandler.ColorPalette.Red);
                tip.AddLine(_text.Get("partition_hint_title"), ignoreEmpty: true, RenderHandler.ColorPalette.Gray)
                    .AddLine(" " + _text.Get("partition_hint_left"), ignoreEmpty: true, RenderHandler.ColorPalette.LightBlue);
                if (_draft.CanAdd)
                    tip.AddLine(" " + _text.Get("partition_hint_middle"), ignoreEmpty: true, RenderHandler.ColorPalette.LightBlue);
                tip.AddLine(" " + _text.Get("partition_hint_right"), ignoreEmpty: true, RenderHandler.ColorPalette.LightBlue);
                _ruleHintBuilders[index] = tip;
                _ruleHints[index] = tip.Build();
            }
        }

        ApplyTones();
    }

    private int SlotOf(int index) => _scrollRow * Columns + index;

    private void ApplyTones()
    {
        if (_grid == null) return;
        var dragging = _gestures.IsDragging && _dragSource >= 0;
        for (var index = 0; index < Visible; index++)
        {
            var slot = SlotOf(index);
            _grid.SetTone(index, dragging && slot == _dragSource ? SlotTone.Source
                : slot == _target ? SlotTone.Target
                : slot == _selected ? SlotTone.Selected
                : SlotTone.Normal);
        }
    }

    private void ScrollTo(int slot)
    {
        if (slot < 0) return;
        var row = slot / Columns;
        if (row < _scrollRow) _scrollRow = row;
        else if (row >= _scrollRow + VisibleRows) _scrollRow = row - VisibleRows + 1;
        _scrollRow = Math.Max(0, Math.Min(MaxScroll, _scrollRow));
    }

    /// <summary>
    /// Reads pointer input every frame for the wheel, rule dragging and native drag highlights.
    /// </summary>
    private void Tick()
    {
        if (_window != null) _dropdown?.CloseOnOutsidePress(CameraOf(_window));
        if (_window == null || !_window.IsVisible || !_session.IsActive || _detail != null || _grid == null)
        {
            _hoverHint.Hide();
            _pointer.Cancel();
            return;
        }
        var camera = CameraOf(_window);
        var mouse = (Vector2)Input.mousePosition;
        // Geometry alone is not enough: a focused native window, such as the workbench, can lie over the editor on the
        // same raised layer, and the pointer then belongs to it.
        var overWindow = IsTopmost();
        var nativeDrag = ItemMouseDragHandler.current?.IsDraggingItem == true ||
            ItemMultiSelectHandler.current?.IsGroupDragging == true;

        // The wheel scrolls the grid, also under a held rule so it can reach any row. During a native drag it does so
        // only over the highlighted grid, where SampleDropHandler stops the held item from rotating (OwnsWheel);
        // elsewhere the wheel keeps its native rotation and amount.
        var wheel = Input.mouseScrollDelta.y;
        if (wheel != 0f && _scrollbar != null && (nativeDrag ? OwnsWheel() : overWindow)) _scrollbar.Wheel(wheel);
        var hovered = overWindow ? _grid.IndexAt(mouse, camera, Visible) : -1;
        var slot = hovered < 0 ? -1 : SlotOf(hovered);

        if (nativeDrag)
        {
            _pointer.Cancel();
            EndRuleDrag();
            TrackSampleDrag(slot);
            return;
        }

        if (!overWindow)
        {
            _pointer.Cancel();
            EndRuleDrag();
            return;
        }

        SetDrop(TerminalDropHighlight.Hidden);
        TrackRuleDrag(slot, mouse, camera);
        RefreshRuleHint(overWindow);
    }

    /// <summary>Gets whether a native drag's wheel scrolls the grid: the pointer is over the highlighted grid range.</summary>
    private bool OwnsWheel()
    {
        if (_window == null || !_session.IsActive || _detail != null || _dropHighlight == null || !_window.IsVisible)
            return false;
        return RectTransformUtility.RectangleContainsScreenPoint(_dropHighlight.rectTransform, Input.mousePosition,
            CameraOf(_window)) && IsTopmost();
    }

    /// <summary>Gets whether the topmost thing under the pointer, past a dragged item's ghost, is this window.</summary>
    private bool IsTopmost() => _window != null && SampleDropHandler.IsPointerOverWindow(_window, InputShield);

    private void TrackSampleDrag(int slot)
    {
        var landing = slot < 0 ? -1 : _draft.LandingSlot(slot);
        SetDrop(slot < 0 ? TerminalDropHighlight.Ready
            : landing < 0 ? TerminalDropHighlight.Rejected
            : TerminalDropHighlight.Hover);
        SetTarget(landing);
    }

    private void TrackRuleDrag(int slot, Vector2 mouse, Camera? camera)
    {
        var down = new[] { Input.GetMouseButtonDown(0), Input.GetMouseButtonDown(1), Input.GetMouseButtonDown(2) };
        var downCount = down.Count(value => value);
        if (downCount > 1 || (_pointer.IsPressed &&
            (Input.GetMouseButton(0) && _pointer.Button != PartitionPointerButton.Left ||
             Input.GetMouseButton(1) && _pointer.Button != PartitionPointerButton.Right ||
             Input.GetMouseButton(2) && _pointer.Button != PartitionPointerButton.Middle)))
        {
            _pointer.Cancel();
            EndRuleDrag();
            return;
        }

        if (downCount == 1)
        {
            var buttonIndex = Array.FindIndex(down, value => value);
            var button = buttonIndex switch
            {
                0 => PartitionPointerButton.Left,
                1 => PartitionPointerButton.Right,
                _ => PartitionPointerButton.Middle
            };
            var rule = slot < 0 ? null : _draft.At(slot);
            var accepted = _pointer.Press(button, slot, rule?.RuleId, _generation, IsTopmost(),
                _session.IsActive, false, false);
            if (accepted && button == PartitionPointerButton.Left)
            {
                _gestures.Press(slot);
                _dragSource = slot;
            }
        }

        if (!_pointer.IsPressed) return;
        var pointerButtonIndex = (int)_pointer.Button;
        var held = Input.GetMouseButton(pointerButtonIndex);
        if (_pointer.Button == PartitionPointerButton.Left && held)
        {
            _pointer.Move(slot);
            if (slot >= 0) _gestures.Move(slot);
            if (_pointer.IsDragging && _dragSource >= 0)
            {
                ShowGhost(mouse, camera);
                SetTarget(slot >= 0 && slot < _draft.Capacity && slot != _dragSource ? slot : -1, force: true);
            }
            return;
        }

        if (!Input.GetMouseButtonUp(pointerButtonIndex)) return;
        var source = _pointer.SourceSlot;
        var pointerButton = _pointer.Button;
        var pointerDragging = _pointer.IsDragging;
        var dragIdentity = pointerButton == PartitionPointerButton.Left && pointerDragging;
        var currentSlot = dragIdentity ? source : slot;
        var currentRule = currentSlot < 0 ? null : _draft.At(currentSlot);
        var gesture = _pointer.Release(pointerButton, slot, currentRule?.RuleId, _generation,
            IsTopmost(), _session.IsActive,
            ItemMouseDragHandler.current?.IsDraggingItem == true || ItemMultiSelectHandler.current?.IsGroupDragging == true,
            Input.GetMouseButton(0) && pointerButton != PartitionPointerButton.Left ||
            Input.GetMouseButton(1) && pointerButton != PartitionPointerButton.Right ||
            Input.GetMouseButton(2) && pointerButton != PartitionPointerButton.Middle);
        var legacy = pointerButton == PartitionPointerButton.Left ? _gestures.Release(slot, Time.unscaledTime) : GridGesture.None;
        EndRuleDrag();
        switch (gesture)
        {
            case PartitionPointerGesture.Configure when !dragIdentity && currentRule != null:
                OpenDetail(slot);
                break;
            case PartitionPointerGesture.Delete when currentRule != null:
                _messageKey = null;
                if (_draft.Remove(slot))
                {
                    _selected = -1;
                    Render();
                }
                break;
            case PartitionPointerGesture.Duplicate when currentRule != null:
                _messageKey = null;
                AddResult(_draft.Duplicate(slot));
                Render();
                break;
            case PartitionPointerGesture.Move when legacy == GridGesture.Drop && source >= 0 && slot >= 0 &&
                                                   slot < _draft.Capacity && source != slot:
                _messageKey = null;
                if (_draft.Move(source, slot))
                {
                    _selected = -1;
                    FillGrid();
                    RefreshStatus();
                }
                break;
        }
    }

    private void RefreshRuleHint(bool overWindow)
    {
        var canShow = overWindow && CustomUIManager.Instance != null && _window != null && _window.IsVisible &&
            _session.IsActive && _detail == null && !HasSubScreen && _body != null && _body.gameObject.activeInHierarchy &&
            _grid != null && !_pointer.IsPressed && !_gestures.IsDragging &&
            !Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2) &&
            ItemMouseDragHandler.current?.IsDraggingItem != true && ItemMultiSelectHandler.current?.IsGroupDragging != true;
        var index = canShow ? _grid!.IndexAt(Input.mousePosition, CameraOf(_window!), Visible) : -1;
        var hint = RuleHint(index);
        _hoverHint.Refresh(index, hint?.nakedText, canShow);
        _ruleTooltip.FollowPointer();
    }

    private RichText? RuleHint(int index) => _ruleHints.TryGetValue(index, out var hint) ? hint : null;

    private RichTextBuilder? RuleHintBuilder(int index) => _ruleHintBuilders.TryGetValue(index, out var hint) ? hint : null;

    private void ShowRuleHint(int index)
    {
        var builder = RuleHintBuilder(index);
        if (builder != null) _ruleTooltip.SetContent(builder);
    }

    private void HideRuleHint()
    {
        _ruleTooltip.Hide();
        _grid?.HideTooltips();
    }

    private void EndRuleDrag()
    {
        _pointer.Cancel();
        if (_gestures.PressSlot >= 0) _gestures.Cancel();
        _dragSource = -1;
        _ghost?.Hide();
        SetTarget(-1);
        _hoverHint.Hide();
    }

    private void SetTarget(int slot, bool force = false)
    {
        if (_target == slot && !force) return;
        _target = slot;
        ApplyTones();
    }

    private void SetDrop(TerminalDropHighlight state)
    {
        if (_dropState == state) return;
        _dropState = state;
        ItemSlotGrid.SetDropHighlight(_dropHighlight, state);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(4f, 4f);
        rect.offsetMax = new Vector2(-4f, -4f);
    }

    private void ShowGhost(Vector2 mouse, Camera? camera)
    {
        _ghost?.Show(Picture(_draft.At(_dragSource)?.DisplayKey), new PointerPoint(mouse.x, mouse.y), camera, null, 0.8f);
    }

    private static Camera? CameraOf(CustomUIWindow window)
    {
        var canvas = window.root.GetComponentInParent<Canvas>();
        return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    private void Edit(Action change, bool needsSelection = false)
    {
        if (!_session.IsActive || (needsSelection && _draft.At(_selected) == null)) return;
        _messageKey = null;
        change();
        Render();
    }

    private void AddResult(DraftAddResult result)
    {
        // Nothing is selected after an add, so the next click selects instead of acting on the new rule.
        _selected = -1;
        switch (result.Status)
        {
            case DraftAddStatus.Added:
                ScrollTo(result.Slot);
                break;
            case DraftAddStatus.Duplicate:
                ScrollTo(result.Slot);
                Message("partition_duplicate_found");
                break;
            default:
                Message("partition_full", _draft.RuleLimit);
                break;
        }
    }

    private bool IsGridSampleTarget(Vector2 pointer)
    {
        if (_window == null || _dropHighlight == null || !IsTopmost()) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(_dropHighlight.rectTransform, pointer, CameraOf(_window));
    }

    private void OnSample(GameItem item)
    {
        if (!_session.IsActive || _detail != null || _grid == null || _window == null) return;
        var key = StorageService.Identify(item);
        if (key == null || !key.IsCanonical)
        {
            Message("partition_sample_unsupported");
            return;
        }

        // Capture the picture while the dragged item is still at hand; it is only read, never moved.
        _dropped[key] = item;
        try { _pictureCache[key] = CapturePicture(key); }
        finally { _dropped.Remove(key); }

        var index = _grid.IndexAt(Input.mousePosition, CameraOf(_window), Visible);
        _messageKey = null;
        AddResult(_draft.AddSample(key, index < 0 ? -1 : SlotOf(index)));
        Render();
    }

    /// <summary>
    /// Saves each draft change with revision validation and one atomic state replacement. Failed changes are reverted
    /// in both editor views.
    /// </summary>
    private void Save()
    {
        if (!_session.IsActive) return;
        var store = new CellPartitionTags(_cell);
        var result = PartitionCommit.Apply(store, _draft.ToConfig(), _draft.Baseline.Revision);
        switch (result.Status)
        {
            case PartitionCommitStatus.Applied:
                _draft.MarkSaved(result.Applied!);
                store.NotifyCommitted(_log);
                return;
            case PartitionCommitStatus.Conflict:
                Reload("partition_conflict");
                return;
            case PartitionCommitStatus.Invalid:
                Revert("partition_invalid_draft");
                return;
            default:
                Revert("partition_failed");
                return;
        }
    }

    /// <summary>Shows the stored partition again after a failed save; the previous configuration is still in force.</summary>
    private void Revert(string messageKey)
    {
        _draft.Rebase(_draft.Baseline);
        var wasInDetail = _detail != null && HasSubScreen;
        ResetDetail();
        if (wasInDetail) ReturnToParent();
        Message(messageKey);
    }

    private void ResetDetail()
    {
        _detail = null;
        _detailSample = null;
    }

    /// <summary>Takes the partition stored on the cell, which changed outside this editor.</summary>
    private void Reload(string messageKey)
    {
        _draft.Rebase(new CellPartitionTags(_cell).Capture().Interpret().Config);
        var wasInDetail = _detail != null && HasSubScreen;
        ResetDetail();
        if (wasInDetail) ReturnToParent();
        Message(messageKey);
    }

    private void OnModulesChanged(GameItem cell)
    {
        if (!_session.IsActive || !ReferenceEquals(cell, _cell)) return;
        int limit;
        try { limit = CellTypeLimitSource.EffectiveOrBase(_cell); }
        catch (ArgumentException) { return; }
        if (limit == _draft.RuleLimit || limit < 1) return;
        _draft.SetRuleLimit(limit);
        if (_detail == null) Render();
        else RefreshStatus();
    }

    private void OpenDetail(int slot)
    {
        var rule = _draft.At(slot);
        if (rule == null) return;
        _selected = slot;
        _detailSample = rule.DisplayKey == null ? null : CanonicalAttributeSource.Instance.Read(rule.DisplayKey);
        _detail = new RuleDraft(rule, _detailSample);
        _showSample = false;
        _messageKey = null;
        _ruleScreen = new PartitionRuleScreen(this);
        SwitchToScreen(_ruleScreen);
    }

    private void Back()
    {
        ReturnToParent();
    }

    /// <summary>
    /// Saves the edited rule when it is valid and differs from the stored one; an invalid rule keeps the last valid
    /// version on the cell until it is fixed.
    /// </summary>
    private void SaveDetail()
    {
        if (_detail == null || !_session.IsActive || _detail.Validate() != null) return;
        var edited = _detail.Complete();
        var stored = _draft.Rules.FirstOrDefault(rule => rule.RuleId == edited.RuleId);
        if (stored == null) return;
        if (PartitionRuleCodec.Encode(new[] { edited.With(slot: stored.Slot) }) == PartitionRuleCodec.Encode(new[] { stored })) return;
        _draft.Replace(edited);
        if (_detail != null) RefreshActiveDetail();
    }

    private string FieldName(PartitionField field) => field.Id.StartsWith(PartitionFieldRegistry.FeaturePrefix, StringComparison.Ordinal)
        ? _text.Get(field.NameKey, field.Id.Substring(PartitionFieldRegistry.FeaturePrefix.Length))
        : _text.Get(field.NameKey);

    private string SampleText(PartitionField field)
    {
        var value = _detailSample?.Get(field.Id);
        if (value == null || !value.IsKnown) return string.Empty;
        return field.Kind switch
        {
            PartitionFieldKind.Bool => _text.Get(value.Flag ? "partition_op_yes" : "partition_op_no"),
            PartitionFieldKind.Number => value.Number.ToString(CultureInfo.InvariantCulture),
            PartitionFieldKind.TypeSet => string.Join(", ", value.Texts.Select(TypeName)),
            PartitionFieldKind.Enum => EnumText(field, value.Text ?? string.Empty),
            _ => value.Text ?? string.Empty
        };
    }

    private string EnumText(PartitionField field, string value) => _text.Get("partition_value_" + field.Id + "_" + value);

    /// <summary>
    /// Gets a type's name as the game shows it (TypeHelper.GetTypeDisplayName reads <c>type_&lt;type&gt;</c> from the
    /// Mechanic table); a type without a translation, such as another mod's, shows its identifier.
    /// </summary>
    private static string TypeName(string type)
    {
        try
        {
            return LocHelper.GetLocalizedMechanicOrDefault("type_" + type.ToLowerInvariant(), type);
        }
        catch
        {
            return type;
        }
    }

    /// <summary>
    /// Gets the selectable item types: the game's full type list (TypeHelper.ALL_TYPES_LIST), the sample's types and
    /// the current value, ordered by displayed name. Types are free strings on items with no registry, so a type
    /// outside these sources stays reachable only through a sample that has it.
    /// </summary>
    private List<string> TypeChoices(string? current)
    {
        var types = new HashSet<string>(TypeHelper.ALL_TYPES_LIST, StringComparer.Ordinal);
        var sample = _detailSample?.Get(PartitionFieldRegistry.Type);
        if (sample != null && sample.IsKnown)
            foreach (var type in sample.Texts) types.Add(type);
        if (!string.IsNullOrEmpty(current)) types.Add(current!);
        return types.OrderBy(TypeName, StringComparer.CurrentCultureIgnoreCase).ThenBy(type => type, StringComparer.Ordinal).ToList();
    }

    private string Summary(PartitionRule rule) => RuleSummary.Describe(rule, (key, args) => _text.Get(key, args),
        (field, value) => field.Kind switch
        {
            PartitionFieldKind.Enum => EnumText(field, value.Text ?? string.Empty),
            PartitionFieldKind.TypeSet when value.Text != null => TypeName(value.Text),
            _ => value.ToString()
        });

    private ItemPicture? Picture(ItemKey? key)
    {
        if (key == null) return null;
        if (_pictureCache.TryGetValue(key, out var cached)) return cached;
        var picture = CapturePicture(key);
        _pictureCache[key] = picture;
        return picture;
    }

    private ItemPicture CapturePicture(ItemKey key)
    {
        var summary = key.ReadSummary();
        var sprite = summary?.SpriteAtlasPath != null && summary.SpritePath != null
            ? summary.SpriteAtlasPath + "|" + summary.SpritePath
            : string.Empty;
        try { return _pictures.Capture(key, sprite); }
        catch (Exception error) { return _pictures.Failed(key, sprite, error); }
    }

    private GameItem? FindInCell(ItemKey key)
    {
        foreach (var root in CellPages.Roots(_cell))
        {
            if (Equals(StorageService.Identify(root), key)) return root;
        }

        return null;
    }

    private static string NumberText(PartitionValue? value) =>
        value == null ? string.Empty : value.Number.ToString(CultureInfo.InvariantCulture);

    private static PartitionValue? ParseNumber(string? text) =>
        long.TryParse(text?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
            ? PartitionValue.Of(number)
            : null;

    private static int IndexOf<T>(IReadOnlyList<T> list, T value)
    {
        for (var index = 0; index < list.Count; index++)
            if (EqualityComparer<T>.Default.Equals(list[index], value)) return index;
        return -1;
    }

    private sealed class FrameHandler : MonoBehaviour
    {
        private Action? _tick;

        internal void Initialize(Action tick) => _tick = tick;

        private void Update() => _tick?.Invoke();
    }
}
