using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>Which pages an empty-page compaction keeps, and the descriptor after it.</summary>
public sealed class PageCompaction
{
    internal PageCompaction(IReadOnlyList<int> kept, IReadOnlyList<int> removed, LayoutDescriptor after)
    {
        Kept = kept;
        Removed = removed;
        After = after;
    }

    /// <summary>Gets the kept page indices in their old order; new index i holds old page <c>Kept[i]</c>.</summary>
    public IReadOnlyList<int> Kept { get; }

    /// <summary>Gets the removed page indices.</summary>
    public IReadOnlyList<int> Removed { get; }

    /// <summary>Gets the descriptor after compaction.</summary>
    public LayoutDescriptor After { get; }

    /// <summary>Gets whether any page would be removed.</summary>
    public bool HasWork => Removed.Count > 0;
}

/// <summary>Plans removing empty pages: the anchor stays, only pages without roots go, order is kept.</summary>
public static class PageCompactionPlanner
{
    /// <summary>Plans a compaction from the descriptor and the root count of each page.</summary>
    /// <exception cref="ArgumentException">The root counts do not match the descriptor's pages or one is negative.</exception>
    public static PageCompaction Plan(LayoutDescriptor descriptor, IReadOnlyList<int> rootCounts)
    {
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (rootCounts == null) throw new ArgumentNullException(nameof(rootCounts));
        if (rootCounts.Count != descriptor.Pages.Count)
            throw new ArgumentException("Every page needs a root count.", nameof(rootCounts));

        var kept = new List<int> { 0 };
        var removed = new List<int>();
        for (var page = 0; page < rootCounts.Count; page++)
        {
            if (rootCounts[page] < 0) throw new ArgumentException("A root count cannot be negative.", nameof(rootCounts));
            if (page == 0) continue;
            (rootCounts[page] == 0 ? removed : kept).Add(page);
        }

        return new PageCompaction(kept, removed, removed.Count == 0 ? descriptor : descriptor.Without(removed));
    }
}
