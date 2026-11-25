using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.KeepOpen;

/// <summary>Registers the persistent storage terminal keep-open control and its final-close guard.</summary>
public static class KeepOpenFeature
{
    /// <summary>The rail action identifier.</summary>
    public const string ActionId = "keep_open";

    /// <summary>The stable final-close guard identifier.</summary>
    public const string GuardId = "terminal_keep_open";

    /// <summary>Registers the window-level control and guard.</summary>
    public static void Register(TerminalRegistry registry, Func<bool> getValue, Action<bool> setValue)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (getValue == null) throw new ArgumentNullException(nameof(getValue));
        if (setValue == null) throw new ArgumentNullException(nameof(setValue));

        registry.AddFinalBackGuard(GuardId, () => !getValue());
        registry.AddAction(new TerminalAction(ActionId, 20, DockSide.Left, ActionPlacement.Rail, ActionScope.AllViews, _ => true,
            _ => new ButtonAppearance(getValue() ? "button_keep_open_on" : "button_keep_open_off",
                "terminal_keep_open_tip", getValue() ? "terminal_keep_open_enabled" : "terminal_keep_open_disabled"),
            _ => setValue(!getValue())));
    }
}
