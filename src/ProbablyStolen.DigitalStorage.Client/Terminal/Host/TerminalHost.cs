using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>
/// The view and sub-screen state of one open terminal window. Views and sub-screens only draw; switching between them,
/// the sub-screen stack, saved view states and fallbacks are decided here.
/// </summary>
public sealed class TerminalHost : ITerminalCommands
{
    private readonly TerminalRegistry _registry;
    private readonly UniversalTerminalHandler _handler;
    private readonly ITerminalHostView _view;
    private readonly Func<IReadOnlyCollection<string>> _installedModules;
    private readonly Func<bool> _testViewEnabled;
    private readonly Action<string> _diagnostic;
    private readonly object _window;
    private readonly ITerminalFeatureData? _featureData;
    private readonly Dictionary<string, object?> _states = new(StringComparer.Ordinal);
    private readonly List<ITerminalScreen> _screens = new();
    private bool _fallbackOnReturn;

    /// <summary>Creates the host of one terminal window.</summary>
    public TerminalHost(TerminalRegistry registry, UniversalTerminalHandler handler, TerminalDocks docks,
        ITerminalHostView view, Func<IReadOnlyCollection<string>> installedModules, Func<bool> testViewEnabled,
        Action<string> diagnostic, object window, ITerminalFeatureData? featureData = null)
    {
        _featureData = featureData;
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        Docks = docks ?? throw new ArgumentNullException(nameof(docks));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _installedModules = installedModules ?? throw new ArgumentNullException(nameof(installedModules));
        _testViewEnabled = testViewEnabled ?? throw new ArgumentNullException(nameof(testViewEnabled));
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        CurrentTerminal = _registry.DefaultTerminalName;
    }

    /// <summary>Gets the docks of this window.</summary>
    public TerminalDocks Docks { get; }

    /// <summary>Gets the name of the shown view, or of the view under the open sub-screens.</summary>
    public string CurrentTerminal { get; private set; }

    /// <summary>Gets the top sub-screen, or null over a view.</summary>
    public ITerminalScreen? TopScreen => _screens.Count == 0 ? null : _screens[_screens.Count - 1];

    /// <summary>Gets the open sub-screens, bottom first.</summary>
    public IReadOnlyList<ITerminalScreen> Screens => _screens;

    /// <summary>Gets whether a sub-screen replaces the view.</summary>
    public bool InSubScreen => _screens.Count > 0;

    /// <summary>Gets a fresh context for evaluating buttons and views.</summary>
    public TerminalContext Context => new(_installedModules(), _testViewEnabled(), CurrentTerminal, this, _window,
        _featureData, TopScreen?.ScreenId, _screens.Count, InSubScreen ? null : _view.Selection);

    /// <summary>Shows the stored view, or the default view when it cannot show.</summary>
    public void Open()
    {
        _states.Clear();
        _screens.Clear();
        _fallbackOnReturn = false;
        Docks.CloseSelection();
        Show(_handler.GetCurrentTerminal(Context), null);
        _view.RefreshButtons();
    }

    /// <inheritdoc />
    public void StoreState()
    {
        if (InSubScreen) return;
        _states[CurrentTerminal] = _view.StoreState();
    }

    /// <summary>Shows another view in the same window with its saved state.</summary>
    public void Switch(string terminalName)
    {
        if (InSubScreen || terminalName == CurrentTerminal) return;
        var terminal = _registry.Find(terminalName);
        if (terminal == null || !_handler.HasTerminal(Context, terminalName)) return;
        Docks.CloseSelection();
        _states.TryGetValue(terminalName, out var state);
        Show(terminal, state);
        _view.RefreshButtons();
    }

    /// <inheritdoc />
    public void PushScreen(ITerminalScreen screen)
    {
        if (screen == null) throw new ArgumentNullException(nameof(screen));
        if (_screens.Contains(screen)) throw new InvalidOperationException("The sub-screen is already open.");
        Docks.CloseSelection();
        if (_view.OpenScreen(screen, TopScreen)) _screens.Add(screen);
        else _diagnostic($"terminal_screen open=failed id={screen.ScreenId}");
        _view.RefreshButtons();
    }

    /// <inheritdoc />
    public void PopScreen()
    {
        var screen = TopScreen;
        if (screen == null) return;
        _screens.RemoveAt(_screens.Count - 1);
        Docks.CloseSelection();
        _view.CloseScreen(screen, TopScreen);
        if (!InSubScreen && _fallbackOnReturn)
        {
            _fallbackOnReturn = false;
            Show(_registry.DefaultTerminal, null);
        }

        _view.RefreshButtons();
    }

    /// <inheritdoc />
    public void PopToView()
    {
        while (InSubScreen) PopScreen();
    }

    /// <inheritdoc />
    public void ClearSelection()
    {
        if (InSubScreen) return;
        _view.ClearSelection();
        _view.RefreshButtons();
    }

    /// <inheritdoc />
    public void ShowMessage(string textKey)
    {
        if (string.IsNullOrEmpty(textKey)) throw new ArgumentException("A message key is required.", nameof(textKey));
        if (!InSubScreen) _view.ShowMessage(textKey);
    }

    /// <summary>Returns one sub-screen and reports true, or reports false so the caller closes the window.</summary>
    public bool HandleBack()
    {
        if (!InSubScreen) return false;
        PopScreen();
        return true;
    }

    /// <summary>
    /// Re-evaluates after modules or settings change: an unavailable view falls back to the default view without writing
    /// the stored view, so it comes back once available again. Under open sub-screens the fallback waits until the last
    /// one returns, so pulling a module in the modules page does not close the page.
    /// </summary>
    public void Invalidate()
    {
        if (CurrentTerminal != _registry.DefaultTerminalName && !_handler.HasTerminal(Context, CurrentTerminal))
        {
            _states.Remove(CurrentTerminal);
            if (InSubScreen)
            {
                _fallbackOnReturn = true;
                CurrentTerminal = _registry.DefaultTerminalName;
            }
            else
            {
                Show(_registry.DefaultTerminal, null);
            }
        }

        _view.RefreshButtons();
    }

    /// <inheritdoc />
    public void NotifySettingChanged(string settingName)
    {
        _view.SettingChanged(settingName);
        _view.RefreshButtons();
    }

    /// <summary>Releases the sub-screens from the top down, then forgets every view state.</summary>
    public void Close()
    {
        _fallbackOnReturn = false;
        Docks.CloseSelection();
        while (InSubScreen)
        {
            var screen = TopScreen!;
            _screens.RemoveAt(_screens.Count - 1);
            _view.CloseScreen(screen, TopScreen);
        }

        _states.Clear();
    }

    private void Show(TerminalDefinition terminal, object? state)
    {
        if (_view.ShowTerminal(terminal, state))
        {
            CurrentTerminal = terminal.TerminalName;
            return;
        }

        _diagnostic($"terminal_view build=failed name={terminal.TerminalName}");
        if (terminal.TerminalName == _registry.DefaultTerminalName) return;
        // Fall back without writing the stored view, so the player's choice survives a broken view.
        _states.Remove(terminal.TerminalName);
        if (_view.ShowTerminal(_registry.DefaultTerminal, null))
            CurrentTerminal = _registry.DefaultTerminalName;
        else
            _diagnostic($"terminal_view build=failed name={_registry.DefaultTerminalName}");
    }
}
