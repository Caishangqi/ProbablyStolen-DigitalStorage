using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.StorageTerminal;

/// <summary>
/// Adds the total unit count to the tooltip of a slot whose pieces hold more than one unit each, since the slot badge
/// counts pieces.
/// </summary>
public sealed class UnitTotalTooltip : IEntryTooltipContributor
{
    /// <summary>The contributor id.</summary>
    public const string ContributorId = "unit_total";

    /// <summary>The localization key of the line.</summary>
    public const string TextKey = "terminal_tooltip_total_units";

    /// <inheritdoc />
    public string Id => ContributorId;

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsActive(TerminalContext context) => true;

    /// <inheritdoc />
    public void Contribute(DisplayEntry entry, TerminalContext context, EntryAttributes attributes, EntryTooltipSink sink)
    {
        if (entry.TotalUnits > entry.Total) sink.Body(TooltipPanelDefinition.HintId, TextKey, entry.TotalUnits);
    }
}
