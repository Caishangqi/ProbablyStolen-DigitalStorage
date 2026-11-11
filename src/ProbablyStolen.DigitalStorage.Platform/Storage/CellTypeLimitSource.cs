using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>Reads the type limit extensions that apply to a cell.</summary>
internal static class CellTypeLimitSource
{
    /// <summary>Gets the extensions of a cell. No content grants one yet; a future module only changes this.</summary>
    internal static IReadOnlyList<TypeLimitExtension> Read(GameItem cell) => Array.Empty<TypeLimitExtension>();

    /// <summary>Gets the effective limit of an assembled cell, or the base limit of a housing without a tier.</summary>
    /// <exception cref="ArgumentException">An extension is invalid; the cell is then unavailable.</exception>
    internal static int EffectiveOrBase(GameItem cell)
    {
        var tier = StorageCellTier.FromCellId(cell.identifier);
        return tier == null ? StorageCellTier.BaseTypeLimit : CellTypeLimit.Effective(tier, Read(cell));
    }
}
