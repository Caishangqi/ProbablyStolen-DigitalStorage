using System;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Ports;

namespace ProbablyStolen.DigitalStorage.Common.Application.Storage;

/// <summary>
/// One storage cell: byte billing, type limit, partition, equal share and status,
/// with native moves through <see cref="ICellPagesPort"/>. Billing counts area units (units * w * h), eight per
/// byte.
/// </summary>
public sealed class BasicCellInventory : IStorageCell
{
    private readonly CellView _view;
    private readonly ICellPagesPort _pages;

    /// <summary>Creates the inventory over a view read for this operation; moves keep the view current.</summary>
    public BasicCellInventory(CellView view, ICellPagesPort pages)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
    }

    /// <summary>Gets the live view of this operation.</summary>
    public CellView View => _view;

    private StorageCellTier Tier => _view.Rules.Tier;

    /// <summary>Gets the byte capacity.</summary>
    public long GetTotalBytes() => Tier.GetBytes();

    /// <summary>Gets <c>ceil(types * typeBudget / typeLimit) + ceil(areaUnits / 8)</c>.</summary>
    public long GetUsedBytes() => _view.UsedBytes();

    /// <summary>Gets the free bytes; negative when an older cell is over its capacity.</summary>
    public long GetFreeBytes() => checked(GetTotalBytes() - GetUsedBytes());

    /// <summary>Gets the stored area units.</summary>
    public long GetStoredItemCount() => _view.Usage.AreaUnits;

    /// <summary>Gets the area units that still fit, including the unused part of the last byte; never negative.</summary>
    public long GetRemainingItemCount()
    {
        var remaining = checked(GetFreeBytes() * CellView.AreaUnitsPerByte + GetUnusedItemCount());
        return Math.Max(0, remaining);
    }

    /// <summary>Gets the stored type count.</summary>
    public long GetStoredItemTypes() => _view.TypeCount;

    /// <summary>Gets the effective type limit.</summary>
    public long GetTotalItemTypes() => _view.TypeLimit;

    /// <summary>Gets how many more types fit, bounded by both the type limit and the free bytes; never negative.</summary>
    public long GetRemainingItemTypes()
    {
        // The largest type count t whose ceil(t * budget / limit) still fits beside the stored area.
        var areaBytes = (GetStoredItemCount() + CellView.AreaUnitsPerByte - 1) / CellView.AreaUnitsPerByte;
        var forTypes = Math.Max(0, checked(GetTotalBytes() - areaBytes));
        var byBytes = checked(forTypes * _view.TypeLimit) / Tier.GetTypeBudget() - GetStoredItemTypes();
        return Math.Max(0, Math.Min(byBytes, GetTotalItemTypes() - GetStoredItemTypes()));
    }

    /// <summary>Gets whether one more type fits: its type cost and at least one area unit, and a free type slot.</summary>
    public bool CanHoldNewItem()
    {
        var usedAfter = CellView.UsedBytes(checked(_view.TypeCount + 1), checked(GetStoredItemCount() + 1),
            Tier.GetTypeBudget(), _view.TypeLimit);
        return usedAfter <= GetTotalBytes() && GetRemainingItemTypes() > 0;
    }

    /// <summary>Gets the area units one type may hold under equal distribution.</summary>
    public long GetEqualShareQuota() => _view.EqualShareQuota();

    /// <inheritdoc />
    public CellState GetStatus()
    {
        if (GetStoredItemTypes() == 0) return CellState.Empty;
        if (CanHoldNewItem()) return CellState.NotEmpty;
        return GetRemainingItemCount() > 0 ? CellState.TypesFull : CellState.Full;
    }

    /// <inheritdoc />
    public bool IsPreferredStorageFor(ItemKey what, IActionSource source) => what != null && _view.Contains(what);

    /// <inheritdoc />
    public InsertResult Insert(IRootSource roots, Actionable mode, IActionSource source)
    {
        if (roots == null) throw new ArgumentNullException(nameof(roots));
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (mode == Actionable.Simulate) return SimulateInsert(roots, source);

        var drain = RootTransfer.Drain(roots, new CellRootSink(_view, _pages, PlanScope.Insert), source);
        return new InsertResult(drain.MovedUnits.Changes.Values.Sum(), drain.LastStep, drain.MovedUnits);
    }

    /// <inheritdoc />
    public ExtractResult Extract(ItemKey what, long amount, IRootSink destination, Actionable mode, IActionSource source)
    {
        if (what == null) throw new ArgumentNullException(nameof(what));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "Extract at least one unit.");
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        if (source == null) throw new ArgumentNullException(nameof(source));

        var stored = _pages.StoredRoots(what);
        var changes = new StorageChangeSet();
        long extracted = 0;
        if (mode == Actionable.Simulate)
        {
            // Each root is checked against the destination alone; the destination cannot simulate a sequence.
            foreach (var root in stored.Snapshot())
            {
                if (extracted >= amount) break;
                var planned = destination.Accept(root, Actionable.Simulate, source);
                if (!planned.IsMoved) return new ExtractResult(extracted, planned, changes);
                extracted = checked(extracted + root.Units);
                changes.Add(root.Key, -root.Units);
            }

            return new ExtractResult(extracted, extracted >= amount ? TransferStep.Moved : TransferStep.SourceEmpty, changes);
        }

        var leaving = new LeavingSink(_view, destination);
        var step = TransferStep.Moved;
        while (extracted < amount)
        {
            step = RootTransfer.MoveFirst(stored, leaving, source);
            if (!step.IsMoved) break;
            extracted = checked(extracted + leaving.LastUnits);
            changes.Add(what, -leaving.LastUnits);
        }

        return new ExtractResult(extracted, step, changes);
    }

    /// <summary>
    /// Moves the one stored root of <paramref name="what"/> whose top-left corner is at <paramref name="page"/>
    /// (<paramref name="x"/>, <paramref name="y"/>) into the destination, through the same atomic transfer as
    /// <see cref="Extract"/>. Page positions are only unique inside one cell, so callers address the cell first.
    /// </summary>
    /// <returns>
    /// <see cref="TransferStep.SourceEmpty"/> when this cell holds no such root. Otherwise the units that actually left:
    /// <see cref="TransferStep.Moved"/> when the whole root left, <see cref="TransferStep.Returned"/> when part of it
    /// stays, or the destination's refusal when nothing left.
    /// </returns>
    public ExtractResult ExtractRoot(ItemKey what, int page, int x, int y, IRootSink destination, Actionable mode, IActionSource source)
    {
        if (what == null) throw new ArgumentNullException(nameof(what));
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        if (source == null) throw new ArgumentNullException(nameof(source));

        var stored = _pages.StoredRoots(what);
        var changes = new StorageChangeSet();
        var found = stored.Snapshot().Where(root => root.Page == page && root.X == x && root.Y == y).Take(1).ToArray();
        if (found.Length == 0) return new ExtractResult(0, TransferStep.SourceEmpty, changes);

        var target = found[0];
        if (mode == Actionable.Simulate)
        {
            var planned = destination.Accept(target, Actionable.Simulate, source);
            if (planned.IsMoved) changes.Add(target.Key, -target.Units);
            return new ExtractResult(planned.IsMoved ? target.Units : 0, planned, changes);
        }

        // A destination may take only part of the root (a caller's unit limit or its room); the rest stays in its slot.
        // The units that left are read back from the pages instead of trusting the step.
        var step = RootTransfer.MoveFirst(new SingleRootSource(stored, target), destination, source);
        var left = stored.Snapshot().Where(root => root.Page == page && root.X == x && root.Y == y).Sum(root => root.Units);
        var moved = target.Units - left;
        if (moved <= 0) return new ExtractResult(0, step.IsMoved ? TransferStep.Returned : step, changes);
        if (left == 0) _view.RecordRemoved(target.Page, _view.SlotAt(target.Page, target.X, target.Y), target.Key, target.Units, target.Size);
        else _view.RecordUnitsRemoved(target.Key, moved, target.Size);
        changes.Add(target.Key, -moved);
        return new ExtractResult(moved, left == 0 ? TransferStep.Moved : TransferStep.Returned, changes);
    }

    /// <inheritdoc />
    public void GetAvailableStacks(KeyCounter output)
    {
        if (output == null) throw new ArgumentNullException(nameof(output));
        foreach (var entry in _view.Usage.Types) output.Add(entry.Key, entry.Value.Units);
    }

    /// <inheritdoc />
    public string GetDescription() => $"{Tier.Name} storage cell";

    // Area units left unused in the last partly used byte.
    private long GetUnusedItemCount()
    {
        var used = GetStoredItemCount() % CellView.AreaUnitsPerByte;
        return used == 0 ? 0 : CellView.AreaUnitsPerByte - used;
    }

    private InsertResult SimulateInsert(IRootSource roots, IActionSource source)
    {
        var copy = _view.Clone();
        var changes = new StorageChangeSet();
        long inserted = 0;
        foreach (var root in roots.Snapshot())
        {
            var step = new CellRootSink(copy, NoNativePages.Instance, PlanScope.Insert).Accept(root, Actionable.Simulate, source);
            if (!step.IsMoved) return new InsertResult(inserted, step, changes);

            var plan = CellStorePlanner.Plan(copy, root.ToCandidate(), PlanScope.Insert);
            if (plan.NewPage is { } geometry) copy.AppendPage(geometry);
            copy.RecordStored(plan.Page, plan.Slot, root.Key, root.Units, root.Size);
            inserted = checked(inserted + root.Units);
            changes.Add(root.Key, root.Units);
        }

        return new InsertResult(inserted, TransferStep.SourceEmpty, changes);
    }

    /// <summary>Forwards to the caller's destination and records each verified departure in the view.</summary>
    private sealed class LeavingSink : IRootSink
    {
        private readonly CellView _view;
        private readonly IRootSink _destination;

        internal LeavingSink(CellView view, IRootSink destination)
        {
            _view = view;
            _destination = destination;
        }

        internal long LastUnits { get; private set; }

        public TransferStep Accept(RootSnapshot root, Actionable mode, IActionSource source)
        {
            var step = _destination.Accept(root, mode, source);
            if (mode == Actionable.Modulate && step.IsMoved)
            {
                _view.RecordRemoved(root.Page, _view.SlotAt(root.Page, root.X, root.Y), root.Key, root.Units, root.Size);
                LastUnits = root.Units;
            }

            return step;
        }
    }

    // Simulation never reaches the port; a call would mean a simulated path tried to move an item.
    private sealed class NoNativePages : ICellPagesPort
    {
        internal static readonly NoNativePages Instance = new();

        public bool TryAppendStripPage(PageGeometry page) => throw new InvalidOperationException("Simulation cannot append pages.");

        public TransferStep MoveInto(RootSnapshot root, int page, int slot) =>
            throw new InvalidOperationException("Simulation cannot move items.");

        public IRootSource StoredRoots(ItemKey key) => throw new InvalidOperationException("Simulation cannot read pages.");
    }
}
