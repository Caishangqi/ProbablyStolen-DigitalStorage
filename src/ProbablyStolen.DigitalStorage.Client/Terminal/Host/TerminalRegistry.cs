using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Registries;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
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
    private readonly Registry<TerminalDefinition> _terminals = new(Identifier.Parse("digital_storage:terminals"));
    private readonly Registry<RegisteredButton> _buttonRegistry = new(Identifier.Parse("digital_storage:terminal_buttons"));
    private readonly List<IStorageEntryProjector> _projectors = new();
    private readonly List<ISlotDecorator> _decorators = new();
    private readonly List<IEntryTooltipContributor> _contributors = new();
    private readonly List<TooltipPanelDefinition> _panels = new() { TooltipPanelDefinition.Hint };
    private readonly HashSet<string> _extensionIds = new(StringComparer.Ordinal);
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
    public IReadOnlyList<RegisteredButton> Buttons => _buttonRegistry.RegistrationEntries.Select(entry => entry.Value).ToArray();

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

        _terminals.Register(RegistryKey.For(definition.TerminalName), definition);
        Terminals = _terminals.Entries.Select(entry => entry.Value).OrderBy(terminal => terminal.Order)
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

    /// <summary>Adds a storage view projector; a repeated id is refused and logged.</summary>
    public bool AddStorageProjector(IStorageEntryProjector projector) =>
        AddExtension(projector ?? throw new ArgumentNullException(nameof(projector)), projector.Id, _projectors);

    /// <summary>Adds a storage slot decorator; a repeated id is refused and logged.</summary>
    public bool AddSlotDecorator(ISlotDecorator decorator) =>
        AddExtension(decorator ?? throw new ArgumentNullException(nameof(decorator)), decorator.Id, _decorators);

    /// <summary>Adds a storage tooltip contributor; a repeated id is refused and logged.</summary>
    public bool AddTooltipContributor(IEntryTooltipContributor contributor) =>
        AddExtension(contributor ?? throw new ArgumentNullException(nameof(contributor)), contributor.Id, _contributors);

    /// <summary>
    /// Adds a tooltip panel next to the native tooltip; a repeated id is refused and logged. The storage view's own
    /// panel is always registered.
    /// </summary>
    public bool AddTooltipPanel(TooltipPanelDefinition panel)
    {
        if (panel == null) throw new ArgumentNullException(nameof(panel));
        EnsureOpen();
        if (_panels.Any(existing => existing.Id == panel.Id))
        {
            _diagnostic($"terminal_registry refused=tooltip_panel id={panel.Id} reason=duplicate");
            return false;
        }

        _panels.Add(panel);
        return true;
    }

    /// <summary>Gets the registered tooltip panels, the storage view's own first.</summary>
    public IReadOnlyList<TooltipPanelDefinition> TooltipPanels => _panels;

    /// <summary>Gets the registered projectors.</summary>
    public IReadOnlyList<IStorageEntryProjector> StorageProjectors => _projectors;

    /// <summary>Creates the storage pipeline of one window over the registered extensions.</summary>
    public StoragePresentationPipeline CreateStoragePipeline(Action<string> diagnostic) =>
        new(_projectors, _decorators, _contributors, _panels,
            diagnostic ?? throw new ArgumentNullException(nameof(diagnostic)));

    /// <summary>Closes registration. The default view must be registered by then.</summary>
    public void Freeze()
    {
        EnsureOpen();
        if (Find(DefaultTerminalName) == null)
            throw new InvalidOperationException("The default terminal must be registered before freezing.");
        _terminals.Freeze();
        _buttonRegistry.Freeze();
        _frozen = true;
    }

    /// <summary>Gets a view by name, or null.</summary>
    public TerminalDefinition? Find(string terminalName) =>
        terminalName == null ? null : _terminals.GetValueOrDefault(RegistryKey.For(terminalName));

    private bool AddButton(TerminalButton button, int order, ActionPlacement placement, string? terminalId,
        Func<TerminalContext, bool> isVisible)
    {
        EnsureOpen();
        var key = RegistryKey.For(button.Id);
        if (_buttonRegistry.TryGetValue(key, out _))
        {
            _diagnostic($"terminal_registry refused=button id={button.Id} reason=duplicate");
            return false;
        }

        var entry = new RegisteredButton(button, order, placement, terminalId, isVisible);
        _buttonRegistry.Register(key, entry);
        return true;
    }

    private bool AddExtension<T>(T extension, string id, List<T> target)
    {
        EnsureOpen();
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("An extension id is required.", nameof(extension));
        if (!_extensionIds.Add(id))
        {
            _diagnostic($"terminal_registry refused=extension id={id} reason=duplicate");
            return false;
        }

        target.Add(extension);
        return true;
    }

    private void EnsureOpen()
    {
        if (_frozen) throw new InvalidOperationException("The terminal registry is frozen.");
    }
}
