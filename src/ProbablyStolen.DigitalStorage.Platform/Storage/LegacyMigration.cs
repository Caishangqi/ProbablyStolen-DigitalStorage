using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Migrates schema 1 cells (12x12 pages) to schema 2 strip pages after a save loads, one item root at a time
/// through <see cref="RootTransfer"/>. Old page positions are not kept.
/// Handles schema 1 to 2 only. Delete this class when schema 3 ships; a schema 1 cell is then locked instead.
/// </summary>
/// <remarks>
/// The schema is written last, so a cell stopped partway is still pending next load and continues from its actual
/// pages; the layout descriptor always matches the pages, so a save in between restores completely. Only anchor and
/// legacy pages are drained, so roots already on strip pages are never moved twice.
/// </remarks>
internal sealed class LegacyMigration
{
    private readonly CellHealth _health;
    private readonly ModLog _log;
    private readonly HashSet<GameItem> _running = new();

    internal LegacyMigration(CellHealth health, ModLog log)
    {
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Migrates every pending cell of the loaded save; runs after the load audit and type backfill.</summary>
    internal void Run()
    {
        int pending = 0, failed = 0;
        foreach (var cell in EmporiumEntry.Instance.GetAllItems().Where(item => CellPages.Tabs(item) != null).ToArray())
        {
            if (StorageCellTier.FromCellId(cell.identifier) == null || !IsPending(cell)) continue;
            pending++;
            if (!TryMigrate(cell)) failed++;
        }

        if (pending > 0)
        {
            var completed = EmporiumEntry.Instance.GetAllItems()
                .Count(item => CellPages.Tabs(item) != null && CellTags.ReadSchema(item) == CellSchema.Current);
            _log.Log($"Digital Storage: storage format migration checked {pending} cell(s); {failed} failed; " +
                $"{completed} cell(s) now use format v{CellSchema.Current}.");
        }

        if (failed > 0) _health.WarnDoNotSave(failed, "failed to migrate");
    }

    /// <summary>Gets whether a cell still uses schema 1 and may be migrated now.</summary>
    internal static bool IsPending(GameItem cell) =>
        CellSchema.Classify(CellTags.ReadSchema(cell)) == SchemaGate.Migrate &&
        CellHealth.Get(cell).Status == CellHealthStatus.Available && !SaveManager.IsDecoding;

    /// <summary>
    /// Migrates one cell if it is pending. Returns false only when migration failed and the cell is now unavailable;
    /// a refused migration leaves the cell extract-only and returns true.
    /// </summary>
    internal bool TryMigrate(GameItem cell)
    {
        if (StorageCellTier.FromCellId(cell.identifier) == null || !IsPending(cell) || !_running.Add(cell)) return true;
        try
        {
            Migrate(cell);
            return true;
        }
        catch (Exception error)
        {
            CellHealth.Set(cell, new CellHealthState(CellHealthStatus.Unavailable, CellUnavailableReason.MigrationFailed));
            _log.Error($"Digital Storage: migration of {cell.identifier} stopped ({error.GetType().Name}: {error.Message}).");
            return false;
        }
        finally
        {
            _running.Remove(cell);
        }
    }

    private void Migrate(GameItem cell)
    {
        if (!CellTags.TryReadLayout(cell, out var layout)) throw new InvalidOperationException("The layout cannot be read.");
        if (!CellPages.IsComplete(cell)) throw new InvalidOperationException("The pages do not match the layout.");

        if (layout!.Pages.Count == 1 && CellPages.IsEmpty(cell))
        {
            CellTags.WriteLayout(cell, layout);
            CellTags.WriteSchema(cell, CellSchema.Current);
            return;
        }

        var view = CellPages.Read(cell, StorageService.Identify, CellWriteState.Writable);
        var source = new CellRootSource(cell, StorageService.Identify, null, unslottedOnly: true);
        var before = Capture(cell);
        var forecast = RootDrainSimulator.Forecast(view, source.Snapshot());
        if (!forecast.Accepted)
        {
            CellHealth.Set(cell, new CellHealthState(CellHealthStatus.ReadOnly));
            _log.Warning($"Digital Storage: migration of {cell.identifier} refused ({forecast.Rejection}); the cell is extract-only.");
            return;
        }

        // From here on the descriptor is the source of truth for restoring pages; page_count holds the downgrade guard.
        CellTags.WriteLayout(cell, layout);
        var drain = RootTransfer.Drain(source, new CellRootSink(view, new CellRootPort(cell, StorageService.Identify),
            PlanScope.Relocate), MigrationSource.Instance);
        if (!drain.Completed) throw new InvalidOperationException($"The drain stopped at {drain.LastStep}.");
        Verify(before, Capture(cell));

        CellPages.Compact(cell);
        if (CellHealth.IsUnavailable(cell)) throw new InvalidOperationException("Removing the legacy pages failed.");
        if (!CellTags.TryReadLayout(cell, out var after) || after!.Pages.Any(page => page.Kind == PageKind.Legacy))
        {
            // Compaction was blocked and rolled back: the empty legacy pages stay and the schema stays 1, so the next
            // load finishes the job. The cell is usable meanwhile.
            return;
        }

        CellTags.WriteSchema(cell, CellSchema.Current);
    }

    private static Dictionary<GameItem, (int Units, int Width, int Height, int Orientation, bool Flipped)> Capture(GameItem cell) =>
        CellPages.Roots(cell).ToDictionary(root => root, root => (root.unitCount, root.modifiedShape.globalWidth,
            root.modifiedShape.globalHeight, root.modifiedShape.orientation, root.modifiedShape.flipped));

    /// <summary>The same root objects, with the same units, size, orientation and flip; only positions change.</summary>
    private static void Verify(
        Dictionary<GameItem, (int Units, int Width, int Height, int Orientation, bool Flipped)> before,
        Dictionary<GameItem, (int Units, int Width, int Height, int Orientation, bool Flipped)> after)
    {
        if (before.Count != after.Count) throw new InvalidOperationException("The cell root count changed during migration.");
        foreach (var entry in before)
        {
            if (!after.TryGetValue(entry.Key, out var now) || !now.Equals(entry.Value))
                throw new InvalidOperationException("A root changed during migration.");
        }
    }
}
