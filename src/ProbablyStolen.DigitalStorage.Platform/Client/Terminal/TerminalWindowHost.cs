using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.TerminalStyle;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using ProbablyStolen.DigitalStorage.Common.Application.Events;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.Modules;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.StorageTerminal;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Widgets;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

/// <summary>
/// Owns the terminal's Unity window. The active view, stacked pages and sidebars share its lifetime and input layer.
/// </summary>
internal class TerminalWindowHost : ITerminalHostView, ITerminalViewHost
{
    internal const string WindowId = "digital_storage_terminal_storage";
    protected readonly ButtonIcons Icons;
    private readonly TerminalRegistry _registry;
    private readonly ITerminalViewSettings _settings;
    private readonly Func<bool> _testViewEnabled;
    private readonly Action<GameItem, GameInventory, SlotMarker> _modulesChanged;
    private readonly Func<bool> _diagnosticsEnabled;
    private readonly Action<string> _writeDiagnostic;
    private readonly Func<IReadOnlyCollection<string>>? _moduleIdentifiers;
    private readonly Func<Action<NetworkChanged>, IDisposable>? _subscribeNetwork;
    private TerminalDiagnostics? _diagnostics;
    private GameItem? _terminal;
    private CustomUIWindow? _window;
    private TreeNodeRender? _shield;
    private PlayerStore? _session;
    private int _rows = TerminalWindowMetrics.Rows;
    private RectTransform? _viewHost;
    private RectTransform? _subHost;
    private readonly List<DockView> _docks = new();
    private readonly List<ScreenPage> _pages = new();
    private TerminalHost? _host;
    private ITerminalView? _currentView;
    private GameGridInventory? _bay;
    private IDisposable? _configSubscription;
    private Vector2? _sizeBeforeSubScreen;
    private Vector2? _positionBeforeSubScreen;
    private bool _closing;
    private bool _rebuilding;
    private StoragePresentationPipeline? _pipeline;

    internal TerminalWindowHost(TerminalRegistry registry, StorageTerminalServices storage, ITerminalViewSettings settings,
        Func<bool> testViewEnabled, ButtonIcons icons) : this(registry, storage.Text, settings, testViewEnabled, storage.DiagnosticsEnabled,
        storage.WriteDiagnostic, null, icons, storage.SubscribeNetwork)
    {
    }

    internal TerminalWindowHost(TerminalRegistry registry, ModText text, ITerminalViewSettings settings,
        Func<bool> testViewEnabled, Func<bool> diagnosticsEnabled, Action<string> writeDiagnostic,
        Func<IReadOnlyCollection<string>>? installedModules, ButtonIcons icons,
        Func<Action<NetworkChanged>, IDisposable>? subscribeNetwork = null)
    {
        Icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _testViewEnabled = testViewEnabled ?? throw new ArgumentNullException(nameof(testViewEnabled));
        _diagnosticsEnabled = diagnosticsEnabled ?? throw new ArgumentNullException(nameof(diagnosticsEnabled));
        _writeDiagnostic = writeDiagnostic ?? throw new ArgumentNullException(nameof(writeDiagnostic));
        _moduleIdentifiers = installedModules;
        _subscribeNetwork = subscribeNetwork;
        _modulesChanged = (_, _, _) => OnModulesChanged();
    }

    internal ModText Text => _text;

    /// <summary>The button icons of this window, for pages that add icon buttons.</summary>
    internal ButtonIcons ButtonIcons => Icons;

    private readonly ModText _text;

    /// <summary>The terminal item this window belongs to.</summary>
    internal GameItem Terminal => _terminal ?? throw new InvalidOperationException("The window is not attached.");

    /// <summary>The open custom window.</summary>
    internal CustomUIWindow CustomWindow => _window ?? throw new InvalidOperationException("The window is closed.");

    internal bool IsHostClosing => _closing;

    internal TreeNodeRender InputShield => _shield ?? throw new InvalidOperationException("The host input shield is unavailable.");

    CustomUIWindow ITerminalViewHost.Window => CustomWindow;

