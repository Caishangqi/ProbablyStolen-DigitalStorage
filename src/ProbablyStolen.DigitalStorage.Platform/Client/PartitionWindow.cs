using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Workbench;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Edits the workbench cell's partition. Overview changes save immediately; detail edits save when valid.
/// The rule limit follows module changes while the editor is open.
/// </summary>
internal sealed class PartitionWindow
{
    private const string WindowId = "digital_storage_partition";
    private const int Columns = 8;
    private const int VisibleRows = 6;
    private const int Visible = Columns * VisibleRows;
    private const float CellPixels = TerminalWindowMetrics.CellPixels;
    private const float GridWidth = Columns * CellPixels + (Columns - 1) * TerminalWindowMetrics.CellSpacing;
    // The grid, its scrollbar and gaps, plus 16 units of machinery frame padding on each side.
    private const float WindowWidth = GridWidth + TerminalWindowMetrics.ScrollbarWidth + 64f;
    private static PartitionWindow? _current;
    private static int _escapeFrame = -1;
    private readonly ModText _text;
    private readonly GameItem _cell;
    private readonly Action<string> _log;
    private readonly PartitionSession _session;
    private readonly PartitionDraft _draft;
    private readonly PartitionReadResult _opened;
    private readonly ItemPictureCapture _pictures;
    private readonly Dictionary<ItemKey, ItemPicture> _pictureCache = new();
    private readonly Dictionary<ItemKey, GameItem> _dropped = new();
    private readonly GridGestureArbiter _gestures = new();
    private readonly List<Image> _ghostLayers = new();
    private CustomUIWindow? _window;
    private RectTransform? _body;
    private RectTransform? _menuList;
    private CustomUIElement? _status;
    private ItemSlotGrid? _grid;
    private TreeNodeRender? _shield;
    private TerminalScrollbar? _scrollbar;
    private Image? _dropHighlight;
    private TerminalDropHighlight _dropState = TerminalDropHighlight.Hidden;
    private RectTransform? _ghost;
    private RectTransform? _ghostView;
    private Image? _ghostIcon;
    private RuleDraft? _detail;
    private ItemAttributeSet? _detailSample;
    private int _scrollRow;
    private PixelWindow? _workbenchWindow;
    private int _selected = -1;
    private int _target = -1;
    private int _dragSource = -1;
    private bool _menuOpen;
    private int _menuRow = -1;
    private string _menuSearch = string.Empty;
    private bool _showSample;
    private string? _messageKey;
    private object[] _messageArgs = Array.Empty<object>();

    private PartitionWindow(ModText text, GameItem cell, Action<string> log)
    {
        _text = text;
        _cell = cell;
        _log = log;
        _session = new PartitionSession(cell);
        _opened = new CellPartitionTags(cell).Capture().Interpret();
        _draft = new PartitionDraft(_opened.Config, CellTypeLimitSource.EffectiveOrBase(cell), Columns,
            () => Guid.NewGuid().ToString("N"));
        _pictures = new ItemPictureCapture(new PictureSourceChain<GameItem>(
            key => _dropped.TryGetValue(key, out var item) ? item : null, FindInCell), log, "partition");
        _draft.Changed += Save;
        _workbenchWindow = cell.parentInventory?.GetParentItem()?.contentWindow;
    }

    /// <summary>Opens the editor for a cell, closing any other partition editor first.</summary>
    internal static void Open(ModText text, GameItem cell, Action<string> log)
    {
        if (CustomUIManager.Instance == null) return;
        CloseCurrent();
        _current = new PartitionWindow(text, cell, log);
        _current.Show();
    }

    /// <summary>Closes the editor when it edits this cell, discarding its draft; used when the cell leaves the workbench.</summary>
    internal static void CloseFor(GameItem cell)
    {
        if (_current != null && _current._session.Accepts(cell)) _current.Close();
    }

