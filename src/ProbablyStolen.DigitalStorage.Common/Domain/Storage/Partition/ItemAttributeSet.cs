using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

/// <summary>What the player can currently see about one item, by partition field identifier.</summary>
public sealed class ItemAttributeSet
{
    private readonly IReadOnlyDictionary<string, AttributeValue> _values;
    private readonly bool _hasHiddenFeatures;

    /// <summary>Creates a set.</summary>
    /// <param name="values">Known values by field identifier; feature fields list only exposed features.</param>
    /// <param name="hasHiddenFeatures">
    /// Whether the item has any feature the player has not exposed. A feature that is not listed is then unknown,
    /// since it may be among the hidden ones; otherwise it is known to be absent.
    /// </param>
    public ItemAttributeSet(IReadOnlyDictionary<string, AttributeValue> values, bool hasHiddenFeatures)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
        _hasHiddenFeatures = hasHiddenFeatures;
    }

    /// <summary>Gets a set where every attribute is unknown, for an item that could not be read.</summary>
    public static ItemAttributeSet Unreadable { get; } = new(new Dictionary<string, AttributeValue>(), true);

    /// <summary>Gets what the player knows about a field.</summary>
    public AttributeValue Get(string fieldId)
    {
        if (fieldId == null) throw new ArgumentNullException(nameof(fieldId));
        if (_values.TryGetValue(fieldId, out var value)) return value;
        if (this == Unreadable) return AttributeValue.Unknown;
        return fieldId.StartsWith(PartitionFieldRegistry.FeaturePrefix, StringComparison.Ordinal) && !_hasHiddenFeatures
            ? AttributeValue.Of(false)
            : AttributeValue.Unknown;
    }
}

/// <summary>Reads what the player can see about a stored or offered item; implemented over native items.</summary>
public interface IItemAttributeSource
{
    /// <summary>Gets the attributes of an item identity; never throws, returning <see cref="ItemAttributeSet.Unreadable"/> instead.</summary>
    ItemAttributeSet Read(ItemKey key);
}
