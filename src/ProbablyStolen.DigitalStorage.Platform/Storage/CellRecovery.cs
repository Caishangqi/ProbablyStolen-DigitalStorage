using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Network;
using ProbablyStolen.DigitalStorage.Platform.Content;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Audits and repairs restored schema 2 strip roots once during late load. It is deliberately a load-only adapter:
/// normal operations continue to use <see cref="CellPages"/> and no Harmony hook is involved.
/// </summary>
internal sealed class CellRecovery
{
    private readonly CellHealth _health;
    private readonly CellDiagnostics _diagnostics;
    private readonly List<GameItem> _heldRecoveryItems = new();

    internal CellRecovery(CellHealth health, CellDiagnostics diagnostics)
    {
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    internal void Run()
    {
        if (SaveManager.IsDecoding) return;
        var worldItems = EmporiumEntry.Instance.GetAllItems().ToArray();
        var cells = worldItems.Where(item => CellPages.Tabs(item) != null).Distinct().ToArray();

        var warned = 0;
        foreach (var cell in cells)
        {
            if (!TryRecover(cell)) warned++;
        }

        // The cache is now warm for installed cells. This is the only load-time enumeration; hover and render paths
        // continue to use their existing samples and never call the recovery audit.
        var prewarmed = new HashSet<GameItem>();
        foreach (var drive in worldItems.Where(item => item.identifier == DriveItem.ItemId))
        {
            foreach (var cell in NativeCellResolver.GetCellItems(drive))
            {
                if (!prewarmed.Add(cell) || !CellHealth.Get(cell).IsMountable) continue;
                try
                {
                    _ = BasicCellHandler.GetCellInventory(cell, _diagnostics);
                }
                catch (Exception error)
                {
                    _diagnostics.Once(cell, LogVerbosity.Error, "cell_prewarm_failed", "Cell prewarm failed.",
                        () => CellLogFields.For(cell), error);
                }
            }
        }

        if (warned > 0) _health.WarnDoNotSave(warned, "storage recovery could not prove item ownership");
    }

    private bool TryRecover(GameItem cell)
    {
        if (CellHealth.IsUnavailable(cell) || CellHealth.Get(cell).Status == CellHealthStatus.Locked) return true;
        if (CellSchema.Classify(CellTags.ReadSchema(cell)) != SchemaGate.Current) return true;
        if (!CellTags.TryReadLayout(cell, out var layout))
        {
            return Fail(cell, "reason=layout_unreadable", -1, -1);
        }
        if (!CellPages.IsComplete(cell))
        {
            // CellHealth already owns the normal warning for unknown schema and broken page linkage.
            _diagnostics.Once(cell, LogVerbosity.Verbose, "cell_recovery_skipped", "Recovery skipped for an unverified cell.",
                () => CellLogFields.For(cell));
            return true;
        }

        var pages = CellPages.Pages(cell);
        var observations = new List<Observation>();
        var protectedPages = new HashSet<int>();
        var rootReferences = new HashSet<GameItem>();
        var pageViews = new List<PageView>(pages.Count);
        try
        {
            for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                var page = pages[pageIndex];
                var geometry = layout!.Pages[pageIndex];
                var slots = new HashSet<int>();
                var duplicateSlot = false;
                foreach (var item in page.childItems.ToArray())
                {
                    if (item == null || item.parentInventory != page || item.unitCount < 1)
                        return Fail(cell, "reason=ownership_or_count", pageIndex, -1);
                    if (!rootReferences.Add(item))
                        return Fail(cell, "reason=duplicate_root_reference", pageIndex, -1);
                    var slot = geometry.Kind == PageKind.Strip
                        ? StripLayout.SlotIndexAt(geometry, item.modifiedShape.minX, item.modifiedShape.minY)
                        : -1;
                    var observation = new Observation(item, pageIndex, page, slot, null, item.unitCount,
                        item.modifiedShape, CellPages.SizeOf(item)) { PageGeometry = geometry };
                    if (NeedsMove(observation))
                    {
                        observation = new Observation(item, pageIndex, page, slot, StorageService.Identify(item),
                            item.unitCount, item.modifiedShape, observation.Size)
                        {
                            PageGeometry = geometry,
                            Descendants = GraphUtils.FindAllChildrenType<GameItem>(item).ToArray()
                        };
                        if (observation.Key == null) protectedPages.Add(pageIndex);
                    }
                    if (geometry.Kind == PageKind.Strip && slot >= 0 && !slots.Add(slot)) duplicateSlot = true;
                    observations.Add(observation);
                }

                if (duplicateSlot) protectedPages.Add(pageIndex);
                pageViews.Add(protectedPages.Contains(pageIndex)
                    ? PageView.Protected(geometry, page.childItems.Count)
                    : geometry.Kind == PageKind.Strip
                        ? PageView.Strip(geometry, slots.OrderBy(slot => slot))
                        : PageView.Unslotted(geometry, page.childItems.Count));
            }
        }
        catch (Exception error)
        {
            return Fail(cell, "reason=audit_failed", -1, -1, error);
        }

        var roots = observations.Where(item => item.Key != null && NeedsMove(item) && !protectedPages.Contains(item.Page))
            .Select(item => new RecoveryRoot(item.Page, item.Slot, item.Size, item.Key!, item.Units)).ToArray();
        var plan = CellRecoveryPlanner.Plan(pageViews, roots);
        if (!plan.IsSafe)
        {
            if (plan.Failure is RecoveryFailure.UnsupportedSize or RecoveryFailure.PageLimit)
            {
                MarkDegraded(cell, observations.Where(NeedsMove).Select(item => item.Page).Concat(protectedPages).Distinct().ToArray(),
                    "planner=" + plan.Failure);
                return true;
            }
            return Fail(cell, "reason=planner_" + plan.Failure, -1, -1);
        }
        if (plan.Moves.Count == 0)
        {
            if (protectedPages.Count > 0) MarkDegraded(cell, protectedPages, "reason=identity_unreadable");
            else CellInventoryCache.ForgetWithRoots(cell);
            return true;
        }

        var anomalous = observations.Where(item => item.Key != null && NeedsMove(item) && !protectedPages.Contains(item.Page)).ToArray();
        if (anomalous.Length != plan.Moves.Count)
            return Fail(cell, "reason=plan_observation_mismatch", -1, -1);

        var journal = observations.Select(item => new Journal(item, pages[item.Page])).ToArray();
        var applied = new List<AppliedMove>();
        var attempts = new List<RecoveryAttempt>();
        try
        {
            for (var index = 0; index < plan.Moves.Count; index++)
            {
                var move = plan.Moves[index];
                if (!PageOrderMatches(cell, pages, attempts))
                    throw new RecoveryException("reason=page_order_changed");
                var entry = anomalous[index];
                var attempt = new RecoveryAttempt(entry, move);
                attempts.Add(attempt);
                if (move.AppendPage)
                {
                    if (move.TargetPage != CellPages.Pages(cell).Count)
                        throw new RecoveryException("reason=append_refused");
                    if (!CellPages.TryAppendStripPage(cell, StripLayout.PageFor(move.Size), page =>
                        {
                            attempt.AppendedPage = page;
                            attempt.Target = page;
                        }))
                    {
                        attempt.AppendedPage = null;
                        if (!PageOrderMatches(cell, pages, attempts))
                            throw new RecoveryException("reason=append_rollback_unverified");
                        throw new RecoveryException("reason=append_refused");
                    }
                    if (!ReferenceEquals(attempt.AppendedPage, CellPages.Pages(cell)[move.TargetPage]))
                        throw new RecoveryException("reason=appended_page_changed");
                }

                var livePages = CellPages.Pages(cell);
                if (move.TargetPage >= livePages.Count) throw new RecoveryException("reason=target_page_missing");
                var target = livePages[move.TargetPage];
                attempt.Target = target;
                var targetGeometry = move.AppendPage ? StripLayout.PageFor(move.Size) : layout!.Pages[move.TargetPage];
                var (x, y) = StripLayout.SlotOrigin(targetGeometry, move.TargetSlot);
                var targetShape = new GridShapeBuilder(entry.Item.modifiedShape).SetPosition(x, y).Build();
                if (!Move(cell, entry, target, targetShape))
                    throw new RecoveryException("reason=native_move_refused");
                applied.Add(new AppliedMove(entry, target, targetShape, move));
            }

            if (!Postcheck(cell, journal, applied, ExpectedPageOrder(pages, attempts)))
                throw new RecoveryException("reason=postcheck_failed");
            if (protectedPages.Count > 0)
                MarkDegraded(cell, protectedPages, "reason=identity_unreadable");
            else
                CellInventoryCache.ForgetWithRoots(cell);
            _diagnostics.Write(LogVerbosity.Info, "cell_recovery_applied", "Restored strip roots were relocated.",
                () => CellLogFields.For(cell, LogField.Of("moves", applied.Count)));
            return true;
        }
        catch (Exception error)
        {
            bool rolledBack;
            try
            {
                rolledBack = Rollback(cell, pages, journal, attempts);
            }
            catch
            {
                rolledBack = false;
            }
            var reason = rolledBack ? "reason=recovery_failed" : "reason=rollback_failed";
            if (rolledBack)
            {
                MarkDegraded(cell, anomalous.Select(item => item.Page).Concat(protectedPages).Distinct().ToArray(), reason);
                return true;
            }
            foreach (var entry in journal)
                if (!_heldRecoveryItems.Contains(entry.Item)) _heldRecoveryItems.Add(entry.Item);
            return Fail(cell, reason, -1, -1, error, rollbackFailed: true);
        }
    }

