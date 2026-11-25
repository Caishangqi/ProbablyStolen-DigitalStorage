using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>A read-only snapshot of one terminal window, taken each time buttons or views are evaluated.</summary>
public sealed class TerminalContext
{
    /// <summary>Creates a context.</summary>
    /// <param name="installedModules">The identifiers of the modules in the terminal's module bay.</param>
    /// <param name="testViewEnabled">Whether the diagnostic test view and its buttons may show; set by the test view option, not the diagnostics log option.</param>
    /// <param name="currentTerminal">The name of the shown terminal view, or of the view under the open sub-screens.</param>
    /// <param name="commands">Host commands that buttons may call.</param>
    /// <param name="window">The platform window, passed through to view and sub-screen factories.</param>
    /// <param name="featureData">Values stored on the terminal item, or null when the window has no terminal item.</param>
    /// <param name="screenId">The id of the top sub-screen, or null over a view.</param>
    /// <param name="screenDepth">The number of open sub-screens.</param>
    /// <param name="selection">The slots the shown view has selected, or null for none.</param>
    public TerminalContext(IReadOnlyCollection<string> installedModules, bool testViewEnabled, string currentTerminal,
        ITerminalCommands commands, object window, ITerminalFeatureData? featureData = null, string? screenId = null,
        int screenDepth = 0, IReadOnlyList<DisplayEntry>? selection = null)
    {
        if (screenDepth < 0) throw new ArgumentOutOfRangeException(nameof(screenDepth));
        if ((screenId == null) != (screenDepth == 0))
            throw new ArgumentException("A screen id is given exactly when a sub-screen is open.", nameof(screenId));
        FeatureData = featureData ?? NoTerminalFeatureData.Instance;
        Window = window ?? throw new ArgumentNullException(nameof(window));
        InstalledModules = installedModules ?? throw new ArgumentNullException(nameof(installedModules));
        TestViewEnabled = testViewEnabled;
        CurrentTerminal = currentTerminal ?? throw new ArgumentNullException(nameof(currentTerminal));
        Commands = commands ?? throw new ArgumentNullException(nameof(commands));
        ScreenId = screenId;
        ScreenDepth = screenDepth;
        Selection = screenDepth == 0 && selection != null ? selection : Array.Empty<DisplayEntry>();
    }

    /// <summary>Gets the identifiers of the installed modules.</summary>
    public IReadOnlyCollection<string> InstalledModules { get; }

    /// <summary>Gets whether the diagnostic test view and its buttons may show.</summary>
    public bool TestViewEnabled { get; }

    /// <summary>Gets the name of the shown terminal view, or of the view under the open sub-screens.</summary>
    public string CurrentTerminal { get; }

    /// <summary>Gets the host commands.</summary>
    public ITerminalCommands Commands { get; }

    /// <summary>Gets the values stored on the terminal item.</summary>
    public ITerminalFeatureData FeatureData { get; }

    /// <summary>Gets the platform window; client code only passes it to platform factories.</summary>
    public object Window { get; }

    /// <summary>Gets the id of the top sub-screen, or null over a view.</summary>
    public string? ScreenId { get; }

    /// <summary>Gets the number of open sub-screens; 0 over a view.</summary>
    public int ScreenDepth { get; }

    /// <summary>
    /// Gets the slots the shown view keeps selected. Empty on a sub-screen, during a drag, and in views without a
    /// selection.
    /// </summary>
    public IReadOnlyList<DisplayEntry> Selection { get; }
}
