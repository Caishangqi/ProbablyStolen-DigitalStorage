using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>
/// Holds every terminal view and function button. Registration happens once at mod start, then <see cref="Freeze"/>
/// closes it. Internal to this mod; not part of the public API.
/// </summary>
public sealed class TerminalRegistry
{
    /// <summary>The name of the built-in storage view, the fallback whenever another view cannot show.</summary>
    public const string StorageTerminalName = "storage";

    private readonly IConfigManager _config;
    private readonly Action<string> _diagnostic;
    private readonly List<TerminalDefinition> _terminals = new();
    private readonly List<RegisteredButton> _buttons = new();
    private bool _frozen;

    /// <summary>Creates an empty registry.</summary>
    /// <param name="config">Stores the values of registered settings.</param>
    /// <param name="diagnostic">Receives refused registrations.</param>
    /// <param name="defaultTerminalName">The fallback view name.</param>
    public TerminalRegistry(IConfigManager config, Action<string> diagnostic, string defaultTerminalName = StorageTerminalName)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        if (string.IsNullOrWhiteSpace(defaultTerminalName)) throw new ArgumentException("A default terminal name is required.", nameof(defaultTerminalName));
        DefaultTerminalName = defaultTerminalName;
    }

    /// <summary>Gets the terminal used when the selected view is unavailable.</summary>
    public string DefaultTerminalName { get; }

    /// <summary>Gets the default view; available after <see cref="Freeze"/>.</summary>
    public TerminalDefinition DefaultTerminal =>
        Find(DefaultTerminalName) ?? throw new InvalidOperationException("The default terminal is not registered.");

    /// <summary>Gets the views by order, ties by name.</summary>
    public IReadOnlyList<TerminalDefinition> Terminals { get; private set; } = Array.Empty<TerminalDefinition>();

    /// <summary>Gets the buttons in registration order.</summary>
    public IReadOnlyList<RegisteredButton> Buttons => _buttons;

    /// <summary>Gets the storage view; available after <see cref="Freeze"/>.</summary>
    public TerminalDefinition StorageTerminal =>
        Find(StorageTerminalName) ?? throw new InvalidOperationException("The storage terminal is not registered.");

    /// <summary>Adds a view; a repeated name is refused and logged.</summary>
    public bool AddTerminal(TerminalDefinition definition)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        EnsureOpen();
        if (Find(definition.TerminalName) != null)
        {
            _diagnostic($"terminal_registry refused=terminal name={definition.TerminalName} reason=duplicate");
            return false;
        }

        _terminals.Add(definition);
        Terminals = _terminals.OrderBy(terminal => terminal.Order)
            .ThenBy(terminal => terminal.TerminalName, StringComparer.Ordinal).ToArray();
        return true;
    }

    /// <summary>Adds an action button; a repeated id is refused and logged.</summary>
    public bool AddAction(TerminalAction action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        return AddButton(new ActionButton(action), action.Order, action.Placement, action.TerminalId, action.IsVisible);
    }

    /// <summary>Adds a setting toggle; its id is the setting name. A repeated id is refused and logged.</summary>
    public bool AddSetting<T>(Setting<T> setting, int order, ActionPlacement placement, string? terminalId)
        where T : struct, Enum
    {
        if (setting == null) throw new ArgumentNullException(nameof(setting));
        return AddButton(new SettingToggleButton<T>(setting, _config), order, placement, terminalId, _ => true);
    }

    /// <summary>Closes registration. The default view must be registered by then.</summary>
    public void Freeze()
    {
        EnsureOpen();
        if (Find(DefaultTerminalName) == null)
            throw new InvalidOperationException("The default terminal must be registered before freezing.");
        _frozen = true;
    }

    /// <summary>Gets a view by name, or null.</summary>
    public TerminalDefinition? Find(string terminalName) =>
        _terminals.FirstOrDefault(terminal => string.Equals(terminal.TerminalName, terminalName, StringComparison.Ordinal));

    private bool AddButton(TerminalButton button, int order, ActionPlacement placement, string? terminalId,
        Func<TerminalContext, bool> isVisible)
    {
        EnsureOpen();
        if (_buttons.Any(entry => string.Equals(entry.Button.Id, button.Id, StringComparison.Ordinal)))
        {
            _diagnostic($"terminal_registry refused=button id={button.Id} reason=duplicate");
            return false;
        }

        _buttons.Add(new RegisteredButton(button, order, placement, terminalId, isVisible));
        return true;
    }

    private void EnsureOpen()
    {
        if (_frozen) throw new InvalidOperationException("The terminal registry is frozen.");
    }
}
