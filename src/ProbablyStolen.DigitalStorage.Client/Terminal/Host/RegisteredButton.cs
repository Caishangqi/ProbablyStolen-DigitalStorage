using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>A button in the registry with where and when it shows.</summary>
public sealed class RegisteredButton
{
    internal RegisteredButton(TerminalButton button, int order, DockSide side, ActionPlacement placement,
        ActionScope scope, Func<TerminalContext, bool> isVisible)
    {
        Button = button;
        Order = order;
        Side = side;
        Placement = placement;
        Scope = scope;
        IsVisible = isVisible;
    }

    /// <summary>Gets the button.</summary>
    public TerminalButton Button { get; }

    /// <summary>Gets the sort order within its group; ties sort by id.</summary>
    public int Order { get; }

    /// <summary>Gets the dock it shows on.</summary>
    public DockSide Side { get; }

    /// <summary>Gets where on the dock it shows.</summary>
    public ActionPlacement Placement { get; }

    /// <summary>Gets the views or sub-screen it applies to.</summary>
    public ActionScope Scope { get; }

    /// <summary>Gets whether it shows in a context.</summary>
    public Func<TerminalContext, bool> IsVisible { get; }
}
