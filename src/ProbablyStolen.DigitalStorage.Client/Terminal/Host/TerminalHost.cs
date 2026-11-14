using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>
/// The view and sub-screen state of one open terminal window. Views and sub-screens only draw; switching between them,
/// saved view states and fallbacks are decided here.
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
    private readonly Dictionary<string, object?> _states = new(StringComparer.Ordinal);
    private bool _fallbackOnReturn;

    /// <summary>Creates the host of one terminal window.</summary>
    public TerminalHost(TerminalRegistry registry, UniversalTerminalHandler handler, VerticalButtonBar bar,
        ITerminalHostView view, Func<IReadOnlyCollection<string>> installedModules, Func<bool> testViewEnabled,
        Action<string> diagnostic, object window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        Bar = bar ?? throw new ArgumentNullException(nameof(bar));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _installedModules = installedModules ?? throw new ArgumentNullException(nameof(installedModules));
        _testViewEnabled = testViewEnabled ?? throw new ArgumentNullException(nameof(testViewEnabled));
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        CurrentTerminal = TerminalRegistry.StorageTerminalName;
    }

    /// <summary>Gets the rail of this window.</summary>
    public VerticalButtonBar Bar { get; }

    /// <summary>Gets the name of the shown view, or of the view under the open sub-screen.</summary>
    public string CurrentTerminal { get; private set; }

    /// <summary>Gets the open sub-screen, or null.</summary>
    public object? SubScreen { get; private set; }

    /// <summary>Gets whether a sub-screen replaces the view.</summary>
    public bool InSubScreen => SubScreen != null;

    /// <summary>Gets a fresh context for evaluating buttons and views.</summary>
    public TerminalContext Context => new(_installedModules(), _testViewEnabled(), CurrentTerminal, this, _window);

    /// <summary>Shows the stored view, or the storage view when it cannot show.</summary>
    public void Open()
    {
        _states.Clear();
        SubScreen = null;
        _fallbackOnReturn = false;
        Bar.CloseSelection();
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
        Bar.CloseSelection();
        _states.TryGetValue(terminalName, out var state);
        Show(terminal, state);
        _view.RefreshButtons();
    }

    /// <summary>Opens a sub-screen over the shown view. An open sub-screen returns first.</summary>
    public void SwitchToScreen(object subScreen)
    {
        if (subScreen == null) throw new ArgumentNullException(nameof(subScreen));
        if (InSubScreen) ReturnToParent();
        Bar.CloseSelection();
        SubScreen = subScreen;
        _view.OpenSubScreen(subScreen);
    }

    /// <summary>
    /// Closes the sub-screen and resumes the view under it. If that view became unavailable meanwhile,
    /// the storage view shows instead.
    /// </summary>
    public void ReturnToParent()
    {
        var subScreen = SubScreen;
        if (subScreen == null) return;
        SubScreen = null;
        _view.CloseSubScreen(subScreen);
        if (_fallbackOnReturn)
        {
            _fallbackOnReturn = false;
            Show(_registry.StorageTerminal, null);
        }

        _view.RefreshButtons();
    }

    /// <summary>Returns from a sub-screen and reports true, or reports false so the caller closes the window.</summary>
    public bool HandleBack()
    {
        if (!InSubScreen) return false;
        ReturnToParent();
        return true;
    }

    /// <summary>
    /// Re-evaluates after modules or settings change: an unavailable view falls back to the storage view without writing
    /// the stored view, so it comes back once available again. Under an open sub-screen the fallback waits for its
    /// return, so pulling a module in the modules page does not close the page.
    /// </summary>
    public void Invalidate()
    {
        if (CurrentTerminal != TerminalRegistry.StorageTerminalName && !_handler.HasTerminal(Context, CurrentTerminal))
        {
            _states.Remove(CurrentTerminal);
            if (InSubScreen)
            {
                _fallbackOnReturn = true;
                CurrentTerminal = TerminalRegistry.StorageTerminalName;
            }
            else
            {
                Show(_registry.StorageTerminal, null);
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

    /// <summary>Releases a sub-screen first, then forgets every view state.</summary>
    public void Close()
    {
        var subScreen = SubScreen;
        SubScreen = null;
        _fallbackOnReturn = false;
        Bar.CloseSelection();
        if (subScreen != null) _view.CloseSubScreen(subScreen);
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
        if (terminal.TerminalName == TerminalRegistry.StorageTerminalName) return;
        // Fall back without writing the stored view, so the player's choice survives a broken view.
        _states.Remove(terminal.TerminalName);
        if (_view.ShowTerminal(_registry.StorageTerminal, null))
            CurrentTerminal = TerminalRegistry.StorageTerminalName;
        else
            _diagnostic($"terminal_view build=failed name={TerminalRegistry.StorageTerminalName}");
    }
}