    /// <summary>Closes any open editor, discarding its draft.</summary>
    internal static void CloseCurrent() => _current?.Close();

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

    /// <summary>
    /// Gets whether the back key belongs to the editor this frame: it is open, or it handled the key this frame and
    /// closed. The native store then must not also close its topmost window (the workbench).
    /// </summary>
    internal static bool OwnsEscape() =>
        _escapeFrame == Time.frameCount ||
        _current is { _window: { IsAlive: true, IsVisible: true } } current && current._session.IsActive;

    private void Show()
    {
        var builder = CustomUIManager.Instance.CreateWindow(WindowId, _text.Get("partition_title"), "overlay", null,
                CustomUIManager.Style.Machinery)
            .SetSize(WindowWidth, 320f).SetDraggable(true).SetCloseOnEscape(false).OnClose(OnClosed);
        builder.AddLabel(string.Empty, "status");
        builder.BeginColumn(4f);
        _body = builder.CurrentContainer;
        builder.End();
        _window = builder.Show();
        _status = _window.Get("status");
        // On the native window layer a dragged item's ghost is drawn above this window, as over the terminal.
        // Raised: the editor opens from the workbench window, a focused native window on the raised layer.
        NativeWindowLayer.Attach(_window, "digital_storage_partition_viewport", raised: true);
        _shield = NativeWindowLayer.AttachInputShield(_window, "partition_input_shield");
        _window.root.AddComponent<SampleDropHandler>().Initialize(_window, _shield, OnSample, IsGridSampleTarget, OwnsWheel);
        _window.root.AddComponent<FrameHandler>().Initialize(Tick);
        CellUpgradeInventory.ModulesChanged += OnModulesChanged;
        CreateGhost(_window.Rect);
        if (_opened.Repaired) Message("partition_repaired", _opened.DroppedRules);
        Render();
    }

    private void Close()
    {
        if (_window != null && _window.IsAlive) _window.Close();
        else OnClosed();
    }

    private void OnClosed()
    {
        _session.End();
        CellUpgradeInventory.ModulesChanged -= OnModulesChanged;
        _workbenchWindow = null;
        _window = null;
        _body = null;
        _grid = null;
        _shield = null;
        _scrollbar = null;
        _dropHighlight = null;
        _ghost = null;
        _ghostView = null;
        _ghostIcon = null;
        _ghostLayers.Clear();
        _status = null;
        _dropped.Clear();
        if (ReferenceEquals(_current, this)) _current = null;
    }

    private void OnEscape()
    {
        if (!_session.IsActive) return;
        if (_menuOpen)
        {
            _menuOpen = false;
            Render();
        }
        else if (_detail != null) Back();
        else Close();
    }

    private void Render()
    {
        if (_window == null || _body == null) return;
        EndRuleDrag();
        CustomUIBuilder.ClearContainer(_body);
        _grid = null;
        _scrollbar = null;
        _dropHighlight = null;
        _dropState = TerminalDropHighlight.Hidden;
        var builder = new CustomUIBuilder(CustomUIManager.Instance, _window, _body);
        if (_detail == null) BuildOverview(builder);
        else BuildDetail(builder);
        RefreshStatus();
        FitHeight();
    }

    /// <summary>
    /// Sizes the window to its content: the frame has no automatic size (CustomUIWindow.SetSize only sets
    /// <c>sizeDelta</c>), so the content's preferred height is measured and the frame's own height (header, rule
    /// and padding, measured from the live window) is added. The width stays the grid's design width; long status
    /// or summary text wraps instead of widening the window.
    /// </summary>
    private void FitHeight()
    {
        var content = _window?.ContentRoot;
        if (_window == null || content == null) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        var frame = _window.Rect.rect.height - content.rect.height;
        var height = Mathf.Ceil(LayoutUtility.GetPreferredHeight(content) + frame);
        if (height > 0f) _window.SetSize(WindowWidth, height);
    }

