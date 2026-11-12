using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>
/// Partition matching preserves unknown results when a rule tests an attribute the player cannot see.
/// </summary>
public interface IPartitionList
{
    /// <summary>Gets whether some rule certainly matches the item.</summary>
    bool IsListed(ItemKey key);

    /// <summary>Gets whether the list has no rule.</summary>
    bool IsEmpty();

    /// <summary>Rules combine with OR while preserving unknown results.</summary>
    PartitionMatch Evaluate(ItemKey key);

    /// <summary>
    /// Gets whether the mode admits the item: <see cref="PartitionMatch.Match"/> admits it,
    /// <see cref="PartitionMatch.NoMatch"/> refuses it and <see cref="PartitionMatch.Unknown"/> cannot decide, which
    /// callers must treat as a refusal. An empty list admits everything in either mode.
    /// </summary>
    PartitionMatch MatchesFilter(ItemKey key, PartitionMode mode)
    {
        if (IsEmpty()) return PartitionMatch.Match;
        var listed = Evaluate(key);
        if (mode == PartitionMode.Whitelist) return listed;
        // A blacklist refuses listed items; an unknown result is never turned into admission by the negation.
        return listed switch
        {
            PartitionMatch.Match => PartitionMatch.NoMatch,
            PartitionMatch.NoMatch => PartitionMatch.Match,
            _ => PartitionMatch.Unknown
        };
    }
}
