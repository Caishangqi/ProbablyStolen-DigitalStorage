using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

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

    /// <summary>Gets the shown view's kept selection; empty when it has none.</summary>
    IReadOnlyList<DisplayEntry> Selection { get; }

    /// <summary>Clears the shown view's kept selection.</summary>
    void ClearSelection();

    /// <summary>
    /// Shows <paramref name="screen"/> above <paramref name="covered"/>, the page it hides, or above the view when
    /// null. A covered page keeps its controls and state. Returns false when the screen cannot be shown; the platform
    /// has then released it and left the covered page or view shown.
    /// </summary>
    bool OpenScreen(ITerminalScreen screen, ITerminalScreen? covered);

    /// <summary>
    /// Releases <paramref name="screen"/> and shows <paramref name="revealed"/>, the page under it, or resumes the view
    /// when null.
    /// </summary>
    void CloseScreen(ITerminalScreen screen, ITerminalScreen? revealed);

    /// <summary>Shows a one-off message on the shown view.</summary>
    void ShowMessage(string textKey);

    /// <summary>Rebuilds the docks and any open selection panel.</summary>
    void RefreshButtons();

    /// <summary>Lets the shown view react to a changed setting.</summary>
    void SettingChanged(string settingName);
}
