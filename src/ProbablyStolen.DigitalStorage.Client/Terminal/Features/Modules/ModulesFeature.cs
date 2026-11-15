using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.Modules;

/// <summary>
/// The modules button, first on the rail for every view; it opens the modules sub-screen of the window.
/// </summary>
public static class ModulesFeature
{
    /// <summary>The button id.</summary>
    public const string ActionId = "modules";

    /// <summary>Registers the modules button.</summary>
    /// <param name="registry">The registry.</param>
    /// <param name="createScreen">Creates the platform modules sub-screen for a platform window.</param>
    public static void Register(TerminalRegistry registry, Func<object, object> createScreen)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (createScreen == null) throw new ArgumentNullException(nameof(createScreen));
        var appearance = new ButtonAppearance("button_modules", "terminal_button_modules_tip");
        registry.AddAction(new TerminalAction(ActionId, 0, ActionPlacement.Rail, null, _ => true, _ => appearance,
            context => context.Commands.SwitchToScreen(createScreen(context.Window))));
    }
}
