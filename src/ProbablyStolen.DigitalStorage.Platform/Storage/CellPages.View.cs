using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

internal static partial class CellPages
{
    /// <summary>Builds a cell view over page contents, reading the cell's current settings.</summary>
    private static CellView ViewOf(GameItem cell, IReadOnlyList<PageView> pages, CellUsage usage, CellWriteState writeState)
    {
        if (pages.Any(page => page.IsProtected))
        {
            if (CellHealth.Get(cell).Status == CellHealthStatus.Available)
                CellHealth.Set(cell, new CellHealthState(CellHealthStatus.Protected));
            writeState = CellWriteState.ExtractOnly;
        }
        var tier = StorageCellTier.FromCellId(cell.identifier) ?? throw new InvalidOperationException("Unknown storage cell tier.");
        var upgrades = CellUpgradeInventory.Read(cell);
        var limits = CellTypeLimit.Calculate(tier, upgrades);
        return new CellView(CellRules.For(tier), limits.StorageTypeLimit, limits.PartitionRuleLimit, pages, usage,
            CellPartitionTags.Read(cell), DrivePriorityTag.ReadForCell(cell), writeState, upgrades);
    }
}
