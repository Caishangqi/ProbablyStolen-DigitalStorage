using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>One root observed while auditing a restored cell.</summary>
public readonly struct RecoveryRoot
{
    /// <summary>Creates an observed root.</summary>
    public RecoveryRoot(int page, int slot, BoundingSize size, ItemKey key, long units)
    {
        if (page < 0) throw new ArgumentOutOfRangeException(nameof(page));
        if (slot < -1) throw new ArgumentOutOfRangeException(nameof(slot));
        if (size.Width < 1 || size.Height < 1) throw new ArgumentOutOfRangeException(nameof(size));
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (units < 1) throw new ArgumentOutOfRangeException(nameof(units));
        Page = page;
        Slot = slot;
        Size = size;
        Key = key;
        Units = units;
    }

    /// <summary>Gets the source page.</summary>
    public int Page { get; }
    /// <summary>Gets the observed slot.</summary>
    public int Slot { get; }
    /// <summary>Gets the observed size.</summary>
    public BoundingSize Size { get; }
    /// <summary>Gets the item identity.</summary>
    public ItemKey Key { get; }
    /// <summary>Gets the item count.</summary>
    public long Units { get; }
}

/// <summary>Why a recovery plan cannot safely change a restored cell.</summary>
public enum RecoveryFailure
{
    /// <summary>No planning failure.</summary>
    None = 0,
    /// <summary>A root or target page was not present.</summary>
    UnknownPage = 1,
    /// <summary>The root cannot fit a supported strip.</summary>
    UnsupportedSize = 2,
    /// <summary>Two roots claim one slot.</summary>
    DuplicateSlot = 3,
    /// <summary>A target or source ownership collision was found.</summary>
    Collision = 4,
    /// <summary>The restorable page limit was reached.</summary>
    PageLimit = 5,
    /// <summary>Ownership could not be proven.</summary>
    InvalidOwnership = 6,
    /// <summary>Native placement refused the move.</summary>
    NativeRefused = 7,
    /// <summary>A native callback changed the planned placement.</summary>
    CallbackChangedPlacement = 8,
    /// <summary>Guarded rollback could not prove restoration.</summary>
    RollbackFailed = 9
}

/// <summary>One confirmed relocation, including whether the target page must be appended first.</summary>
public readonly struct RecoveryMove
{
    internal RecoveryMove(int sourcePage, int sourceSlot, int targetPage, int targetSlot, BoundingSize size,
        bool appendPage)
    {
        SourcePage = sourcePage;
        SourceSlot = sourceSlot;
        TargetPage = targetPage;
        TargetSlot = targetSlot;
        Size = size;
        AppendPage = appendPage;
    }

    /// <summary>Gets the source page.</summary>
    public int SourcePage { get; }
    /// <summary>Gets the source slot.</summary>
    public int SourceSlot { get; }
    /// <summary>Gets the target page.</summary>
    public int TargetPage { get; }
    /// <summary>Gets the target slot.</summary>
    public int TargetSlot { get; }
    /// <summary>Gets the target size.</summary>
    public BoundingSize Size { get; }
    /// <summary>Gets whether the target page must be appended.</summary>
    public bool AppendPage { get; }
}

/// <summary>
/// Deterministic, side-effect-free recovery planning for restored strip pages. Native code applies each move and
/// must still verify the callbacks and ownership after it runs. A plan never changes the schema or compacts pages.
/// </summary>
public sealed class CellRecoveryPlan
{
    internal CellRecoveryPlan(IReadOnlyList<RecoveryMove> moves, RecoveryFailure failure)
    {
        Moves = moves;
        Failure = failure;
    }

    /// <summary>Gets planned relocations.</summary>
    public IReadOnlyList<RecoveryMove> Moves { get; }
    /// <summary>Gets the blocking failure, if any.</summary>
    public RecoveryFailure Failure { get; }
    /// <summary>Gets whether the plan is safe to execute.</summary>
    public bool IsSafe => Failure == RecoveryFailure.None;
}

