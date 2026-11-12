using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

/// <summary>
/// Reads partition attributes from an item identity's canonical state. The identity already records the item types,
/// tags and feature exposure, so no live game object is needed and the result depends only on the identity.
/// </summary>
/// <remarks>
/// Tag meanings follow the game: <c>CONTRABAND_ITEM_TAG</c> with the
/// <c>CONTRABAND_LEVEL</c> string, which players see as an exposed market feature; <c>STOLEN_TAG</c> with heat in
/// <c>STOLEN_VALUE_INT</c>. A known legal item has heat 0.
/// </remarks>
public sealed class CanonicalAttributeSource : IItemAttributeSource
{
    private const string ContrabandTag = "CONTRABAND_ITEM_TAG";
    private const string ContrabandLevelTag = "CONTRABAND_LEVEL";
    private const string StolenTag = "STOLEN_TAG";
    private const string HeatTag = "STOLEN_VALUE_INT";

    private static readonly IReadOnlyDictionary<string, string> Levels = new Dictionary<string, string>
    {
        ["CONTRABAND_LEVEL_NONE"] = "none",
        ["CONTRABAND_LEVEL_LOW"] = "low",
        ["CONTRABAND_LEVEL_MID"] = "mid",
        ["CONTRABAND_LEVEL_HIGH"] = "high",
        ["CONTRABAND_LEVEL_CRITICAL"] = "critical"
    };

    /// <summary>Gets the shared instance; it holds no state.</summary>
    public static CanonicalAttributeSource Instance { get; } = new();

    /// <inheritdoc />
    public ItemAttributeSet Read(ItemKey key)
    {
        if (key == null) return ItemAttributeSet.Unreadable;
        var summary = key.ReadSummary();
        // A legacy identity records only its identifier; every other attribute stays unknown.
        if (summary == null)
            return new ItemAttributeSet(new Dictionary<string, AttributeValue>
            {
                [PartitionFieldRegistry.Item] = AttributeValue.Of(key.Identifier)
            }, hasHiddenFeatures: true);

        var values = new Dictionary<string, AttributeValue>
        {
            [PartitionFieldRegistry.Item] = AttributeValue.Of(summary.Identifier),
            [PartitionFieldRegistry.Type] = AttributeValue.OfSet(summary.ItemTypes),
            [PartitionFieldRegistry.Legality] = Legality(summary)
        };

        var stolen = summary.IsTag(StolenTag);
        values[PartitionFieldRegistry.Stolen] = AttributeValue.Of(stolen);
        values[PartitionFieldRegistry.Heat] = AttributeValue.Of(stolen && summary.Tags.TryGetValue(HeatTag, out var heat)
            ? heat.ValueInt
            : 0L);

        var active = summary.Features.Where(feature => !feature.Disabled).ToList();
        foreach (var feature in active.Where(feature => feature.Exposed))
            values[PartitionFieldRegistry.FeatureField(feature.Identifier)] = AttributeValue.Of(true);
        return new ItemAttributeSet(values, hasHiddenFeatures: active.Any(feature => !feature.Exposed));
    }

    private static AttributeValue Legality(CanonicalItemSummary summary)
    {
        if (!summary.IsTag(ContrabandTag)) return AttributeValue.Of("none");
        var level = summary.Tags.TryGetValue(ContrabandLevelTag, out var tag) ? tag.ValueString : null;
        // An unrecognised level from a newer game version is unknown rather than guessed.
        return level != null && Levels.TryGetValue(level, out var value) ? AttributeValue.Of(value) : AttributeValue.Unknown;
    }
}