    private bool Move(GameItem cell, Observation entry, GameGridInventory target, GridShape targetShape)
    {
        var source = entry.Item.parentInventory;
        if (source == null || !ReferenceEquals(source, entry.SourcePage) || !source.childItems.Contains(entry.Item))
            return false;
        if (ReferenceEquals(entry.SourcePage, target))
        {
            if (!target.CheckShapePlacement(entry.Item, targetShape, new List<GameItem> { entry.Item })) return false;
            entry.Item.modifiedShape = targetShape;
            if (OwnsAt(entry.Item, target, targetShape, entry)) return true;
            entry.Item.modifiedShape = entry.OriginalShape;
            return false;
        }
        if (!target.CheckShapePlacement(entry.Item, targetShape, new List<GameItem>())) return false;

        var result = ItemMove.Run(NativeItemMove.Into(entry.Item, target, targetShape));
        if (result.Outcome == ItemMoveOutcome.Unowned || result.Error != null)
        {
            var level = result.Outcome == ItemMoveOutcome.Unowned ? LogVerbosity.Error : LogVerbosity.Warning;
            _diagnostics.Once(cell, level, "cell_recovery_move_failed", "A recovery move did not end cleanly.",
                () => CellLogFields.For(cell, LogField.Of("page", entry.Page), LogField.Of("slot", entry.Slot),
                    LogField.Of("outcome", result.Outcome.ToString())), result.Error);
        }
        return result.Outcome == ItemMoveOutcome.Moved && OwnsAt(entry.Item, target, targetShape, entry);
    }

