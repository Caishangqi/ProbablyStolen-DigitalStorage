using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>
/// Reads and writes the current terminal view of one terminal item. A fallback is never written back, so the stored
/// view returns once it is available again.
/// </summary>
public sealed class UniversalTerminalHandler
{
    private readonly TerminalRegistry _registry;
    private readonly ICurrentTerminalStore _store;
    private readonly Action<string> _diagnostic;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    /// <summary>Creates a handler for one terminal item.</summary>
    public UniversalTerminalHandler(TerminalRegistry registry, ICurrentTerminalStore store, Action<string> diagnostic)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    /// <summary>Gets the views available in a context, in registry order.</summary>
    public IReadOnlyList<TerminalDefinition> InstalledTerminals(TerminalContext context) =>
        _registry.Terminals.Where(terminal => IsAvailable(terminal, context)).ToArray();

    /// <summary>Gets whether a view is registered and available.</summary>
    public bool HasTerminal(TerminalContext context, string terminalName) =>
        _registry.Find(terminalName) is { } terminal && IsAvailable(terminal, context);

    /// <summary>
    /// Gets the stored view when it is registered and available, otherwise the default view. Never writes the store.
    /// </summary>
    public TerminalDefinition GetCurrentTerminal(TerminalContext context)
    {
        string? stored;
        try
        {
            stored = _store.Read();
        }
        catch (Exception error)
        {
            _diagnostic($"current_terminal read=failed error={error.GetType().Name}");
            return _registry.DefaultTerminal;
        }

        if (string.IsNullOrEmpty(stored)) return _registry.DefaultTerminal;
        var terminal = _registry.Find(stored!);
        return terminal != null && IsAvailable(terminal, context) ? terminal : _registry.DefaultTerminal;
    }

    /// <summary>Stores a view the player chose. Only the name is written.</summary>
    public void SetCurrentTerminal(TerminalDefinition terminal)
    {
        if (terminal == null) throw new ArgumentNullException(nameof(terminal));
        try
        {
            _store.Write(terminal.TerminalName);
        }
        catch (Exception error)
        {
            _diagnostic($"current_terminal write=failed error={error.GetType().Name}");
        }
    }

    private bool IsAvailable(TerminalDefinition terminal, TerminalContext context)
    {
        try
        {
            return terminal.IsAvailable(context);
        }
        catch (Exception error)
        {
            if (_reported.Add(terminal.TerminalName))
                _diagnostic($"terminal_available failed name={terminal.TerminalName} error={error.GetType().Name}");
            return false;
        }
    }
}
