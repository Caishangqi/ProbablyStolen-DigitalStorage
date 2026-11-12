using System;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>Why a stored partition cannot be used.</summary>
public enum CellPartitionFailure
{
    /// <summary>The partition is valid.</summary>
    None = 0,

    /// <summary>The recorded mode is unknown.</summary>
    UnknownMode = 1,

    /// <summary>A rule names a field this version does not know; skipping it would widen the filter.</summary>
    UnknownField = 2,

    /// <summary>The recorded rules cannot be read or contain an invalid rule.</summary>
    MalformedRule = 3
}

/// <summary>
/// A cell's effective partition filter. An invalid partition refuses every new item but keeps stored items
/// readable and extractable; its unknown parts are never skipped, since that would widen a whitelist or blacklist.
/// </summary>
public sealed class CellPartition
{
    private CellPartition(PartitionMode mode, IPartitionList list, int ruleCount, int revision, CellPartitionFailure failure)
    {
        Mode = mode;
        List = list;
        RuleCount = ruleCount;
        Revision = revision;
        Failure = failure;
    }

    /// <summary>Gets the default partition: a whitelist without rules, which admits everything.</summary>
    public static CellPartition Unfiltered { get; } =
        new(PartitionMode.Whitelist, RulePartitionList.Empty, 0, 0, CellPartitionFailure.None);

    /// <summary>Gets the filter mode.</summary>
    public PartitionMode Mode { get; }

    /// <summary>Gets the rules as a partition list.</summary>
    public IPartitionList List { get; }

    /// <summary>Gets the number of rules; 0 for an invalid partition, whose rules could not be read.</summary>
    public int RuleCount { get; }

    /// <summary>Gets the configuration revision.</summary>
    public int Revision { get; }

    /// <summary>Gets why the partition is invalid, or <see cref="CellPartitionFailure.None"/>.</summary>
    public CellPartitionFailure Failure { get; }

    /// <summary>Gets whether the partition can filter.</summary>
    public bool IsValid => Failure == CellPartitionFailure.None;

    /// <summary>Builds the partition of a stored configuration, validating it first.</summary>
    public static CellPartition From(PartitionConfig config, IItemAttributeSource attributes)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (attributes == null) throw new ArgumentNullException(nameof(attributes));
        var problems = config.Validate();
        if (problems.Count > 0)
        {
            var failure = problems.Any(p => p.Issue == PartitionIssue.UnknownMode) ? CellPartitionFailure.UnknownMode
                : problems.Any(p => p.Issue == PartitionIssue.UnknownField) ? CellPartitionFailure.UnknownField
                : CellPartitionFailure.MalformedRule;
            return Invalid(config.Mode, failure, config.Revision);
        }

        return new CellPartition(config.Mode, new RulePartitionList(config.Rules, attributes), config.Rules.Count,
            config.Revision, CellPartitionFailure.None);
    }

    /// <summary>Creates an invalid partition that refuses new items.</summary>
    public static CellPartition Invalid(PartitionMode mode, CellPartitionFailure failure, int revision = 0)
    {
        if (failure == CellPartitionFailure.None) throw new ArgumentException("An invalid partition needs a failure.", nameof(failure));
        return new CellPartition(mode, RulePartitionList.Empty, 0, revision, failure);
    }

    /// <summary>Gets why a new item is refused, or null when the partition admits it.</summary>
    public StoreRejection? Rejection(ItemKey? key)
    {
        if (!IsValid) return StoreRejection.PartitionInvalid;
        if (key == null) return StoreRejection.PartitionRejected;
        return List.MatchesFilter(key, Mode) switch
        {
            PartitionMatch.Match => null,
            PartitionMatch.NoMatch => StoreRejection.PartitionRejected,
            _ => StoreRejection.PartitionUnknown
        };
    }

    /// <summary>
    /// A matching whitelist rule makes this cell preferred storage for the item. A blacklist never grants preference.
    /// </summary>
    public bool IsPreferred(ItemKey? key) =>
        key != null && IsValid && Mode == PartitionMode.Whitelist && !List.IsEmpty() && List.IsListed(key);
}
