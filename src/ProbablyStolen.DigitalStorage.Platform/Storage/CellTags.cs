using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Reads and writes the storage tags kept on a cell item. Item tags are part of the native <c>itemState</c>,
/// so the native save graph persists them with the cell.
/// </summary>
internal static class CellTags
{
    internal const string PageCountTag = "digital_storage.page_count";
    internal const string PartitionTag = "digital_storage.partition";

    /// <summary>
    /// Gets the recorded page count, clamped to [1, <see cref="CellLayout.AbsoluteMaxPages"/>]; a cell without the
    /// tag has one page. It is deliberately not clamped to the configured limit: saves restore child items by page
    /// position, so a page that is not rebuilt loses its items.
    /// </summary>
    internal static int ReadPageCount(GameItem cell)
    {
        var count = cell.IsTag(PageCountTag) ? cell.GetTagReadonly(PageCountTag).GetInt() : 1;
        return Math.Max(1, Math.Min(count, CellLayout.AbsoluteMaxPages));
    }

    internal static void WritePageCount(GameItem cell, int count) =>
        cell.ModifyTag(PageCountTag, state => state.Enable().SetInt(count));

    /// <summary>Gets the cell partition; an absent tag means unfiltered, an unreadable tag yields an invalid partition.</summary>
    internal static CellPartition ReadPartition(GameItem cell)
    {
        if (!cell.IsTag(PartitionTag))
        {
            return CellPartition.Unfiltered;
        }

        var text = cell.GetTagReadonly(PartitionTag).GetString() ?? string.Empty;
        var keys = new List<ItemKey?>();
        foreach (var line in text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            keys.Add(CanonicalFilterKeyCodec.TryDecode(line, out var key) ? key : null);
        }

        return CellPartition.FromConfiguration(
            keys.Count == 0 ? CellPartitionMode.Unfiltered : CellPartitionMode.Whitelist, keys);
    }

    /// <summary>Writes a partition; an unfiltered partition removes the tag.</summary>
    internal static void WritePartition(GameItem cell, CellPartition partition)
    {
        if (partition.Mode == CellPartitionMode.Unfiltered || partition.Keys.Count == 0)
        {
            cell.DisableTag(PartitionTag);
            return;
        }

        var text = string.Join("\n", partition.Keys.Select(key => CanonicalFilterKeyCodec.Encode(key)));
        cell.ModifyTag(PartitionTag, state => state.Enable().SetString(text));
    }
}
