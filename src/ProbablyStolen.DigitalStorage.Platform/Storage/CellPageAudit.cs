using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Platform.Localization;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// After a save loads, compares each cell's actual page count with its page count tag. Fewer pages mean the
/// restore postfix did not run in time and some items were not reattached; the cell is marked unavailable and
/// the player is told not to save, so the save file on disk still holds the items.
/// </summary>
internal sealed class CellPageAudit
{
    private const string WindowId = "digital_storage.page_restore_warning";
    private static readonly HashSet<GameItem> Unavailable = new();
    private readonly ModText _text;
    private readonly ModLog _log;

    internal CellPageAudit(ModText text, ModLog log)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Gets whether a cell failed the last load audit.</summary>
    internal static bool IsUnavailable(GameItem cell) => Unavailable.Contains(cell);

    /// <summary>Audits every cell reachable from the store's inventories.</summary>
    internal void Run()
    {
        Unavailable.Clear();
        var failed = 0;
        foreach (var item in EmporiumEntry.Instance.GetAllItems())
        {
            if (CellPages.Tabs(item) == null)
            {
                continue;
            }

            if (!CellTags.HasValidPageCount(item) || CellPages.Pages(item).Count < CellTags.ReadPageCount(item))
            {
                Unavailable.Add(item);
                failed++;
            }
        }

        if (CellPageRestore.LastFailure is { } error)
        {
            _log.Error($"Digital Storage: page restore failed ({error.GetType().Name}).");
        }

        if (failed == 0)
        {
            return;
        }

        _log.Error($"Digital Storage: {failed} storage cell(s) lost pages while loading. Do not save.");
        CustomUIManager.Window(WindowId, _text.Get("storage_restore_failed_title"), CustomUIManager.LAYER_PAPER)
            .AddWarning(_text.Get("storage_restore_failed_body"))
            .AddCloseButton(() => CustomUIManager.Instance.CloseWindow(WindowId))
            .Show();
    }
}
