using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api.Client;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.StorageTerminal;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.DisplaySettings;

/// <summary>
/// The slot size, icon margin and dock button size page of the storage terminal and the partition editor: a rail button
/// on the left dock opens it, and the dock's back button returns. Both windows show the same global choice.
/// </summary>
public static class DisplaySettingsFeature
{
    /// <summary>The rail action identifier.</summary>
    public const string ActionId = "display_settings";

    /// <summary>The page identifier.</summary>
    public const string ScreenId = "display.settings";

    /// <summary>The back button identifier of the page.</summary>
    public const string BackActionId = ScreenId + ScreenBackButton.IdSuffix;

    /// <summary>The rail button icon.</summary>
    public const string IconKey = "button_settings";

    /// <summary>The rail button tooltip.</summary>
    public const string TooltipKey = "terminal_button_display_settings_tip";

    /// <summary>Gets the slot and dock button sizes in the order the page lists them.</summary>
    public static IReadOnlyList<SizeTier> Sizes { get; } =
        new[] { SizeTier.Default, SizeTier.Small, SizeTier.Medium, SizeTier.Large };

    /// <summary>Gets the icon margins in the order the page lists them.</summary>
    public static IReadOnlyList<IconMarginTier> IconMargins { get; } =
        new[] { IconMarginTier.Compact, IconMarginTier.Standard, IconMarginTier.Loose };

    /// <summary>Registers the rail button and the page's back button.</summary>
    /// <param name="registry">The registry; its left dock must be registered by freeze.</param>
    /// <param name="order">The rail position among the shared buttons.</param>
    /// <param name="createScreen">Creates the page for the context's window.</param>
    public static void Register(TerminalRegistry registry, int order, Func<object, ITerminalScreen> createScreen)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (createScreen == null) throw new ArgumentNullException(nameof(createScreen));
        var appearance = new ButtonAppearance(IconKey, TooltipKey);
        registry.AddAction(new TerminalAction(ActionId, order, DockSide.Left, ActionPlacement.Rail, ActionScope.AllViews,
            _ => true, _ => appearance, context => context.Commands.PushScreen(createScreen(context.Window))));
        ScreenBackButton.Register(registry, ScreenId, StorageTerminalFeature.RightDockTitleKey);
    }

    /// <summary>Gets the label key of a slot or dock button size.</summary>
    public static string SizeLabelKey(SizeTier tier) => "size_tier_" + tier.ToString().ToLowerInvariant();

    /// <summary>Gets the label key of an icon margin.</summary>
    public static string IconMarginLabelKey(IconMarginTier margin) => "icon_margin_" + margin.ToString().ToLowerInvariant();
}
