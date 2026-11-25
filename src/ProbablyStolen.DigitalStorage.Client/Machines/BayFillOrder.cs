using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Client.Machines;

/// <summary>
/// The order in which a group dropped onto one bay spreads over its sibling bays: the bays after the target in grid
/// order, then the ones before it, so the group lands next to where it was dropped.
/// </summary>
public static class BayFillOrder
{
    /// <summary>Gets the sibling indices to try after the target, which the native drop has already filled.</summary>
    /// <param name="target">The index of the bay under the pointer.</param>
    /// <param name="count">The number of bays in the grid.</param>
    public static IReadOnlyList<int> After(int target, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (target < 0 || target >= count) throw new ArgumentOutOfRangeException(nameof(target));
        var order = new int[count - 1];
        for (var step = 1; step < count; step++) order[step - 1] = (target + step) % count;
        return order;
    }
}
