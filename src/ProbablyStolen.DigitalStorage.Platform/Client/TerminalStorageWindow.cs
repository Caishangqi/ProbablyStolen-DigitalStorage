using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Common.Application.Events;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Identity;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Builds the terminal overlay from display snapshots without inserting display items into native storage.</summary>
internal sealed class TerminalStorageWindow
{
    private const string WindowId = "digital_storage_terminal_storage";
    private const int Columns = TerminalWindowMetrics.Columns;
    private const int CellPixels = TerminalWindowMetrics.CellPixels;
    // Session-only view settings shared by every terminal, like AE2's client-side terminal style; never saved.
    private static TerminalSortOrder _sortOrder = TerminalSortOrder.Name;
    private static TerminalSortDirection _sortDirection = TerminalSortDirection.Ascending;
    private static TerminalSize _size = TerminalSize.Small;
    private readonly ModText _text;
    private readonly Func<TerminalInventorySnapshot> _readSnapshot;
    private readonly Func<TerminalEntry, long, TerminalExtractionResult> _extract;
    private readonly Func<ItemKey, long> _stackLimit;
    private readonly Action _openModules;
    private readonly Func<bool> _openPartitions;
    private readonly Func<IReadOnlyList<GameItem>, Action<string>, TerminalInsertionResult> _storeSelection;
    private readonly Func<GameItem, int, Action<string>, TerminalInsertionResult> _storeSingle;
    private readonly Func<GameItem, int, bool> _canStore;
    private Image? _dropHighlight;
    private RectTransform? _slotRoot;
    private readonly Func<bool> _diagnosticsEnabled;
    private readonly Action<string> _writeDiagnostic;
    private TerminalDiagnostics? _diagnostics;
    private readonly StorageScreen _screen;
    private readonly TerminalSearchService _search;
    private long _searchRevision;
    private readonly TerminalSnapshotCache<SlotPicture> _pictures;
    private readonly HashSet<ItemKey> _pictureFailuresLogged = new();
    private TMP_InputField? _searchInput;
    private int _rows = TerminalWindowMetrics.Rows;
    private TerminalScrollbar? _scrollbar;
    private readonly StorageMenu _menu = new();
    // Pages are locked to players, so outside the terminal's own operations contents only change when cells,
    // machines, batteries or controller power change; those raise NetworkChanged and the next frame rereads.
    private readonly TerminalInventoryMonitor _inventoryMonitor = new();
    private readonly Func<Action<NetworkChanged>, IDisposable> _subscribeNetwork;
    private IDisposable? _networkSubscription;
    private readonly Func<ItemKey, GameItem?> _findSample;
    private ItemKey? _hoverKey;
    private GameItemElement? _hoverSample;
    private bool _showingTooltip;
    // One box serves every terminal; only one terminal window shows at a time.
    private static readonly TooltipSideBox HintBox = new();
    private RichText? _hintText;
    private CustomUIWindow? _window;
    private TerminalInputHighlighter? _inputHighlighter;
    private CustomUIElement? _status;
    private CustomUIElement?[] _buttons = Array.Empty<CustomUIElement?>();
    private Image?[] _icons = Array.Empty<Image?>();
    private RectTransform?[] _views = Array.Empty<RectTransform?>();
    private List<Image>[] _viewLayers = Array.Empty<List<Image>>();
    private string? _messageKey;
    private long _messageAmount;
    private PlayerStore? _session;
    private bool _storageUnavailable;

    internal TerminalStorageWindow(ModText text, Func<TerminalInventorySnapshot> readSnapshot,
        Func<TerminalEntry, long, TerminalExtractionResult> extract, Func<ItemKey, long> stackLimit,
        Action openModules, Func<bool> openPartitions,
        Func<IReadOnlyList<GameItem>, Action<string>, TerminalInsertionResult> storeSelection,
        Func<GameItem, int, Action<string>, TerminalInsertionResult> storeSingle,
        Func<GameItem, int, bool> canStore, Func<ItemKey, GameItem?> findSample,
        Func<bool> diagnosticsEnabled, Action<string> writeDiagnostic,
        Func<Action<NetworkChanged>, IDisposable> subscribeNetwork, TerminalSearchService search)
    {
        _subscribeNetwork = subscribeNetwork ?? throw new ArgumentNullException(nameof(subscribeNetwork));
        _findSample = findSample ?? throw new ArgumentNullException(nameof(findSample));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _readSnapshot = readSnapshot ?? throw new ArgumentNullException(nameof(readSnapshot));
        _extract = extract ?? throw new ArgumentNullException(nameof(extract));
        _stackLimit = stackLimit ?? throw new ArgumentNullException(nameof(stackLimit));
        _openModules = openModules ?? throw new ArgumentNullException(nameof(openModules));
        _openPartitions = openPartitions ?? throw new ArgumentNullException(nameof(openPartitions));
        _storeSelection = storeSelection ?? throw new ArgumentNullException(nameof(storeSelection));
        _storeSingle = storeSingle ?? throw new ArgumentNullException(nameof(storeSingle));
        _canStore = canStore ?? throw new ArgumentNullException(nameof(canStore));
        _diagnosticsEnabled = diagnosticsEnabled;
        _writeDiagnostic = writeDiagnostic;
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _screen = new StorageScreen(Columns, TerminalWindowMetrics.Rows, CellPixels, _search);
        _pictures = new TerminalSnapshotCache<SlotPicture>(CapturePicture, PictureCaptureFailed);
    }

