namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>What buttons may ask of the terminal window that shows them.</summary>
public interface ITerminalCommands
{
    /// <summary>Opens a sub-screen above the shown view or the top sub-screen.</summary>
    void PushScreen(ITerminalScreen screen);

    /// <summary>Closes the top sub-screen and shows the page or view under it.</summary>
    void PopScreen();

    /// <summary>Closes every sub-screen and shows the view.</summary>
    void PopToView();

    /// <summary>Clears the shown view's selection, so actions that read it hide.</summary>
    void ClearSelection();

    /// <summary>Shows a one-off message on the shown view, on its status line where it has one.</summary>
    void ShowMessage(string textKey);

    /// <summary>Saves the shown view's state so switching back restores it.</summary>
    void StoreState();

    /// <summary>Shows another terminal view in the same window.</summary>
    void Switch(string terminalName);

    /// <summary>Tells the window that a setting changed, so views and buttons update.</summary>
    void NotifySettingChanged(string settingName);
}
