using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

/// <summary>
/// Decides which buttons one dock shows and in what order; the platform draws what <see cref="GetButtons"/> returns.
/// The open selection panel is shared with the other side through <see cref="TerminalDocks"/>.
/// </summary>
public sealed class VerticalButtonBar
{
    private readonly TerminalRegistry _registry;
    private readonly UniversalTerminalHandler _handler;
    private readonly TerminalDocks _docks;
    private readonly Action<string> _diagnostic;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    internal VerticalButtonBar(TerminalRegistry registry, UniversalTerminalHandler handler, DockDefinition dock,
        TerminalDocks docks, Action<string> diagnostic)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        Dock = dock ?? throw new ArgumentNullException(nameof(dock));
        _docks = docks ?? throw new ArgumentNullException(nameof(docks));
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        ActionSelection = new ActionSelectionButton(this);
        TerminalSelection = dock.Side == DockSide.Left ? new TerminalSelectionButton(this, registry, handler) : null;
    }

    /// <summary>Gets the dock this bar draws.</summary>
    public DockDefinition Dock { get; }

    /// <summary>Gets the title key the dock shows in a context.</summary>
    public string TitleKey(TerminalContext context) => _registry.DockTitleKey(Dock, context);

    /// <summary>Gets the side of the dock.</summary>
    public DockSide Side => Dock.Side;

    /// <summary>Gets the function selection button of this dock.</summary>
    public ActionSelectionButton ActionSelection { get; }

    /// <summary>Gets the current terminal button; only the left dock has one.</summary>
    public TerminalSelectionButton? TerminalSelection { get; }

    /// <summary>Gets this bar's selection button whose panel is open, or null.</summary>
    public SelectionButton? OpenSelection =>
        _docks.OpenSelection is { } open && ReferenceEquals(open.Bar, this) ? open : null;

    /// <summary>
    /// Gets the dock in order: shared rail buttons, then the rail buttons of the shown view or top sub-screen, each by
    /// order then id; then the function selection button when at least one picker button applies; then, on the left
    /// dock over a view, the current terminal button when at least two views are available. An empty list means the
    /// dock collapses.
    /// </summary>
    public IReadOnlyList<ButtonState> GetButtons(TerminalContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        var buttons = new List<ButtonState>();
        buttons.AddRange(Resolve(context, ActionPlacement.Rail, shared: true));
        buttons.AddRange(Resolve(context, ActionPlacement.Rail, shared: false));
        if (GetPickerButtons(context).Count > 0) AddIfResolved(buttons, ActionSelection, context);
        else if (ReferenceEquals(OpenSelection, ActionSelection)) CloseSelection();
        if (TerminalSelection != null)
        {
            if (context.ScreenId == null && _handler.InstalledTerminals(context).Count >= 2)
                AddIfResolved(buttons, TerminalSelection, context);
            else if (ReferenceEquals(OpenSelection, TerminalSelection)) CloseSelection();
        }

        return buttons;
    }

    /// <summary>Gets this dock's picker buttons that apply in the context, by order then id.</summary>
    public IReadOnlyList<ButtonState> GetPickerButtons(TerminalContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        var result = new List<ButtonState>(Resolve(context, ActionPlacement.Picker, shared: true));
        result.AddRange(Resolve(context, ActionPlacement.Picker, shared: false));
        return result;
    }

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

    /// <summary>Opens the button's panel, closing any other on either side, or closes it when it is open.</summary>
    public void Toggle(SelectionButton button) => _docks.Toggle(button);

    /// <summary>Closes the open panel on either side.</summary>
    public void CloseSelection() => _docks.CloseSelection();

    private TerminalButton? FindButton(string id)
    {
        if (id == ActionSelection.Id) return ActionSelection;
        if (TerminalSelection != null && id == TerminalSelection.Id) return TerminalSelection;
        return _registry.Buttons.FirstOrDefault(entry => entry.Side == Side && entry.Button.Id == id)?.Button;
    }

    private IReadOnlyList<ButtonState> Resolve(TerminalContext context, ActionPlacement placement, bool shared)
    {
        var result = new List<ButtonState>();
        var entries = _registry.Buttons
            .Where(entry => entry.Side == Side && entry.Placement == placement && entry.Scope.IsShared == shared &&
                entry.Scope.Includes(context))
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
