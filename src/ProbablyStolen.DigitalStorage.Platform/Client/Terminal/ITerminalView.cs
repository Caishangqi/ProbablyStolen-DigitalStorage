using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

/// <summary>
/// The body content of a terminal window: one terminal view (storage, diagnostics, later module views). A view is
/// created for one window, built once into the body container, and suspended while hidden behind another view or a
/// sub-screen: a hidden grid keeps its geometry, so its hover, wheel, drop and polling must stop answering.
/// </summary>
internal interface ITerminalView
{
    /// <summary>Builds the controls into the body container. Called once, before the first <see cref="Resume"/>.</summary>
    void Build(RectTransform container);

    /// <summary>Starts or restarts the view: subscribes to its sources and rereads them, then answers input.</summary>
    void Resume();

    /// <summary>Stops answering input and hides anything the view shows over other windows.</summary>
    void Suspend();

    /// <summary>
    /// Called instead of <see cref="Resume"/> when a sub-screen returns to this view.
    /// </summary>
    void OnReturnFromSubScreen();

    /// <summary>Captures what a rebuilt view needs to look the same, or null.</summary>
    object? StoreState();

    /// <summary>Applies a state from <see cref="StoreState"/> of the same view type; other values are ignored.</summary>
    void RestoreState(object? state);

    /// <summary>A rail setting changed; the view applies the ones it shows.</summary>
    void SettingChanged(string settingName);

    /// <summary>Shows a one-off message, on the status line where the view has one; other views ignore it.</summary>
    void ShowMessage(string textKey);

    /// <summary>Removes the view's controls and handlers. Safe to call while the window is closing.</summary>
    void Dispose();
}

/// <summary>The window services a terminal view uses; the terminal window implements it.</summary>
internal interface ITerminalViewHost
{
    /// <summary>The window the view is built in.</summary>
    CustomUIWindow Window { get; }

    /// <summary>The native input shield behind the window, used to test whether the pointer is over it.</summary>
    TreeNodeRender? Shield { get; }

    /// <summary>The window's diagnostics, or null when none were created.</summary>
    TerminalDiagnostics? Diagnostics { get; }

    /// <summary>The grid rows for the current terminal size.</summary>
    int Rows { get; }

    /// <summary>Whether the window still belongs to the loaded game; closes the window when it does not.</summary>
    bool IsCurrentSession();

    /// <summary>The window's button and overlay images.</summary>
    ButtonIcons Icons { get; }

    /// <summary>The storage extensions of this window.</summary>
    ProbablyStolen.DigitalStorage.Client.Terminal.Extensions.StoragePresentationPipeline Pipeline { get; }

    /// <summary>A fresh context of the window, or null while it is not open.</summary>
    ProbablyStolen.DigitalStorage.Client.Terminal.Host.TerminalContext? Context { get; }
}
