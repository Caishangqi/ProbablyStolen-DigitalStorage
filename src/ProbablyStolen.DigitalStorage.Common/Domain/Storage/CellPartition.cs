using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>Explains why a supplied partition configuration cannot be used.</summary>
public enum CellPartitionFailure
{
    /// <summary>The configuration is usable.</summary>
    None = 0,

    /// <summary>The configuration requested a mode this version does not define.</summary>
    UnknownMode = 1,

    /// <summary>The configuration contained a null identity.</summary>
    NullKey = 4,

    /// <summary>The configuration listed more unique identities than a partition allows.</summary>
    TooManyKeys = 5,

    /// <summary>The configuration supplied no key collection at all.</summary>
    MissingKeys = 6
}

/// <summary>Immutable partition configuration of one cell: a mode and an exact identity list.</summary>
/// <remarks>
/// An empty list accepts every identity in both modes.
/// </remarks>
public sealed class CellPartition
{
    private static readonly CellPartition UnfilteredPartition =
        new(IncludeExclude.Whitelist, PrecisePriorityList.Empty, false, CellPartitionFailure.None);

    private CellPartition(IncludeExclude mode, IPartitionList list, bool equalDistribution, CellPartitionFailure failure)
    {
        Mode = mode;
        List = list;
        EqualDistribution = equalDistribution;
        Failure = failure;
        IsValid = failure == CellPartitionFailure.None;
    }

    /// <summary>Gets the partition with an empty list, which accepts every non-null identity.</summary>
    public static CellPartition Unfiltered => UnfilteredPartition;

    /// <summary>Gets the requested filter mode.</summary>
    public IncludeExclude Mode { get; }

    /// <summary>Gets the listed identities; empty for invalid configurations.</summary>
    public IPartitionList List { get; }

    /// <summary>Gets whether each type is limited to an equal share of the cell capacity.</summary>
    public bool EqualDistribution { get; }

    /// <summary>Gets whether this configuration may accept new identities.</summary>
    public bool IsValid { get; }

    /// <summary>Gets why the configuration is unusable, or <see cref="CellPartitionFailure.None"/> when it is usable.</summary>
    public CellPartitionFailure Failure { get; }

    /// <summary>Checks whether a supplied identity passes this partition.</summary>
    /// <param name="key">The identity to check, or null when no identity is supplied.</param>
    /// <returns>Whether the identity passes; invalid configurations and null never pass.</returns>
    public bool MatchesFilter(ItemKey? key) => key != null && IsValid && List.MatchesFilter(key, Mode);

    /// <summary>
    /// Gets how many types share the capacity under equal distribution: the listed count of a non-empty whitelist,
    /// capped at the type limit, otherwise the type limit.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The type limit is not positive.</exception>
    public int EqualShareBase(int typeLimit)
    {
        if (typeLimit < 1) throw new ArgumentOutOfRangeException(nameof(typeLimit), typeLimit, "A type limit is positive.");
        var listed = List.GetItems().Count;
        return Mode == IncludeExclude.Whitelist && listed > 0 ? Math.Min(listed, typeLimit) : typeLimit;
    }

    /// <summary>Builds the whitelist produced by a player's filter edit.</summary>
    /// <param name="submitted">The identities the edit left in the filter; empty when the edit cleared it.</param>
    /// <returns>A whitelist partition, or <see cref="Unfiltered"/> when the edit removed every identity.</returns>
    /// <exception cref="ArgumentNullException">No submission was supplied.</exception>
    /// <param name="maxKeys">The most unique identities the edit may list.</param>
    /// <exception cref="ArgumentException">A submitted identity is null or the edit exceeds the unique key limit.</exception>
    public static CellPartition FromUserSubmission(IEnumerable<ItemKey?> submitted, int maxKeys)
    {
        if (submitted == null)
        {
            throw new ArgumentNullException(nameof(submitted));
        }

        RequirePositive(maxKeys);

        var unique = new List<ItemKey>();
        var seen = new HashSet<ItemKey>();
        foreach (var key in submitted)
        {
            if (key == null)
            {
                throw new ArgumentException("A submitted filter identity cannot be null.", nameof(submitted));
            }

            if (seen.Add(key))
            {
                unique.Add(key);
            }
        }

        if (unique.Count == 0)
        {
            return UnfilteredPartition;
        }

        if (unique.Count > maxKeys)
        {
            throw new ArgumentException(
                $"A partition accepts at most {maxKeys} unique identities.", nameof(submitted));
        }

        return new CellPartition(IncludeExclude.Whitelist, new PrecisePriorityList(unique), false, CellPartitionFailure.None);
    }

    /// <summary>Validates a configuration supplied by persisted or otherwise external data.</summary>
    /// <param name="mode">The requested filter mode.</param>
    /// <param name="keys">The listed identities, possibly empty; a null collection is not a usable configuration.</param>
    /// <param name="equalDistribution">Whether equal distribution is on.</param>
    /// <param name="maxKeys">The most unique identities, normally the cell's effective type limit.</param>
    /// <returns>A usable partition, or an invalid partition that never matches.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The key limit is not positive.</exception>
    public static CellPartition FromConfiguration(IncludeExclude mode, IEnumerable<ItemKey?>? keys,
        bool equalDistribution, int maxKeys)
    {
        RequirePositive(maxKeys);
        if (mode != IncludeExclude.Whitelist && mode != IncludeExclude.Blacklist)
        {
            return CreateInvalid(mode, equalDistribution, CellPartitionFailure.UnknownMode);
        }

        // A missing collection is malformed external data, never the explicit empty list.
        if (keys == null)
        {
            return CreateInvalid(mode, equalDistribution, CellPartitionFailure.MissingKeys);
        }

        var unique = new List<ItemKey>();
        var seen = new HashSet<ItemKey>();
        foreach (var key in keys)
        {
            if (key == null)
            {
                return CreateInvalid(mode, equalDistribution, CellPartitionFailure.NullKey);
            }

            if (seen.Add(key))
            {
                unique.Add(key);
            }
        }

        if (unique.Count > maxKeys)
        {
            return CreateInvalid(mode, equalDistribution, CellPartitionFailure.TooManyKeys);
        }

        return unique.Count == 0 && mode == IncludeExclude.Whitelist && !equalDistribution
            ? UnfilteredPartition
            : new CellPartition(mode, new PrecisePriorityList(unique), equalDistribution, CellPartitionFailure.None);
    }

    private static void RequirePositive(int maxKeys)
    {
        if (maxKeys < 1) throw new ArgumentOutOfRangeException(nameof(maxKeys), maxKeys, "A key limit is positive.");
    }

    private static CellPartition CreateInvalid(IncludeExclude mode, bool equalDistribution, CellPartitionFailure failure) =>
        new(mode, PrecisePriorityList.Empty, equalDistribution, failure);
}
