using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>One localized lore line: a text key and its arguments.</summary>
public readonly struct LoreLine
{
    /// <summary>Creates a line.</summary>
    public LoreLine(string key, params object[] arguments)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Arguments = arguments ?? Array.Empty<object>();
    }

    /// <summary>Gets the text key.</summary>
    public string Key { get; }

    /// <summary>Gets the arguments.</summary>
    public object[] Arguments { get; }

    /// <inheritdoc />
    public override string ToString() => Key + "(" + string.Join(",", Arguments) + ")";
}

/// <summary>What a cell's lore shows.</summary>
public sealed class CellLoreInput
{
    /// <summary>Gets or sets the recorded schema.</summary>
    public int Schema { get; set; } = CellSchema.Current;

    /// <summary>Gets or sets the session state.</summary>
    public CellHealthState Health { get; set; } = CellHealthState.Available;

    /// <summary>Gets or sets the cell contents, or null when they cannot be read.</summary>
    public CellView? View { get; set; }

    /// <summary>Gets or sets the module bay expansion stage.</summary>
    public int ExpansionStage { get; set; }
}

/// <summary>
/// Builds cell lore in a fixed order: bytes, types, partition, storage format, then any status line.
/// Upgrade guidance appears in the lore instead of a dialog.
/// </summary>
public static class CellLore
{
    /// <summary>Gets the lore lines.</summary>
    public static IReadOnlyList<LoreLine> Lines(CellLoreInput input)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        var lines = new List<LoreLine>();
        if (input.View is { } view)
        {
            lines.Add(new LoreLine("cell_storage_bytes", view.UsedBytes(), view.Rules.Tier.GetBytes()));
            lines.Add(new LoreLine("cell_storage_types", view.TypeCount, view.TypeLimit));
            var partition = view.Partition;
            if (!partition.IsValid)
                lines.Add(new LoreLine("cell_storage_partition_invalid"));
            else if (partition.RuleCount > 0)
                lines.Add(new LoreLine(PartitionKey(partition), partition.RuleCount, view.TypeLimit));
            var typeModules = view.Upgrades.GetInstalledUpgrades(CellModuleKind.TypeLimit);
            if (typeModules > 0) lines.Add(new LoreLine("cell_storage_type_modules", typeModules));
            if (view.EqualDistribution) lines.Add(new LoreLine("cell_storage_equal_distribution"));
            if (input.ExpansionStage > 0)
                lines.Add(new LoreLine("cell_storage_expansion", input.ExpansionStage, ModuleBayExpansion.MaxStage(view.Rules.Tier)));
        }

        lines.Add(new LoreLine("cell_storage_schema", input.Schema, CellSchema.Current));
        if (StatusLine(input) is { } status) lines.Add(status);
        return lines;
    }

    private static string PartitionKey(CellPartition partition) =>
        partition.Mode == PartitionMode.Blacklist ? "cell_storage_partition_exclude" : "cell_storage_partition_include";

    private static LoreLine? StatusLine(CellLoreInput input)
    {
        var health = input.Health;
        switch (health.Status)
        {
            case CellHealthStatus.ReadOnly:
                return new LoreLine("cell_storage_migration_refused");
            case CellHealthStatus.CompactionBlocked:
                return new LoreLine("cell_storage_compaction_blocked");
            case CellHealthStatus.Locked:
                return CellSchema.UpgradePath.TryGetValue(input.Schema, out var version)
                    ? new LoreLine("cell_storage_locked", version)
                    : new LoreLine("cell_storage_unavailable");
            case CellHealthStatus.Unavailable:
                return health.Reason switch
                {
                    CellUnavailableReason.SchemaTooNew => new LoreLine("cell_storage_schema_too_new", CellSchema.Current),
                    CellUnavailableReason.MigrationFailed => new LoreLine("cell_storage_migration_failed"),
                    CellUnavailableReason.RollbackFailed => new LoreLine("cell_storage_rollback_failed"),
                    CellUnavailableReason.InvalidTypeLimit => new LoreLine("cell_storage_type_limit_invalid"),
                    _ => new LoreLine("cell_storage_unavailable")
                };
            default:
                return null;
        }
    }
}
