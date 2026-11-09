using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Application.Storage;

/// <summary>
/// Orders candidate cells for one item type, following the preferred-storage idea of AE2's
/// <c>MEInventoryHandler.isPreferredStorageFor</c>: cells whose partition lists the type first, then unfiltered
/// cells that already hold it, then other unfiltered cells. Cells whose partition rejects the type are excluded.
/// </summary>
public static class StorageRouter
{
    /// <summary>Gets candidate indices into <paramref name="cells"/> in preference order, keeping input order within a group.</summary>
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

        var partitioned = new List<int>();
        var holding = new List<int>();
        var others = new List<int>();
        for (var index = 0; index < cells.Count; index++)
        {
            var cell = cells[index];
            if (!cell.Partition.Matches(key))
            {
                continue;
            }

            if (cell.Partition.Mode == CellPartitionMode.Whitelist)
            {
                partitioned.Add(index);
            }
            else if (cell.Contains(key))
            {
                holding.Add(index);
            }
            else
            {
                others.Add(index);
            }
        }

        partitioned.AddRange(holding);
        partitioned.AddRange(others);
        return partitioned;
    }
}
