using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using ProbablyStolen.DigitalStorage.Api.Client.Tooltips;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.StorageTerminal;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Common.Application.Events;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Client.Drag;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Identity;
using ProbablyStolen.DigitalStorage.Platform.Storage;
using GestureButton = ProbablyStolen.DigitalStorage.Client.Interaction.Drag.PointerButton;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.StorageTerminal;

/// <summary>
/// The storage view: search, item grid, scrollbar and status line over display snapshots, without inserting display
/// items into native storage. One instance serves one window; the search text lives in
/// <see cref="StorageTerminalServices.SearchText"/>, so it survives reopening.
/// </summary>
internal sealed class StorageTerminalView : ITerminalView
{
    private const int Columns = TerminalWindowMetrics.Columns;
    private const float PreviewRefreshInterval = 0.2f;
    private const float Gap = 4f;
    private static readonly char[] LineBreaks = { (char)13, (char)10 };

    private readonly StorageTerminalServices _services;
    private readonly ITerminalViewHost _host;
    private readonly StorageScreen _screen;
    private readonly ItemPictureCapture _pictureCapture;
    private readonly TerminalSnapshotCache<ItemPicture> _pictures;
    private readonly ItemSlotGrid _grid;
    private readonly SlotDecorationLayer _decorations;
    private readonly TerminalDecorationRefresh _decorationRefresh;
    private readonly StorageMenu _menu = new();
    private readonly TerminalPotentialTargetHighlightView _potentialTargetHighlight;
    private const int MaxDumpedMembers = 4;
    private readonly EntryAttributeReader _attributes;
    private long _pipelineRevision = -1;
    private DisplayEntry? _hoverHintGroup;
    private long _hoverHintRevision = -1;
    private IReadOnlyList<string>? _hoverSelectionIds;
    private IReadOnlyList<string> _notifiedSelectionIds = Array.Empty<string>();
    private bool _notifiedSelectionKept;
    private IReadOnlyList<PanelText>? _hoverPanels;
    private IReadOnlyList<PanelText>? _hintPanels;
    private ITooltip? _hoverTooltip;
    private IReadOnlyList<PanelText>? _submittedPanels;
    private ItemKey? _submittedPanelSource;
    // Pages are locked to players, so outside the terminal's own operations contents only change when cells,
    // machines, batteries or controller power change, or when the public API moves items (another mod or an
    // integration plugin); those raise NetworkChanged and the next frame rereads.
    private readonly TerminalInventoryMonitor _inventoryMonitor = new();
    private RectTransform? _container;
    private TMP_InputField? _searchInput;
    private TerminalInputHighlighter? _inputHighlighter;
    private TerminalScrollbar? _scrollbar;
    private CustomUIElement? _status;
    private Image? _dropHighlight;
    private TerminalDropInputHandler? _dropInput;
    private TerminalDragController? _dragController;
    private TerminalDragTargetResolver? _dragTargetResolver;
    private readonly TerminalTargetHoverView _targetHover = new();
    private DragGhostView? _dragGhost;
    private TerminalDragBatch? _previewBatch;
    private InventoryDropTarget? _previewTarget;
    private GridDropAnchor? _previewAnchor;
    private bool? _previewAdmission;
    private float _previewCheckedAt;
    private OverlayHandler? _targetTooltipOverlay;
    private RichText? _targetTooltip;
    private Vector2 _targetTooltipOffset;
    private bool _multiGhostPrepared;
    private bool _potentialTargetsPrepared;
    private TerminalSelectionBandView? _selectionBand;
    private TerminalInventoryRefreshHandler? _refreshHandler;
    private IDisposable? _networkSubscription;
    private long _searchRevision;
    // Prepared search documents per raw document. The platform reuses an entry's raw document while its type stays
    // stored, so only new types are prepared; a search configuration change prepares all of them again.
    private ConditionalWeakTable<SearchDocument, SearchDocument> _prepared = new();
    private long _preparedRevision = -1;
    private ItemKey? _hoverKey;
    private GameItemElement? _hoverSample;
    private bool _showingTooltip;
    private string? _messageKey;
    private long _messageAmount;
    private bool _storageUnavailable;
    private bool _suspended = true;

