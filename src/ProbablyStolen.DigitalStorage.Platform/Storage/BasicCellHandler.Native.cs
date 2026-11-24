using System;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;
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
    internal static BasicCellInventory? GetCellInventory(GameItem cell, CellDiagnostics diagnostics,
        Action? storedIdentityChanged = null, TransferOperation? operation = null, bool useCachedContents = true)
    {
        if (!IsCell(cell) || SaveManager.IsDecoding || CellPages.Tabs(cell) == null) return null;
        var health = CellHealth.Get(cell);
        if (!health.IsMountable) return null;
        try
        {
            var state = health.Status is CellHealthStatus.ReadOnly or CellHealthStatus.Protected
                ? CellWriteState.ExtractOnly : CellWriteState.Writable;
            if (!CellPages.IsComplete(cell))
                throw new InvalidOperationException("The pages do not match the layout.");
            var view = useCachedContents ? CellPages.ReadCached(cell, state) : CellPages.Read(cell, StorageService.Identify, state);
            diagnostics.Once(cell, LogVerbosity.Verbose, "cell_mounted", "Cell mounted.", () =>
                CellLogFields.For(cell, LogField.Of("types", view.TypeCount), LogField.Of("area", view.Usage.AreaUnits)));
            return new BasicCellInventory(view,
                new CellRootPort(cell, StorageService.Identify, view, storedIdentityChanged, operation));
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            var reason = error is ArgumentException ? CellUnavailableReason.InvalidTypeLimit : CellUnavailableReason.InvalidLayout;
            CellHealth.Set(cell, new CellHealthState(CellHealthStatus.Unavailable, reason));
            diagnostics.Once(cell, LogVerbosity.Error, "cell_read_failed", "Cell cannot be read and is unavailable.",
                () => CellLogFields.For(cell, LogField.Of("reason", reason.ToString())), error);
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
                var state = input.Health.Status is CellHealthStatus.ReadOnly or CellHealthStatus.Protected
                    ? CellWriteState.ExtractOnly : CellWriteState.Writable;
                input.View = CellPages.ReadCached(cell, state);
                input.Health = CellHealth.Get(cell);
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