    private void RefreshStatus()
    {
        if (_status == null) return;
        var parts = new List<string> { _text.Get("partition_status", _draft.Rules.Count, _draft.RuleLimit) };
        if (_messageKey != null) parts.Add(_text.Get(_messageKey, _messageArgs));
        _status.SetText(string.Join("  ·  ", parts));
    }

    private void Message(string key, params object[] args)
    {
        _messageKey = key;
        _messageArgs = args;
        RefreshStatus();
    }

    private int TotalRows => Math.Max(VisibleRows, (_draft.Capacity + Columns - 1) / Columns);

    private int MaxScroll => TotalRows - VisibleRows;

    private void BuildOverview(CustomUIBuilder builder)
    {
        _scrollRow = Math.Max(0, Math.Min(MaxScroll, _scrollRow));
        builder.BeginRow(4f);
        builder.AddButton(_text.Get("partition_mode_button", _text.Get(ModeKey(_draft.Mode))), () =>
        {
            if (!_session.IsActive) return;
            _draft.ToggleMode();
            Render();
        }, "mode");
        builder.End();
        if (_draft.Rules.Count == 0) builder.AddLabel(_text.Get("partition_empty_list_note"));

        builder.BeginRow(4f);
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
                _scrollRow = row;
                FillGrid();
            });
        builder.End();
        _dropHighlight = _grid.CreateDropHighlight(GridWidth, TerminalWindowMetrics.GetGridHeight(VisibleRows));

        builder.BeginRow(4f);
        builder.AddButton(_text.Get("partition_open"), () => { if (_draft.At(_selected) != null) OpenDetail(_selected); });
        builder.AddButton(_text.Get("partition_duplicate"), () => Edit(() => AddResult(_draft.Duplicate(_selected)), needsSelection: true));
        builder.AddButton(_text.Get("partition_remove"), () => Edit(() => { _draft.Remove(_selected); _selected = -1; }, needsSelection: true));
        builder.AddButton(_text.Get("partition_clear"), () => Edit(() => { _draft.Clear(); _selected = -1; }));
        builder.AddButton(_text.Get("partition_undo"), () => Edit(() => _draft.Undo()));
        builder.End();
        FillGrid();
    }

    private static string ModeKey(PartitionMode mode) =>
        mode == PartitionMode.Blacklist ? "partition_mode_blacklist" : "partition_mode_whitelist";

    private void FillGrid()
    {
        if (_grid == null) return;
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
                var tip = rule.Name ?? Summary(rule);
                foreach (var warning in warnings[rule.RuleId])
                    tip += "\n" + _text.Get("partition_warning_" + warning.Kind.ToString().ToLowerInvariant());
                _grid.SetTooltip(index, tip);
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
    /// Reads the pointer and keys every frame, as the native input handlers do: Esc, the wheel, rule dragging and the
    /// highlights of a native drag. Closing and the rule limit follow events instead.
    /// </summary>
    private void Tick()
    {
        // The game's back key (rebindable), as CustomUIManager and the store read it; a focused text field keeps it.
        // StoreUIManagerEscapePatch stops the store from also closing the workbench (OwnsEscape).
        if (KeyBindHelper.GetBackDown() && !StoreUIManager.IsTextFieldFocused())
        {
            _escapeFrame = Time.frameCount;
            OnEscape();
        }

        if (_window == null || !_session.IsActive || _detail != null || _grid == null) return;
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
            EndRuleDrag();
            TrackSampleDrag(slot);
            return;
        }

        SetDrop(TerminalDropHighlight.Hidden);
        TrackRuleDrag(slot, mouse, camera);
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
    private bool IsTopmost() => _window != null && _shield != null && SampleDropHandler.IsPointerOverWindow(_window, _shield);

    private void TrackSampleDrag(int slot)
    {
        // The whole grid shows as a drop target, and the slot shows where the sample would land.
        var landing = slot < 0 ? -1 : _draft.LandingSlot(slot);
        SetDrop(slot < 0 ? TerminalDropHighlight.Ready
            : landing < 0 ? TerminalDropHighlight.Rejected
            : TerminalDropHighlight.Hover);
        SetTarget(landing);
    }

    private void TrackRuleDrag(int slot, Vector2 mouse, Camera? camera)
    {
        if (Input.GetMouseButtonDown(0) && slot >= 0)
        {
            _gestures.Press(slot);
            _dragSource = _draft.At(slot) != null ? slot : -1;
        }

        if (_gestures.PressSlot < 0) return;
        if (Input.GetMouseButton(0))
        {
            if (slot >= 0) _gestures.Move(slot);
            if (_gestures.IsDragging && _dragSource >= 0)
            {
                ShowGhost(mouse, camera);
                SetTarget(slot >= 0 && slot < _draft.Capacity && slot != _dragSource ? slot : -1, force: true);
            }

            return;
        }

        // Released: one gesture per release (GridGestureArbiter); a drag never also counts as a click.
        var source = _dragSource;
        var gesture = _gestures.Release(slot, Time.unscaledTime);
        EndRuleDrag();
        switch (gesture)
        {
            case GridGesture.Drop when source >= 0 && slot >= 0:
                // Cleared first, so a message from a failed save of the move stays visible.
                _messageKey = null;
                if (!_draft.Move(source, slot)) break;
                _selected = -1;
                FillGrid();
                RefreshStatus();
                break;
            case GridGesture.DoubleClick when _draft.At(slot) != null:
                OpenDetail(slot);
                break;
            case GridGesture.Click:
                _selected = _draft.At(slot) != null ? slot : -1;
                ApplyTones();
                break;
        }
    }

    private void EndRuleDrag()
    {
        if (_gestures.PressSlot >= 0) _gestures.Cancel();
        _dragSource = -1;
        if (_ghost != null) _ghost.gameObject.SetActive(false);
        SetTarget(-1);
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

    private void CreateGhost(RectTransform parent)
    {
        var root = new GameObject("partition_drag_ghost", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        root.AddComponent<LayoutElement>().ignoreLayout = true;
        var group = root.AddComponent<CanvasGroup>();
        group.alpha = 0.8f;
        group.blocksRaycasts = false;
        group.interactable = false;
        _ghost = (RectTransform)root.transform;
        _ghost.sizeDelta = new Vector2(CellPixels, CellPixels);
        _ghostIcon = new GameObject("partition_drag_icon", typeof(RectTransform)).AddComponent<Image>();
        _ghostIcon.transform.SetParent(root.transform, false);
        _ghostIcon.preserveAspect = true;
        _ghostIcon.raycastTarget = false;
        Stretch(_ghostIcon.rectTransform);
        _ghostView = (RectTransform)new GameObject("partition_drag_view", typeof(RectTransform)).transform;
        _ghostView.SetParent(root.transform, false);
        Stretch(_ghostView);
        root.SetActive(false);
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
        if (_ghost == null || _ghost.parent is not RectTransform parent) return;
        if (!_ghost.gameObject.activeSelf)
        {
            _pictures.Show(_ghostView, _ghostLayers, _ghostIcon, Picture(_draft.At(_dragSource)?.DisplayKey), true);
            _ghost.gameObject.SetActive(true);
            _ghost.SetAsLastSibling();
        }

        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, mouse, camera, out var world))
            _ghost.position = world;
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
        ResetDetail();
        Message(messageKey);
    }

    private void ResetDetail()
    {
        _detail = null;
        _detailSample = null;
        _menuOpen = false;
    }

    /// <summary>Takes the partition stored on the cell, which changed outside this editor.</summary>
    private void Reload(string messageKey)
    {
        _draft.Rebase(new CellPartitionTags(_cell).Capture().Interpret().Config);
        ResetDetail();
        Message(messageKey);
    }

    /// <summary>
    /// Updates the rule limit when <see cref="CellUpgradeInventory.ModulesChanged"/> reports a module change.
    /// </summary>
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
        _menuOpen = false;
        _showSample = false;
        _messageKey = null;
        Render();
    }

    private void Back()
    {
        _detail = null;
        _menuOpen = false;
        Render();
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
    }

    private void BuildDetail(CustomUIBuilder builder)
    {
        var rule = _detail!;
        builder.AddInput(_text.Get("partition_rule_name"), rule.Name ?? string.Empty, value =>
        {
            rule.Name = value;
            SaveDetail();
            // A failed save leaves the detail page; redraw so later edits never reach a discarded copy.
            if (_detail == null) Render();
        }, "rule_name");
        builder.AddLabel(_text.Get("partition_all_conditions"));
        builder.BeginScroll(176f);
        for (var index = 0; index < rule.Rows.Count; index++) BuildRow(builder, rule, index);
        builder.End();
        builder.AddButton(_text.Get("partition_add_condition"), () =>
        {
            _menuRow = -1;
            _menuOpen = true;
            _menuSearch = string.Empty;
            Render();
        });
        if (_menuOpen) BuildMenu(builder);

        var issue = rule.Validate();
        builder.AddLabel(issue == null
            ? _text.Get("partition_summary_label", Summary(rule.Complete()))
            : _text.Get("partition_not_saved", _text.Get("partition_issue_" + issue.Value.ToString().ToLowerInvariant())));
        foreach (var warning in rule.Lint()) builder.AddLabel(_text.Get("partition_warning_" + warning.Kind.ToString().ToLowerInvariant()));

        builder.AddToggle(_text.Get("partition_sample_attributes"), _showSample, value =>
        {
            _showSample = value;
            Render();
        });
        if (_showSample) BuildSample(builder);

        builder.AddButton(_text.Get("partition_back"), Back);
    }

    private void BuildRow(CustomUIBuilder builder, RuleDraft rule, int index)
    {
        var row = rule.Rows[index];
        builder.BeginRow(4f);
        builder.AddButton(FieldName(row.Field), () =>
        {
            _menuRow = index;
            _menuOpen = true;
            _menuSearch = string.Empty;
            Render();
        });
        var operators = row.Field.Operators;
        var labels = operators.Select(op => _text.Get(row.Field.Kind == PartitionFieldKind.Bool
            ? (op == PartitionOperator.Is ? "partition_op_yes" : "partition_op_no")
            : "partition_op_" + op.ToString().ToLowerInvariant())).ToList();
        builder.AddDropdown(labels, Math.Max(0, IndexOf(operators, row.Operator)), choice =>
        {
            rule.SetOperator(index, operators[choice]);
            SaveDetail();
            Render();
        });

        switch (row.Field.Kind)
        {
            case PartitionFieldKind.Enum:
                var values = row.Field.EnumValues!;
                var options = new List<string> { _text.Get("partition_choose") };
                options.AddRange(values.Select(value => EnumText(row.Field, value)));
                var current = row.Target?.Text == null ? 0 : IndexOf(values, row.Target.Text) + 1;
                builder.AddDropdown(options, Math.Max(0, current), choice =>
                {
                    rule.SetTarget(index, choice == 0 ? null : PartitionValue.Of(values[choice - 1]));
                    SaveDetail();
                    Render();
                });
                break;
            case PartitionFieldKind.Number:
                builder.AddInput(_text.Get("partition_value_placeholder"), NumberText(row.Target),
                    value =>
                    {
                        rule.SetTarget(index, ParseNumber(value));
                        SaveDetail();
                        Render();
                    });
                if (row.Operator == PartitionOperator.Range)
                    builder.AddInput(_text.Get("partition_upper_placeholder"), NumberText(row.Upper),
                        value =>
                        {
                            rule.SetUpper(index, ParseNumber(value));
                            SaveDetail();
                            Render();
                        });
                if (row.Field.UnitKey != null) builder.AddLabel(_text.Get(row.Field.UnitKey));
                break;
            case PartitionFieldKind.TypeSet:
                // The game's type list, by localized name; types of the sample or an existing value are added.
                var types = TypeChoices(row.Target?.Text);
                var typeOptions = new List<string> { _text.Get("partition_choose") };
                typeOptions.AddRange(types.Select(TypeName));
                var currentType = row.Target?.Text == null ? 0 : IndexOf(types, row.Target.Text) + 1;
                builder.AddDropdown(typeOptions, Math.Max(0, currentType), choice =>
                {
                    rule.SetTarget(index, choice == 0 ? null : PartitionValue.Of(types[choice - 1]));
                    SaveDetail();
                    Render();
                });
                break;
            case PartitionFieldKind.ItemId:
                builder.AddInput(_text.Get("partition_value_placeholder"), row.Target?.Text ?? string.Empty, value =>
                {
                    rule.SetTarget(index, string.IsNullOrWhiteSpace(value) ? null : PartitionValue.Of(value.Trim()));
                    SaveDetail();
                    Render();
                });
                break;
        }

        builder.AddButton(_text.Get("partition_remove_condition"), () =>
        {
            rule.Remove(index);
            SaveDetail();
            Render();
        });
        builder.End();
    }

    private void BuildMenu(CustomUIBuilder builder)
    {
        builder.AddInput(_text.Get("partition_search"), _menuSearch, null, "menu_search", typed =>
        {
            _menuSearch = typed ?? string.Empty;
            RebuildMenuList();
        });
        builder.BeginScroll(160f);
        _menuList = builder.CurrentContainer;
        builder.End();
        RebuildMenuList();
    }

    private void RebuildMenuList()
    {
        if (_window == null || _menuList == null || _detail == null) return;
        RebuildMenuEntries();
        FitHeight();
    }

    private void RebuildMenuEntries()
    {
        if (_window == null || _menuList == null || _detail == null) return;
        CustomUIBuilder.ClearContainer(_menuList);
        var builder = new CustomUIBuilder(CustomUIManager.Instance, _window, _menuList);
        foreach (var field in MenuFields())
        {
            var name = FieldName(field);
            if (_menuSearch.Length > 0 && name.IndexOf(_menuSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var sample = SampleText(field);
            var id = field.Id;
            builder.AddButton(sample.Length == 0 ? name : name + "    <color=#8a8296>" + sample + "</color>", () =>
            {
                if (_detail == null) return;
                if (_menuRow < 0) _detail.AddCondition(id);
                else _detail.ChangeField(_menuRow, id);
                SaveDetail();
                _menuOpen = false;
                Render();
            });
        }
    }

    private IEnumerable<PartitionField> MenuFields()
    {
        foreach (var field in PartitionFieldRegistry.All) yield return field;
        var summary = _detail == null || _draft.At(_selected)?.DisplayKey is not { } key ? null : key.ReadSummary();
        if (summary == null) yield break;
        foreach (var feature in summary.Features.Where(feature => feature.Exposed && !feature.Disabled))
            yield return PartitionFieldRegistry.Find(PartitionFieldRegistry.FeatureField(feature.Identifier))!;
    }

    private void BuildSample(CustomUIBuilder builder)
    {
        foreach (var field in MenuFields())
        {
            var value = SampleText(field);
            builder.AddLabel(FieldName(field) + ": " + (value.Length == 0 ? _text.Get("partition_unknown_value") : value));
        }
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

    /// <summary>
    /// Calls the editor once per frame: Esc closes a menu or detail page before the window, and the grid reads the
    /// pointer.
    /// </summary>
    private sealed class FrameHandler : MonoBehaviour
    {
        private Action? _tick;

        internal void Initialize(Action tick) => _tick = tick;

        private void Update() => _tick?.Invoke();
    }
}
