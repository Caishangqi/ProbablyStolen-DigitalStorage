using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

/// <summary>
/// Runs a <see cref="TerminalAction"/> on press, so modules can add their own actions.
/// </summary>
public sealed class ActionButton : TerminalButton
{
    /// <summary>Creates a button for an action.</summary>
    public ActionButton(TerminalAction action) : base(action?.Id ?? string.Empty)
    {
        Action = action ?? throw new ArgumentNullException(nameof(action));
    }

    /// <summary>Gets the action.</summary>
    public TerminalAction Action { get; }

    /// <inheritdoc />
    public override ButtonAppearance GetAppearance(TerminalContext context) => Action.Appearance(context);

    /// <inheritdoc />
    public override void OnPress(TerminalContext context, bool backwards) => Action.OnPress(context);
}