    private bool Rollback(GameItem cell, IReadOnlyList<GameGridInventory> originalPages, IReadOnlyList<Journal> journal,
        IReadOnlyList<RecoveryAttempt> attempts)
    {
        var okay = PageOrderMatches(cell, originalPages, attempts);
        for (var index = attempts.Count - 1; index >= 0; index--)
        {
            if (!PageOrderMatches(cell, originalPages, attempts))
            {
                okay = false;
                continue;
            }
            var attempt = attempts[index];
            okay &= GuardedRestore(attempt.Entry, cell, attempt.Target);
        }

        for (var index = attempts.Count - 1; index >= 0; index--)
        {
            var attempt = attempts[index];
            if (!attempt.Move.AppendPage) continue;
            var earlierAttempts = attempts.Take(index + 1).ToArray();
            if (!PageOrderMatches(cell, originalPages, earlierAttempts))
            {
                okay = false;
                continue;
            }
            okay &= CellPages.TryRemoveEmptyTailStripPage(cell, attempt.Move.TargetPage,
                attempt.Move.Size, attempt.AppendedPage);
        }

        return okay && PageOrderMatches(cell, originalPages, Array.Empty<RecoveryAttempt>()) &&
            Postcheck(cell, journal, Array.Empty<AppliedMove>(), originalPages);
    }

