using System;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Formats storage cell lore from the cell's live pages.</summary>
public sealed class CellTooltipPresenter
{
    private readonly ModText _text;

    /// <summary>Creates a native text presenter over the mod localization adapter.</summary>
    internal CellTooltipPresenter(ModText text)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <summary>
    /// Adds the player-facing cell summary, AE2 style: one usage line (space and item kinds), plus a partition line
    /// only when a whitelist is set or the stored partition is invalid. Stored items are not listed.
    /// </summary>
    internal void AddCellLore(RichTextBuilder builder, GameItem cell)
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (cell == null || CellPages.Tabs(cell) == null || CellPageAudit.IsUnavailable(cell))
        {
            Add(builder, "cell_storage_unavailable");
            return;
        }

        var layout = CellPages.Layout;
        var view = CellPages.Read(cell, NetworkStorage.Identify);
        var usedCells = CellPages.Roots(cell).Sum(root =>
        {
            var size = CellPages.SizeOf(root);
            return size.Width * size.Height;
        });
        // A cell keeps its pages when the limit is lowered, so its capacity is whichever is larger.
        var capacity = Math.Max(view.Pages.Count, layout.MaxPages) * layout.CellsPerPage;
        Add(builder, "cell_storage_usage", usedCells, capacity, view.TypeCount, layout.MaxTypes);
        var partition = view.Partition;
        if (!partition.IsValid)
        {
            Add(builder, "cell_storage_partition_invalid");
        }
        else if (partition.Mode != CellPartitionMode.Unfiltered)
        {
            Add(builder, "cell_storage_partitioned", partition.Keys.Count);
        }
    }

    private void Add(RichTextBuilder builder, string key, params object[] arguments) =>
        builder.AddLine(_text.Get(key, arguments), ignoreEmpty: true, RenderHandler.ColorPalette.LightBlue);
}
