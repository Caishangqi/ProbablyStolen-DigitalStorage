using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Client.Machines;

/// <summary>
/// Plans a block move within one bay grid. The grabbed cell lands under the pointer and the other cells keep their
/// offsets from it.
/// </summary>
public static class BayGroupShift
{
    /// <summary>
    /// Plans a shift in a grid indexed row by row (<c>y * columns + x</c>). Returns null if the group stays in place
    /// or any destination is outside the grid, lacks a bay, or holds a cell outside the group.
    /// </summary>
    /// <param name="columns">The grid width in slots.</param>
    /// <param name="isBay">Which positions contain bays.</param>
    /// <param name="isBlocked">Which bays hold cells outside the group.</param>
    /// <param name="group">The occupied positions in the group.</param>
    /// <param name="anchor">The grabbed position in <paramref name="group"/>.</param>
    /// <param name="target">The position under the pointer.</param>
    /// <returns>Moves starting at the leading edge so each destination is vacant when its turn comes.</returns>
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