    internal StorageTerminalView(StorageTerminalServices services, ITerminalViewHost host)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _hoverTooltip = TerminalTooltipPresenter.Service?.CreateTooltip(this);
        _screen = new StorageScreen(Columns, TerminalWindowMetrics.Rows, services.Search);
        _pictureCapture = new ItemPictureCapture(new PictureSourceChain<GameItem>(services.FindSample),
            message => services.WriteDiagnostic(message), "terminal");
        _pictures = new TerminalSnapshotCache<ItemPicture>(entry => _pictureCapture.Capture(entry.Key, entry.SpriteKey),
            (entry, error) => _pictureCapture.Failed(entry.Key, entry.SpriteKey, error));
        _grid = new ItemSlotGrid(_pictureCapture, showBadge: true, host.SlotMetrics);
        _decorations = new SlotDecorationLayer(_grid, key => _host.Icons.Overlay(key), services.WriteDiagnostic);
        _potentialTargetHighlight = new TerminalPotentialTargetHighlightView(services.WriteDiagnostic);
        _attributes = new EntryAttributeReader(services.AttributeCaptures, services.FindSample, services.WriteDiagnostic);
        _menu.SetProjection(Project);
        _decorationRefresh = new TerminalDecorationRefresh(services.Decorations, ScheduleDecorationRefresh,
            DrawDecorations, _decorations.Clear);
    }

    private CustomUIWindow Window => _host.Window;

    private TerminalDiagnostics? Diagnostics => _host.Diagnostics;

    /// <inheritdoc />
    public void Build(RectTransform container)
    {
        if (_container != null) throw new InvalidOperationException("The storage view is already built.");
        _container = container ?? throw new ArgumentNullException(nameof(container));
        var rows = _host.Rows;
        _screen.SetRows(rows);
        var text = _services.Text;
        var slots = _host.SlotMetrics;
        var gridWidth = TerminalWindowMetrics.GetGridWidth(slots);
        var gridHeight = TerminalWindowMetrics.GetGridHeight(slots, rows);
        var bodyWidth = gridWidth + Gap + TerminalWindowMetrics.ScrollbarWidth;
        var builder = new CustomUIBuilder(CustomUIManager.Instance, Window, container);
        builder.BeginColumn(spacing: 6f);

        // As wide as the grid, not over the scrollbar, so the search box and the grid share their edges.
        _screen.SetSearchText(_services.SearchText);
        builder.AddInput(text.Get("terminal_storage_search_placeholder"), _services.SearchText,
            onTyping: query =>
            {
                _services.SearchText = query ?? string.Empty;
                CancelDrag();
                _screen.SetSearchText(query);
                Refresh();
            });
        SearchInputs.Attach(builder.Last, text, "terminal_search_tip_title", _services.Search, syntax: true);
        _searchInput = builder.Last.input;
        _inputHighlighter = _searchInput == null ? null : new TerminalInputHighlighter(_searchInput, _services.Search,
            _screen.HasSearchResults);
        TerminalControls.SetSize(builder.Last, gridWidth, TerminalWindowMetrics.GetSearchHeight(slots));
        TerminalControls.GrowText(builder.Last, TerminalWindowMetrics.TextStep(slots));

        builder.BeginRow(spacing: Gap);
        builder.BeginGrid(Columns, slots.SlotPixels, slots.SlotPixels, spacing: TerminalWindowMetrics.CellSpacing);
        // New slot panels hold no overlays yet, whatever the layer drew on the previous ones.
        _decorations.Reset();
        _grid.Build(builder, Columns * rows,
            index =>
            {
                Extract(index, IsShiftHeld() ? TerminalClick.All : TerminalClick.Stack);
            },
            index =>
            {
                if (_dropInput?.SuppressesViewInput == true) return;
                Extract(index, TerminalClick.Half);
            });
        _grid.DisableClickTint();
        builder.End();
        _scrollbar = TerminalScrollbar.Create(builder.CurrentContainer, TerminalWindowMetrics.ScrollbarWidth,
            gridHeight, TerminalWindowMetrics.ScrollbarHandle, row =>
            {
                CancelDrag();
                _screen.SetScroll(row);
                Refresh();
            });
        builder.End();

        builder.AddLabel(string.Empty);
        _status = builder.Last;
        // One line; the full text stays in the tooltip. Part of TerminalWindowMetrics.GetChrome.
        TerminalControls.SetSize(_status, bodyWidth, TerminalWindowMetrics.GetStatusHeight(slots));
        if (_status.text != null)
        {
            _status.text.fontSize = TerminalWindowMetrics.GetStatusFontSize(slots);
            _status.text.overflowMode = TextOverflowModes.Ellipsis;
        }

        builder.End();

        _dropHighlight = _grid.CreateDropHighlight(gridWidth, gridHeight);
        // The drop handler sits on the window root: its raycast treats every hit under that root as the terminal or
        // its frame. It answers only while this view is shown.
        _dropInput = Window.root.AddComponent<TerminalDropInputHandler>();
        _dropInput.Initialize(Window, () => !_suspended && _host.IsCurrentSession(), StoreSelection, Diagnostics,
            ShowDragHint, StoreSingle, _services.StoreRefusals, SetDropHighlight, IsPointerOverSlots);
        _dragGhost = new DragGhostView(Window.root.transform as RectTransform ?? throw new InvalidOperationException(
            "The terminal window root must be a RectTransform."), _pictureCapture, slots);
        _selectionBand = new TerminalSelectionBandView(Window.root.transform as RectTransform ?? throw new InvalidOperationException(
            "The terminal window root must be a RectTransform."));
        _dragTargetResolver = new TerminalDragTargetResolver(Window, () => !_suspended && _host.IsCurrentSession(),
            _dragGhostTransform(), _selectionBandTransform(), Diagnostics);
        _dragController = new TerminalDragController(() => IsDragSessionCurrent(),
            CurrentDragPage, () => _host.Pipeline.Revision,
            (shown, current, ids, revision, amount) => _menu.CreateDragBatch(shown, current, ids, revision, amount),
            ResolveDragTarget, ExtractDragBatch, CompleteDragBatch, HighlightGroups,
            ShowSelectionBand, ShowDragGhost, HideDragGhost, SetDragTargetFeedback, IsDragPointCurrent);
        _dropInput.SetGestureHandler(HandleDragGesture, () => _dragController?.AllowsPrimaryClick == true);
        // On the root as well, so the per-frame session check keeps running while the view is hidden.
        _refreshHandler = Window.root.AddComponent<TerminalInventoryRefreshHandler>();
        _refreshHandler.Initialize(PollSnapshot, ShowDecorations, _decorationRefresh.Hide);
    }

    /// <inheritdoc />
    public void Resume()
    {
        if (_container == null) return;
        CancelDrag();
        _suspended = false;
        _menu.SetSort(_services.Settings.SortOrder, _services.Settings.SortDirection);
        _networkSubscription?.Dispose();
        _networkSubscription = _services.SubscribeNetwork(change =>
        {
            // The settings panel can change the sort while this window is open; a size change applies on reopen.
            if (change.Reason == NetworkChangeReason.ConfigChanged && _host.IsCurrentSession()) ApplySort();
            _inventoryMonitor.MarkDirty();
        });
        _inventoryMonitor.Reset(Time.unscaledTime);
        // A fresh view starts at row 0 either way; a resumed one keeps its row.
        Refresh(ReadSnapshot(), preserveView: true);
        // A page such as the stacking rules may have changed how entries group while this view was hidden.
        Regroup();
        ShowDecorations();
    }

    /// <inheritdoc />
    public void Suspend()
    {
        _suspended = true;
        _decorationRefresh.Hide();
        CancelDrag();
        _hoverTooltip?.Dispose();
        _hoverTooltip = null;
        _submittedPanels = null;
        _submittedPanelSource = null;
        _networkSubscription?.Dispose();
        _networkSubscription = null;
        HideHoverTooltip();
        SetDropHighlight(TerminalDropHighlight.Hidden);
    }

    /// <inheritdoc />
    public void OnReturnFromSubScreen() => Resume();

    /// <inheritdoc />
    public IReadOnlyList<DisplayEntry> Selection
    {
        get
        {
            var controller = _dragController;
            if (_suspended || controller == null || controller.Phase != TerminalDragPhase.Selected)
                return Array.Empty<DisplayEntry>();
            var selected = new HashSet<string>(controller.SelectedIds, StringComparer.Ordinal);
            return _screen.GetPage().Slots.Select(slot => slot.Group)
                .Where(group => group != null && selected.Contains(group.Id)).Select(group => group!).ToArray();
        }
    }

    /// <inheritdoc />
    public void ClearSelection() => CancelDrag();

    /// <inheritdoc />
    public object? StoreState() => new StorageTerminalViewState(_screen.GetPage().SearchText, _screen.ScrollRow);

    /// <inheritdoc />
    public void RestoreState(object? state)
    {
        if (state is not StorageTerminalViewState stored) return;
        CancelDrag();
        _services.SearchText = stored.SearchText;
        _screen.SetSearchText(stored.SearchText);
        _searchInput?.SetTextWithoutNotify(stored.SearchText);
        _screen.SetScroll(stored.ScrollRow);
        Refresh();
    }

    /// <inheritdoc />
    public void SettingChanged(string settingName)
    {
        if (settingName == StorageTerminalFeature.SortByName || settingName == StorageTerminalFeature.SortDirectionName)
            ApplySort();
    }

    /// <inheritdoc />
    public void ShowMessage(string textKey)
    {
        _messageKey = textKey;
        _messageAmount = 0;
        Refresh();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _suspended = true;
        _decorationRefresh.Hide();
        CancelDrag();
        _hoverTooltip?.Dispose();
        _hoverTooltip = null;
        _submittedPanels = null;
        _submittedPanelSource = null;
        HideHoverTooltip();
        _networkSubscription?.Dispose();
        _networkSubscription = null;
        if (_dropInput != null)
        {
            _dropInput.SetGestureHandler(null);
            UnityEngine.Object.Destroy(_dropInput);
        }
        if (_refreshHandler != null)
        {
            _refreshHandler.Detach();
            UnityEngine.Object.Destroy(_refreshHandler);
        }
        _dragGhost?.Dispose();
        _selectionBand?.Dispose();
        _targetHover.Dispose();
        _potentialTargetHighlight.Dispose();
        _dragController = null;
        _dragTargetResolver = null;
        _dragGhost = null;
        _selectionBand = null;
        _dropInput = null;
        _refreshHandler = null;
        _inputHighlighter?.Dispose();
        _inputHighlighter = null;
        _searchInput = null;
        _pictures.Clear();
        _pictureCapture.ResetDiagnostics();
        _dropHighlight = null;
        _status = null;
        _scrollbar = null;
        _inventoryMonitor.Reset();
        _menu.Reset();
        _storageUnavailable = false;
        _grid.Reset();
        _decorations.Reset();
        _messageKey = null;
        _hintPanels = null;
        _hoverPanels = null;
        if (_container != null) CustomUIBuilder.ClearContainer(_container);
        _container = null;
    }

    private void ApplySort()
    {
        CancelDrag();
        _menu.SetSort(_services.Settings.SortOrder, _services.Settings.SortDirection);
        ApplyMenuView(preserveView: true);
    }

    private bool IsPointerOverSlots()
    {
        if (_dropHighlight == null || _suspended) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(_dropHighlight.rectTransform, Input.mousePosition,
            EventCamera());
    }

    private Camera? EventCamera()
    {
        var canvas = Window.root.GetComponentInParent<Canvas>();
        return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    private void SetDropHighlight(TerminalDropHighlight state) => ItemSlotGrid.SetDropHighlight(_dropHighlight, state);

    private bool HandleDragGesture(PointerGesture gesture, float? nativeThreshold)
    {
        var controller = _dragController;
        if (controller == null || !gesture.IsValid) return false;
        switch (gesture.Phase)
        {
            case PointerGesturePhase.Press when gesture.Button == GestureButton.Primary:
                var page = CurrentDragPage();
                var candidates = BuildDragCandidates(page);
                var slotIndex = _grid.IndexAt(new Vector2(gesture.Position.X, gesture.Position.Y), EventCamera(), page.Slots.Count);
                var region = DragRegionAt(gesture.Position, slotIndex, page);
                var slotId = slotIndex >= 0 ? page.Slots[slotIndex].Group?.Id : null;
                return controller.Press(gesture.Position, region, slotId, page, candidates, nativeThreshold,
                    allPieces: gesture.Shift);
            case PointerGesturePhase.Press when gesture.Button == GestureButton.Secondary:
                controller.Halve(gesture.Position);
                return controller.OwnsGesture;
            case PointerGesturePhase.Move:
                controller.Move(gesture.Position);
                return controller.OwnsGesture;
            case PointerGesturePhase.Wheel:
                controller.Wheel(gesture.WheelSteps, gesture.Position);
                return controller.OwnsGesture;
            case PointerGesturePhase.Release when gesture.Button == GestureButton.Primary:
                return controller.Release(gesture.Position);
            case PointerGesturePhase.Cancel:
                controller.Cancel();
                return false;
            default:
                return false;
        }
    }

    private IReadOnlyList<DragSelectionCandidate<string>> BuildDragCandidates(StorageScreenPage page)
    {
        var candidates = new List<DragSelectionCandidate<string>>();
        for (var index = 0; index < page.Slots.Count; index++)
        {
            var group = page.Slots[index].Group;
            if (group == null || group.Total <= 0 ||
                !_grid.TryGetScreenBounds(index, EventCamera(), out var bounds) || !bounds.HasArea) continue;
            candidates.Add(new DragSelectionCandidate<string>(group.Id, bounds, index));
        }
        return candidates;
    }

    private TerminalDragRegion DragRegionAt(PointerPoint point, int slotIndex, StorageScreenPage page)
    {
        var container = _container;
        var screenPoint = new Vector2(point.X, point.Y);
        var camera = EventCamera();
        if (container == null || !RectTransformUtility.RectangleContainsScreenPoint(container, screenPoint, camera) ||
            !NativeWindowLayer.AllowsTerminalGesture(Window, screenPoint) ||
            IsControlArea(_searchInput?.transform as RectTransform, screenPoint, camera) ||
            IsControlArea(_scrollbar?.transform as RectTransform, screenPoint, camera))
            return TerminalDragRegion.Other;
        if (slotIndex >= 0 && slotIndex < page.Slots.Count)
        {
            var group = page.Slots[slotIndex].Group;
            return group != null && group.Total > 0 ? TerminalDragRegion.Slot : TerminalDragRegion.Content;
        }

        return TerminalDragRegion.Content;
    }

    private static bool IsControlArea(RectTransform? rect, Vector2 point, Camera? camera) =>
        rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, point, camera);

    private bool ResolveDragTarget(PointerPoint point, out InventoryDropTarget? target,
        out TerminalDragTargetState state)
    {
        if (_dragTargetResolver != null) return _dragTargetResolver.TryResolveCandidate(point, out target, out state);
        target = null;
        state = TerminalDragTargetState.Rejected;
        return false;
    }

    private TerminalExtractionResult ExtractDragBatch(TerminalDragBatch batch, InventoryDropTarget target)
    {
        var operation = _services.Transfers.Begin("drag", batch.RequestedPieces);
        try
        {
            var result = _services.ExtractBatch(batch, target, operation);
            _services.Transfers.Extracted(operation, result);
            return result;
        }
        catch (Exception error)
        {
            operation.Record(error);
            _services.Transfers.Failed(operation, extraction: true);
            throw;
        }
    }

    private void CompleteDragBatch(TerminalExtractionResult? result, Exception? error)
    {
        if (!IsDragSessionCurrent()) return;
        if (error != null)
        {
            _messageKey = "terminal_storage_no_items";
            _messageAmount = 0;
        }
        else if (result != null)
        {
            _messageKey = result.MessageKey;
            _messageAmount = result.ExtractedAmount;
        }
        Refresh(ReadSnapshot(), preserveView: true);
    }

    private void HighlightGroups(IReadOnlyList<string> selectedIds)
    {
        var selected = new HashSet<string>(selectedIds, StringComparer.Ordinal);
        var slots = _screen.GetPage().Slots;
        for (var index = 0; index < slots.Count; index++)
        {
            var group = slots[index].Group;
            _grid.SetSelected(index, group != null && selected.Contains(group.Id));
        }
    }

    private void ShowSelectionBand(PointerRectangle? bounds)
    {
        if (bounds.HasValue) _selectionBand?.Show(bounds.Value, EventCamera());
        else _selectionBand?.Hide();
    }

    private void ShowDragGhost(PointerPoint point, string? amount)
    {
        var controller = _dragController;
        if (controller == null || _dragGhost == null) return;
        if (controller.Phase == TerminalDragPhase.DraggingSelection)
        {
            if (!_multiGhostPrepared)
            {
                var selected = new HashSet<string>(controller.SelectedIds, StringComparer.Ordinal);
                var groups = _screen.GetPage().Slots.Select(slot => slot.Group)
                    .Where(group => group != null && selected.Contains(group.Id))
                    .ToDictionary(group => group!.Id, group => group!, StringComparer.Ordinal);
                var pictures = new List<ItemPicture?>();
                var offsets = new List<DragGhostOffset>();
                foreach (var candidate in controller.Candidates)
                {
                    if (!groups.TryGetValue(candidate.Id, out var group)) continue;
                    pictures.Add(_pictures.Get(group.Representative.Key));
                    offsets.Add(new DragGhostOffset(candidate.Bounds, controller.Origin));
                }
                _dragGhost.BeginMultiple(pictures, offsets);
                _multiGhostPrepared = true;
            }
            _dragGhost.MoveMultiple(point, EventCamera());
            return;
        }
        var selectedId = controller.SelectedIds.FirstOrDefault();
        if (!_potentialTargetsPrepared)
        {
            var batch = controller.CreatePreviewBatch();
            var source = batch?.Steps.FirstOrDefault().Key;
            if (source != null && _services.FindPreviewSample(source) is { } root) _potentialTargetHighlight.Show(root);
            _potentialTargetsPrepared = true;
        }
        var singleGroup = _screen.GetPage().Slots.Select(slot => slot.Group)
            .FirstOrDefault(candidate => candidate != null && candidate.Id == selectedId);
        _dragGhost.Show(singleGroup == null ? null : _pictures.Get(singleGroup.Representative.Key), point,
            EventCamera(), amount, 0.8f);
    }

    private void HideDragGhost()
    {
        _multiGhostPrepared = false;
        _potentialTargetsPrepared = false;
        _potentialTargetHighlight.Hide();
        _dragGhost?.Hide();
    }

    private void SetDragTargetFeedback(TerminalDragTargetState state, InventoryDropTarget? target)
    {
        if (state == TerminalDragTargetState.None || target == null)
        {
            ClearTargetPreview();
            _potentialTargetHighlight.SetCovered(null, null, _targetHover);
            _targetHover.Hide();
            HideTargetTooltip();
            return;
        }

        var batch = _dragController?.CreatePreviewBatch();
        var useAnchor = _dragController?.PreviewUsesAnchor == true && target.Anchor.HasValue;
        if (state == TerminalDragTargetState.Rejected) ClearTargetPreview();
        else if (batch != null) UpdateTargetPreview(batch, target, useAnchor);
        else ClearTargetPreview();

        var admission = state == TerminalDragTargetState.Rejected ? false : _previewAdmission;
        var color = admission.HasValue
            ? admission.Value ? RenderHandler.BackgroundColor.Green : RenderHandler.BackgroundColor.Red
            : RenderHandler.BackgroundColor.Blue;
        _potentialTargetHighlight.SetCovered(target.HoveredItem,
            target.HoveredItem == null && !useAnchor ? target.Inventory : null, _targetHover);
        _targetHover.Show(target, useAnchor, color);
        ShowTargetTooltip();
    }

    private void UpdateTargetPreview(TerminalDragBatch batch, InventoryDropTarget target, bool useAnchor)
    {
        var anchor = useAnchor ? target.Anchor : null;
        if (ReferenceEquals(_previewBatch, batch) && SameTarget(_previewTarget, target) &&
            Nullable.Equals(_previewAnchor, anchor) && Time.unscaledTime - _previewCheckedAt < PreviewRefreshInterval)
            return;

        _previewTarget = target;
        _previewAnchor = anchor;
        _previewBatch = batch;
        _previewCheckedAt = Time.unscaledTime;
        _previewAdmission = null;
        _targetTooltip = null;

        if (_dragController?.Phase == TerminalDragPhase.DraggingSelection)
        {
            var checkedKeys = new HashSet<ItemKey>();
            foreach (var step in batch.Steps)
            {
                if (!checkedKeys.Add(step.Key)) continue;
                var eligible = ProbeTarget(target, step.Key, false, out _);
                if (eligible == true)
                {
                    _previewAdmission = true;
                    return;
                }
                if (eligible == false) _previewAdmission = false;
            }
            return;
        }

        _previewAdmission = ProbeTarget(target, batch.Steps[0].Key, useAnchor, out var source, single: true);
        if (source == null || target.HoveredItem is not { } item || UISettings.current == null) return;
        try
        {
            var builder = new RichTextBuilder(60);
            if (_previewAdmission is { } admission)
            {
                _targetTooltip = RichTextGenerator.InsertItemInOtherItem(source, item, admission, builder);
            }
            else
            {
                // Auto-placement is decided on release. Show the native action text in a neutral color.
                var sourceName = source.GetDisplayName(preferCustomName: true);
                var targetName = item.GetDisplayName(preferCustomName: true);
                _targetTooltip = builder.Add(LocHelper.GetLocalizedMechanic("mech_drag_insert_into", sourceName,
                    $"<u>{targetName}</u>"), RenderHandler.ColorPalette.LightGray).Build();
            }
            _targetTooltipOffset = new Vector2(32f + source.modifiedShape.globalWidth * UISettings.current.gridSize / 2f, 0f);
        }
        catch (Exception error) when (error is not OutOfMemoryException && error is not StackOverflowException &&
                                      error is not AccessViolationException)
        {
            Diagnostics?.Record("terminal_drop_tooltip_failed", $"error={error.GetType().Name}");
        }
    }

    private bool? ProbeTarget(InventoryDropTarget target, ItemKey key, bool useAnchor, out GameItem? root,
        bool single = false)
    {
        root = null;
        try
        {
            root = _services.FindPreviewSample(key);
            if (root == null) return null;
            return single ? target.PreviewWholeAcceptance(root, useAnchor) : target.MayAccept(root);
        }
        catch (Exception error) when (error is not OutOfMemoryException && error is not StackOverflowException &&
                                      error is not AccessViolationException)
        {
            root = null;
            Diagnostics?.Record("terminal_drop_preview_failed", $"error={error.GetType().Name}");
            return null;
        }
    }

    private void ShowTargetTooltip()
    {
        var overlay = OverlayHandler.current;
        if (_targetTooltip == null || overlay == null)
        {
            HideTargetTooltip();
            return;
        }
        if (_showingTooltip) HideHoverTooltip();
        overlay.ShowTooltip(Input.mousePosition, _targetTooltipOffset, _targetTooltip);
        _targetTooltipOverlay = overlay;
    }

    private static bool SameTarget(InventoryDropTarget? left, InventoryDropTarget right) =>
        left != null && ReferenceEquals(left.Inventory, right.Inventory) &&
        ReferenceEquals(left.HoveredItem, right.HoveredItem);

    private void ClearTargetPreview()
    {
        _previewTarget = null;
        _previewAnchor = null;
        _previewBatch = null;
        _previewAdmission = null;
        _targetTooltip = null;
    }

    private void HideTargetTooltip()
    {
        var overlay = _targetTooltipOverlay;
        _targetTooltipOverlay = null;
        if (overlay != null && ReferenceEquals(overlay, OverlayHandler.current)) overlay.HideTooltip();
    }

    private bool IsDragSessionCurrent() => !_suspended && Application.isFocused && _host.IsCurrentSession() &&
        Window.IsVisible && NativeWindowLayer.IsFocused(Window);

    private bool IsDragPointCurrent(PointerPoint point) => IsDragSessionCurrent() &&
        RectTransformUtility.RectangleContainsScreenPoint(Window.Rect, new Vector2(point.X, point.Y), EventCamera());

    // Only the revision actually projected into the screen may label its groups.
    private StorageScreenPage CurrentDragPage() => _screen.GetPage(_pipelineRevision);

    private void CancelDrag()
    {
        _dragController?.Cancel();
        NotifySelectionChanged();
    }

    /// <summary>Lets the docks re-evaluate actions that read the kept selection once it changes.</summary>
    private void NotifySelectionChanged()
    {
        var controller = _dragController;
        var kept = controller != null && controller.Phase == TerminalDragPhase.Selected;
        var ids = kept ? controller!.SelectedIds : Array.Empty<string>();
        if (kept == _notifiedSelectionKept && ReferenceEquals(ids, _notifiedSelectionIds)) return;
        _notifiedSelectionKept = kept;
        _notifiedSelectionIds = ids;
        _host.RefreshButtons();
    }

    private Transform? _dragGhostTransform() => Window.root.transform.Find("drag_ghost");

    private Transform? _selectionBandTransform() => Window.root.transform.Find("terminal_selection_band");

    private void SyncSlotPicture(int index, TerminalEntry? entry)
    {
        _grid.ShowPicture(index, entry == null ? null : _pictures.Get(entry.Key), entry != null);
    }

    /// <summary>
    /// Shows the drop hint over the terminal, or hides it when <paramref name="count"/> is zero. A null
    /// <paramref name="refusal"/> accepts a single-item preview unless <paramref name="pending"/> is true.
    /// </summary>
    private void ShowDragHint(int count, GameItem? refused, string? refusal, bool pending)
    {
        var overlay = OverlayHandler.current;
        if (overlay == null)
        {
            return;
        }

        if (count == 0)
        {
            overlay.HideTooltip();
            return;
        }

        _grid.HideTooltips();

        overlay.ShowTooltip(Input.mousePosition, new Vector2(32f, 0f),
            TerminalDropHint.Build(_services.Text, count, refused, refusal, pending));
    }

    private void StoreSelection(IReadOnlyList<GameItem> items)
    {
        if (!_host.IsCurrentSession())
        {
            return;
        }

        var operation = _services.Transfers.Begin("store_selection", items.Count);
        var result = Store(operation, () => _menu.HandleInsertion(() => _services.StoreSelection(items, operation)));
        RefreshAfterStore(result, operation, items.Count);
    }

    private void StoreSingle(GameItem item, int amount)
    {
        if (!_host.IsCurrentSession()) return;
        var operation = _services.Transfers.Begin("store_single", 1);
        var result = Store(operation, () => _menu.HandleInsertion(() => _services.StoreSingle(item, amount, operation)));
        RefreshAfterStore(result, operation, 1);
    }

    private void RefreshAfterStore(TerminalInsertionResult result, TransferOperation operation, int selectedCount)
    {
        _messageKey = result.MessageKey;
        _messageAmount = result.StoredAmount;
        var startedAt = Stopwatch.GetTimestamp();
        Refresh(ReadSnapshot(), preserveView: true);
        Diagnostics?.Record("store_refresh",
            $"operation_id={operation.Id} selected={selectedCount} elapsed_ms={TransferOperation.ElapsedMillisecondsSince(startedAt)}");
    }

    private TerminalInsertionResult Store(TransferOperation operation, Func<TerminalInsertionResult> store)
    {
        try
        {
            var result = store();
            _services.Transfers.Stored(operation, result);
            return result;
        }
        catch (Exception error)
        {
            operation.Record(error);
            _services.Transfers.Failed(operation, extraction: false);
            throw;
        }
    }

    private TerminalInventorySnapshot ReadSnapshot()
    {
        try
        {
            return _services.ReadSnapshot();
        }
        catch (Exception error)
        {
            // Identity failures carry their failure code. Other messages come from native code and are not
            // controlled here, so only the first line is kept, capped at 120 characters.
            Diagnostics?.Record("inventory_read_failed",
                $"entries_cleared=true error={error.GetType().Name} detail={DescribeReadFailure(error)}");
            return new TerminalInventorySnapshot(false, Array.Empty<TerminalEntry>());
        }
    }

    private static string DescribeReadFailure(Exception error)
    {
        switch (error)
        {
            case NativeItemIdentityReader.UnsupportedIdentityException unsupported:
                return unsupported.Failure.ToString();
            case NativeItemIdentityReader.StaleIdentityException stale:
                return stale.Failure.ToString();
        }

        var message = error.Message ?? string.Empty;
        var end = message.IndexOfAny(LineBreaks);
        if (end >= 0) message = message.Substring(0, end);
        return message.Length <= 120 ? message : message.Substring(0, 120);
    }

    private void PollSnapshot()
    {
        if (_container == null) return;
        if (_dragController?.Phase != TerminalDragPhase.Idle && !IsDragSessionCurrent()) CancelDrag();
        NotifySelectionChanged();
        if (!_host.IsCurrentSession() || _suspended) return;
        _targetHover.RetryIncompleteHighlight();
        var search = _services.Search;
        if (_searchRevision != search.ConfigurationRevision)
        {
            CancelDrag();
            _searchRevision = search.ConfigurationRevision;
            Refresh();
        }
        if (_pipelineRevision != _host.Pipeline.Revision) Regroup();
        ApplyWheel();
        // Releasing Shift during a drag must not reorder its source slot.
        var viewChanged = _menu.SetPaused(IsShiftHeld() || _dragController?.PausesResort == true);
        var reason = _inventoryMonitor.BeginRefresh(Time.unscaledTime, Window.IsVisible, _services.DiagnosticsEnabled());
        if (reason == TerminalRefreshReason.None)
        {
            if (viewChanged) ApplyMenuView(preserveView: true);
            UpdateHoverTooltip();
            return;
        }

        var snapshot = ReadSnapshot();
        if (!_inventoryMonitor.HasChanged(snapshot))
        {
            // A fresh inventory read can replace preview samples without changing the display snapshot.
            _decorationRefresh.SamplesChanged();
            if (viewChanged) ApplyMenuView(preserveView: true);
            UpdateHoverTooltip();
            return;
        }
        // An audit runs only with no pending event, so a change seen here came from an unsignalled source.
        if (reason == TerminalRefreshReason.Audit)
            Diagnostics?.Record("missed_network_event", $"online={snapshot.Online} entries={snapshot.Entries.Count}");
        Diagnostics?.Record("inventory_refresh",
            $"online={snapshot.Online} unavailable={snapshot.StorageUnavailable} entries={snapshot.Entries.Count}");
        Refresh(snapshot, preserveView: true);
    }

    /// <summary>
    /// Scrolls one page per wheel notch while the pointer is over the window. During a native drag the
    /// wheel belongs to the game (rotation and drag amount), so it is left alone.
    /// </summary>
    private void ApplyWheel()
    {
        if (_dropInput?.SuppressesViewInput == true) return;
        var delta = Input.mouseScrollDelta.y;
        if (delta == 0f) return;
        if (TerminalTooltipPresenter.ConsumeScroll(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt), delta, Time.frameCount)) return;
        if (_scrollbar == null || !Window.IsVisible || _suspended || _host.Shield == null) return;
        if (ItemMouseDragHandler.current?.IsDraggingItem == true ||
            ItemMultiSelectHandler.current?.IsGroupDragging == true)
        {
            return;
        }

        if (RectTransformUtility.RectangleContainsScreenPoint(Window.Rect, Input.mousePosition, EventCamera()) &&
            SampleDropHandler.IsPointerOverWindow(Window, _host.Shield))
        {
            _scrollbar.Wheel(delta);
        }
    }

    private void Refresh(TerminalInventorySnapshot snapshot, bool preserveView = false)
    {
        // Stored items may have moved; the next hover reads a fresh sample.
        _hoverKey = null;
        _hoverSample = null;
        var observedSnapshot = snapshot;
        _inventoryMonitor.ApplySnapshot(observedSnapshot, () =>
        {
            // A refresh can keep the same display group while its captured appraisals change.
            _attributes.Clear();
            _hoverPanels = null;
            var entries = snapshot.Entries.Select(entry => new TerminalEntry(entry.Key, entry.Amount, entry.DisplayName,
                entry.SpriteKey, entry.SpriteWidth, entry.SpriteHeight, entry.Serial,
                Prepared(entry.Document), entry.TotalUnits)).ToArray();
            snapshot = new TerminalInventorySnapshot(snapshot.Online, entries, snapshot.StorageUnavailable);
            _pictures.Accept(snapshot.Entries);
            _menu.BroadcastChanges(snapshot);
            _storageUnavailable = snapshot.StorageUnavailable;
            ApplyMenuView(preserveView, refresh: false);
            Refresh();
            if (_host.IsCurrentSession()) UpdateHoverTooltip();
            else HideHoverTooltip();
        });
    }

    private SearchDocument Prepared(SearchDocument document)
    {
        var search = _services.Search;
        if (_preparedRevision != search.ConfigurationRevision)
        {
            _prepared = new ConditionalWeakTable<SearchDocument, SearchDocument>();
            _preparedRevision = search.ConfigurationRevision;
        }

        return _prepared.GetValue(document, search.PrepareDocument);
    }

    private void ApplyMenuView(bool preserveView, bool refresh = true)
    {
        if (!_menu.GetLinkState().IsConnected) _screen.SetOffline();
        else if (preserveView) _screen.UpdateEntriesInOrder(_menu.GetEntries());
        else _screen.SetEntriesInOrder(_menu.GetEntries());
        if (refresh) Refresh();
    }

    private static bool IsShiftHeld() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

    /// <summary>Takes from a slot: left click one piece, right click half of its pieces, Shift + left click all.</summary>
    private void Extract(int index, TerminalClick click)
    {
        var suppressed = click == TerminalClick.Half
            ? _dropInput?.SuppressesViewInput
            : _dropInput?.SuppressesPrimaryClick;
        if (suppressed == true)
        {
            Diagnostics?.Record("extract_click_suppressed", $"click={click} phase={_dragController?.Phase}");
            return;
        }
        if (!_host.IsCurrentSession()) return;

        var page = _screen.GetPage();
        if (index < 0 || index >= page.Slots.Count) return;
        var group = page.Slots[index].Group;
        if (group == null || group.Total <= 0) return;
        var pieces = TerminalClickAmount.For(click, group.Total);
        RecordIdentities(group);
        var mode = click == TerminalClick.Half ? "half" : click == TerminalClick.All ? "all" : "single";
        var operation = _services.Transfers.Begin(mode, pieces);
        TerminalExtractionResult result;
        try
        {
            result = _menu.HandleGroupInteraction(group, pieces, (entry, count) => _services.Extract(entry, count, operation));
        }
        catch (Exception error)
        {
            operation.Record(error);
            _services.Transfers.Failed(operation, extraction: true);
            throw;
        }
        _services.Transfers.Extracted(operation, result);
        _messageKey = result.MessageKey;
        _messageAmount = result.ExtractedAmount;
        Refresh(ReadSnapshot(), preserveView: true);
    }

    // With the item identity log on, writes the full identity of the clicked slot's members before they move, so the
    // log shows exactly which data keeps entries apart. Values are included; ordinary logs never get this.
    private void RecordIdentities(DisplayEntry group)
    {
        if (!_services.IdentityLogEnabled()) return;
        foreach (var member in group.Members.Take(MaxDumpedMembers))
            _services.WriteDiagnostic(
                $"Digital Storage: item_identity pieces={member.Amount} units={member.TotalUnits}\n{ItemIdentityDump.ToJson(member.Key)}");
    }

    private IReadOnlyList<ProjectedGroup> Project(IReadOnlyList<TerminalEntry> entries)
    {
        var context = _host.Context;
        var projector = _host.Pipeline.ActiveProjector(context);
        var attributes = projector == null ? EntryAttributes.Empty : _attributes.Read(entries, projector.RequiredAttributes);
        return _host.Pipeline.Project(entries, context, attributes);
    }

    /// <summary>Groups and sorts again, for example after the stacking rules or the installed modules changed.</summary>
    private void Regroup()
    {
        if (_container == null) return;
        CancelDrag();
        _pipelineRevision = _host.Pipeline.Revision;
        _attributes.Clear();
        _hoverPanels = null;
        _menu.SetProjection(Project);
        _screen.InvalidatePage();
        ApplyMenuView(preserveView: true);
    }

    /// <summary>
    /// Shows the native tooltip of a stored item of the hovered entry's type. The quantity stays on the slot badge
    /// and is never written to the sample. Only a tooltip this view showed is hidden again.
    /// </summary>
    private void UpdateHoverTooltip()
    {
        // The drag hint shares the overlay tooltip while an item is being dragged over the terminal.
        var dragging = ItemMouseDragHandler.current?.IsDraggingItem == true ||
            ItemMultiSelectHandler.current?.IsGroupDragging == true;
        var slotIndex = -1;
        var entry = Window.IsVisible && !dragging ? HoveredEntry(out slotIndex) : null;
        if (entry == null || entry.Amount <= 0)
        {
            HideHoverTooltip();
            return;
        }

        // Keep the hovered picture live (animation frames, growing crops); other slots update on each refresh.
        SyncSlotPicture(slotIndex, entry);

        if (!entry.Key.Equals(_hoverKey))
        {
            _hoverKey = entry.Key;
            _hoverSample = _services.FindSample(entry.Key) as GameItemElement;
        }

        var overlay = OverlayHandler.current;
        if (_hoverSample == null || _hoverSample.IsDestroyed() || overlay == null)
        {
            HideHoverTooltip();
            return;
        }

        if (!_grid.MirrorLive(slotIndex, _hoverSample)) SyncSlotPicture(slotIndex, entry);

        // Rebuild every frame like ItemTooltipHandler: GetTooltip fires ModHook.OnCreateTooltip*, and mods that add
        // side panels (for example a contents preview) keep them only while the tooltip keeps being created.
        overlay.ShowTooltip(Input.mousePosition, new Vector2(32f, 0f), _services.RenderStoredTooltip(_hoverSample, entry.Key),
            NativeTooltipWidth(_hoverSample));
        _showingTooltip = true;
        ShowPanels(PanelsFor(slotIndex));
    }

    /// <summary>Uses the same maximum width rules as the native ItemTooltipHandler.</summary>
    private static int NativeTooltipWidth(GameItem item) =>
        item.IsTag("FEED_DISPENSER_TAG") ? 90
        : item.IsGameItemType("MACHINE") ? 72
        : item.IsTag("MACHINE_MANUAL_TAG") ? 120
        : 60;

    private TerminalEntry? HoveredEntry(out int slotIndex)
    {
        slotIndex = -1;
        // A hidden grid keeps its geometry, so its slots must not answer hovers while the view is suspended.
        if (!Window.IsVisible || _host.Shield == null || _suspended ||
            !SampleDropHandler.IsPointerOverWindow(Window, _host.Shield))
        {
            return null;
        }

        var page = _screen.GetPage();
        var hovered = _grid.IndexAt(Input.mousePosition, EventCamera(), page.Slots.Count);
        if (hovered >= 0)
        {
            slotIndex = hovered;
            return page.Slots[hovered].Entry;
        }

        return null;
    }

    /// <summary>
    /// Shows the panels of the hovered slot below the native tooltip: the view's own hint panel first, then the panels
    /// features registered, each in the colors its feature chose. The native tooltip stays untouched.
    /// </summary>
    private void ShowPanels(IReadOnlyList<PanelText> panels)
    {
        if (_hoverTooltip == null) _hoverTooltip = TerminalTooltipPresenter.Service?.CreateTooltip(this);
        if (_hoverTooltip == null) return;
        if (!ReferenceEquals(_submittedPanels, panels) || !Equals(_submittedPanelSource, _hoverKey))
        {
            if (_hoverTooltip is NativeTooltipService.TooltipGroup group)
            {
                group.SetSource(_hoverKey?.ToString() ?? string.Empty);
                group.HidePanelsExcept(panels.Select(panel => panel.Id));
            }
            foreach (var panel in panels)
            {
                var target = _hoverTooltip.AddPanel(panel.Id, panel.Order);
                if (target is NativeTooltipService.TooltipPanel native) native.SetExpandToContent(panel.ExpandToContent);
                target.SetRichText(panel.Builder);
                target.SetStyle(panel.Style);
            }
            _submittedPanels = panels;
            _submittedPanelSource = _hoverKey;
        }
        if (_hoverSample != null) _hoverTooltip.Show(Input.mousePosition);
    }

    private IReadOnlyList<PanelText> PanelsFor(int slotIndex)
    {
        var page = _screen.GetPage();
        var group = slotIndex >= 0 && slotIndex < page.Slots.Count ? page.Slots[slotIndex].Group : null;
        if (group == null)
            return _hintPanels ??= new[] { new PanelText(TooltipPanelDefinition.HintId, 0, BuildHint(null), _host.Pipeline.HintPanel.BorderRgb) };

        var selection = SelectedGroupsFor(group, page);
        var selectionIds = selection?.Select(selected => selected.Id).ToArray() ?? Array.Empty<string>();
        if (_hoverPanels != null && ReferenceEquals(_hoverHintGroup, group) &&
            _hoverHintRevision == _host.Pipeline.Revision && _hoverSelectionIds!.SequenceEqual(selectionIds))
            return _hoverPanels;

        _hoverHintGroup = group;
        _hoverHintRevision = _host.Pipeline.Revision;
        _hoverSelectionIds = selectionIds;
        var attributes = EntryAttributes.Empty;
        if ((selection != null || group.IsMixed) && _host.Pipeline.ActiveProjector(_host.Context) is { } projector)
        {
            var members = selection == null ? group.Members : selection.SelectMany(selected => selected.Members);
            attributes = _attributes.Read(members, projector.RequiredAttributes);
        }
        _hoverPanels = BuildPanels(group, selection, attributes);
        return _hoverPanels;
    }

    private IReadOnlyList<DisplayEntry>? SelectedGroupsFor(DisplayEntry hovered, StorageScreenPage page)
    {
        var controller = _dragController;
        if (controller?.Phase != TerminalDragPhase.Selected || controller.SelectedIds.Count < 2 ||
            !controller.SelectedIds.Contains(hovered.Id, StringComparer.Ordinal)) return null;
        var selected = new HashSet<string>(controller.SelectedIds, StringComparer.Ordinal);
        var groups = page.Slots.Select(slot => slot.Group)
            .Where(group => group != null && selected.Contains(group.Id) &&
                            group.Representative.Key.Identifier == hovered.Representative.Key.Identifier)
            .Select(group => group!).ToArray();
        return groups.Length > 1 ? groups : null;
    }

    private IReadOnlyList<PanelText> BuildPanels(DisplayEntry group, IReadOnlyList<DisplayEntry>? selection,
        EntryAttributes attributes)
    {
        var panels = selection == null
            ? _host.Pipeline.Tooltip(group, _host.Context, attributes)
            : _host.Pipeline.TooltipForSelection(group, _host.Context, selection, attributes);
        var result = new List<PanelText>
        {
            new(TooltipPanelDefinition.HintId, 0,
                BuildHint(panels.FirstOrDefault(panel => panel.Definition.Id == TooltipPanelDefinition.HintId)),
                _host.Pipeline.HintPanel.BorderRgb)
        };
        foreach (var panel in panels)
        {
            if (panel.Definition.Id == TooltipPanelDefinition.HintId) continue;
            result.Add(new PanelText(panel.Definition.Id, panel.Definition.Order,
                AddLines(new RichTextBuilder(), panel), panel.Definition.BorderRgb, panel.Definition.ExpandToContent));
        }

        return result;
    }

    /// <summary>The view's own panel: lines extensions added to it, then the usage hints.</summary>
    private RichTextBuilder BuildHint(TooltipPanel? added)
    {
        var builder = new RichTextBuilder(60);
        if (added != null) AddLines(builder, added).AddLine(string.Empty);
        var text = _services.Text;
        var hint = _host.Pipeline.HintPanel;
        return builder
            .AddLine(text.Get("terminal_hint_title"), ignoreEmpty: true, Palette(hint.TitleRgb))
            .AddLine(text.Get("terminal_hint_left"), ignoreEmpty: true, Palette(hint.LabelRgb))
            .AddLine(text.Get("terminal_hint_right"), ignoreEmpty: true, Palette(hint.LabelRgb))
            .AddLine(text.Get("terminal_hint_shift"), ignoreEmpty: true, Palette(hint.LabelRgb))
            .AddLine(text.Get("terminal_hint_drag"), ignoreEmpty: true, Palette(hint.LabelRgb))
            .AddLine(text.Get("terminal_hint_drag_amount"), ignoreEmpty: true, Palette(hint.LabelRgb));
    }

    private RichTextBuilder AddLines(RichTextBuilder builder, TooltipPanel panel)
    {
        foreach (var line in panel.Lines)
            builder.AddLine(Localize(line.TextKey, line.Args), ignoreEmpty: true, Palette(panel.Definition.ColorOf(line.Role)));
        return builder;
    }

    private string Localize(string key, IReadOnlyList<object> args) =>
        _services.Text.Get(key, args.Select(arg => arg is LocalizedText inner ? Localize(inner.TextKey, inner.Args) : arg).ToArray());

    // The game's palette values are 0xRRGGBB colors, and its rich text accepts any such value.
    private static RenderHandler.ColorPalette Palette(int rgb) => (RenderHandler.ColorPalette)rgb;

    private sealed class PanelText
    {
        internal PanelText(string id, int order, RichTextBuilder builder, int? borderRgb, bool expandToContent = false)
        {
            Id = id;
            Order = order;
            Builder = builder;
            ExpandToContent = expandToContent;
            Style = borderRgb.HasValue
                ? new TooltipStyle(TooltipStyleKind.Rgb, borderRgb, null, null)
                : TooltipStyle.Native;
        }

        internal string Id { get; }
        internal int Order { get; }
        internal RichTextBuilder Builder { get; }
        internal TooltipStyle Style { get; }
        internal bool ExpandToContent { get; }
    }

    private void HideHoverTooltip()
    {
        if (_showingTooltip) OverlayHandler.current?.HideTooltip();
        _hoverTooltip?.Hide();

        _showingTooltip = false;
        _hoverKey = null;
        _hoverSample = null;
    }

    private void ShowDecorations()
    {
        if (CanShowDecorations()) _decorationRefresh.Show();
    }

    private bool CanShowDecorations() => !_suspended && _container != null && _refreshHandler != null &&
        _refreshHandler.isActiveAndEnabled && _host.IsCurrentSession();

    private Action? ScheduleDecorationRefresh(Action refresh)
    {
        var handle = ModHelper.StartCoroutine(_services.ModId, NextFrame(refresh));
        return handle == null ? null : () => StopDecorationRefresh(handle);
    }

    private void StopDecorationRefresh(Coroutine handle)
    {
        try { ModHelper.StopCoroutine(_services.ModId, handle); }
        catch (Exception error) { _services.WriteDiagnostic($"terminal_decoration_cancel failed={error.GetType().Name}"); }
    }

    private static IEnumerator NextFrame(Action refresh)
    {
        yield return null;
        refresh();
    }

    private void DrawDecorations(StorageScreenPage page, bool storageAvailable)
    {
        if (!CanShowDecorations()) return;
        var external = _services.Decorations.EvaluatePage(page, storageAvailable, FindOwnedPreviewSample);
        var decorations = _host.Pipeline.DecoratePage(page, _host.Context, external);
        for (var index = 0; index < decorations.Count; index++) _decorations.Set(index, decorations[index]);
    }

    private GameItem? FindOwnedPreviewSample(ItemKey key)
    {
        var sample = _services.FindPreviewSample(key);
        return sample != null && GeneralHelper.IsItemOwned(sample) ? sample : null;
    }

    private void Refresh()
    {
        if (_container == null) return;

        var text = _services.Text;
        var page = _screen.GetPage();
        for (var index = 0; index < page.Slots.Count; index++)
        {
            var slot = page.Slots[index];
            if (slot.Entry == null)
            {
                _grid.SetEmpty(index);
                continue;
            }

            // The native item tooltip replaces the plain name hint (see UpdateHoverTooltip).
            var available = slot.Amount > 0;
            _grid.SetEntry(index, available ? _pictures.Get(slot.Entry.Key) : null, available, available, slot.QuantityBadge);
        }

        _decorationRefresh.SetDisplayedPage(page, !_storageUnavailable);
        _dragController?.SynchronizePage();
        _status?.SetText(_messageKey == null && _storageUnavailable
            ? text.Get("terminal_storage_unavailable")
            : _messageKey == null ? page.State switch
            {
                TerminalDisplayState.Loading => text.Get("terminal_storage_loading"),
                TerminalDisplayState.Offline => text.Get("terminal_storage_offline"),
                TerminalDisplayState.Empty => text.Get("terminal_storage_empty"),
                _ => string.Empty
            }
            : text.Get(_messageKey, _messageAmount));
        if (_status != null)
        {
            TerminalTooltipPresenter.SetText(_status, _status.GetText());
        }

        _messageKey = null;
        _scrollbar?.SetRange(page.MaxScroll, _screen.PageSize, page.ScrollRow);
        _inputHighlighter?.Refresh();
    }
}

/// <summary>What a rebuilt storage view restores: the search text and the first visible row.</summary>
internal sealed class StorageTerminalViewState
{
    internal StorageTerminalViewState(string searchText, int scrollRow)
    {
        SearchText = searchText ?? string.Empty;
        ScrollRow = scrollRow;
    }

    internal string SearchText { get; }

    internal int ScrollRow { get; }
}
