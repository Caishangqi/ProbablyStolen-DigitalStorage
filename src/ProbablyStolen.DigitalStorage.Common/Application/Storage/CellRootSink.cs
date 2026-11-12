using System;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Ports;

namespace ProbablyStolen.DigitalStorage.Common.Application.Storage;

/// <summary>
/// Stores roots in one cell's strip pages. It plans with <see cref="CellStorePlanner"/>, appends a page when the
/// plan needs one, moves the root through the port, and updates the view only after a verified move.
/// </summary>
public sealed class CellRootSink : IRootSink
{
    private readonly CellView _view;
    private readonly ICellPagesPort _pages;
    private readonly PlanScope _scope;

    /// <summary>Creates a sink; <see cref="PlanScope.Relocate"/> means the roots already belong to this cell.</summary>
    public CellRootSink(CellView view, ICellPagesPort pages, PlanScope scope)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _scope = scope;
    }

    /// <inheritdoc />
    public TransferStep Accept(RootSnapshot root, Actionable mode, IActionSource source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (_scope == PlanScope.Insert && _view.Partition.Rejection(root.Key) is { } refused)
            return TransferStep.Rejected(refused);

        var plan = CellStorePlanner.Plan(_view, root.ToCandidate(), _scope);
        if (plan.Rejection is { } rejection) return TransferStep.Rejected(rejection);
        if (mode == Actionable.Simulate) return TransferStep.Moved;

        if (plan.NewPage is { } geometry)
        {
            if (!_pages.TryAppendStripPage(geometry)) return TransferStep.Rejected(StoreRejection.PhysicalLimit);
            if (_view.AppendPage(geometry) != plan.Page)
                throw new InvalidOperationException("The appended page is not the planned page.");
        }

        var step = _pages.MoveInto(root, plan.Page, plan.Slot);
        if (!step.IsMoved) return step;

        if (_scope == PlanScope.Insert)
            _view.RecordStored(plan.Page, plan.Slot, root.Key, root.Units, root.Size);
        else
            _view.RecordRelocated(root.Page, _view.SlotAt(root.Page, root.X, root.Y), plan.Page, plan.Slot);
        return step;
    }
}