/// <summary>Builds a deterministic recovery plan without touching native objects.</summary>
public static class CellRecoveryPlanner
{
    /// <summary>
    /// Plans only roots whose actual size/slot does not match the current strip geometry. Existing valid roots reserve
    /// their slots. A source slot of an anomalous root is released before selecting a target, but duplicate ownership
    /// or an occupied target remains a hard failure.
    /// </summary>
    public static CellRecoveryPlan Plan(IReadOnlyList<PageView> pages, IReadOnlyList<RecoveryRoot> roots)
    {
        if (pages == null) throw new ArgumentNullException(nameof(pages));
        if (roots == null) throw new ArgumentNullException(nameof(roots));
        if (pages.Count == 0 || pages[0].Kind != PageKind.Anchor)
            return Failed(RecoveryFailure.UnknownPage);

        var occupied = new HashSet<(int Page, int Slot)>();
        var protectedPages = pages.Select((page, index) => (page, index))
            .Where(entry => entry.page.IsProtected).Select(entry => entry.index).ToHashSet();
        var slotCounts = roots.GroupBy(root => (root.Page, root.Slot))
            .ToDictionary(group => group.Key, group => group.Count());
        var pageGeometries = pages.Select(page => page.Geometry).ToList();
        var anomalous = new List<RecoveryRoot>();
        foreach (var root in roots)
        {
            if (root.Page >= pages.Count) return Failed(RecoveryFailure.UnknownPage);
            var page = pages[root.Page];
            if (page.IsProtected) continue;
            if (page.Kind != PageKind.Strip)
            {
                anomalous.Add(root);
                continue;
            }

            var slotIsValid = root.Slot >= 0 && root.Slot < page.Geometry.SlotCount;
            var shapeMatches = root.Size.Equals(page.Geometry.SlotSize);
            if (slotIsValid && shapeMatches)
            {
                if (!occupied.Add((root.Page, root.Slot))) return Failed(RecoveryFailure.DuplicateSlot);
                continue;
            }

            if (slotIsValid && !occupied.Add((root.Page, root.Slot))) return Failed(RecoveryFailure.DuplicateSlot);
            anomalous.Add(root);
        }

        // The native page projection is also authoritative for roots already known to be in legal slots.
        for (var page = 0; page < pages.Count; page++)
        {
            var view = pages[page];
            if (view.IsProtected || view.Kind != PageKind.Strip) continue;
            for (var slot = 0; slot < view.Geometry.SlotCount; slot++)
                if (view.IsOccupied(slot) && !occupied.Contains((page, slot)))
                    occupied.Add((page, slot));
        }

        var moves = new List<RecoveryMove>();
        foreach (var root in anomalous)
        {
            if (!StripLayout.Fits(root.Size)) return Failed(RecoveryFailure.UnsupportedSize, moves);

            // A malformed root that claims an occupied source slot cannot be distinguished from the root already in
            // that slot. Refuse the cell rather than guessing which native object owns the position.
            if (root.Slot >= 0 && occupied.Contains((root.Page, root.Slot)))
            {
                occupied.Remove((root.Page, root.Slot));
                if (slotCounts[(root.Page, root.Slot)] > 1)
                    return Failed(RecoveryFailure.Collision, moves);
            }

            var target = FindFreeTarget(pageGeometries, protectedPages, occupied, root.Page, root.Size);
            if (target.Page < 0)
            {
                if (pageGeometries.Count >= LayoutDescriptor.MaxPages)
                    return Failed(RecoveryFailure.PageLimit, moves);
                target = (pageGeometries.Count, 0);
                pageGeometries.Add(StripLayout.PageFor(root.Size));
                occupied.Add(target);
                moves.Add(new RecoveryMove(root.Page, root.Slot, target.Page, target.Slot, root.Size, appendPage: true));
                continue;
            }

            occupied.Add(target);
            moves.Add(new RecoveryMove(root.Page, root.Slot, target.Page, target.Slot, root.Size, appendPage: false));
        }

        return new CellRecoveryPlan(moves, RecoveryFailure.None);
    }

    private static (int Page, int Slot) FindFreeTarget(IReadOnlyList<PageGeometry> pages, HashSet<int> protectedPages,
        HashSet<(int Page, int Slot)> occupied, int sourcePage, BoundingSize size)
    {
        var samePage = sourcePage < pages.Count && pages[sourcePage].Kind == PageKind.Strip &&
            pages[sourcePage].SlotSize.Equals(size);
        if (samePage && TryPage(sourcePage, pages[sourcePage], occupied, out var slot)) return (sourcePage, slot);
        for (var page = 0; page < pages.Count; page++)
        {
            if (page == sourcePage || protectedPages.Contains(page) || pages[page].Kind != PageKind.Strip ||
                !pages[page].SlotSize.Equals(size)) continue;
            if (TryPage(page, pages[page], occupied, out slot)) return (page, slot);
        }

        return (-1, -1);
    }

    private static bool TryPage(int page, PageGeometry view, HashSet<(int Page, int Slot)> occupied, out int slot)
    {
        for (slot = 0; slot < view.SlotCount; slot++)
            if (!occupied.Contains((page, slot))) return true;
        slot = -1;
        return false;
    }

    private static CellRecoveryPlan Failed(RecoveryFailure failure, IReadOnlyList<RecoveryMove>? moves = null) =>
        new(moves ?? Array.Empty<RecoveryMove>(), failure);
}
