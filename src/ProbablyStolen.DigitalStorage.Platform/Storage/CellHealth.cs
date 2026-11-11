using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
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
    private readonly ModLog _log;

    internal CellHealth(ModText text, ModLog log)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _log = log ?? throw new ArgumentNullException(nameof(log));
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
            if (state.RisksItemLoss) atRisk++;
        }

        if (CellPageRestore.TakeLastFailure() is { } error)
            _log.Error($"Digital Storage: page restore failed ({error.GetType().Name}).");
        if (atRisk > 0) WarnDoNotSave(atRisk, "lost pages while loading");
    }

    /// <summary>Tells the player not to save; used only when saving now could lose items.</summary>
    internal void WarnDoNotSave(int cells, string cause)
    {
        _log.Error($"Digital Storage: {cells} storage cell(s) {cause}. Do not save.");
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
            _log.Error($"Digital Storage: the do-not-save window could not be shown ({error.GetType().Name}).");
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
            CellTypeLimitSource.EffectiveOrBase(cell);
        }
        catch (ArgumentException)
        {
            return new CellHealthState(CellHealthStatus.Unavailable, CellUnavailableReason.InvalidTypeLimit);
        }

        return state;
    }
}
