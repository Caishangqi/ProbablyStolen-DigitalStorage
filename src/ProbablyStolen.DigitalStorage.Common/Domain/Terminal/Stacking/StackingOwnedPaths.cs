using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

/// <summary>
/// The raw identity data one stacking field owns on the root item. Ignoring the field removes exactly this data from
/// the grouping key; everything else keeps its exact comparison.
/// </summary>
public sealed class StackingOwnedPaths
{
    /// <summary>Owns nothing; derived fields use it.</summary>
    public static StackingOwnedPaths None { get; } = new();

    /// <summary>Creates owned paths.</summary>
    /// <param name="valueFields">Whether the four stored value fields belong to the field.</param>
    /// <param name="tagKeys">The root tag keys the field owns.</param>
    /// <param name="featureIds">The root feature identifiers the field owns.</param>
    /// <param name="contentWindow">Whether the whole content window, child items included, belongs to the field.</param>
    public StackingOwnedPaths(bool valueFields = false, IEnumerable<string>? tagKeys = null,
        IEnumerable<string>? featureIds = null, bool contentWindow = false)
    {
        ValueFields = valueFields;
        TagKeys = Freeze(tagKeys);
        FeatureIds = Freeze(featureIds);
        ContentWindow = contentWindow;
    }

    /// <summary>Gets whether the stored value fields belong to the field.</summary>
    public bool ValueFields { get; }

    /// <summary>Gets the owned root tag keys.</summary>
    public IReadOnlyCollection<string> TagKeys { get; }

    /// <summary>Gets the owned root feature identifiers.</summary>
    public IReadOnlyCollection<string> FeatureIds { get; }

    /// <summary>Gets whether the content window belongs to the field.</summary>
    public bool ContentWindow { get; }

    /// <summary>Gets whether nothing is owned.</summary>
    public bool IsEmpty => !ValueFields && !ContentWindow && TagKeys.Count == 0 && FeatureIds.Count == 0;

    /// <summary>Combines two sets of paths; a path shared by both is owned once.</summary>
    public StackingOwnedPaths Union(StackingOwnedPaths other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        return new StackingOwnedPaths(ValueFields || other.ValueFields, TagKeys.Concat(other.TagKeys),
            FeatureIds.Concat(other.FeatureIds), ContentWindow || other.ContentWindow);
    }

    /// <summary>Gets the paths shared by two fields.</summary>
    public StackingOwnedPaths Intersect(StackingOwnedPaths other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        return new StackingOwnedPaths(ValueFields && other.ValueFields,
            TagKeys.Intersect(other.TagKeys, StringComparer.Ordinal),
            FeatureIds.Intersect(other.FeatureIds, StringComparer.Ordinal),
            ContentWindow && other.ContentWindow);
    }

    /// <summary>Removes paths that another field explicitly requires to remain compared.</summary>
    public StackingOwnedPaths Without(StackingOwnedPaths other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        return new StackingOwnedPaths(ValueFields && !other.ValueFields,
            TagKeys.Except(other.TagKeys, StringComparer.Ordinal), FeatureIds.Except(other.FeatureIds, StringComparer.Ordinal),
            ContentWindow && !other.ContentWindow);
    }

    private static IReadOnlyCollection<string> Freeze(IEnumerable<string>? values)
    {
        if (values == null) return Array.Empty<string>();
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("An owned path name is required.", nameof(values));
            set.Add(value);
        }

        return set.ToArray();
    }
}
