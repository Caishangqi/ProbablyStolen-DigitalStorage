using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>A button in the registry with where and when it shows.</summary>
public sealed class RegisteredButton
{
    internal RegisteredButton(TerminalButton button, int order, ActionPlacement placement, string? terminalId,
        Func<TerminalContext, bool> isVisible)
    {
        Button = button;
        Order = order;
        Placement = placement;
        TerminalId = terminalId;
        IsVisible = isVisible;
    }

    /// <summary>Gets the button.</summary>
    public TerminalButton Button { get; }

    /// <summary>Gets the sort order within its group; ties sort by id.</summary>
    public int Order { get; }

    /// <summary>Gets where it shows.</summary>
    public ActionPlacement Placement { get; }

    /// <summary>Gets the terminal view it belongs to, or null for every view.</summary>
    public string? TerminalId { get; }

    /// <summary>Gets whether it shows in a context.</summary>
    public Func<TerminalContext, bool> IsVisible { get; }
}
