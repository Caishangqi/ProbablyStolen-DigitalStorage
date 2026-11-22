using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Localization;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Session state of every cell. After a save loads it checks each cell's schema, layout and rebuilt pages. Cells
/// that could lose items when saved are unavailable and the player is told not to save, so the save file on disk
/// still holds the items. Locked cells (schema too old) keep their restored pages and are safe to save; their
/// guidance is shown only in lore, never in a window.
/// </summary>
internal sealed class CellHealth
{
    private const string WindowId = "digital_storage.page_restore_warning";
    private static readonly Dictionary<GameItem, CellHealthState> States = new();
    private static CellHealth? _current;
    private readonly ModText _text;
    private readonly CellDiagnostics _diagnostics;

    internal CellHealth(ModText text, CellDiagnostics diagnostics)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _diagnostics = diagnostics;
        _current = this;
    }

    /// <summary>Releases the static handle when the composition that created this instance is disposed.</summary>
    internal void Detach()
    {
        if (ReferenceEquals(_current, this)) _current = null;
    }

    /// <summary>Warns about one cell that became unsafe to save during play, outside the load audit.</summary>
    internal static void WarnDoNotSaveDuringPlay(string cause) => _current?.WarnDoNotSave(1, cause);

    /// <summary>Gets the session state of a cell; cells never checked are available.</summary>
    internal static CellHealthState Get(GameItem cell) =>
        cell != null && States.TryGetValue(cell, out var state) ? state : CellHealthState.Available;

    /// <summary>Gets whether a cell is unavailable.</summary>
    internal static bool IsUnavailable(GameItem cell) => Get(cell).Status == CellHealthStatus.Unavailable;

    /// <summary>Records a new state; an unavailable cell never becomes available again this session.</summary>
    internal static void Set(GameItem cell, CellHealthState state)
    {
        if (IsUnavailable(cell)) return;
        if (state.Status == CellHealthStatus.Available) States.Remove(cell);
        else States[cell] = state;
    }

    /// <summary>Checks every cell reachable from the store's inventories after a save loaded.</summary>
    internal void Run()
    {
        States.Clear();
        var atRisk = 0;
        foreach (var item in EmporiumEntry.Instance.GetAllItems())
        {
            if (CellPages.Tabs(item) == null) continue;
            var state = Assess(item);
            Set(item, state);
            if (state.RisksItemLoss)
            {
                atRisk++;
                _diagnostics.Once(item, LogVerbosity.Error, "cell_health_unavailable", "A storage cell may lose items if saved.",
                    () => CellLogFields.For(item, LogField.Of("reason", state.Reason.ToString())));
            }
        }

        if (CellPageRestore.TakeLastFailure() is { } error)
            _diagnostics.Write(LogVerbosity.Error, "cell_page_restore_failed", "Page restore failed.", error: error);
        if (atRisk > 0) WarnDoNotSave(atRisk, "lost pages while loading");
    }

    /// <summary>Tells the player not to save; used only when saving now could lose items.</summary>
    internal void WarnDoNotSave(int cells, string cause)
    {
        _diagnostics.Write(LogVerbosity.Error, "storage_save_risk", "Do not save while storage cells are at risk.",
            () => new[] { LogField.Of("cells", cells), LogField.Of("reason", cause) });
        try
        {
            // The window id is shared, so a second warning replaces the first instead of stacking.
            CustomUIManager.Window(WindowId, _text.Get("storage_restore_failed_title"), CustomUIManager.LAYER_PAPER)
                .AddWarning(_text.Get("storage_restore_failed_body"))
                .AddCloseButton(() => CustomUIManager.Instance.CloseWindow(WindowId))
                .Show();
        }
        catch (Exception error)
        {
            _diagnostics.Write(LogVerbosity.Error, "restore_warning_failed", "The save risk warning could not be shown.", error: error);
        }
    }

    private static CellHealthState Assess(GameItem cell)
    {
        var gate = CellSchema.Classify(CellTags.ReadSchema(cell));
        var layoutValid = CellTags.TryReadLayout(cell, out _);
        var state = CellHealthState.AfterLoad(gate, layoutValid, layoutValid && CellPages.IsComplete(cell));
        if (state.Status == CellHealthStatus.Unavailable) return state;
        try
        {
            _ = CellTypeLimitSource.ReadOrBase(cell).StorageTypeLimit;
        }
        catch (ArgumentException)
        {
            return new CellHealthState(CellHealthStatus.Unavailable, CellUnavailableReason.InvalidTypeLimit);
        }

        return state;
    }
}