    /// <summary>Makes this window the one that opens for the terminal, in place of its native machinery window.</summary>
    internal void Attach(GameItem terminal)
    {
        if (terminal == null)
        {
            throw new ArgumentNullException(nameof(terminal));
        }

        TerminalOpenRouter.Register(terminal, this);
        var native = terminal.contentWindow;
        native.openCallback += () =>
        {
            if (TerminalOpenRouter.IsNativeRequested) return;
            // Reached when the native window was shown without the patched OpenContentAction, for example by a
            // direct Show call (OverlayHandler.ShowInventory) or when the patch is missing. Show has already
            // finished, so hiding here only leaves a one-frame flash.
            try
            {
                Open();
                native.Hide();
            }
            catch (Exception error)
            {
                // Show is still running; keep the native window so the click still opens something.
                _writeDiagnostic($"Digital Storage: terminal window failed to open ({error.GetType().Name}); native window kept");
            }
        };
    }

    /// <summary>Closes the window when it is showing, otherwise opens it, like double-clicking a native container.</summary>
    internal void Toggle()
    {
        if (_window != null && _window.IsAlive && _window.IsVisible) _window.Close();
        else Open();
    }

    private void Open()
    {
        if (CustomUIManager.Instance == null)
        {
            return;
        }

        if (_window == null || !_window.IsAlive)
        {
            ClearControls();
            Build();
        }

        if (_window == null)
        {
            return;
        }

        _session = PlayerStore.Instance;
        _menu.SetSort(_sortOrder, _sortDirection);
        _networkSubscription?.Dispose();
        _networkSubscription = _subscribeNetwork(_ => _inventoryMonitor.MarkDirty());
        _inventoryMonitor.Reset(Time.unscaledTime);
        Refresh(ReadSnapshot());
        _window.Show();
        _window.frame?.dragHandle?.MoveFront();
    }

    private void Build()
    {
        _diagnostics = new TerminalDiagnostics(_diagnosticsEnabled, _writeDiagnostic);
        _rows = CountRows();
        _screen.SetRows(_rows);
        var builder = CustomUIManager.Instance.CreateWindow(WindowId, _text.Get("terminal_storage_title"),
            "overlay", null, CustomUIManager.Style.Machinery);
        if (builder == null)
        {
            return;
        }

        const float gap = 4f;
        var bodyWidth = TerminalWindowMetrics.ToolbarButton + gap + TerminalWindowMetrics.GridWidth + gap +
            TerminalWindowMetrics.ScrollbarWidth;
        builder.SetDraggable(true).SetCloseOnEscape()
            .SetSize(TerminalWindowMetrics.Width, TerminalWindowMetrics.GetHeight(_rows))
            .OnClose(ClearControls);

        // AE2 layout: the search field sits above the grid, the toolbar runs down its left side and the scrollbar
        // down its right side.
        builder.BeginRow(spacing: gap);
        builder.AddSpace(TerminalWindowMetrics.ToolbarButton);
        builder.AddInput(_text.Get("terminal_storage_search_placeholder"), _screen.GetPage().SearchText, id: "search",
            onTyping: query =>
            {
                _screen.SetSearchText(query);
                Refresh();
            });
        _searchInput = builder.Last.input;
        _inputHighlighter = _searchInput == null ? null : new TerminalInputHighlighter(_searchInput, _search,
            query => _menu.GetEntries().Any(entry => entry.Matches(_search, query)));
        SetControlSize(builder.Last, bodyWidth - TerminalWindowMetrics.ToolbarButton - gap);
        builder.End();

        builder.BeginRow(spacing: gap);
        builder.BeginColumn(spacing: TerminalWindowMetrics.ToolbarSpacing);
        AddToolbarButton(builder, "tool_modules", _openModules);
        AddToolbarButton(builder, "tool_sort", CycleSortOrder);
        AddToolbarButton(builder, "tool_direction", CycleSortDirection);
        AddToolbarButton(builder, "tool_partitions", OpenPartitions);
        AddToolbarButton(builder, "tool_size", CycleSize);
        builder.End();

        builder.BeginGrid(Columns, CellPixels, CellPixels, spacing: TerminalWindowMetrics.CellSpacing);
        var slotCount = Columns * _rows;
        _buttons = new CustomUIElement?[slotCount];
        _icons = new Image?[slotCount];
        _views = new RectTransform?[slotCount];
        _viewLayers = new List<Image>[slotCount];
        for (var index = 0; index < slotCount; index++)
        {
            var slotIndex = index;
            builder.BeginPanel(CellPixels, CellPixels, padding: 2f, spacing: 0f, id: $"slot_{index}");
            if (index == 0) _slotRoot = builder.Last.Rect.parent as RectTransform;
            ConfigureSlotBorder(builder.Last);
            builder.AddButton(string.Empty, () => Extract(slotIndex, IsShiftHeld() ? TerminalClick.All : TerminalClick.Stack),
                $"button_{index}");
            builder.Last.root.AddComponent<TerminalSlotClick>().Initialize(() => Extract(slotIndex, TerminalClick.Half));
            _buttons[index] = builder.Last;
            _icons[index] = ConfigureSlotButton(builder.Last);
            _views[index] = CreateItemView(builder.Last);
            _viewLayers[index] = new List<Image>();
            builder.AddLabel(string.Empty, $"badge_{index}");
            ConfigureBadge(builder.Last);
            builder.End();
        }

        builder.End();
        _scrollbar = TerminalScrollbar.Create(builder.CurrentContainer, TerminalWindowMetrics.ScrollbarWidth,
            TerminalWindowMetrics.GetGridHeight(_rows), TerminalWindowMetrics.ScrollbarHandle, row =>
            {
                _screen.SetScroll(row);
                Refresh();
            });
        builder.End();

        builder.AddLabel(string.Empty, "status");
        SetControlSize(builder.Last, bodyWidth, 40f);
        if (builder.Last.text != null)
        {
            builder.Last.text.fontSize = 14f;
            builder.Last.text.overflowMode = TextOverflowModes.Ellipsis;
        }

        _window = builder.GetWindow();
        var contentLayout = _window.ContentRoot.GetComponent<VerticalLayoutGroup>();
        if (contentLayout != null)
        {
            contentLayout.spacing = 6f;
        }
        _status = _window.Get("status");
        UpdateToolbar();
        AttachToNativeWindowLayer();
        AttachNativeInputShield();
        CreateDropHighlight();
        var dropInput = _window.root.AddComponent<TerminalDropInputHandler>();
        dropInput.Initialize(_window, IsCurrentSession, StoreSelection, _diagnostics,
            count => ShowDragHint(count), StoreSingle, _canStore, SetDropHighlight, IsPointerOverSlots);
        _window.root.AddComponent<TerminalInventoryRefreshHandler>().Initialize(PollSnapshot);
    }

