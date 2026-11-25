using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>
/// A registered function button, described as data so modules and terminal views can add their own.
/// </summary>
public sealed class TerminalAction
{
    /// <summary>Creates an action.</summary>
    /// <param name="id">The unique id.</param>
    /// <param name="order">The sort order within its group; ties sort by id.</param>
    /// <param name="side">The dock it shows on; that side must have a registered dock.</param>
    /// <param name="placement">Whether it shows on the dock directly or in the dock's selection panel.</param>
    /// <param name="scope">The views or sub-screen it applies to.</param>
    /// <param name="isVisible">Whether it shows in a context.</param>
    /// <param name="appearance">Its label and tooltip in a context.</param>
    /// <param name="onPress">Runs on press.</param>
    public TerminalAction(string id, int order, DockSide side, ActionPlacement placement, ActionScope scope,
        Func<TerminalContext, bool> isVisible, Func<TerminalContext, ButtonAppearance> appearance,
        Action<TerminalContext> onPress)
    {
        Id = string.IsNullOrEmpty(id) ? throw new ArgumentException("An action id is required.", nameof(id)) : id;
        Order = order;
        Side = side;
        Placement = placement;
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        IsVisible = isVisible ?? throw new ArgumentNullException(nameof(isVisible));
        Appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        OnPress = onPress ?? throw new ArgumentNullException(nameof(onPress));
    }

    /// <summary>Gets the unique id.</summary>
    public string Id { get; }

    /// <summary>Gets the sort order within its group.</summary>
    public int Order { get; }

    /// <summary>Gets the dock it shows on.</summary>
    public DockSide Side { get; }

    /// <summary>Gets where on the dock it shows.</summary>
    public ActionPlacement Placement { get; }

    /// <summary>Gets the views or sub-screen it applies to.</summary>
    public ActionScope Scope { get; }

    /// <summary>Gets whether it shows in a context.</summary>
    public Func<TerminalContext, bool> IsVisible { get; }

    /// <summary>Gets its label and tooltip in a context.</summary>
    public Func<TerminalContext, ButtonAppearance> Appearance { get; }

    /// <summary>Gets the press handler.</summary>
    public Action<TerminalContext> OnPress { get; }
}
