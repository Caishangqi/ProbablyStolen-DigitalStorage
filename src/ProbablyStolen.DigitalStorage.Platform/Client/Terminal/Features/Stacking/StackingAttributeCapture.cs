using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.Stacking;

/// <summary>
/// Reads the estimated value the item tooltip shows. The same call as the native tooltip is used, so grouping and the
/// tooltip agree; it includes child items, power sources and active store events, and only writes debug text when the
/// game's price debug panel is open.
/// </summary>
internal sealed class StackingAttributeCapture : IEntryAttributeCapture
{
    /// <inheritdoc />
    public string Name => StackingFieldCatalog.AppraisedValue;

    /// <inheritdoc />
    public bool TryRead(GameItem sample, out long value)
    {
        value = 0;
        // Store stations are created with the store scene; early in a load the event list is not there yet.
        if (sample == null || StoreStation.Instance?.storeEventManager == null) return false;
        // Other mods may rewrite the item while its value is read; the stored root stays as stored.
        using var read = StoredItemChanges.Read(sample);
        value = sample.GetCurrentValue(useRetailMarkup: false, withChild: true, includeEvents: true, forceMarkup: false, 0L);
        return true;
    }
}
