using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>Computes the storage and retained partition limits from one module snapshot.</summary>
public static class CellTypeLimit
{
    /// <summary>The retained base for partition rules, equal shares, and tierless display.</summary>
    public const int BasePartitionRuleLimit = 128;

    /// <summary>Gets both limits, applying the summed percentage after the flat bonuses and rounding down.</summary>
    public static (int StorageTypeLimit, int PartitionRuleLimit) Calculate(StorageCellTier tier, IUpgradeInventory modules)
    {
        if (tier == null) throw new ArgumentNullException(nameof(tier));
        if (modules == null) throw new ArgumentNullException(nameof(modules));

        long rawAdd = 0, effectiveAdd = 0, percent = 0;
        foreach (var module in modules.Modules)
        {
            if (module.Kind != CellModuleKind.TypeLimit || !module.IsValid) continue;
            var roll = module.Roll!;
            rawAdd = checked(rawAdd + roll.Add);
            effectiveAdd = checked(effectiveAdd + roll.EffectiveAdd);
            percent = checked(percent + roll.Percent);
        }

        return (Apply(tier.GetTotalTypes(), effectiveAdd, percent), Apply(BasePartitionRuleLimit, rawAdd, percent));
    }

    private static int Apply(int baseLimit, long add, long percent)
    {
        var total = checked((baseLimit + add) * (100 + percent) / 100);
        return (int)Math.Max(1, Math.Min(total, int.MaxValue));
    }
}