    TreeNodeRender? ITerminalViewHost.Shield => _shield;

    TerminalDiagnostics? ITerminalViewHost.Diagnostics => _diagnostics;

    int ITerminalViewHost.Rows => _rows;

    ButtonIcons ITerminalViewHost.Icons => Icons;

    bool ITerminalViewHost.IsCurrentSession() => IsCurrentSession();

    /// <summary>The storage extensions of this window, created once from the frozen registry.</summary>
    internal StoragePresentationPipeline StoragePipeline => _pipeline ??= _registry.CreateStoragePipeline(WriteDiagnostic);

    StoragePresentationPipeline ITerminalViewHost.Pipeline => StoragePipeline;

    TerminalContext? ITerminalViewHost.Context => _host?.Context;

    /// <summary>A fresh context of the window, or null while it is not open.</summary>
    internal TerminalContext? CurrentContext => _host?.Context;

    /// <summary>Writes one diagnostic line to the mod log.</summary>
    internal void WriteDiagnostic(string message) => _writeDiagnostic(message);

    protected virtual string HostWindowId => WindowId;

    protected virtual string HostTitleKey => "terminal_storage_title";

    protected virtual TerminalSize HostSize => _settings.Size;

    protected virtual int HostHeight => TerminalWindowMetrics.GetHeight(_rows);

    protected virtual float HostWidth => TerminalWindowMetrics.Width;

    protected virtual GameItem? PositionOwner => _terminal;

    protected virtual ICurrentTerminalStore CreateCurrentTerminalStore() => new TerminalTags(Terminal);

    protected virtual ITerminalFeatureData CreateFeatureData() => new TerminalFeatureData(Terminal, IsCurrentSession);

    protected virtual string HostLayerId => "digital_storage_terminal_viewport";

    protected virtual string HostShieldId => "terminal_native_input_shield";

    protected virtual bool EndDomainSessionOnClose => true;

    protected bool HasCurrentView => _currentView != null;

    protected virtual void OnHostOpened()
    {
    }

    protected virtual void OnHostClosed(bool actualClose)
    {
    }

    /// <summary>Runs once a newly built window is visible, when its layout can be measured.</summary>
    protected virtual void OnHostShown()
    {
    }

    protected virtual void SubscribeHostSources() => SubscribeSources();

    protected virtual bool SessionIsCurrent() => _session != null &&
        ReferenceEquals(_session, PlayerStore.Instance) && !SaveManager.IsDecoding;

    internal virtual ITerminalView CreateStorageView() => throw new InvalidOperationException("This window has no storage view.");

    /// <summary>Opens a page above the view or the top page.</summary>
    internal void PushScreen(SubScreen screen) => _host?.PushScreen(screen);

    /// <summary>Returns one page, as the back button does.</summary>
    internal void PopScreen() => _host?.PopScreen();

    /// <summary>Gets whether any page covers the view.</summary>
    internal bool InSubScreen => _host?.InSubScreen == true;

    /// <summary>Gets whether the left dock holds the back button of a screen.</summary>
    internal bool HasDockedBack(string screenId) => ScreenBackButton.IsRegistered(_registry, screenId);

    /// <summary>Gets whether <paramref name="screen"/> is the page on top.</summary>
    internal bool IsTopScreen(SubScreen screen) => _host != null && ReferenceEquals(_host.TopScreen, screen);