    private static bool GuardedRestore(Observation entry, GameItem cell, GameGridInventory? attemptedTarget)
    {
        var pages = CellPages.Pages(cell);
        if (entry.Page >= pages.Count || !ReferenceEquals(pages[entry.Page], entry.SourcePage) ||
            entry.Item is GameItemElement element && element.IsDestroyed() ||
            entry.Item.unitCount != entry.Units || !DescendantsMatch(entry) || entry.Key != null &&
            (StorageService.Identify(entry.Item) is not { } key || !key.Equals(entry.Key)))
            return false;
        var source = entry.SourcePage;
        if (ReferenceEquals(entry.Item.parentInventory, source) && source.childItems.Contains(entry.Item))
        {
            entry.Item.modifiedShape = entry.OriginalShape;
            return OwnsAt(entry.Item, source, entry.OriginalShape, entry);
        }
        if (entry.Item.parentInventory != null &&
            (!ReferenceEquals(entry.Item.parentInventory, attemptedTarget) || attemptedTarget == null ||
                !attemptedTarget.childItems.Contains(entry.Item))) return false;
        if (attemptedTarget != null && ReferenceEquals(entry.Item.parentInventory, attemptedTarget) &&
            !attemptedTarget.Expel(entry.Item)) return false;
        if (entry.Item.parentInventory != null || source.childItems.Contains(entry.Item)) return false;

        entry.Item.modifiedShape = entry.OriginalShape;
        source.UncheckedAccept(entry.Item);
        return OwnsAt(entry.Item, source, entry.OriginalShape, entry);
    }

    private static bool Postcheck(GameItem cell, IReadOnlyList<Journal> journal, IReadOnlyList<AppliedMove> applied,
        IReadOnlyList<GameGridInventory> expectedPages)
    {
        if (!CellPages.IsComplete(cell)) return false;
        var livePages = CellPages.Pages(cell);
        if (livePages.Count != expectedPages.Count) return false;
        for (var index = 0; index < expectedPages.Count; index++)
            if (!ReferenceEquals(livePages[index], expectedPages[index])) return false;
        var pageSet = new HashSet<GameGridInventory>(livePages);
        var locations = new Dictionary<GameItem, GameGridInventory>();
        foreach (var page in livePages)
        foreach (var item in page.childItems)
            if (item == null || !locations.TryAdd(item, page)) return false;
        if (locations.Count != journal.Count) return false;
        var appliedByRoot = applied.ToDictionary(move => move.Entry.Item);
        foreach (var entry in journal)
        {
            if (entry.Page >= livePages.Count || !ReferenceEquals(livePages[entry.Page], entry.SourcePage)) return false;
            var moved = appliedByRoot.TryGetValue(entry.Item, out var move);
            if (moved && (move.Move.TargetPage >= livePages.Count ||
                !ReferenceEquals(livePages[move.Move.TargetPage], move.Target))) return false;
            var expectedPage = moved ? move.Target : entry.SourcePage;
            var expectedShape = moved ? move.Shape : entry.OriginalShape;
            if (!locations.TryGetValue(entry.Item, out var actualPage) || !ReferenceEquals(actualPage, expectedPage) ||
                entry.Item.parentInventory != expectedPage || entry.Item.unitCount != entry.Units ||
                !DescendantsMatch(entry) ||
                entry.Key != null && (StorageService.Identify(entry.Item) is not { } key || !key.Equals(entry.Key)) ||
                entry.Item.modifiedShape.minX != expectedShape.minX || entry.Item.modifiedShape.minY != expectedShape.minY ||
                entry.Item.modifiedShape.globalWidth != expectedShape.globalWidth ||
                entry.Item.modifiedShape.globalHeight != expectedShape.globalHeight || !pageSet.Contains(expectedPage))
                return false;
        }

        return true;
    }

    private static GameGridInventory[] ExpectedPageOrder(IReadOnlyList<GameGridInventory> originals,
        IReadOnlyList<RecoveryAttempt> attempts) => originals.Concat(attempts
            .Where(attempt => attempt.AppendedPage != null).Select(attempt => attempt.AppendedPage!)).ToArray();

    private static bool PageOrderMatches(GameItem cell, IReadOnlyList<GameGridInventory> originals,
        IReadOnlyList<RecoveryAttempt> attempts)
    {
        var pages = CellPages.Pages(cell);
        var appended = attempts.Where(attempt => attempt.AppendedPage != null)
            .Select(attempt => attempt.AppendedPage!).ToArray();
        if (pages.Count != originals.Count + appended.Length) return false;
        for (var index = 0; index < originals.Count; index++)
            if (!ReferenceEquals(pages[index], originals[index])) return false;
        for (var index = 0; index < appended.Length; index++)
            if (!ReferenceEquals(pages[originals.Count + index], appended[index])) return false;
        return true;
    }

    private static bool DescendantsMatch(Observation entry)
    {
        if (entry.Descendants == null) return true;
        var current = GraphUtils.FindAllChildrenType<GameItem>(entry.Item).ToArray();
        if (current.Length != entry.Descendants.Length) return false;
        for (var index = 0; index < current.Length; index++)
            if (!ReferenceEquals(current[index], entry.Descendants[index])) return false;
        return true;
    }

