using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.ExtractDestination;

/// <summary>Where a left click in the storage terminal puts items first.</summary>
public enum ExtractionPriority
{
    /// <summary>The warehouse first, then the back counter.</summary>
    WarehouseFirst = 0,

    /// <summary>The back counter first, then the warehouse.</summary>
    CounterFirst = 1
}

/// <summary>A place a left click can put items.</summary>
public enum ExtractionDestination
{
    /// <summary>The player's warehouse.</summary>
    Warehouse,

    /// <summary>The back counter of the store.</summary>
    Counter
}

/// <summary>The order in which a left click fills its destinations.</summary>
public static class ExtractionOrder
{
    private static readonly ExtractionDestination[] WarehouseFirst = { ExtractionDestination.Warehouse, ExtractionDestination.Counter };
    private static readonly ExtractionDestination[] CounterFirst = { ExtractionDestination.Counter, ExtractionDestination.Warehouse };

    /// <summary>Gets every destination, the preferred one first; an unknown priority falls back to the warehouse.</summary>
    public static IReadOnlyList<ExtractionDestination> For(ExtractionPriority priority) =>
        priority == ExtractionPriority.CounterFirst ? CounterFirst : WarehouseFirst;

    /// <summary>Gets the other priority.</summary>
    public static ExtractionPriority Next(ExtractionPriority priority) =>
        priority == ExtractionPriority.CounterFirst ? ExtractionPriority.WarehouseFirst : ExtractionPriority.CounterFirst;
}

/// <summary>Registers the storage view button that switches where a left click puts items first.</summary>
public static class ExtractDestinationFeature
{
    /// <summary>The rail action identifier.</summary>
    public const string ActionId = "extract_destination";

    /// <summary>The tooltip key; its argument names the current priority.</summary>
    public const string TooltipKey = "terminal_extract_destination_tip";

    /// <summary>Registers the button.</summary>
    /// <param name="registry">The registry.</param>
    /// <param name="getValue">Reads the shared priority.</param>
    /// <param name="setValue">Stores the shared priority.</param>
    public static void Register(TerminalRegistry registry, Func<ExtractionPriority> getValue, Action<ExtractionPriority> setValue)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (getValue == null) throw new ArgumentNullException(nameof(getValue));
        if (setValue == null) throw new ArgumentNullException(nameof(setValue));

        // After the sort order (0) and sort direction (10) buttons, before stacking rules (20).
        registry.AddAction(new TerminalAction(ActionId, 15, DockSide.Left, ActionPlacement.Rail,
            ActionScope.View(TerminalRegistry.StorageTerminalName), _ => true, _ => Appearance(getValue()),
            _ => setValue(ExtractionOrder.Next(getValue()))));
    }

    /// <summary>Gets the button appearance for a priority.</summary>
    public static ButtonAppearance Appearance(ExtractionPriority priority) => priority == ExtractionPriority.CounterFirst
        ? new ButtonAppearance("button_extract_counter", TooltipKey, "terminal_extract_counter_first")
        : new ButtonAppearance("button_extract_warehouse", TooltipKey, "terminal_extract_warehouse_first");
}
