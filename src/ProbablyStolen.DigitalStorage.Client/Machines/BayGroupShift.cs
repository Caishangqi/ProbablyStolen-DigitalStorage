using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Client.Machines;

/// <summary>
/// Moves a group of cells that already sit in one bay grid as a block: the grabbed cell goes to the bay under the
/// pointer and every other cell keeps its offset from it, as the drag ghosts show.
/// </summary>
public static class BayGroupShift
{
    /// <summary>
    /// Plans the moves for the shift, or returns null when any cell would leave the grid, land outside a bay or land on
    /// a cell that is not part of the group. Cells are numbered row by row (<c>y * columns + x</c>).
    /// </summary>
    /// <param name="columns">The grid width.</param>
    /// <param name="isBay">For each cell, whether it holds a bay.</param>
    /// <param name="isBlocked">For each cell, whether its bay holds a cell outside the group.</param>
    /// <param name="group">The cells the group occupies now.</param>
    /// <param name="anchor">The grabbed cell, one of <paramref name="group"/>.</param>
    /// <param name="target">The cell under the pointer.</param>
    /// <returns>
    /// The moves in an order where each destination is already empty: the cells furthest along the shift go first.
    /// </returns>
    public static IReadOnlyList<(int From, int To)>? Plan(int columns, IReadOnlyList<bool> isBay,
        IReadOnlyList<bool> isBlocked, IReadOnlyList<int> group, int anchor, int target)
    {
        if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
        if (isBay == null) throw new ArgumentNullException(nameof(isBay));
        if (isBlocked == null || isBlocked.Count != isBay.Count) throw new ArgumentException("Every cell needs a blocked flag.", nameof(isBlocked));
        if (group == null) throw new ArgumentNullException(nameof(group));
        if (!group.Contains(anchor)) throw new ArgumentException("The grabbed cell must belong to the group.", nameof(anchor));
        if (target < 0 || target >= isBay.Count) throw new ArgumentOutOfRangeException(nameof(target));

        var rows = (isBay.Count + columns - 1) / columns;
        var dx = target % columns - anchor % columns;
        var dy = target / columns - anchor / columns;
        if (dx == 0 && dy == 0) return null;

        var moves = new List<(int From, int To, int Lead)>();
        foreach (var cell in group.Distinct())
        {
            if (cell < 0 || cell >= isBay.Count) return null;
            var x = cell % columns + dx;
            var y = cell / columns + dy;
            if (x < 0 || x >= columns || y < 0 || y >= rows) return null;
            var to = y * columns + x;
            if (to >= isBay.Count || !isBay[to] || isBlocked[to]) return null;
            moves.Add((cell, to, (cell % columns) * dx + (cell / columns) * dy));
        }

        return moves.OrderByDescending(move => move.Lead).Select(move => (move.From, move.To)).ToArray();
    }
}