    private static bool OwnsAt(GameItem item, GameGridInventory page, GridShape shape, Observation entry) =>
        item.parentInventory == page && page.childItems.Contains(item) && item.unitCount == entry.Units &&
        (entry.Key == null || StorageService.Identify(item) is { } key && key.Equals(entry.Key)) &&
        item.modifiedShape.minX == shape.minX && item.modifiedShape.minY == shape.minY &&
        item.modifiedShape.globalWidth == shape.globalWidth && item.modifiedShape.globalHeight == shape.globalHeight;

    private static bool NeedsMove(Observation item) => item.PageGeometry.Kind != PageKind.Strip ||
        !item.Size.Equals(item.PageGeometry.SlotSize) || item.Slot < 0;

    private void MarkDegraded(GameItem cell, IEnumerable<int> pages, string reason)
    {
        var pageList = pages.Distinct().OrderBy(page => page).ToArray();
        if (CellHealth.Get(cell).Status == CellHealthStatus.Available)
            CellHealth.Set(cell, new CellHealthState(CellHealthStatus.Protected));
        CellInventoryCache.ForgetWithRoots(cell);
        _diagnostics.Write(LogVerbosity.Warning, "cell_recovery_degraded", "Protected pages prevent insertion and compaction.",
            () => CellLogFields.For(cell, LogField.Of("protected_pages", pageList.Length), LogField.Of("reason", reason)));
    }
    private bool Fail(GameItem cell, string reason, int page, int slot, Exception? error = null, bool rollbackFailed = false)
    {
        try
        {
            foreach (var root in CellPages.Roots(cell))
                if (!_heldRecoveryItems.Contains(root)) _heldRecoveryItems.Add(root);
        }
        catch
        {
            // The cell itself remains protected by CellHealth; retaining roots is best effort after linkage failure.
        }
        CellHealth.Set(cell, new CellHealthState(CellHealthStatus.Unavailable,
            rollbackFailed ? CellUnavailableReason.RollbackFailed : CellUnavailableReason.RestoreFailed));
        _diagnostics.Once(cell, LogVerbosity.Error, "cell_recovery_unavailable", "Cell recovery stopped. Do not save.",
            () => CellLogFields.For(cell, LogField.Of("reason", reason), LogField.Of("page", page), LogField.Of("slot", slot)), error);
        return false;
    }

    private class Observation
    {
        internal Observation(GameItem item, int page, GameGridInventory sourcePage, int slot, ItemKey? key, int units,
            GridShape originalShape, BoundingSize size)
        {
            Item = item;
            Page = page;
            SourcePage = sourcePage;
            Slot = slot;
            Key = key;
            Units = units;
            OriginalShape = originalShape;
            Size = size;
            PageGeometry = default;
        }

        internal GameItem Item { get; }
        internal int Page { get; }
        internal GameGridInventory SourcePage { get; }
        internal int Slot { get; }
        internal ItemKey? Key { get; }
        internal int Units { get; }
        internal GridShape OriginalShape { get; }
        internal BoundingSize Size { get; }
        internal PageGeometry PageGeometry { get; set; }
        internal GameItem[]? Descendants { get; set; }
    }

    private sealed class Journal : Observation
    {
        internal Journal(Observation source, GameGridInventory sourcePage) : base(source.Item, source.Page,
            sourcePage, source.Slot, source.Key, source.Units, source.OriginalShape, source.Size)
        {
            PageGeometry = source.PageGeometry;
            Descendants = source.Descendants;
        }
    }

    private readonly struct AppliedMove
    {
        internal AppliedMove(Observation entry, GameGridInventory target, GridShape shape, RecoveryMove move)
        {
            Entry = entry;
            Target = target;
            Shape = shape;
            Move = move;
        }

        internal Observation Entry { get; }
        internal GameGridInventory Target { get; }
        internal GridShape Shape { get; }
        internal RecoveryMove Move { get; }
    }

    private sealed class RecoveryAttempt
    {
        internal RecoveryAttempt(Observation entry, RecoveryMove move)
        {
            Entry = entry;
            Move = move;
        }

        internal Observation Entry { get; }
        internal RecoveryMove Move { get; }
        internal GameGridInventory? Target { get; set; }
        internal GameGridInventory? AppendedPage { get; set; }
    }

    private sealed class RecoveryException : Exception
    {
        internal RecoveryException(string message) : base(message) { }
    }
}
