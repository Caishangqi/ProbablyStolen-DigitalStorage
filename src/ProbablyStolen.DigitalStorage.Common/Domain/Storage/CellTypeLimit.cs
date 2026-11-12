using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>The only place that computes a cell's effective type limit.</summary>
public static class CellTypeLimit
{
    /// <summary>
    /// Gets <c>floor((base + sumAdd) * (100 + sumPercent) / 100)</c> over the valid type limit modules, at least 1: flat bonuses
    /// first, then percentages summed, not compounded. There is no upper clamp: the type cost is spread over the
    /// limit (<see cref="StorageCellTier.GetTypeBudget"/>), so a full cell's types always cost half its bytes.
    /// </summary>
    /// <exception cref="ArgumentNullException">No tier or module view was supplied.</exception>
    public static int Effective(StorageCellTier tier, IUpgradeInventory modules)
    {
        if (tier == null) throw new ArgumentNullException(nameof(tier));
        if (modules == null) throw new ArgumentNullException(nameof(modules));

        long add = 0, percent = 0;
        foreach (var module in modules.Modules)
        {
            // An unreadable roll adds nothing, so a damaged module can never raise the limit.
            if (module.Kind != CellModuleKind.TypeLimit || !module.IsValid) continue;
            add = checked(add + module.Roll!.Add);
            percent = checked(percent + module.Roll.Percent);
        }

        var total = checked((tier.GetTotalTypes() + add) * (100 + percent) / 100);
        return (int)Math.Max(1, Math.Min(total, int.MaxValue));
    }
}
