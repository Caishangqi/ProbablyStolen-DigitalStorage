using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

/// <summary>Builds partitions and module sets for storage tests.</summary>
internal static class Partitions
{
    /// <summary>Gets a partition with one default "item is" rule per key.</summary>
    internal static CellPartition Of(PartitionMode mode, params ItemKey[] items) =>
        CellPartition.From(new PartitionConfig(mode, items.Select((key, slot) => PartitionRule.ForSample($"r{slot}", slot, key))),
            CanonicalAttributeSource.Instance);

    /// <summary>Gets a whitelist of default rules.</summary>
    internal static CellPartition Whitelist(params ItemKey[] items) => Of(PartitionMode.Whitelist, items);

    /// <summary>Gets a blacklist of default rules.</summary>
    internal static CellPartition Blacklist(params ItemKey[] items) => Of(PartitionMode.Blacklist, items);

    /// <summary>Gets modules that enable equal distribution.</summary>
    internal static IUpgradeInventory EqualDistribution { get; } = UpgradeInventory.Empty.With(InstalledModule.EqualDistribution());
}
