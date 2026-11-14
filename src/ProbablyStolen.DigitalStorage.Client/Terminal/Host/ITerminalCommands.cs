namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>What buttons may ask of the terminal window that shows them.</summary>
public interface ITerminalCommands
{
    /// <summary>
    /// Replaces the shown view with a sub-screen until it returns; the object is a platform sub-screen.
    /// </summary>
    void SwitchToScreen(object subScreen);

    /// <summary>Saves the shown view's state so switching back restores it.</summary>
    void StoreState();

    /// <summary>Shows another terminal view in the same window.</summary>
    void Switch(string terminalName);

    /// <summary>Tells the window that a setting changed, so views and buttons update.</summary>
    void NotifySettingChanged(string settingName);
}