    /// <summary>Counts rows for the current size from the native window canvas, falling back to the default.</summary>
    private static int CountRows()
    {
        var canvas = RenderHandler.current?.windowRoot?.rectTransform.GetComponentInParent<Canvas>();
        return TerminalWindowMetrics.GetRows(_size, LetterboxManager.GameAreaHeight,
            canvas == null ? 0f : canvas.scaleFactor);
    }

    private static void AddToolbarButton(CustomUIBuilder builder, string id, Action onClick)
    {
        builder.AddButton(string.Empty, onClick, id);
        SetControlSize(builder.Last, TerminalWindowMetrics.ToolbarButton, TerminalWindowMetrics.ToolbarButton);
        if (builder.Last.text != null)
        {
            builder.Last.text.fontSize = 14f;
            builder.Last.text.alignment = TextAlignmentOptions.Center;
        }
    }

    /// <summary>Shows each toolbar button's current setting as its label and its full meaning as its tooltip.</summary>
    private void UpdateToolbar()
    {
        if (_window == null) return;
        SetToolbar("tool_modules", _text.Get("terminal_button_modules"), _text.Get("terminal_button_modules_tip"));
        var byAmount = _sortOrder == TerminalSortOrder.Amount;
        SetToolbar("tool_sort", _text.Get(byAmount ? "terminal_button_sort_amount" : "terminal_button_sort_name"),
            _text.Get("terminal_button_sort_tip", _text.Get(byAmount ? "terminal_sort_amount" : "terminal_sort_name")));
        var descending = _sortDirection == TerminalSortDirection.Descending;
        SetToolbar("tool_direction",
            _text.Get(descending ? "terminal_button_direction_desc" : "terminal_button_direction_asc"),
            _text.Get("terminal_button_direction_tip",
                _text.Get(descending ? "terminal_direction_desc" : "terminal_direction_asc")));
        SetToolbar("tool_partitions", _text.Get("terminal_button_partitions"),
            _text.Get("terminal_button_partitions_tip"));
        var size = _size.ToString().ToLowerInvariant();
        SetToolbar("tool_size", _text.Get("terminal_button_size_" + size),
            _text.Get("terminal_button_size_tip", _text.Get("terminal_size_" + size)));
    }

    private void SetToolbar(string id, string label, string tooltip)
    {
        var button = _window?.Get(id);
        button?.SetText(label);
        TerminalTooltipPresenter.SetText(button, tooltip);
    }

    private void CycleSortOrder()
    {
        _sortOrder = _sortOrder == TerminalSortOrder.Name ? TerminalSortOrder.Amount : TerminalSortOrder.Name;
        ApplySort();
    }

