using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.TerminalStyle;
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
/// The terminal window, which replaces the terminal's native machinery window. It is the only lifecycle owner: the
/// body shows the current terminal view or a sub-screen, and the dock on the left shows the rail and selection panel,
/// so all of them open, move, come forward and close together. View and sub-screen switching is decided by
/// <see cref="TerminalHost"/>; this class carries it out on Unity objects.
/// </summary>
internal class TerminalWindowHost : ITerminalHostView, ITerminalViewHost
{
    internal const string WindowId = "digital_storage_terminal_storage";
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
    private DockView? _dock;
    private TerminalHost? _host;
    private ITerminalView? _currentView;
    private SubScreen? _subScreen;
    private GameGridInventory? _bay;
    private IDisposable? _configSubscription;
    private Vector2? _sizeBeforeSubScreen;
    private Vector2? _positionBeforeSubScreen;
    private bool _closing;
    private bool _rebuilding;

    internal TerminalWindowHost(TerminalRegistry registry, StorageTerminalServices storage, ITerminalViewSettings settings,
        Func<bool> testViewEnabled) : this(registry, storage.Text, settings, testViewEnabled, storage.DiagnosticsEnabled,
        storage.WriteDiagnostic, null, storage.SubscribeNetwork)
    {
    }

    internal TerminalWindowHost(TerminalRegistry registry, ModText text, ITerminalViewSettings settings,
        Func<bool> testViewEnabled, Func<bool> diagnosticsEnabled, Action<string> writeDiagnostic,
        Func<IReadOnlyCollection<string>>? installedModules,
        Func<Action<NetworkChanged>, IDisposable>? subscribeNetwork = null)
    {
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

    bool ITerminalViewHost.IsCurrentSession() => IsCurrentSession();

    /// <summary>Writes one diagnostic line to the mod log.</summary>
    internal void WriteDiagnostic(string message) => _writeDiagnostic(message);

    /// <summary>Creates the storage view for this window; registered as the storage terminal's view factory.</summary>
    protected virtual string HostWindowId => WindowId;

    protected virtual string HostTitleKey => "terminal_storage_title";

    protected virtual TerminalSize HostSize => _settings.Size;

    protected virtual int HostHeight => TerminalWindowMetrics.GetHeight(_rows);

    protected virtual GameItem? PositionOwner => _terminal;

    protected virtual ICurrentTerminalStore CreateCurrentTerminalStore() => new TerminalTags(Terminal);

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

    protected virtual void SubscribeHostSources() => SubscribeSources();

    protected virtual bool SessionIsCurrent() => _session != null &&
        ReferenceEquals(_session, PlayerStore.Instance) && !SaveManager.IsDecoding;

    internal virtual ITerminalView CreateStorageView() => throw new InvalidOperationException("This window has no storage view.");

    /// <summary>Returns from the open sub-screen to the view, as the back button does.</summary>
    internal void SwitchToScreen(SubScreen screen) => _host?.SwitchToScreen(screen);

    internal bool HasSubScreen => _host?.InSubScreen == true;

    internal void ReturnToParent() => _host?.ReturnToParent();

    /// <summary>Makes this window the one that opens for the terminal, in place of its native machinery window.</summary>
    internal void Attach(GameItem terminal)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        TerminalOpenRouter.Register(terminal, this);
        var native = terminal.contentWindow;
        native.openCallback += () =>
        {
            // The module bay cannot be drawn in two places; the native window takes it back.
            if (_subScreen is TerminalModulesScreen) ReturnToParent();
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
        _dock?.RebuildNow();
        _window.Show();
        _window.frame?.dragHandle?.MoveFront();
    }

    private void Build()
    {
        _diagnostics = new TerminalDiagnostics(_diagnosticsEnabled, WriteDiagnostic);
        _rows = CountRows();
        var builder = CustomUIManager.Instance.CreateWindow(HostWindowId, Text.Get(HostTitleKey),
            "overlay", null, CustomUIManager.Style.Machinery);
        if (builder == null) return;

        builder.SetDraggable(true).SetCloseOnEscape(false)
            .SetSize(TerminalWindowMetrics.Width, HostHeight)
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
        _subHost.gameObject.SetActive(false);
        AttachToNativeWindowLayer();
        WindowPositionMemory.Restore(PositionOwner, _window);
        WindowPositionMemory.Track(PositionOwner, _window);
        _shield = NativeWindowLayer.AttachInputShield(_window, HostShieldId);
        _diagnostics.Record("input_shield", "native_node=true placement_target=false bounds=window");

        var handler = new UniversalTerminalHandler(_registry, CreateCurrentTerminalStore(), WriteDiagnostic);
        var bar = new VerticalButtonBar(_registry, handler, WriteDiagnostic);
        _host = new TerminalHost(_registry, handler, bar, this, InstalledModules, _testViewEnabled, WriteDiagnostic, this);
        var host = _host;
        _dock = DockView.Create(_window, Text, bar, () => host.Context);
    }

    private int CountRows()
    {
        var canvas = RenderHandler.current?.windowRoot?.rectTransform.GetComponentInParent<Canvas>();
        return TerminalWindowMetrics.GetRows(HostSize, LetterboxManager.GameAreaHeight,
            canvas == null ? 0f : canvas.scaleFactor);
    }

    /// <summary>The back key returns from a sub-screen first; only then does it close the window.</summary>
    private void Back()
    {
        if (_subScreen is ITerminalBackHandler screenBack && screenBack.HandleBack()) return;
        if (_currentView is ITerminalBackHandler viewBack && viewBack.HandleBack()) return;
        if (_host?.HandleBack() != true) _window?.Close();
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
        // dock counts as part of the window when it is fitted into the viewport, so it never ends up off screen sideways.
        if (NativeWindowLayer.Attach(_window, HostLayerId, Back, _diagnostics, raised: true,
                leftExtent: TerminalWindowMetrics.DockWidth + TerminalWindowMetrics.DockGap) == null) return;
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
        if (_window != null && !_closing && IsCurrentSession()) _host?.Invalidate();
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

    /// <inheritdoc />
    public void OpenSubScreen(object subScreen)
    {
        if (_viewHost == null || _subHost == null) return;
        _currentView?.Suspend();
        if (_window != null)
        {
            _sizeBeforeSubScreen ??= _window.Rect.sizeDelta;
            _positionBeforeSubScreen ??= _window.Rect.anchoredPosition;
            _window.root.GetComponent<NativeWindowSizer>()?.SetLeftExtent(0f);
        }
        _viewHost.gameObject.SetActive(false);
        _dock?.SetShown(false);
        _subHost.gameObject.SetActive(true);
        var screen = subScreen as SubScreen;
        _subScreen = screen;
        var shown = false;
        try
        {
            shown = screen != null && screen.Show(_subHost);
        }
        catch (Exception error)
        {
            _diagnostics?.Record("sub_screen", $"result=failed error={error.GetType().Name}");
        }

        if (shown)
        {
            FitSubScreen();
            return;
        }

        // Back to the view, with the module panel left in (or returned to) the native machinery window.
        _diagnostics?.Record("sub_screen", $"type={subScreen.GetType().Name} result=unavailable");
        _host?.ReturnToParent();
        _currentView?.ShowMessage(screen?.UnavailableKey ?? SubScreen.DefaultUnavailableKey);
    }

    /// <inheritdoc />
    public void CloseSubScreen(object subScreen)
    {
        try
        {
            (subScreen as SubScreen)?.Dispose();
        }
        catch (Exception error)
        {
            _diagnostics?.Record("sub_screen", $"dispose=failed error={error.GetType().Name}");
        }

        _subScreen = null;
        if (_closing || _viewHost == null || _subHost == null) return;
        if (_sizeBeforeSubScreen is { } size && _window != null) _window.SetSize(size.x, size.y);
        if (_positionBeforeSubScreen is { } position && _window != null) _window.Rect.anchoredPosition = position;
        _sizeBeforeSubScreen = null;
        _positionBeforeSubScreen = null;
        _subHost.gameObject.SetActive(false);
        _viewHost.gameObject.SetActive(true);
        _dock?.SetShown(true);
        _window?.root.GetComponent<NativeWindowSizer>()?.SetLeftExtent(TerminalWindowMetrics.DockWidth + TerminalWindowMetrics.DockGap);
        _currentView?.OnReturnFromSubScreen();
    }

    protected bool RefreshSubScreenContent(SubScreen screen)
    {
        if (!ReferenceEquals(_subScreen, screen) || _subHost == null || !HasSubScreen) return false;
        screen.RefreshContent();
        FitSubScreen();
        return true;
    }

    internal void FitSubScreenContent(SubScreen screen)
    {
        if (ReferenceEquals(_subScreen, screen) && !_closing) FitSubScreen();
    }

    /// <summary>Measures the active page and restores the view's baseline when the page is closed.</summary>
    private void FitSubScreen()
    {
        if (_window == null || _subHost == null) return;
        var baseline = _sizeBeforeSubScreen ?? _window.Rect.sizeDelta;
        _sizeBeforeSubScreen = baseline;
        _window.SetSize(baseline.x, baseline.y);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_subHost);
        var content = _window.ContentRoot.rect.size;
        var needed = new Vector2(LayoutUtility.GetPreferredWidth(_subHost), LayoutUtility.GetPreferredHeight(_subHost));
        var chrome = _window.Rect.rect.size - content;
        var width = SubScreenMetrics.GetLength(baseline.x, needed.x, chrome.x, _subScreen?.FitsContent == true);
        var height = SubScreenMetrics.GetLength(baseline.y, needed.y, chrome.y, _subScreen?.FitsContent == true);
        _window.SetSize(width, height);
    }

    /// <inheritdoc />
    public void RefreshButtons() => _dock?.MarkDirty();

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
            // Close already disposed it; this covers a host that failed before getting there.
            _subScreen?.Dispose();
            _subScreen = null;
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
            // Destroyed with the root at the end of the frame; until then its LateUpdate must not rebuild into it.
            if (_dock != null) _dock.enabled = false;
            _dock = null;
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
}
