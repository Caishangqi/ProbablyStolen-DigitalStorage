using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.StorageTerminal;

/// <summary>
/// The built-in storage view and the buttons only it has: sorting applies to stored items, so other views do not show
/// the sort buttons.
/// </summary>
public static class StorageTerminalFeature
{
    /// <summary>The key of the view name.</summary>
    public const string TranslationKey = "terminal_view_storage";

    /// <summary>The sort order setting name, which is also its existing global config key.</summary>
    public const string SortByName = "terminal_view_sort";

    /// <summary>The sort direction setting name, which is also its existing global config key.</summary>
    public const string SortDirectionName = "terminal_view_direction";

    /// <summary>The sort order of stored items.</summary>
    public static readonly Setting<TerminalSortOrder> SortBy = new(SortByName,
        new[] { TerminalSortOrder.Name, TerminalSortOrder.Amount },
        order => order == TerminalSortOrder.Amount
            ? new ButtonAppearance("button_sort_amount", "terminal_button_sort_tip", "terminal_sort_amount")
            : new ButtonAppearance("button_sort_name", "terminal_button_sort_tip", "terminal_sort_name"));

    /// <summary>The sort direction of stored items.</summary>
    public static readonly Setting<TerminalSortDirection> SortDirection = new(SortDirectionName,
        new[] { TerminalSortDirection.Ascending, TerminalSortDirection.Descending },
        direction => direction == TerminalSortDirection.Descending
            ? new ButtonAppearance("button_sort_descending", "terminal_button_direction_tip", "terminal_direction_desc")
            : new ButtonAppearance("button_sort_ascending", "terminal_button_direction_tip", "terminal_direction_asc"));

    /// <summary>The title key of the left dock.</summary>
    public const string LeftDockTitleKey = "terminal_dock_title";

    /// <summary>The title key of the right dock.</summary>
    public const string RightDockTitleKey = "terminal_dock_actions_title";

    /// <summary>
    /// Registers the storage terminal's docks, "Functions" on the left and "Actions" on the right, and the storage
    /// view, always available and first, with its sort buttons on the left dock.
    /// </summary>
    /// <param name="registry">The registry.</param>
    /// <param name="createView">Creates the platform storage view for a platform window.</param>
    public static void Register(TerminalRegistry registry, Func<object, object> createView)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        registry.AddDock(DockSide.Left, LeftDockTitleKey);
        registry.AddDock(DockSide.Right, RightDockTitleKey);
        registry.AddTerminal(new TerminalDefinition(TerminalRegistry.StorageTerminalName, int.MinValue, TranslationKey,
            _ => true, createView));
        registry.AddSetting(SortBy, 0, DockSide.Left, ActionPlacement.Rail, ActionScope.View(TerminalRegistry.StorageTerminalName));
        registry.AddSetting(SortDirection, 10, DockSide.Left, ActionPlacement.Rail, ActionScope.View(TerminalRegistry.StorageTerminalName));
        registry.AddTooltipContributor(new UnitTotalTooltip());
    }
}
