using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Application.Storage;

/// <summary>
/// Orders cells like AE2's <c>NetworkStorage</c>. Inserting walks priority groups from high to low and runs the
/// two-pass <c>insert</c> inside each group: first every cell that is preferred storage for the type
/// (<c>MEInventoryHandler.isPreferredStorageFor</c>: its whitelist lists the type, or it already holds the type), then
/// every other accepting cell. Extracting walks the groups from low to high (<c>extract</c> uses
/// <c>descendingMap</c>). Within a pass or group, cells keep mount order.
/// </summary>
public static class StorageRouter
{
    /// <summary>
    /// Gets candidate indices into <paramref name="cells"/> for storing one type. Cells whose partition rejects the
    /// type are excluded.
    /// </summary>
    public static IReadOnlyList<int> Order(IReadOnlyList<CellView> cells, ItemKey key)
    {
        if (cells == null)
        {
            throw new ArgumentNullException(nameof(cells));
        }

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        var result = new List<int>();
        foreach (var group in GroupByPriority(cells.Select(cell => cell.Priority).ToArray(), descending: true))
        {
            var others = new List<int>();
            foreach (var index in group)
            {
                var cell = cells[index];
                if (!cell.Partition.Matches(key))
                {
                    continue;
                }

                if (cell.Partition.Mode == CellPartitionMode.Whitelist || cell.Contains(key))
                {
                    result.Add(index);
                }
                else
                {
                    others.Add(index);
                }
            }

            result.AddRange(others);
        }

        return result;
    }

    /// <summary>Gets cell indices for extracting: lowest priority first, mount order within a priority.</summary>
    public static IReadOnlyList<int> ExtractOrder(IReadOnlyList<int> priorities)
    {
        if (priorities == null)
        {
            throw new ArgumentNullException(nameof(priorities));
        }

        return GroupByPriority(priorities, descending: false).SelectMany(group => group).ToArray();
    }

    // OrderBy is a stable sort, so mount order survives inside each priority.
    private static IEnumerable<IEnumerable<int>> GroupByPriority(IReadOnlyList<int> priorities, bool descending)
    {
        var groups = Enumerable.Range(0, priorities.Count).GroupBy(index => priorities[index]);
        return descending ? groups.OrderByDescending(group => group.Key) : groups.OrderBy(group => group.Key);
    }
}
