using System;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>
/// Puts a sub-screen's back button on the left dock instead of the page, and titles that dock for the page while it is
/// on top. A page whose back button is registered here draws none of its own.
/// </summary>
public static class ScreenBackButton
{
    /// <summary>The icon of the back button.</summary>
    public const string IconKey = "button_to_left";

    /// <summary>The tooltip of the back button.</summary>
    public const string TooltipKey = "terminal_back";

    /// <summary>The suffix of a back button id; the screen id comes first.</summary>
    public const string IdSuffix = ".back";

    /// <summary>Gets the back button id of a screen.</summary>
    public static string ActionIdFor(string screenId) =>
        (string.IsNullOrEmpty(screenId) ? throw new ArgumentException("A screen id is required.", nameof(screenId)) : screenId) +
        IdSuffix;

    /// <summary>Registers the back button of one screen; it returns one page.</summary>
    /// <param name="registry">The registry; its left dock must be registered by freeze.</param>
    /// <param name="screenId">The screen the button belongs to.</param>
    /// <param name="dockTitleKey">The left dock title while the screen is on top.</param>
    public static void Register(TerminalRegistry registry, string screenId, string dockTitleKey)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        var scope = ActionScope.Screen(screenId);
        var appearance = new ButtonAppearance(IconKey, TooltipKey);
        registry.AddDockTitle(DockSide.Left, scope, dockTitleKey);
        registry.AddAction(new TerminalAction(ActionIdFor(screenId), 0, DockSide.Left, ActionPlacement.Rail, scope, _ => true,
            _ => appearance, context => context.Commands.PopScreen()));
    }

    /// <summary>Gets whether a screen's back button sits on the dock.</summary>
    public static bool IsRegistered(TerminalRegistry registry, string screenId)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        var id = ActionIdFor(screenId);
        return registry.Buttons.Any(entry => entry.Button.Id == id);
    }
}
