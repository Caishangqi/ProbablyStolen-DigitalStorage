namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>The platform side of a terminal window that <see cref="TerminalHost"/> drives.</summary>
public interface ITerminalHostView
{
    /// <summary>
    /// Replaces the shown view with a new one built from <paramref name="terminal"/> and restores its saved state.
    /// Returns false, with nothing shown, when the view cannot be built.
    /// </summary>
    bool ShowTerminal(TerminalDefinition terminal, object? state);

    /// <summary>Gets the shown view's state, or null when it has none.</summary>
    object? StoreState();

    /// <summary>Suspends and hides the shown view and the rail, and shows the sub-screen.</summary>
    void OpenSubScreen(object subScreen);

    /// <summary>Destroys the sub-screen and resumes the hidden view and the rail.</summary>
    void CloseSubScreen(object subScreen);

    /// <summary>Rebuilds the rail and any open selection panel.</summary>
    void RefreshButtons();

    /// <summary>Lets the shown view react to a changed setting.</summary>
    void SettingChanged(string settingName);
}
