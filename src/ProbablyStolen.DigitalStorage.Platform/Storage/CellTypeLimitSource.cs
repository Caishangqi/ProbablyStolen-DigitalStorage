using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>Reads the modules that set a cell's effective type limit.</summary>
internal static class CellTypeLimitSource
{
    /// <summary>Gets the modules installed in a cell's module bay.</summary>
    internal static IUpgradeInventory Read(GameItem cell) => CellUpgradeInventory.Read(cell);

    /// <summary>Gets the effective limit of an assembled cell, or the base limit of a housing without a tier.</summary>
    internal static int EffectiveOrBase(GameItem cell)
    {
        var tier = StorageCellTier.FromCellId(cell.identifier);
        return tier == null ? StorageCellTier.BaseTypeLimit : CellTypeLimit.Effective(tier, Read(cell));
    }
}