    private void CycleSortDirection()
    {
        _sortDirection = _sortDirection == TerminalSortDirection.Ascending
            ? TerminalSortDirection.Descending
            : TerminalSortDirection.Ascending;
        ApplySort();
    }

    private void ApplySort()
    {
        if (!IsCurrentSession()) return;
        _menu.SetSort(_sortOrder, _sortDirection);
        UpdateToolbar();
        ApplyMenuView(preserveView: true);
    }

    private void OpenPartitions()
    {
        if (!IsCurrentSession()) return;
        if (!_openPartitions())
        {
            _messageKey = "terminal_partition_none";
            Refresh();
        }
    }

    /// <summary>Cycles AE2's terminal styles and rebuilds the grid, keeping the search, scroll row and position.</summary>
    private void CycleSize()
    {
        if (!IsCurrentSession() || _window == null) return;
        _size = _size == TerminalSize.Full ? TerminalSize.Small : _size + 1;
        var position = _window.Rect.anchoredPosition;
        var scroll = _screen.ScrollRow;
        _window.Close();
        Open();
        if (_window == null) return;
        _window.Rect.anchoredPosition = position;
        _screen.SetScroll(scroll);
        Refresh();
    }

    private void AttachNativeInputShield()
    {
        if (_window == null) return;
        // RenderHandler skips ordinary graphics. This background ends its search without being an inventory.
        // Keep it behind controls: adding native event interfaces to input/button children would steal their events.
        var shield = TreeNodeRender.InstantiateAsRoot(_window.Rect);
        shield.gameObject.name = "terminal_native_input_shield";
        shield.rectTransform.anchorMin = Vector2.zero;
        shield.rectTransform.anchorMax = Vector2.one;
        shield.rectTransform.offsetMin = shield.rectTransform.offsetMax = Vector2.zero;
        shield.rectTransform.SetAsFirstSibling();
        shield.image.sprite = null;
        shield.image.color = new Color(0f, 0f, 0f, 0.004f);
        shield.image.raycastTarget = true;
        // Render node prefabs start with the Image disabled, and GraphicRaycaster skips disabled graphics; without
        // this the native drag search passed through the terminal to the inventory behind it.
        shield.image.enabled = true;
        shield.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        _diagnostics?.Record("input_shield", "native_node=true placement_target=false bounds=window");
    }

    private void CreateDropHighlight()
    {
        if (_slotRoot == null) return;
        var root = new GameObject("terminal_drop_range", typeof(RectTransform));
        root.transform.SetParent(_slotRoot, false);
        root.transform.SetAsFirstSibling();
        var rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        // A two-unit gutter keeps the native border visible behind opaque slot panels.
        rect.anchoredPosition = new Vector2(-2f, 2f);
        rect.sizeDelta = new Vector2(TerminalWindowMetrics.GridWidth + 4f, TerminalWindowMetrics.GetGridHeight(_rows) + 4f);
        root.AddComponent<LayoutElement>().ignoreLayout = true;
        _dropHighlight = root.AddComponent<Image>();
        _dropHighlight.type = Image.Type.Sliced;
        _dropHighlight.raycastTarget = false;
        _dropHighlight.enabled = false;
    }

    private bool IsPointerOverSlots()
    {
        if (_window == null || _dropHighlight == null) return false;
        var canvas = _window.root.GetComponentInParent<Canvas>();
        var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        return RectTransformUtility.RectangleContainsScreenPoint(_dropHighlight.rectTransform, Input.mousePosition, camera);
    }

    private void SetDropHighlight(TerminalDropHighlight state)
    {
        if (_dropHighlight == null) return;
        if (state == TerminalDropHighlight.Hidden || RenderHandler.current == null)
        {
            _dropHighlight.enabled = false;
            return;
        }

        var color = state == TerminalDropHighlight.Hover ? RenderHandler.BackgroundColor.Green :
            state == TerminalDropHighlight.Rejected ? RenderHandler.BackgroundColor.Red : RenderHandler.BackgroundColor.Blue;
        RenderHandler.current.SetHighlightColor(_dropHighlight, color, RenderHandler.BackgroundType.Border);
    }

    private static void SetControlSize(CustomUIElement control, float width, float height = 32f)
    {
        var layout = control.root.GetComponent<LayoutElement>() ?? control.root.AddComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = width;
        layout.minHeight = layout.preferredHeight = height;
        layout.flexibleWidth = layout.flexibleHeight = 0f;
    }

