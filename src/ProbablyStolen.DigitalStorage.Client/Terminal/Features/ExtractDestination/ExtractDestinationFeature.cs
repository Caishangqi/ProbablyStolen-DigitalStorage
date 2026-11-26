using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.ExtractDestination;

/// <summary>Destination priority for left-click extraction from the storage terminal.</summary>
public enum ExtractionPriority
{
    /// <summary>The warehouse first, then the back counter.</summary>
    WarehouseFirst = 0,

    /// <summary>The back counter first, then the warehouse.</summary>
    CounterFirst = 1
}

/// <summary>An inventory that can receive items from a terminal left click.</summary>
public enum ExtractionDestination
{
    /// <summary>The player's warehouse.</summary>
    Warehouse,

    /// <summary>The back counter of the store.</summary>
    Counter
}

/// <summary>The order in which the terminal tries inventories for each extracted item.</summary>
public static class ExtractionOrder
{
    private static readonly ExtractionDestination[] WarehouseFirst = { ExtractionDestination.Warehouse, ExtractionDestination.Counter };
    private static readonly ExtractionDestination[] CounterFirst = { ExtractionDestination.Counter, ExtractionDestination.Warehouse };

    /// <summary>Lists destinations in order. An unknown priority uses the warehouse first.</summary>
    public static IReadOnlyList<ExtractionDestination> For(ExtractionPriority priority) =>
        priority == ExtractionPriority.CounterFirst ? CounterFirst : WarehouseFirst;

    /// <summary>Switches the preferred destination between the warehouse and the back counter.</summary>
    public static ExtractionPriority Next(ExtractionPriority priority) =>
        priority == ExtractionPriority.CounterFirst ? ExtractionPriority.WarehouseFirst : ExtractionPriority.CounterFirst;
}

/// <summary>The storage terminal toolbar control for extraction priority.</summary>
public static class ExtractDestinationFeature
{
    /// <summary>The toolbar action key for switching extraction priority.</summary>
    public const string ActionId = "extract_destination";

    /// <summary>The tooltip key for the currently preferred inventory.</summary>
    public const string TooltipKey = "terminal_extract_destination_tip";

    /// <summary>Adds the destination switch to the storage view toolbar.</summary>
    /// <param name="registry">The registry that owns the terminal actions.</param>
    /// <param name="getValue">Reads the priority shared by all storage terminals.</param>
    /// <param name="setValue">Updates the shared priority.</param>
    public static void Register(TerminalRegistry registry, Func<ExtractionPriority> getValue, Action<ExtractionPriority> setValue)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (getValue == null) throw new ArgumentNullException(nameof(getValue));
        if (setValue == null) throw new ArgumentNullException(nameof(setValue));

        // Keep the destination switch between the sort buttons and stacking rules.
        registry.AddAction(new TerminalAction(ActionId, 15, DockSide.Left, ActionPlacement.Rail,
            ActionScope.View(TerminalRegistry.StorageTerminalName), _ => true, _ => Appearance(getValue()),
            _ => setValue(ExtractionOrder.Next(getValue()))));
    }

    /// <summary>Uses the preferred inventory's icon and tooltip on the destination switch.</summary>
    public static ButtonAppearance Appearance(ExtractionPriority priority) => priority == ExtractionPriority.CounterFirst
        ? new ButtonAppearance("button_extract_counter", TooltipKey, "terminal_extract_counter_first")
        : new ButtonAppearance("button_extract_warehouse", TooltipKey, "terminal_extract_warehouse_first");
}
