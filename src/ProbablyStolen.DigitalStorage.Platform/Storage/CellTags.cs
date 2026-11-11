using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Reads and writes the storage tags kept on a cell item. Item tags are part of the native <c>itemState</c>,
/// so the native save graph persists them with the cell. Their meaning is decided by Common <see cref="CellRecord"/>.
/// Unknown or invalid values are never rewritten here; readers report them and the cell becomes unavailable.
/// </summary>
internal static class CellTags
{
    internal const string SchemaTag = "digital_storage.schema";
    internal const string LayoutTag = "digital_storage.layout";
    internal const string PageCountTag = "digital_storage.page_count";
    internal const string PartitionTag = "digital_storage.partition";
    internal const string PartitionModeTag = "digital_storage.partition_mode";
    internal const string PartitionEqualTag = "digital_storage.partition_equal";

    /// <summary>Gets the storage schema; a cell without the tag is schema 1.</summary>
    internal static int ReadSchema(GameItem cell) =>
        CellRecord.Schema(cell.IsTag(SchemaTag), cell.IsTag(SchemaTag) ? cell.GetTagReadonly(SchemaTag).GetInt() : 0);

    internal static void WriteSchema(GameItem cell, int schema) =>
        cell.ModifyTag(SchemaTag, state => state.Enable().SetInt(schema));

    /// <summary>
    /// Gets the page layout: the descriptor when present, otherwise the schema 1 page count. Values that cannot be
    /// read give false; they are never shortened, because native decoding restores child items by page position.
    /// </summary>
    internal static bool TryReadLayout(GameItem cell, out LayoutDescriptor? layout)
    {
        var text = cell.IsTag(LayoutTag) ? cell.GetTagReadonly(LayoutTag).GetString() ?? string.Empty : null;
        var hasCount = cell.IsTag(PageCountTag);
        return CellRecord.TryLayout(text, hasCount, hasCount ? cell.GetTagReadonly(PageCountTag).GetInt() : 0, out layout);
    }

    /// <summary>Writes the layout descriptor and the downgrade guard page count together.</summary>
    internal static void WriteLayout(GameItem cell, LayoutDescriptor layout)
    {
        var text = layout.Format();
        cell.ModifyTag(LayoutTag, state => state.Enable().SetString(text));
        cell.ModifyTag(PageCountTag, state => state.Enable().SetInt(CellRecord.DowngradeGuardPageCount));
    }

    /// <summary>Gets whether the recorded layout equals <paramref name="layout"/>.</summary>
    internal static bool HasLayout(GameItem cell, LayoutDescriptor layout) =>
        TryReadLayout(cell, out var recorded) && layout.Equals(recorded);

    /// <summary>
    /// Gets the cell partition; an absent tag means unfiltered. Unreadable entries, an unknown mode or more entries
    /// than the effective type limit give an invalid partition, which refuses new items but keeps stored ones readable.
    /// </summary>
    internal static CellPartition ReadPartition(GameItem cell, int maxKeys)
    {
        var modeText = cell.IsTag(PartitionModeTag) ? cell.GetTagReadonly(PartitionModeTag).GetString() ?? string.Empty : null;
        // An unknown mode is passed on as an undefined value so the partition reports UnknownMode.
        var mode = CellRecord.PartitionMode(modeText) ?? (IncludeExclude)(-1);
        var equal = cell.IsTag(PartitionEqualTag);
        var keys = new List<ItemKey?>();
        if (cell.IsTag(PartitionTag))
        {
            var text = cell.GetTagReadonly(PartitionTag).GetString() ?? string.Empty;
            foreach (var line in text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                keys.Add(CanonicalFilterKeyCodec.TryDecode(line, out var key) ? key : null);
            }
        }

        return CellPartition.FromConfiguration(mode, keys, equal, maxKeys);
    }

    /// <summary>Gets the partition with the cell's effective type limit as its entry limit.</summary>
    internal static CellPartition ReadPartition(GameItem cell) => ReadPartition(cell, CellTypeLimitSource.EffectiveOrBase(cell));

    /// <summary>Writes a partition. A whitelist removes the mode tag, so data stays readable by older versions.</summary>
    internal static void WritePartition(GameItem cell, CellPartition partition)
    {
        if (!partition.IsValid) throw new ArgumentException("An invalid partition cannot be written.", nameof(partition));
        if (partition.List.IsEmpty())
        {
            cell.DisableTag(PartitionTag);
        }
        else
        {
            var text = string.Join("\n", partition.List.GetItems().Select(key => CanonicalFilterKeyCodec.Encode(key)));
            cell.ModifyTag(PartitionTag, state => state.Enable().SetString(text));
        }

        if (partition.Mode == IncludeExclude.Blacklist)
            cell.ModifyTag(PartitionModeTag, state => state.Enable().SetString(CellRecord.BlacklistMode));
        else
            cell.DisableTag(PartitionModeTag);

        if (partition.EqualDistribution) cell.ModifyTag(PartitionEqualTag, state => state.Enable().SetInt(1));
        else cell.DisableTag(PartitionEqualTag);
    }
}
