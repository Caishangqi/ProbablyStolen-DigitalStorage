using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>Identities listed by a partition.</summary>
public interface IPartitionList
{
    /// <summary>Gets whether the identity is listed.</summary>
    bool IsListed(ItemKey key);

    /// <summary>Gets whether no identity is listed.</summary>
    bool IsEmpty();

    /// <summary>Gets the listed identities in their stored order.</summary>
    IReadOnlyList<ItemKey> GetItems();

    /// <summary>Checks the identity against this list in the given mode; an empty list accepts everything.</summary>
    bool MatchesFilter(ItemKey key, IncludeExclude mode)
    {
        if (IsEmpty())
        {
            return true;
        }

        return mode == IncludeExclude.Whitelist ? IsListed(key) : !IsListed(key);
    }
}