    private void AttachToNativeWindowLayer()
    {
        var native = RenderHandler.current;
        if (_window == null || native == null || native.windowRoot == null)
        {
            _diagnostics?.Record("layer_unavailable", "native_window_root=false");
            return;
        }

        // Native dragged ghosts and lore stay in overlayRoot, above ordinary windows.
        var previousLayer = DescribeCanvas(_window.root);
        var viewport = new GameObject("digital_storage_terminal_viewport", typeof(RectTransform)).GetComponent<RectTransform>();
        viewport.SetParent(native.windowRoot.rectTransform, false);
        viewport.anchorMin = viewport.anchorMax = viewport.pivot = new Vector2(0.5f, 0.5f);
        _window.root.transform.SetParent(viewport, false);
        _window.Rect.anchorMin = _window.Rect.anchorMax = _window.Rect.pivot = new Vector2(0.5f, 0.5f);
        _window.Rect.anchoredPosition = Vector2.zero;

        // UIDragHandler caches its Canvas in Awake, so bind a fresh instance after reparenting.
        var previousDrag = _window.root.GetComponent<UIDragHandler>();
        if (previousDrag != null)
        {
            previousDrag.enabled = false;
            UnityEngine.Object.Destroy(previousDrag);
        }

        var drag = _window.root.AddComponent<UIDragHandler>();
        drag.ignoreClamp = true;
        drag.moveFrontObjects = new List<GameObject> { viewport.gameObject };
        _diagnostics?.Record("drag_rebound", "native_canvas=true parent=private_viewport clamp=full_window focus=viewport");
        if (_window.frame != null)
        {
            _window.frame.dragHandle = drag;
        }

        _window.root.AddComponent<TerminalWindowSizer>().Initialize(viewport, _diagnostics);
        _diagnostics?.Record("window_layer",
            $"previous={previousLayer} window={DescribeCanvas(_window.root)} native_overlay={DescribeCanvas(native.overlayRootObject)} style=machinery slots=flat-dark");
    }

    private static string DescribeCanvas(GameObject? root)
    {
        var canvas = root == null ? null : root.GetComponentInParent<Canvas>();
        return canvas == null ? "none" :
            $"mode:{(int)canvas.renderMode},order:{canvas.sortingOrder},layer:{canvas.sortingLayerID},override:{canvas.overrideSorting},root_order:{canvas.rootCanvas.sortingOrder},scale:{canvas.scaleFactor:0.##}";
    }

    private static void ConfigureSlotBorder(CustomUIElement slot)
    {
        slot.image.sprite = null;
        slot.image.color = new Color32(57, 51, 65, 255);
        slot.image.raycastTarget = false;
    }

    private static Image ConfigureSlotButton(CustomUIElement button)
    {
        button.image.sprite = null;
        button.image.color = new Color32(35, 32, 45, 255);
        button.button.transition = Selectable.Transition.ColorTint;
        var icon = new GameObject("terminal_icon", typeof(RectTransform)).AddComponent<Image>();
        icon.transform.SetParent(button.root.transform, false);
        var rect = (RectTransform)icon.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(6f, 6f);
        rect.offsetMax = new Vector2(-6f, -6f);
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return icon;
    }

    /// <summary>Creates the container that holds the mirrored native item layers, over the icon area.</summary>
    private static RectTransform CreateItemView(CustomUIElement button)
    {
        var view = (RectTransform)new GameObject("terminal_item_view", typeof(RectTransform)).transform;
        view.SetParent(button.root.transform, false);
        view.anchorMin = Vector2.zero;
        view.anchorMax = Vector2.one;
        view.offsetMin = new Vector2(6f, 6f);
        view.offsetMax = new Vector2(-6f, -6f);
        view.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return view;
    }

    private sealed class SlotPicture
    {
        internal SlotPicture(NativeItemView.Picture? layers, Sprite? fallback) { Layers = layers; Fallback = fallback; }
        internal NativeItemView.Picture? Layers { get; }
        internal Sprite? Fallback { get; }
    }

    private SlotPicture CapturePicture(TerminalEntry entry)
    {
        var sample = _findSample(entry.Key) as GameItemElement;
        NativeItemView.Picture? layers = null;
        if (sample != null && !sample.IsDestroyed())
        {
            NativeItemView.Redraw(sample);
            layers = NativeItemView.Capture(sample);
        }
        return new SlotPicture(layers, GetSprite(entry.SpriteKey));
    }

    private SlotPicture PictureCaptureFailed(TerminalEntry entry, Exception error)
    {
        if (_pictureFailuresLogged.Add(entry.Key))
        {
            try { _writeDiagnostic($"Digital Storage: terminal picture unavailable ({error.GetType().Name}); using sprite fallback."); }
            catch { /* A diagnostic callback cannot block an inventory display update. */ }
        }
        Sprite? fallback = null;
        try { fallback = GetSprite(entry.SpriteKey); }
        catch { /* A missing atlas leaves only the quantity badge, without hiding inventory. */ }
        return new SlotPicture(null, fallback);
    }

