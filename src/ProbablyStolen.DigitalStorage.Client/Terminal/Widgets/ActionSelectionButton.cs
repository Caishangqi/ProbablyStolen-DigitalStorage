using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

/// <summary>
/// Opens the panel of the dock's <see cref="ActionPlacement.Picker"/> buttons, the same way
/// <see cref="TerminalSelectionButton"/> opens the panel of views.
/// </summary>
public sealed class ActionSelectionButton : SelectionButton
{
    /// <summary>The button id on the left dock.</summary>
    public const string ButtonId = "action_selection";

    /// <summary>The icon resource key.</summary>
    public const string IconKey = "button_placeholder";

    /// <summary>The tooltip key.</summary>
    public const string TooltipKey = "terminal_button_functions_tip";

    /// <summary>Creates the button.</summary>
    public ActionSelectionButton(VerticalButtonBar bar) : base(IdFor(bar?.Side ?? DockSide.Left), bar!)
    {
    }

    /// <summary>Gets the button id on a dock side; each side has its own so both can show at once.</summary>
    public static string IdFor(DockSide side) => side == DockSide.Left ? ButtonId : ButtonId + "_right";

    /// <inheritdoc />
    public override ButtonAppearance GetAppearance(TerminalContext context) => new(IconKey, TooltipKey);

    /// <inheritdoc />
    public override IReadOnlyList<ButtonState> GetEntries(TerminalContext context) => Bar.GetPickerButtons(context);

    /// <summary>Closes the panel, then runs the chosen button.</summary>
    public override void Select(TerminalContext context, string entryId)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        Bar.CloseSelection();
        Bar.Press(context, entryId, backwards: false);
    }
}
