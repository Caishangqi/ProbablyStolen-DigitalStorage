using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

internal static class CellTypeLimitSource
{
    internal static (int StorageTypeLimit, int PartitionRuleLimit) ReadOrBase(GameItem cell)
    {
        var tier = StorageCellTier.FromCellId(cell.identifier);
        return tier == null ? (CellTypeLimit.BasePartitionRuleLimit, CellTypeLimit.BasePartitionRuleLimit)
            : CellTypeLimit.Calculate(tier, CellUpgradeInventory.Read(cell));
    }
}