    /// <summary>Renders only snapshot-time cached pictures during filtering and scrolling.</summary>
    private void SyncSlotPicture(int index, TerminalEntry? entry)
    {
        var view = index < _views.Length ? _views[index] : null;
        var layers = index < _viewLayers.Length ? _viewLayers[index] : null;
        var icon = index < _icons.Length ? _icons[index] : null;
        if (entry == null)
        {
            if (layers != null) NativeItemView.Clear(layers);
            if (icon != null) icon.enabled = false;
            return;
        }

        var picture = _pictures.Get(entry.Key);
        var mirrored = view != null && layers != null && picture?.Layers != null &&
            NativeItemView.Sync(view, layers, picture.Layers);
        if (!mirrored && layers != null) NativeItemView.Clear(layers);
        if (icon != null)
        {
            icon.sprite = mirrored ? null : picture?.Fallback;
            icon.enabled = !mirrored && icon.sprite != null;
        }
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

        foreach (var button in _buttons)
        {
            var trigger = button?.root.GetComponent<CustomUITooltipTrigger>();
            if (trigger != null)
            {
                CustomUIManager.Instance?.HideTooltip(trigger);
            }
        }

        var hint = count < 0 ? _text.Get("terminal_storage_drag_rejected") :
            _text.Get("terminal_storage_drag_hint", count, _text.Get("terminal_name"));
        overlay.ShowTooltip(Input.mousePosition, new Vector2(32f, 0f),
            new RichTextBuilder(60).AddLine(hint, ignoreEmpty: true, RenderHandler.ColorPalette.LightBlue).Build());
    }

    private static void ConfigureBadge(CustomUIElement badge)
    {
        var layout = badge.root.GetComponent<LayoutElement>() ?? badge.root.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        badge.Rect.anchorMin = Vector2.zero;
        badge.Rect.anchorMax = Vector2.one;
        badge.Rect.offsetMin = new Vector2(3f, 2f);
        badge.Rect.offsetMax = new Vector2(-3f, -2f);
        if (badge.text != null)
        {
            badge.text.alignment = TextAlignmentOptions.BottomRight;
            badge.text.fontSize = 12f;
            badge.text.raycastTarget = false;
        }
    }

    private void StoreSelection(IReadOnlyList<GameItem> items)
    {
        if (!IsCurrentSession())
        {
            return;
        }

        var result = _menu.HandleInsertion(() => _storeSelection(items,
            code => _diagnostics?.Record("store_source", $"result={code}")));
        _diagnostics?.Record("store_result", $"status={result.Status} sources={items.Count} stored={result.StoredAmount}");
        _messageKey = result.MessageKey;
        _messageAmount = result.StoredAmount;
        Refresh(ReadSnapshot(), preserveView: true);
    }

    private void StoreSingle(GameItem item, int amount)
    {
        if (!IsCurrentSession()) return;
        var result = _menu.HandleInsertion(() => _storeSingle(item, amount,
            code => _diagnostics?.Record("store_source", $"result={code}")));
        _diagnostics?.Record("store_result", $"status={result.Status} sources=1 requested={amount} stored={result.StoredAmount}");
        _messageKey = result.MessageKey;
        _messageAmount = result.StoredAmount;
        Refresh(ReadSnapshot(), preserveView: true);
    }

    private TerminalInventorySnapshot ReadSnapshot()
    {
        try
        {
            return _readSnapshot();
        }
        catch (Exception error)
        {
            // Identity failures carry their failure code. Other messages come from native code and are not
            // controlled here, so only the first line is kept, capped at 120 characters.
            _diagnostics?.Record("inventory_read_failed",
                $"entries_cleared=true error={error.GetType().Name} detail={DescribeReadFailure(error)}");
            return new TerminalInventorySnapshot(false, Array.Empty<TerminalEntry>());
        }
    }

