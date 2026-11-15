using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.StorageTerminal;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Application.Events;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Identity;
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
    private const int CellPixels = TerminalWindowMetrics.CellPixels;
    private const float Gap = 4f;
    private static readonly char[] LineBreaks = { (char)13, (char)10 };
    // One box serves every terminal; only one terminal window shows at a time.
    private static readonly TooltipSideBox HintBox = new();

    // Overlay windows outlive views, so feature panels keep one box each for the whole game session.
    private static readonly Dictionary<string, TooltipSideBox> PanelBoxes = new(StringComparer.Ordinal);

    private readonly StorageTerminalServices _services;
    private readonly ITerminalViewHost _host;
    private readonly StorageScreen _screen;
    private readonly ItemPictureCapture _pictureCapture;
    private readonly TerminalSnapshotCache<ItemPicture> _pictures;
    private readonly ItemSlotGrid _grid;
    private readonly SlotDecorationLayer _decorations;
    private readonly StorageMenu _menu = new();
    private const int MaxDumpedMembers = 4;
    private readonly EntryAttributeReader _attributes;
    private long _pipelineRevision = -1;
    private DisplayEntry? _hoverHintGroup;
    private long _hoverHintRevision = -1;
    private IReadOnlyList<PanelText>? _hoverPanels;
    private IReadOnlyList<PanelText>? _hintPanels;
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
    private TerminalInventoryRefreshHandler? _refreshHandler;
    private IDisposable? _networkSubscription;
    private long _searchRevision;
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
        _screen = new StorageScreen(Columns, TerminalWindowMetrics.Rows, CellPixels, services.Search);
        _pictureCapture = new ItemPictureCapture(new PictureSourceChain<GameItem>(services.FindSample),
            message => services.WriteDiagnostic(message), "terminal");
        _pictures = new TerminalSnapshotCache<ItemPicture>(entry => _pictureCapture.Capture(entry.Key, entry.SpriteKey),
            (entry, error) => _pictureCapture.Failed(entry.Key, entry.SpriteKey, error));
        _grid = new ItemSlotGrid(_pictureCapture, showBadge: true, CellPixels);
        _decorations = new SlotDecorationLayer(_grid, key => _host.Icons.Overlay(key), services.WriteDiagnostic);
        _attributes = new EntryAttributeReader(services.AttributeCaptures, services.FindSample, services.WriteDiagnostic);
        _menu.SetProjection(Project);
        _screen.SetDecorator(group => _host.Pipeline.Decorate(group, _host.Context));
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
        var bodyWidth = TerminalWindowMetrics.GridWidth + Gap + TerminalWindowMetrics.ScrollbarWidth;
        var builder = new CustomUIBuilder(CustomUIManager.Instance, Window, container);
        builder.BeginColumn(spacing: 6f);

        // As wide as the grid, not over the scrollbar, so the search box and the grid share their edges.
        _screen.SetSearchText(_services.SearchText);
        builder.AddInput(text.Get("terminal_storage_search_placeholder"), _services.SearchText,
            onTyping: query =>
            {
                _services.SearchText = query ?? string.Empty;
                _screen.SetSearchText(query);
                Refresh();
            });
        _searchInput = builder.Last.input;
        _inputHighlighter = _searchInput == null ? null : new TerminalInputHighlighter(_searchInput, _services.Search,
            _screen.HasSearchResults);
        TerminalControls.SetSize(builder.Last, TerminalWindowMetrics.GridWidth);

        builder.BeginRow(spacing: Gap);
        builder.BeginGrid(Columns, CellPixels, CellPixels, spacing: TerminalWindowMetrics.CellSpacing);
        // New slot panels hold no overlays yet, whatever the layer drew on the previous ones.
        _decorations.Reset();
        _grid.Build(builder, Columns * rows,
            index => Extract(index, IsShiftHeld() ? TerminalClick.All : TerminalClick.Stack),
            index => Extract(index, TerminalClick.Half));
        builder.End();
        _scrollbar = TerminalScrollbar.Create(builder.CurrentContainer, TerminalWindowMetrics.ScrollbarWidth,
            TerminalWindowMetrics.GetGridHeight(rows), TerminalWindowMetrics.ScrollbarHandle, row =>
            {
                _screen.SetScroll(row);
                Refresh();
            });
        builder.End();

        builder.AddLabel(string.Empty);
        _status = builder.Last;
        // One line; the full text stays in the tooltip. Part of TerminalWindowMetrics.Chrome.
        TerminalControls.SetSize(_status, bodyWidth, 20f);
        if (_status.text != null)
        {
            _status.text.fontSize = 14f;
            _status.text.overflowMode = TextOverflowModes.Ellipsis;
        }

        builder.End();

        _dropHighlight = _grid.CreateDropHighlight(TerminalWindowMetrics.GridWidth, TerminalWindowMetrics.GetGridHeight(rows));
        // The drop handler sits on the window root: its raycast treats every hit under that root as the terminal or
        // its frame. It answers only while this view is shown.
        _dropInput = Window.root.AddComponent<TerminalDropInputHandler>();
        _dropInput.Initialize(Window, () => !_suspended && _host.IsCurrentSession(), StoreSelection, Diagnostics,
            ShowDragHint, StoreSingle, _services.CanStore, SetDropHighlight, IsPointerOverSlots);
        // On the root as well, so the per-frame session check keeps running while the view is hidden.
        _refreshHandler = Window.root.AddComponent<TerminalInventoryRefreshHandler>();
        _refreshHandler.Initialize(PollSnapshot);
    }

    /// <inheritdoc />
    public void Resume()
    {
        if (_container == null) return;
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
    }

    /// <inheritdoc />
    public void Suspend()
    {
        _suspended = true;
        _networkSubscription?.Dispose();
        _networkSubscription = null;
        HideHoverTooltip();
        SetDropHighlight(TerminalDropHighlight.Hidden);
    }

    /// <inheritdoc />
    public void OnReturnFromSubScreen() => Resume();

    /// <inheritdoc />
    public object? StoreState() => new StorageTerminalViewState(_screen.GetPage().SearchText, _screen.ScrollRow);

    /// <inheritdoc />
    public void RestoreState(object? state)
    {
        if (state is not StorageTerminalViewState stored) return;
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
        HideHoverTooltip();
        _networkSubscription?.Dispose();
        _networkSubscription = null;
        if (_dropInput != null) UnityEngine.Object.Destroy(_dropInput);
        if (_refreshHandler != null) UnityEngine.Object.Destroy(_refreshHandler);
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

    private void SyncSlotPicture(int index, TerminalEntry? entry)
    {
        _grid.ShowPicture(index, entry == null ? null : _pictures.Get(entry.Key), entry != null);
    }

    private void ShowDragHint(int count)
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

        var text = _services.Text;
        var hint = count < 0 ? text.Get("terminal_storage_drag_rejected") :
            text.Get("terminal_storage_drag_hint", count, text.Get("terminal_name"));
        overlay.ShowTooltip(Input.mousePosition, new Vector2(32f, 0f),
            new RichTextBuilder(60).AddLine(hint, ignoreEmpty: true, RenderHandler.ColorPalette.LightBlue).Build());
    }

    private void StoreSelection(IReadOnlyList<GameItem> items)
    {
        if (!_host.IsCurrentSession())
        {
            return;
        }

        var result = _menu.HandleInsertion(() => _services.StoreSelection(items,
            code => Diagnostics?.Record("store_source", $"result={code}")));
        Diagnostics?.Record("store_result", $"status={result.Status} sources={items.Count} stored={result.StoredAmount}");
        _messageKey = result.MessageKey;
        _messageAmount = result.StoredAmount;
        Refresh(ReadSnapshot(), preserveView: true);
    }

    private void StoreSingle(GameItem item, int amount)
    {
        if (!_host.IsCurrentSession()) return;
        var result = _menu.HandleInsertion(() => _services.StoreSingle(item, amount,
            code => Diagnostics?.Record("store_source", $"result={code}")));
        Diagnostics?.Record("store_result", $"status={result.Status} sources=1 requested={amount} stored={result.StoredAmount}");
        _messageKey = result.MessageKey;
        _messageAmount = result.StoredAmount;
        Refresh(ReadSnapshot(), preserveView: true);
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
        if (_container == null || !_host.IsCurrentSession() || _suspended) return;
        var search = _services.Search;
        if (_searchRevision != search.ConfigurationRevision)
        {
            _searchRevision = search.ConfigurationRevision;
            Refresh();
        }
        if (_pipelineRevision != _host.Pipeline.Revision) Regroup();
        UpdateHoverTooltip();
        ApplyWheel();
        var viewChanged = _menu.SetPaused(IsShiftHeld());
        var reason = _inventoryMonitor.BeginRefresh(Time.unscaledTime, Window.IsVisible, _services.DiagnosticsEnabled());
        if (reason == TerminalRefreshReason.None)
        {
            if (viewChanged) ApplyMenuView(preserveView: true);
            return;
        }

        var snapshot = ReadSnapshot();
        if (!_inventoryMonitor.HasChanged(snapshot))
        {
            if (viewChanged) ApplyMenuView(preserveView: true);
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
        var delta = Input.mouseScrollDelta.y;
        if (delta == 0f || _scrollbar == null || !Window.IsVisible || _suspended || _host.Shield == null) return;
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
            // Stored items changed, so values read for grouping (an estimated value, for example) are read again.
            _attributes.Clear();
            var entries = snapshot.Entries.Select(entry => new TerminalEntry(entry.Key, entry.Amount, entry.DisplayName,
                entry.SpriteKey, entry.SpriteWidth, entry.SpriteHeight, entry.Serial,
                _services.Search.PrepareDocument(entry.Document), entry.TotalUnits)).ToArray();
            snapshot = new TerminalInventorySnapshot(snapshot.Online, entries, snapshot.StorageUnavailable);
            _pictures.Accept(snapshot.Entries);
            _menu.BroadcastChanges(snapshot);
            _storageUnavailable = snapshot.StorageUnavailable;
            ApplyMenuView(preserveView);
        });
    }

    private void ApplyMenuView(bool preserveView)
    {
        if (!_menu.GetLinkState().IsConnected) _screen.SetOffline();
        else if (preserveView) _screen.UpdateEntriesInOrder(_menu.GetEntries());
        else _screen.SetEntriesInOrder(_menu.GetEntries());
        Refresh();
    }

    private static bool IsShiftHeld() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

    /// <summary>Takes from a slot: left click one piece, right click half of its pieces, Shift + left click all.</summary>
    private void Extract(int index, TerminalClick click)
    {
        if (!_host.IsCurrentSession())
        {
            return;
        }

        var page = _screen.GetPage();
        if (index < 0 || index >= page.Slots.Count) return;
        var group = page.Slots[index].Group;
        if (group == null || group.Total <= 0) return;
        var pieces = TerminalClickAmount.For(click, group.Total);
        RecordIdentities(group);
        var result = _menu.HandleGroupInteraction(group, pieces, _services.Extract);
        Diagnostics?.Record("extract_result",
            $"click={click} stored={group.Total} members={group.Members.Count} requested={pieces} moved={result.ExtractedAmount} status={result.Status}");
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
        _pipelineRevision = _host.Pipeline.Revision;
        _attributes.Clear();
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
        overlay.ShowTooltip(Input.mousePosition, new Vector2(32f, 0f), _hoverSample.GetTooltip(),
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
        TooltipSideBox? previous = null;
        foreach (var panel in panels)
        {
            var box = panel.Id == TooltipPanelDefinition.HintId ? HintBox : BoxFor(panel.Id);
            var frame = panel.BorderRgb.HasValue ? _host.Icons.Frame(FrameKey, FrameBorder) : null;
            box.ShowFramed(panel.Text, frame, frame == null ? Color.white : Tint(panel.BorderRgb!.Value), after: previous);
            previous = box;
        }

        foreach (var pair in PanelBoxes)
            if (!panels.Any(panel => panel.Id == pair.Key)) pair.Value.Hide();
    }

    private const string FrameKey = "window_frame";
    private const int FrameBorder = 4;

    private static Color Tint(int rgb) => RenderHandler.GetColorFromPalette(Palette(rgb));

    private static TooltipSideBox BoxFor(string panelId)
    {
        if (!PanelBoxes.TryGetValue(panelId, out var box)) PanelBoxes.Add(panelId, box = new TooltipSideBox());
        return box;
    }

    private IReadOnlyList<PanelText> PanelsFor(int slotIndex)
    {
        var page = _screen.GetPage();
        var group = slotIndex >= 0 && slotIndex < page.Slots.Count ? page.Slots[slotIndex].Group : null;
        if (group == null)
            return _hintPanels ??= new[] { new PanelText(TooltipPanelDefinition.HintId, BuildHint(null), _host.Pipeline.HintPanel.BorderRgb) };
        if (_hoverPanels != null && ReferenceEquals(_hoverHintGroup, group) && _hoverHintRevision == _host.Pipeline.Revision)
            return _hoverPanels;
        _hoverHintGroup = group;
        _hoverHintRevision = _host.Pipeline.Revision;
        _hoverPanels = BuildPanels(group);
        return _hoverPanels;
    }

    private IReadOnlyList<PanelText> BuildPanels(DisplayEntry group)
    {
        var panels = _host.Pipeline.Tooltip(group, _host.Context);
        var result = new List<PanelText>
        {
            new(TooltipPanelDefinition.HintId,
                BuildHint(panels.FirstOrDefault(panel => panel.Definition.Id == TooltipPanelDefinition.HintId)),
                _host.Pipeline.HintPanel.BorderRgb)
        };
        foreach (var panel in panels)
        {
            if (panel.Definition.Id == TooltipPanelDefinition.HintId) continue;
            result.Add(new PanelText(panel.Definition.Id, AddLines(new RichTextBuilder(60), panel).Build(),
                panel.Definition.BorderRgb));
        }

        return result;
    }

    /// <summary>The view's own panel: lines extensions added to it, then the usage hints.</summary>
    private RichText BuildHint(TooltipPanel? added)
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
            .Build();
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
        internal PanelText(string id, RichText text, int? borderRgb)
        {
            Id = id;
            Text = text;
            BorderRgb = borderRgb;
        }

        internal string Id { get; }

        internal RichText Text { get; }

        internal int? BorderRgb { get; }
    }

    private void HideHoverTooltip()
    {
        if (_showingTooltip)
        {
            OverlayHandler.current?.HideTooltip();
            HintBox.Hide();
            foreach (var box in PanelBoxes.Values) box.Hide();
        }

        _showingTooltip = false;
        _hoverKey = null;
        _hoverSample = null;
    }

    private void Refresh()
    {
        if (_container == null)
        {
            return;
        }

        var text = _services.Text;
        var page = _screen.GetPage();
        for (var index = 0; index < page.Slots.Count; index++)
        {
            var slot = page.Slots[index];
            _decorations.Set(index, slot.Decorations);
            if (slot.Entry == null)
            {
                _grid.SetEmpty(index);
                continue;
            }

            // The native item tooltip replaces the plain name hint (see UpdateHoverTooltip).
            var available = slot.Amount > 0;
            _grid.SetEntry(index, available ? _pictures.Get(slot.Entry.Key) : null, available, available, slot.QuantityBadge);
        }

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