    /// <summary>Makes this window the one that opens for the terminal, in place of its native machinery window.</summary>
    internal void Attach(GameItem terminal)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        TerminalOpenRouter.Register(terminal, this);
        var native = terminal.contentWindow;
        native.openCallback += () =>
        {
            // The module bay cannot be drawn in two places; the native window takes it back.
            if (_pages.Any(page => page.Screen is TerminalModulesScreen)) _host?.PopToView();
            // Reached when the native window was shown without the patched OpenContentAction, for example by a
            // direct Show call (OverlayHandler.ShowInventory) or when the patch is missing. Show has already
            // finished, so hiding here still flashes for one frame.
            try
            {
                Open();
                native.Hide();
            }
            catch (Exception error)
            {
                // Show is still running; keep the native window so the click still opens something.
                WriteDiagnostic($"Digital Storage: terminal window failed to open ({error.GetType().Name}); native window kept");
            }
        };
    }

    /// <summary>Toggles the window as a native container does on a double click.</summary>
    internal void Toggle()
    {
        if (_window != null && _window.IsAlive && _window.IsVisible) _window.Close();
        else Open();
    }

    internal static void CloseCurrent() => CustomUIManager.Instance?.CloseWindow(WindowId);

    internal virtual void Open()
    {
        if (CustomUIManager.Instance == null) return;
        if (!Icons.Ready)
        {
            Icons.ShowUnavailable(Text);
            return;
        }
        if (_window != null && _window.IsAlive && _host != null)
        {
            _window.Show();
            _window.frame?.dragHandle?.MoveFront();
            return;
        }

        if (_window != null || _host != null) ClearControls();
        Build();
        if (_window == null || _host == null)
        {
            OnHostClosed(actualClose: true);
            return;
        }
        _session = PlayerStore.Instance;
        SubscribeHostSources();
        _host.Open();
        OnHostOpened();
        if (_window == null || _host == null) return;
        foreach (var dock in _docks) dock.RebuildNow();
        _window.Show();
        _window.frame?.dragHandle?.MoveFront();
        OnHostShown();
    }

    private void Build()
    {
        _diagnostics = new TerminalDiagnostics(_diagnosticsEnabled, WriteDiagnostic);
        _rows = CountRows();
        var builder = CustomUIManager.Instance.CreateWindow(HostWindowId, Text.Get(HostTitleKey),
            "overlay", null, CustomUIManager.Style.Machinery);
        if (builder == null) return;

        builder.SetDraggable(true).SetCloseOnEscape(false)
            .SetSize(HostWidth, HostHeight)
            .OnClose(ClearControls);
        builder.BeginColumn(spacing: 0f);
        _viewHost = builder.CurrentContainer;
        _viewHost.name = "view_host";
        builder.End();
        builder.BeginColumn(spacing: 0f);
        _subHost = builder.CurrentContainer;
        _subHost.name = "sub_screen_host";
        builder.End();

        _window = builder.GetWindow();
        _subHost.gameObject.AddComponent<SubScreenFitWatcher>().Initialize(_subHost, FitSubScreen);
        _subHost.gameObject.SetActive(false);
        AttachToNativeWindowLayer();
        WindowPositionMemory.Restore(PositionOwner, _window);
        WindowPositionMemory.Track(PositionOwner, _window);
        _shield = NativeWindowLayer.AttachInputShield(_window, HostShieldId);
        NativeWindowLayer.RestrictTerminalDrag(_window, _shield, IsWindowMoveArea);
        _diagnostics.Record("input_shield", "native_node=true placement_target=false bounds=window");

        var handler = new UniversalTerminalHandler(_registry, CreateCurrentTerminalStore(), WriteDiagnostic);
        var docks = new TerminalDocks(_registry, handler, WriteDiagnostic);
        _host = new TerminalHost(_registry, handler, docks, this, InstalledModules, _testViewEnabled, WriteDiagnostic, this,
            CreateFeatureData());
        var host = _host;
        foreach (var bar in docks.Bars)
            _docks.Add(DockView.Create(_window, Text, Icons, bar, () => host.Context, RefreshButtons));
    }

    internal bool IsWindowMoveArea(Vector2 point)
    {
        if (_window == null || _window.frame == null) return false;
        var canvas = _window.root.GetComponentInParent<Canvas>();
        var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var close = _window.frame.closeButton;
        if (close != null && RectTransformUtility.RectangleContainsScreenPoint(close.transform as RectTransform, point, camera))
            return false;
        var header = _window.frame.titleText?.transform.parent as RectTransform;
        if (header != null && RectTransformUtility.RectangleContainsScreenPoint(header, point, camera)) return true;
        var image = _window.root.GetComponent<Image>();
        if (image == null || image.sprite == null || image.type != Image.Type.Sliced ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(_window.Rect, point, camera, out var local)) return false;
        var rect = _window.Rect.rect;
        if (!rect.Contains(local)) return false;
        var border = image.sprite.border / image.pixelsPerUnit;
        return local.x < rect.xMin + border.x || local.x > rect.xMax - border.z ||
            local.y < rect.yMin + border.y || local.y > rect.yMax - border.w;
    }

    internal bool IsContentArea(Vector2 point)
    {
        if (_window == null || IsWindowMoveArea(point)) return false;
        var canvas = _window.root.GetComponentInParent<Canvas>();
        var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        return RectTransformUtility.RectangleContainsScreenPoint(_window.Rect, point, camera);
    }

    private int CountRows()
    {
        var canvas = RenderHandler.current?.windowRoot?.rectTransform.GetComponentInParent<Canvas>();
        return TerminalWindowMetrics.GetRows(HostSize, LetterboxManager.GameAreaHeight,
            canvas == null ? 0f : canvas.scaleFactor);
    }

    /// <summary>The back key returns one page first; only over the view does it close the window.</summary>
    private bool Back()
    {
        if (TopPage?.Screen is ITerminalBackHandler screenBack && screenBack.HandleBack()) return true;
        if (_currentView is ITerminalBackHandler viewBack && viewBack.HandleBack()) return true;
        if (_host?.HandleBack() == true) return true;
        if (!_registry.AllowsFinalBackClose()) return false;
        _window?.Close();
        return true;
    }

    private void AttachToNativeWindowLayer()
    {
        var native = RenderHandler.current;
        if (_window == null || native == null || native.windowRoot == null)
        {
            _diagnostics?.Record("layer_unavailable", "native_window_root=false");
            return;
        }

        // Raised, so the terminal and native windows share one layer and order by focus like native windows do. The
        // registered docks count as part of the window when it is fitted into the viewport, so they never end up off
        // screen sideways.
        if (NativeWindowLayer.Attach(_window, HostLayerId, Back, _diagnostics, raised: true,
                leftExtent: DockExtent(DockSide.Left), rightExtent: DockExtent(DockSide.Right)) == null) return;
        _diagnostics?.Record("drag_rebound", "native_canvas=true parent=private_viewport clamp=window_and_dock focus=viewport");
    }

    protected virtual void RebuildForSize()
    {
        if (_window == null || _host == null || !IsCurrentSession()) return;
        var position = _window.Rect.anchoredPosition;
        var terminal = _host.CurrentTerminal;
        var state = _currentView?.StoreState();
        _rebuilding = true;
        try { _window.Close(); }
        finally { _rebuilding = false; }
        Open();
        if (_window == null || _host == null) return;
        _window.Rect.anchoredPosition = position;
        if (_host.CurrentTerminal == terminal) _currentView?.RestoreState(state);
    }

    protected virtual void SubscribeSources()
    {
        _configSubscription?.Dispose();
        // The settings panel can switch the test view or change the sort while the window is open.
        _configSubscription = _subscribeNetwork == null ? null : _subscribeNetwork(change =>
        {
            if (change.Reason == NetworkChangeReason.ConfigChanged && IsCurrentSession()) _host?.Invalidate();
        });
        if (_terminal != null && ModuleBayHost.TryFind(_terminal, out var bay))
        {
            _bay = bay;
            _bay.onSlotAddItemEndFunc += _modulesChanged;
            _bay.onSlotRemoveItemEndFunc += _modulesChanged;
        }
        else
        {
            _diagnostics?.Record("module_bay", "subscribed=false reason=bay_not_found");
        }
    }

    private void OnModulesChanged()
    {
        if (_window == null || _closing || !IsCurrentSession()) return;
        StoragePipeline.Invalidate();
        _host?.Invalidate();
    }

    private IReadOnlyCollection<string> InstalledModules()
    {
        if (_moduleIdentifiers != null) return _moduleIdentifiers();
        try
        {
            return _bay == null
                ? Array.Empty<string>()
                : _bay.childItems.Where(item => item != null && !string.IsNullOrEmpty(item.identifier))
                    .Select(item => item.identifier).ToArray();
        }
        catch (Exception error)
        {
            _diagnostics?.Record("module_bay", $"read=failed error={error.GetType().Name}");
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public bool ShowTerminal(TerminalDefinition terminal, object? state)
    {
        if (_viewHost == null) return false;
        ITerminalView? view = null;
        try
        {
            view = terminal.CreateView(this) as ITerminalView;
            if (view == null)
            {
                _diagnostics?.Record("terminal_view", $"name={terminal.TerminalName} result=not_a_view");
                return false;
            }

            DisposeCurrentView();
            view.Build(_viewHost);
            NativeWindowLayer.RebindTerminalDrag(_window!);
            view.Resume();
            view.RestoreState(state);
            _currentView = view;
            _diagnostics?.Record("terminal_view", $"name={terminal.TerminalName} result=shown");
            return true;
        }
        catch (Exception error)
        {
            // The host falls back to the storage view, so a broken view never leaves the terminal unusable.
            _diagnostics?.Record("terminal_view", $"name={terminal.TerminalName} result=failed error={error.GetType().Name}");
            TryDispose(view);
            if (ReferenceEquals(_currentView, view)) _currentView = null;
            return false;
        }
    }

    /// <inheritdoc />
    public object? StoreState() => _currentView?.StoreState();

    /// <summary>
    /// Resizes the window so a machine body fits; the docks stay outside the frame. Layout measures only active objects,
    /// so this runs while the window is visible.
    /// </summary>
    internal void FitBodyTo(RectTransform content)
    {
        var window = CustomWindow;
        if (!content.gameObject.activeInHierarchy) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(window.Rect);
        var chrome = window.Rect.rect.size - window.ContentRoot.rect.size;
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        window.SetSize(LayoutUtility.GetPreferredWidth(content) + chrome.x, LayoutUtility.GetPreferredHeight(content) + chrome.y);
        LayoutRebuilder.ForceRebuildLayoutImmediate(window.Rect);
    }

    /// <inheritdoc />
    public IReadOnlyList<DisplayEntry> Selection => _currentView?.Selection ?? Array.Empty<DisplayEntry>();

    /// <inheritdoc />
    public void ClearSelection() => _currentView?.ClearSelection();

    /// <inheritdoc />
    public void ShowMessage(string textKey) => _currentView?.ShowMessage(textKey);

    /// <inheritdoc />
    public bool OpenScreen(ITerminalScreen screen, ITerminalScreen? covered)
    {
        if (_window == null || _viewHost == null || _subHost == null || screen is not SubScreen page) return false;
        var below = TopPage;
        if (below == null)
        {
            _currentView?.Suspend();
            _sizeBeforeSubScreen ??= _window.Rect.sizeDelta;
            _positionBeforeSubScreen ??= _window.Rect.anchoredPosition;
            _viewHost.gameObject.SetActive(false);
            _subHost.gameObject.SetActive(true);
        }
        else
        {
            below.Container.gameObject.SetActive(false);
            TryRun(below.Screen.OnCovered, "cover");
        }

        var entry = new ScreenPage(page, CreatePageContainer(_window, _subHost));
        _pages.Add(entry);
        NativeWindowLayer.RebindTerminalDrag(_window);
        var shown = false;
        try
        {
            shown = page.Show(entry.Container);
        }
        catch (Exception error)
        {
            _diagnostics?.Record("sub_screen", $"result=failed error={error.GetType().Name}");
        }

        if (shown)
        {
            FitSubScreen();
            return true;
        }

        // Back to what was shown before, with a borrowed native panel left in (or returned to) its native window.
        _diagnostics?.Record("sub_screen", $"type={page.GetType().Name} result=unavailable");
        RemovePage(entry);
        if (below != null)
        {
            below.Container.gameObject.SetActive(true);
            NativeWindowLayer.RebindTerminalDrag(_window);
            FitSubScreen();
        }
        else
        {
            ShowView();
            _currentView?.ShowMessage(page.UnavailableKey);
        }

        return false;
    }

    /// <inheritdoc />
    public void CloseScreen(ITerminalScreen screen, ITerminalScreen? revealed)
    {
        var entry = _pages.FirstOrDefault(page => ReferenceEquals(page.Screen, screen));
        if (entry != null) RemovePage(entry);
        if (_closing || _window == null || _viewHost == null || _subHost == null) return;
        var next = _pages.FirstOrDefault(page => ReferenceEquals(page.Screen, revealed));
        if (next == null)
        {
            ShowView();
            return;
        }

        next.Container.gameObject.SetActive(true);
        NativeWindowLayer.RebindTerminalDrag(_window);
        TryRun(next.Screen.OnRevealed, "reveal");
        FitSubScreen();
    }

    /// <summary>Rebuilds <paramref name="screen"/> when it is the page on top; false otherwise.</summary>
    protected bool RefreshScreenContent(SubScreen screen)
    {
        if (!IsTopScreen(screen) || _subHost == null) return false;
        screen.RefreshContent();
        FitSubScreen();
        return true;
    }

    internal void FitSubScreenContent(SubScreen screen)
    {
        if (ReferenceEquals(TopPage?.Screen, screen) && !_closing) FitSubScreen();
    }

    private ScreenPage? TopPage => _pages.Count == 0 ? null : _pages[_pages.Count - 1];

    private float DockExtent(DockSide side) =>
        _registry.FindDock(side) == null ? 0f : TerminalWindowMetrics.DockWidth + TerminalWindowMetrics.DockGap;

    private static RectTransform CreatePageContainer(CustomUIWindow window, RectTransform host)
    {
        var builder = new CustomUIBuilder(CustomUIManager.Instance, window, host);
        builder.BeginColumn(spacing: 0f);
        var container = builder.CurrentContainer;
        container.name = "sub_screen_page";
        builder.End();
        return container;
    }

    private void RemovePage(ScreenPage entry)
    {
        _pages.Remove(entry);
        TryRun(entry.Screen.Dispose, "dispose");
        if (entry.Container == null) return;
        // Inactive at once so the layout and the next fit ignore it; destroyed at the end of the frame.
        entry.Container.gameObject.SetActive(false);
        UnityEngine.Object.Destroy(entry.Container.gameObject);
    }

    /// <summary>Restores the view's size, position and controls once the last page is gone.</summary>
    private void ShowView()
    {
        if (_window == null || _viewHost == null || _subHost == null) return;
        if (_sizeBeforeSubScreen is { } size) _window.SetSize(size.x, size.y);
        if (_positionBeforeSubScreen is { } position) _window.Rect.anchoredPosition = position;
        _sizeBeforeSubScreen = null;
        _positionBeforeSubScreen = null;
        _subHost.gameObject.SetActive(false);
        _viewHost.gameObject.SetActive(true);
        NativeWindowLayer.RebindTerminalDrag(_window);
        _currentView?.OnReturnFromSubScreen();
    }

    private void TryRun(Action action, string stage)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            _diagnostics?.Record("sub_screen", $"{stage}=failed error={error.GetType().Name}");
        }
    }

    /// <summary>Measures the top page against the view's baseline size.</summary>
    private void FitSubScreen()
    {
        if (_window == null || _subHost == null) return;
        var baseline = _sizeBeforeSubScreen ?? _window.Rect.sizeDelta;
        _sizeBeforeSubScreen = baseline;
        _window.SetSize(baseline.x, baseline.y);
        // A refit starts from a window the previous fit enlarged; lay the frame out at the baseline first, or the content
        // root still reports the enlarged size and the frame chrome comes out too small or negative.
        LayoutRebuilder.ForceRebuildLayoutImmediate(_window.Rect);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_subHost);
        var content = _window.ContentRoot.rect.size;
        var needed = new Vector2(LayoutUtility.GetPreferredWidth(_subHost), LayoutUtility.GetPreferredHeight(_subHost));
        var chrome = Vector2.Max(Vector2.zero, _window.Rect.rect.size - content);
        var fits = TopPage?.Screen.FitsContent == true;
        var width = SubScreenMetrics.GetLength(baseline.x, needed.x, chrome.x, fits);
        var height = SubScreenMetrics.GetLength(baseline.y, needed.y, chrome.y, fits);
        _window.SetSize(width, height);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_window.Rect);
    }

    /// <inheritdoc />
    public void RefreshButtons()
    {
        foreach (var dock in _docks) dock.MarkDirty();
    }

    /// <inheritdoc />
    public void SettingChanged(string settingName)
    {
        if (settingName == TerminalStyleFeature.SettingName || settingName == "partition_view_size") RebuildForSize();
        else _currentView?.SettingChanged(settingName);
    }

    protected virtual bool IsCurrentSession()
    {
        if (SessionIsCurrent()) return true;
        // Closing returns the module bay first (ClearControls), so a load never finds it inside this window.
        _diagnostics?.Record("session", SaveManager.IsDecoding ? "decoding=true" : "changed=true");
        _window?.Close();
        return false;
    }

    private void DisposeCurrentView()
    {
        var view = _currentView;
        _currentView = null;
        TryDispose(view);
    }

    private void TryDispose(ITerminalView? view)
    {
        if (view == null) return;
        try
        {
            view.Suspend();
            view.Dispose();
        }
        catch (Exception error)
        {
            _diagnostics?.Record("terminal_view", $"dispose=failed error={error.GetType().Name}");
        }
    }

    /// <summary>
    /// Runs from <c>CustomUIWindow.Close</c> before the root is destroyed, and before every rebuild. Every close path
    /// (button, back key, session change, size rebuild, other windows closing it by id) ends here.
    /// </summary>
    private void ClearControls()
    {
        _closing = true;
        try
        {
            _host?.Close();
            // Close already released them; this covers a host that failed before getting there.
            for (var index = _pages.Count - 1; index >= 0; index--) RemovePage(_pages[index]);
            DisposeCurrentView();
            _configSubscription?.Dispose();
            _configSubscription = null;
            if (_bay != null)
            {
                _bay.onSlotAddItemEndFunc -= _modulesChanged;
                _bay.onSlotRemoveItemEndFunc -= _modulesChanged;
            }

            _bay = null;
            _sizeBeforeSubScreen = null;
            _positionBeforeSubScreen = null;
            NativeWindowLayer.Detach(_window);
            // Destroyed with the root at the end of the frame; until then their LateUpdate must not rebuild into them.
            foreach (var dock in _docks)
                if (dock != null) dock.enabled = false;
            _docks.Clear();
            _host = null;
            _viewHost = null;
            _subHost = null;
            _window = null;
            _shield = null;
            _session = null;
            _diagnostics?.Record("window_closed", "controls_cleared=true");
            OnHostClosed(!_rebuilding && EndDomainSessionOnClose);
            _diagnostics = null;
        }
        finally
        {
            _closing = false;
        }
    }

    private sealed class ScreenPage
    {
        internal ScreenPage(SubScreen screen, RectTransform container) => (Screen, Container) = (screen, container);

        internal SubScreen Screen { get; }

        internal RectTransform Container { get; }
    }

    // A borrowed native panel can settle its size a frame after the page was fitted, without any callback. While the
    // page is shown, refit whenever its measured size changes; the measure reads Unity's cached layout values.
    private sealed class SubScreenFitWatcher : MonoBehaviour
    {
        private RectTransform? _content;
        private Action? _fit;
        private Vector2 _fitted;

        internal void Initialize(RectTransform content, Action fit) => (_content, _fit) = (content, fit);

        private void OnEnable() => _fitted = Vector2.negativeInfinity;

        private void LateUpdate()
        {
            if (_content == null || _fit == null) return;
            var size = new Vector2(LayoutUtility.GetPreferredWidth(_content), LayoutUtility.GetPreferredHeight(_content));
            if (Mathf.Approximately(size.x, _fitted.x) && Mathf.Approximately(size.y, _fitted.y)) return;
            _fitted = size;
            _fit();
        }
    }
}
