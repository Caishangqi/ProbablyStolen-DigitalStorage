using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>Selects whether a cell partition restricts the identities it accepts.</summary>
public enum CellPartitionMode
{
    /// <summary>Accepts every supported identity.</summary>
    Unfiltered = 0,

    /// <summary>Accepts only the identities listed by the partition.</summary>
    Whitelist = 1
}

/// <summary>Explains why a supplied partition configuration cannot be used.</summary>
public enum CellPartitionFailure
{
    /// <summary>The configuration is usable.</summary>
    None = 0,

    /// <summary>The configuration requested a mode this build does not define.</summary>
    UnknownMode = 1,

    /// <summary>A whitelist configuration listed no identities.</summary>
    EmptyWhitelist = 2,

    /// <summary>An unfiltered configuration also listed identities.</summary>
    UnfilteredWithKeys = 3,

    /// <summary>The configuration contained a null identity.</summary>
    NullKey = 4,

    /// <summary>The configuration listed more unique identities than a partition allows.</summary>
    TooManyKeys = 5,

    /// <summary>The configuration supplied no key collection at all.</summary>
    MissingKeys = 6
}

/// <summary>Matches supplied item identities against an immutable exact whitelist.</summary>
public sealed class CellPartition
{
    private const int MaxUniqueKeys = 63;

    private static readonly CellPartition UnfilteredPartition =
        new(CellPartitionMode.Unfiltered, Array.Empty<ItemKey>(), CellPartitionFailure.None);

    private readonly IReadOnlyList<ItemKey> _keys;

    private CellPartition(CellPartitionMode mode, ItemKey[] keys, CellPartitionFailure failure)
    {
        Mode = mode;
        Failure = failure;
        IsValid = failure == CellPartitionFailure.None;
        _keys = Array.AsReadOnly(keys);
    }

    /// <summary>Gets the partition that accepts every non-null supplied identity.</summary>
    public static CellPartition Unfiltered => UnfilteredPartition;

    /// <summary>Gets the requested partition mode.</summary>
    public CellPartitionMode Mode { get; }

    /// <summary>Gets whether this configuration may accept new identities.</summary>
    public bool IsValid { get; }

    /// <summary>Gets why the configuration is unusable, or <see cref="CellPartitionFailure.None"/> when it is usable.</summary>
    public CellPartitionFailure Failure { get; }

    /// <summary>Gets a read-only copy of the unique whitelist identities in submission order.</summary>
    public IReadOnlyList<ItemKey> Keys => _keys;

    /// <summary>Checks whether a supplied identity is accepted by this partition.</summary>
    /// <param name="key">The identity to check, or null when no identity is supplied.</param>
    /// <returns>Whether the identity matches; invalid configurations and null never match.</returns>
    public bool Matches(ItemKey? key)
    {
        if (key == null || !IsValid)
        {
            return false;
        }

        if (Mode == CellPartitionMode.Unfiltered)
        {
            return true;
        }

        foreach (var stored in _keys)
        {
            if (stored.Equals(key))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Builds the partition produced by a player's filter edit.</summary>
    /// <param name="submitted">The identities the edit left in the filter; empty when the edit cleared it.</param>
    /// <returns>A whitelist partition, or <see cref="Unfiltered"/> when the edit removed every identity.</returns>
    /// <exception cref="ArgumentNullException">No submission was supplied.</exception>
    /// <exception cref="ArgumentException">A submitted identity is null or the edit exceeds the unique key limit.</exception>
    public static CellPartition FromUserSubmission(IEnumerable<ItemKey?> submitted)
    {
        if (submitted == null)
        {
            throw new ArgumentNullException(nameof(submitted));
        }

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

        if (unique.Count > MaxUniqueKeys)
        {
            throw new ArgumentException(
                $"A partition accepts at most {MaxUniqueKeys} unique identities.", nameof(submitted));
        }

        return new CellPartition(CellPartitionMode.Whitelist, unique.ToArray(), CellPartitionFailure.None);
    }

    /// <summary>Validates a configuration supplied by persisted or otherwise external data.</summary>
    /// <param name="mode">The requested partition mode.</param>
    /// <param name="keys">The listed identities; a null collection is not a usable configuration.</param>
    /// <returns>A usable partition, or an invalid partition that never matches.</returns>
    public static CellPartition FromConfiguration(CellPartitionMode mode, IEnumerable<ItemKey?>? keys)
    {
        // A missing collection is malformed external data. For a known mode it must never become the
        // explicit empty user edit that FromUserSubmission clears back to Unfiltered. Unknown modes are
        // classified by the switch below, so a null collection is not inspected there.
        if (keys == null && (mode == CellPartitionMode.Unfiltered || mode == CellPartitionMode.Whitelist))
        {
            return CreateInvalid(mode, CellPartitionFailure.MissingKeys);
        }

        var supplied = new List<ItemKey?>(keys ?? Array.Empty<ItemKey?>());
        switch (mode)
        {
            case CellPartitionMode.Unfiltered:
                return supplied.Count == 0
                    ? UnfilteredPartition
                    : CreateInvalid(mode, CellPartitionFailure.UnfilteredWithKeys);
            case CellPartitionMode.Whitelist:
                return CreateWhitelist(supplied);
            default:
                return CreateInvalid(mode, CellPartitionFailure.UnknownMode);
        }
    }

    private static CellPartition CreateWhitelist(IReadOnlyList<ItemKey?> supplied)
    {
        if (supplied.Count == 0)
        {
            return CreateInvalid(CellPartitionMode.Whitelist, CellPartitionFailure.EmptyWhitelist);
        }

        var unique = new List<ItemKey>();
        var seen = new HashSet<ItemKey>();
        foreach (var key in supplied)
        {
            if (key == null)
            {
                return CreateInvalid(CellPartitionMode.Whitelist, CellPartitionFailure.NullKey);
            }

            if (seen.Add(key))
            {
                unique.Add(key);
            }
        }

        return unique.Count > MaxUniqueKeys
            ? CreateInvalid(CellPartitionMode.Whitelist, CellPartitionFailure.TooManyKeys)
            : new CellPartition(CellPartitionMode.Whitelist, unique.ToArray(), CellPartitionFailure.None);
    }

    private static CellPartition CreateInvalid(CellPartitionMode mode, CellPartitionFailure failure) =>
        new(mode, Array.Empty<ItemKey>(), failure);
}
