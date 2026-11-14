using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>A read-only snapshot of one terminal window, taken each time buttons or views are evaluated.</summary>
public sealed class TerminalContext
{
    /// <summary>Creates a context.</summary>
    /// <param name="installedModules">The identifiers of the modules in the terminal's module bay.</param>
    /// <param name="testViewEnabled">Whether the diagnostic test view and its buttons may show; set by the test view option, not the diagnostics log option.</param>
    /// <param name="currentTerminal">The name of the shown terminal view.</param>
    /// <param name="commands">Host commands that buttons may call.</param>
    /// <param name="window">The platform window, passed through to view and sub-screen factories.</param>
    public TerminalContext(IReadOnlyCollection<string> installedModules, bool testViewEnabled, string currentTerminal,
        ITerminalCommands commands, object window)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        InstalledModules = installedModules ?? throw new ArgumentNullException(nameof(installedModules));
        TestViewEnabled = testViewEnabled;
        CurrentTerminal = currentTerminal ?? throw new ArgumentNullException(nameof(currentTerminal));
        Commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    /// <summary>Gets the identifiers of the installed modules.</summary>
    public IReadOnlyCollection<string> InstalledModules { get; }

    /// <summary>Gets whether the diagnostic test view and its buttons may show.</summary>
    public bool TestViewEnabled { get; }

    /// <summary>Gets the name of the shown terminal view.</summary>
    public string CurrentTerminal { get; }

    /// <summary>Gets the host commands.</summary>
    public ITerminalCommands Commands { get; }

    /// <summary>Gets the platform window; client code only passes it to platform factories.</summary>
    public object Window { get; }
}
