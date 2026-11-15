using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

/// <summary>
/// Shows the current terminal view and opens the panel of available views.
/// </summary>
public sealed class TerminalSelectionButton : SelectionButton
{
    /// <summary>The button id.</summary>
    public const string ButtonId = "terminal_selection";

    /// <summary>The tooltip key; its argument is the current view's name.</summary>
    public const string TooltipKey = "terminal_button_current_tip";

    private readonly TerminalRegistry _registry;
    private readonly UniversalTerminalHandler _handler;

    /// <summary>Creates the button.</summary>
    public TerminalSelectionButton(VerticalButtonBar bar, TerminalRegistry registry, UniversalTerminalHandler handler)
        : base(ButtonId, bar)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <inheritdoc />
    public override ButtonAppearance GetAppearance(TerminalContext context)
    {
        var current = _registry.Find(context.CurrentTerminal) ?? _registry.StorageTerminal;
        return new ButtonAppearance(current.IconKey, TooltipKey, current.TranslationKey);
    }

    /// <inheritdoc />
    public override IReadOnlyList<ButtonState> GetEntries(TerminalContext context) =>
        _handler.InstalledTerminals(context).Select(terminal => new ButtonState(terminal.TerminalName,
            new ButtonAppearance(terminal.IconKey, terminal.TranslationKey),
            terminal.TerminalName == context.CurrentTerminal)).ToArray();

    /// <summary>
    /// Saves the shown view, closes the panel, stores the choice and switches; choosing the shown view only closes the
    /// panel.
    /// </summary>
    public override void Select(TerminalContext context, string entryId)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (entryId == context.CurrentTerminal || !_handler.HasTerminal(context, entryId))
        {
            Bar.CloseSelection();
            return;
        }

        context.Commands.StoreState();
        Bar.CloseSelection();
        _handler.SetCurrentTerminal(_registry.Find(entryId)!);
        context.Commands.Switch(entryId);
    }
}
