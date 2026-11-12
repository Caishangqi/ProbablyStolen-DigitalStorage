using System;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Localization;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

internal sealed partial class BasicCellHandler
{
    private readonly ModText _text;

    /// <summary>Creates a native text presenter over the mod localization adapter.</summary>
    internal BasicCellHandler(ModText text)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <summary>
    /// Reads a cell for one storage operation. Returns null for
    /// cells that must not be mounted: locked, unavailable, still decoding, or unreadable. An unreadable cell becomes
    /// unavailable; it is never treated as empty.
    /// </summary>
    internal static BasicCellInventory? GetCellInventory(GameItem cell, Action<string> log)
    {
        if (!IsCell(cell) || SaveManager.IsDecoding || CellPages.Tabs(cell) == null) return null;
        var health = CellHealth.Get(cell);
        if (!health.IsMountable) return null;
        try
        {
            var state = health.Status == CellHealthStatus.ReadOnly ? CellWriteState.ExtractOnly : CellWriteState.Writable;
            if (!CellPages.IsComplete(cell)) throw new InvalidOperationException("The pages do not match the layout.");
            var view = CellPages.Read(cell, StorageService.Identify, state);
            return new BasicCellInventory(view, new CellRootPort(cell, StorageService.Identify));
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            var reason = error is ArgumentException ? CellUnavailableReason.InvalidTypeLimit : CellUnavailableReason.InvalidLayout;
            CellHealth.Set(cell, new CellHealthState(CellHealthStatus.Unavailable, reason));
            log($"Digital Storage: {cell.identifier} cannot be read ({error.GetType().Name}); it is now unavailable.");
            return null;
        }
    }

    /// <summary>
    /// Adds the cell summary for players: bytes, item types, partition, then the storage format and,
    /// when needed, a status and upgrade line. Stored items are not listed.
    /// </summary>
    internal void AddCellInformationToTooltip(RichTextBuilder builder, GameItem cell)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (cell == null || CellPages.Tabs(cell) == null)
        {
            Add(builder, "cell_storage_unavailable");
            return;
        }

        var input = new CellLoreInput
        {
            Schema = CellTags.ReadSchema(cell), Health = CellHealth.Get(cell), ExpansionStage = CellUpgradeInventory.Stage(cell)
        };
        if (input.Health.Status != CellHealthStatus.Unavailable && StorageCellTier.FromCellId(cell.identifier) != null &&
            !SaveManager.IsDecoding && CellPages.IsComplete(cell))
        {
            try
            {
                var state = input.Health.Status == CellHealthStatus.ReadOnly ? CellWriteState.ExtractOnly : CellWriteState.Writable;
                input.View = CellPages.Read(cell, StorageService.Identify, state);
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            {
                // The lore only reports; the next storage operation records the cell as unavailable.
                input.Health = new CellHealthState(CellHealthStatus.Unavailable, CellUnavailableReason.InvalidLayout);
            }
        }

        foreach (var line in CellLore.Lines(input)) Add(builder, line.Key, line.Arguments);
    }

    private void Add(RichTextBuilder builder, string key, params object[] arguments) =>
        builder.AddLine(_text.Get(key, arguments), ignoreEmpty: true, RenderHandler.ColorPalette.LightBlue);
}
