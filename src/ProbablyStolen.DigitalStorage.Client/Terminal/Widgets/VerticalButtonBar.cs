using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

/// <summary>
/// Decides which buttons the function rail shows and in what order, and which selection panel is open; the platform
/// draws what <see cref="GetButtons"/> returns.
/// </summary>
public sealed class VerticalButtonBar
{
    private readonly TerminalRegistry _registry;
    private readonly UniversalTerminalHandler _handler;
    private readonly Action<string> _diagnostic;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    /// <summary>Creates the bar of one terminal window.</summary>
    public VerticalButtonBar(TerminalRegistry registry, UniversalTerminalHandler handler, Action<string> diagnostic)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        ActionSelection = new ActionSelectionButton(this);
        TerminalSelection = new TerminalSelectionButton(this, registry, handler);
    }

    /// <summary>Gets the function selection button.</summary>
    public ActionSelectionButton ActionSelection { get; }

    /// <summary>Gets the current terminal button.</summary>
    public TerminalSelectionButton TerminalSelection { get; }

    /// <summary>Gets the selection button whose panel is open, or null.</summary>
    public SelectionButton? OpenSelection { get; private set; }

    /// <summary>
    /// Gets the rail in order: rail buttons for every view, then the shown view's rail buttons, each by order then id;
    /// then the function selection button when at least one picker button applies; then the current terminal button
    /// when at least two views are available.
    /// </summary>
    public IReadOnlyList<ButtonState> GetButtons(TerminalContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        var buttons = new List<ButtonState>();
        buttons.AddRange(Resolve(context, ActionPlacement.Rail, terminalId => terminalId == null));
        buttons.AddRange(Resolve(context, ActionPlacement.Rail, terminalId => terminalId == context.CurrentTerminal));
        if (GetPickerButtons(context).Count > 0) AddIfResolved(buttons, ActionSelection, context);
        else if (ReferenceEquals(OpenSelection, ActionSelection)) OpenSelection = null;
        if (_handler.InstalledTerminals(context).Count >= 2) AddIfResolved(buttons, TerminalSelection, context);
        else if (ReferenceEquals(OpenSelection, TerminalSelection)) OpenSelection = null;
        return buttons;
    }

    /// <summary>Gets the picker buttons that apply to the shown view, by order then id.</summary>
    public IReadOnlyList<ButtonState> GetPickerButtons(TerminalContext context) =>
        Resolve(context, ActionPlacement.Picker,
            terminalId => terminalId == null || terminalId == context.CurrentTerminal);

    /// <summary>Gets the entries of the open selection panel, or none.</summary>
    public IReadOnlyList<ButtonState> GetOpenEntries(TerminalContext context)
    {
        var open = OpenSelection;
        if (open == null) return Array.Empty<ButtonState>();
        try
        {
            return open.GetEntries(context);
        }
        catch (Exception error)
        {
            Report(open.Id, "entries", error);
            return Array.Empty<ButtonState>();
        }
    }

    /// <summary>Presses a rail button, a selection button or a picker button by id. A failing handler is logged.</summary>
    public void Press(TerminalContext context, string id, bool backwards)
    {
        var button = FindButton(id);
        if (button == null) return;
        try
        {
            button.OnPress(context, backwards);
        }
        catch (Exception error)
        {
            // A broken module button must not take the terminal down: the click is dropped, the rest keeps working.
            Report(id, "press", error);
        }
    }

    /// <summary>Selects an entry in the open panel. A failing handler is logged.</summary>
    public void Select(TerminalContext context, string entryId)
    {
        var open = OpenSelection;
        if (open == null) return;
        try
        {
            open.Select(context, entryId);
        }
        catch (Exception error)
        {
            CloseSelection();
            Report(open.Id, "select", error);
        }
    }

    /// <summary>Opens the button's panel, closing any other, or closes it when it is open.</summary>
    public void Toggle(SelectionButton button)
    {
        if (button == null) throw new ArgumentNullException(nameof(button));
        OpenSelection = ReferenceEquals(OpenSelection, button) ? null : button;
    }

    /// <summary>Closes the open panel.</summary>
    public void CloseSelection() => OpenSelection = null;

    private TerminalButton? FindButton(string id)
    {
        if (id == ActionSelection.Id) return ActionSelection;
        if (id == TerminalSelection.Id) return TerminalSelection;
        return _registry.Buttons.FirstOrDefault(entry => entry.Button.Id == id)?.Button;
    }

    private IReadOnlyList<ButtonState> Resolve(TerminalContext context, ActionPlacement placement,
        Func<string?, bool> inScope)
    {
        var result = new List<ButtonState>();
        var entries = _registry.Buttons
            .Where(entry => entry.Placement == placement && inScope(entry.TerminalId))
            .OrderBy(entry => entry.Order).ThenBy(entry => entry.Button.Id, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!IsVisible(entry, context)) continue;
            AddIfResolved(result, entry.Button, context);
        }

        return result;
    }

    private bool IsVisible(RegisteredButton entry, TerminalContext context)
    {
        try
        {
            return entry.IsVisible(context);
        }
        catch (Exception error)
        {
            Report(entry.Button.Id, "visible", error);
            return false;
        }
    }

    private void AddIfResolved(List<ButtonState> buttons, TerminalButton button, TerminalContext context)
    {
        try
        {
            buttons.Add(new ButtonState(button.Id, button.GetAppearance(context),
                button is SelectionButton selection && selection.MenuOpen));
        }
        catch (Exception error)
        {
            Report(button.Id, "appearance", error);
        }
    }

    private void Report(string id, string stage, Exception error)
    {
        // Once per button and stage while this window lives, so a broken button does not flood the log each frame.
        if (_reported.Add(id + "/" + stage))
            _diagnostic($"terminal_button failed id={id} stage={stage} error={error.GetType().Name}");
    }
}
