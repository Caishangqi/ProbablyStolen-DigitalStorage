using System;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;
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
    internal static BasicCellInventory? GetCellInventory(GameItem cell, Action<string> log, StorageDiagnostics? diagnostics = null)
    {
        if (!IsCell(cell) || SaveManager.IsDecoding || CellPages.Tabs(cell) == null) return null;
        var health = CellHealth.Get(cell);
        if (!health.IsMountable) return null;
        try
        {
            var state = health.Status == CellHealthStatus.ReadOnly ? CellWriteState.ExtractOnly : CellWriteState.Writable;
            if (!CellPages.IsComplete(cell, out var failure))
                throw new InvalidOperationException("The pages do not match the layout. " + failure);
            var view = CellPages.ReadCached(cell, state);
            diagnostics?.VerboseOnce(cell, "cell_mounted", "Digital Storage: cell mounted.", () =>
                NativeStorageDiagnosticContext.Cell(cell) + $" types={view.TypeCount} area={view.Usage.AreaUnits}");
            return new BasicCellInventory(view, new CellRootPort(cell, StorageService.Identify, view));
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            var reason = error is ArgumentException ? CellUnavailableReason.InvalidTypeLimit : CellUnavailableReason.InvalidLayout;
            CellHealth.Set(cell, new CellHealthState(CellHealthStatus.Unavailable, reason));
            const string message = "Digital Storage: cell cannot be read and is now unavailable.";
            if (diagnostics == null)
                StorageDiagnostics.Report(log, "cell_read_failed", message, error, () => NativeStorageDiagnosticContext.Cell(cell));
            else diagnostics.Failure(cell, "cell_read_failed", message, error, () => NativeStorageDiagnosticContext.Cell(cell));
            return null;
        }
    }

    /// <summary>
    /// Adds the cell summary for players: bytes, item types, partition, then the storage format and,
    /// when needed, a status and upgrade line. Stored items are not listed.
    /// </summary>
    internal void AddCellInformationToTooltip(RichTextBuilder builder, GameItem cell, Func<bool>? isActive = null)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (isActive?.Invoke() == false) return;
        if (cell == null || CellPages.Tabs(cell) == null)
        {
            Add(builder, isActive, "cell_storage_unavailable");
            return;
        }

        var input = new CellLoreInput
        {
            Schema = CellTags.ReadSchema(cell),
            Health = CellHealth.Get(cell),
            ExpansionStage = CellUpgradeInventory.Stage(cell)
        };
        if (input.Health.Status != CellHealthStatus.Unavailable && StorageCellTier.FromCellId(cell.identifier) != null &&
            !SaveManager.IsDecoding && CellPages.IsComplete(cell))
        {
            try
            {
                var state = input.Health.Status == CellHealthStatus.ReadOnly ? CellWriteState.ExtractOnly : CellWriteState.Writable;
                input.View = CellPages.ReadCached(cell, state);
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            {
                // The lore only reports; the next storage operation records the cell as unavailable.
                input.Health = new CellHealthState(CellHealthStatus.Unavailable, CellUnavailableReason.InvalidLayout);
            }
        }

        foreach (var line in CellLore.Lines(input))
        {
            if (isActive?.Invoke() == false) break;
            Add(builder, isActive, line.Key, line.Arguments);
        }
    }

    private void Add(RichTextBuilder builder, Func<bool>? isActive, string key, params object[] arguments)
    {
        var text = _text.Get(key, arguments);
        if (isActive?.Invoke() != false)
            builder.AddLine(text, ignoreEmpty: true, RenderHandler.ColorPalette.LightBlue);
    }
}