    private static readonly char[] LineBreaks = { (char)13, (char)10 };

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
        if (_window == null || !IsCurrentSession()) return;
        if (_searchRevision != _search.ConfigurationRevision)
        {
            _searchRevision = _search.ConfigurationRevision;
            Refresh();
            _inputHighlighter?.Refresh();
        }
        UpdateHoverTooltip();
        ApplyWheel();
        var viewChanged = _menu.SetPaused(IsShiftHeld());
        var reason = _inventoryMonitor.BeginRefresh(Time.unscaledTime, _window.IsVisible, _diagnosticsEnabled());
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
            _diagnostics?.Record("missed_network_event", $"online={snapshot.Online} entries={snapshot.Entries.Count}");
        _diagnostics?.Record("inventory_refresh",
            $"online={snapshot.Online} unavailable={snapshot.StorageUnavailable} entries={snapshot.Entries.Count}");
        Refresh(snapshot, preserveView: true);
    }

    /// <summary>
    /// Scrolls one page per wheel notch while the pointer is over the window, as AE2 does. During a native drag the
    /// wheel belongs to the game (rotation and drag amount), so it is left alone.
    /// </summary>
    private void ApplyWheel()
    {
        var delta = Input.mouseScrollDelta.y;
        if (delta == 0f || _scrollbar == null || _window == null || !_window.IsVisible) return;
        if (ItemMouseDragHandler.current?.IsDraggingItem == true ||
            ItemMultiSelectHandler.current?.IsGroupDragging == true)
        {
            return;
        }

        var canvas = _window.root.GetComponentInParent<Canvas>();
        var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (RectTransformUtility.RectangleContainsScreenPoint(_window.Rect, Input.mousePosition, camera))
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
            var entries = snapshot.Entries.Select(entry => new TerminalEntry(entry.Key, entry.Amount, entry.DisplayName,
                entry.SpriteKey, entry.SpriteWidth, entry.SpriteHeight, entry.Serial, _search.PrepareDocument(entry.Document))).ToArray();
            snapshot = new TerminalInventorySnapshot(snapshot.Online, entries, snapshot.StorageUnavailable);
            _pictures.Accept(snapshot.Entries);
            _menu.BroadcastChanges(snapshot);
            _storageUnavailable = snapshot.StorageUnavailable;
            ApplyMenuView(preserveView);
            _inputHighlighter?.Refresh();
        });
    }

    private void ApplyMenuView(bool preserveView)
    {
        if (!_menu.GetLinkState().IsConnected) _screen.SetOffline();
        else if (preserveView) _screen.UpdateEntriesInOrder(_menu.GetEntries());
        else _screen.SetEntriesInOrder(_menu.GetEntries());
        Refresh();
    }

    private long StackLimit(ItemKey key)
    {
        try
        {
            return _stackLimit(key);
        }
        catch
        {
            // Without a readable limit a click still takes one stored stack.
            return 1;
        }
    }

    private static bool IsShiftHeld() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

    /// <summary>Takes from a slot like AE2: left click one stack, right click half, Shift + left click everything.</summary>
    private void Extract(int index, TerminalClick click)
    {
        if (!IsCurrentSession())
        {
            return;
        }

        var page = _screen.GetPage();
        if (index < 0 || index >= page.Slots.Count || page.Slots[index].Entry == null)
        {
            return;
        }

        var entry = page.Slots[index].Entry!;
        if (entry.Amount <= 0 || entry.Serial < 0) return;
        var limit = StackLimit(entry.Key);
        var amount = TerminalClickAmount.For(click, entry.Amount, limit);
        var result = _menu.HandleInteraction(entry.Serial, amount, _extract);
        _diagnostics?.Record("extract_result",
            $"click={click} stored={entry.Amount} stack_limit={limit} requested={amount} moved={result.ExtractedAmount} status={result.Status}");
        _messageKey = result.MessageKey;
        _messageAmount = result.ExtractedAmount;
        Refresh(ReadSnapshot(), preserveView: true);
    }

    /// <summary>
    /// Shows the native tooltip of a stored item of the hovered entry's type. The quantity stays on the slot badge
    /// and is never written to the sample. Only a tooltip this window showed is hidden again.
    /// </summary>
    private void UpdateHoverTooltip()
    {
        // The drag hint shares the overlay tooltip while an item is being dragged over the terminal.
        var dragging = ItemMouseDragHandler.current?.IsDraggingItem == true ||
            ItemMultiSelectHandler.current?.IsGroupDragging == true;
        var slotIndex = -1;
        var entry = _window != null && _window.IsVisible && !dragging ? HoveredEntry(out slotIndex) : null;
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
            _hoverSample = _findSample(entry.Key) as GameItemElement;
            // Diagnostics for terminal item pictures; uncomment together with the detail block in NativeItemView.Sync.
            // var mirrored = slotIndex >= 0 && slotIndex < _icons.Length && _icons[slotIndex]?.enabled != true;
            // _writeDiagnostic($"Digital Storage: item_view item={_hoverSample?.identifier ?? "<none>"} " +
            //     $"mirrored={mirrored} {NativeItemView.LastSummary}");
        }

        var overlay = OverlayHandler.current;
        if (_hoverSample == null || _hoverSample.IsDestroyed() || overlay == null)
        {
            HideHoverTooltip();
            return;
        }

        if (slotIndex >= 0 && slotIndex < _views.Length && _views[slotIndex] != null)
        {
            var mirrored = NativeItemView.Sync(_views[slotIndex]!, _viewLayers[slotIndex], _hoverSample);
            if (mirrored && _icons[slotIndex] != null) _icons[slotIndex]!.enabled = false;
            if (!mirrored) SyncSlotPicture(slotIndex, entry);
        }

        // Rebuild every frame like ItemTooltipHandler: GetTooltip fires ModHook.OnCreateTooltip*, and mods that add
        // side panels (for example a contents preview) keep them only while the tooltip keeps being created.
        overlay.ShowTooltip(Input.mousePosition, new Vector2(32f, 0f), _hoverSample.GetTooltip(),
            NativeTooltipWidth(_hoverSample));
        _showingTooltip = true;
        HintBox.Show(_hintText ??= BuildHintText());
    }

    /// <summary>Uses the same maximum width rules as the native ItemTooltipHandler.</summary>
    private static int NativeTooltipWidth(GameItem item) =>
        item.IsTag("FEED_DISPENSER_TAG") ? 90
        : item.IsGameItemType("MACHINE") ? 72
        : item.IsTag("MACHINE_MANUAL_TAG") ? 120
        : 60;

    private TerminalEntry? HoveredEntry() => HoveredEntry(out _);

    private TerminalEntry? HoveredEntry(out int slotIndex)
    {
        slotIndex = -1;
        if (_window == null)
        {
            return null;
        }

        var canvas = _window.root.GetComponentInParent<Canvas>();
        var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var page = _screen.GetPage();
        for (var index = 0; index < _buttons.Length && index < page.Slots.Count; index++)
        {
            var button = _buttons[index];
            if (button != null && RectTransformUtility.RectangleContainsScreenPoint(button.Rect, Input.mousePosition, camera))
            {
                slotIndex = index;
                return page.Slots[index].Entry;
            }
        }

        return null;
    }

    /// <summary>Lists the slot clicks, shown in a box under the hovered item's tooltip.</summary>
    private RichText BuildHintText() => new RichTextBuilder(60)
        .AddLine(_text.Get("terminal_hint_title"), ignoreEmpty: true, RenderHandler.ColorPalette.White)
        .AddLine(_text.Get("terminal_hint_left"), ignoreEmpty: true, RenderHandler.ColorPalette.LightBlue)
        .AddLine(_text.Get("terminal_hint_right"), ignoreEmpty: true, RenderHandler.ColorPalette.LightBlue)
        .AddLine(_text.Get("terminal_hint_shift"), ignoreEmpty: true, RenderHandler.ColorPalette.LightBlue)
        .Build();

    private void HideHoverTooltip()
    {
        if (_showingTooltip)
        {
            OverlayHandler.current?.HideTooltip();
            HintBox.Hide();
        }

        _showingTooltip = false;
        _hoverKey = null;
        _hoverSample = null;
    }

    internal static void CloseCurrent() => CustomUIManager.Instance?.CloseWindow(WindowId);

    private bool IsCurrentSession()
    {
        if (_session != null && ReferenceEquals(_session, PlayerStore.Instance) && !SaveManager.IsDecoding)
        {
            return true;
        }

        _window?.Close();
        return false;
    }

    private void ClearControls()
    {
        HideHoverTooltip();
        _inputHighlighter?.Dispose();
        _inputHighlighter = null;
        _searchInput = null;
        _pictures.Clear();
        _pictureFailuresLogged.Clear();
        _window = null;
        _dropHighlight = null;
        _slotRoot = null;
        _status = null;
        _scrollbar = null;
        _session = null;
        _networkSubscription?.Dispose();
        _networkSubscription = null;
        _inventoryMonitor.Reset();
        _menu.Reset();
        _storageUnavailable = false;
        _buttons = Array.Empty<CustomUIElement?>();
        _icons = Array.Empty<Image?>();
        _views = Array.Empty<RectTransform?>();
        _viewLayers = Array.Empty<List<Image>>();
        _diagnostics?.Record("window_closed", "controls_cleared=true");
        _diagnostics = null;
        _messageKey = null;
        // Rebuilt on the next hover, so a language change shows after the window reopens.
        _hintText = null;
        _screen.SetLoading();
    }

    private static Sprite? GetSprite(string key)
    {
        var separator = key.IndexOf('|');
        if (separator <= 0 || separator >= key.Length - 1)
        {
            return null;
        }

        return RenderHandler.TryLoadFromAtlas(key.Substring(0, separator), key.Substring(separator + 1));
    }

    private void Refresh()
    {
        if (_window == null)
        {
            return;
        }

        var page = _screen.GetPage();
        for (var index = 0; index < page.Slots.Count; index++)
        {
            var slot = page.Slots[index];
            var button = _buttons[index];
            var badge = _window?.Get($"badge_{index}");
            if (slot.Entry == null)
            {
                button?.SetVisible(true);
                TerminalTooltipPresenter.SetText(button, string.Empty);
                if (button?.button != null)
                {
                    button.button.interactable = false;
                }

                SyncSlotPicture(index, null);
                badge?.SetVisible(false);
                continue;
            }

            button?.SetVisible(true).SetPreferredSize(CellPixels - 4f, CellPixels - 4f);
            // The native item tooltip replaces the plain name hint (see UpdateHoverTooltip).
            TerminalTooltipPresenter.SetText(button, string.Empty);
            if (button?.button != null)
            {
                button.button.interactable = slot.Entry.Amount > 0;
            }

            SyncSlotPicture(index, slot.Entry.Amount > 0 ? slot.Entry : null);
            badge?.SetVisible(true).SetText(slot.QuantityBadge);
        }

        _status?.SetText(_messageKey == null && _storageUnavailable
            ? _text.Get("terminal_storage_unavailable")
            : _messageKey == null ? page.State switch
            {
                TerminalDisplayState.Loading => _text.Get("terminal_storage_loading"),
                TerminalDisplayState.Offline => _text.Get("terminal_storage_offline"),
                TerminalDisplayState.Empty => _text.Get("terminal_storage_empty"),
                _ => string.Empty
            }
            : _text.Get(_messageKey, _messageAmount));
        if (_status != null)
        {
            TerminalTooltipPresenter.SetText(_status, _status.GetText());
        }

        _messageKey = null;
        _scrollbar?.SetRange(page.MaxScroll, _screen.PageSize, page.ScrollRow);
    }
}
