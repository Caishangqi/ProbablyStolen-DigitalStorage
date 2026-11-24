using System;
using System.Diagnostics.CodeAnalysis;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Ports;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class IoPortTransferServiceTests
{
    private static readonly BoundingSize Size = new(1, 1);
    private static readonly IActionSource Actor = new MachineSource("io-port");
    private readonly IoPortTransferService _service = new();

    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(100, 1)]
    [TestCase(101, 2)]
    public void Transfer_ChargesOncePerHundredVerifiedRoots(int roots, int fees)
    {
        var job = new IoPortTransferJob();
        var source = NewCell(roots, units: 2);
        var target = NewCell(0);
        var power = new Energy(10);

        var result = Run(job, source, target, power, maxRoots: 1000);

        Assert.That(result.MovedRoots, Is.EqualTo(roots));
        Assert.That(job.CommittedRootCount, Is.EqualTo(roots));
        Assert.That(source.GetStoredItemCount(), Is.Zero);
        Assert.That(target.GetStoredItemCount(), Is.EqualTo(roots * 2));
        Assert.That((power.Commits, power.Cancels, power.Available), Is.EqualTo((fees, 0, 10 - fees)));
        Assert.That(result.Settled, Is.True);
        Assert.That(result.Status, Is.EqualTo(roots == 0 ? IoPortRuntimeStatus.NoEligibleContent : IoPortRuntimeStatus.Working));
    }

    [Test]
    public void Transfer_RestoredCountContinuesItsPaidBlock()
    {
        var job = new IoPortTransferJob(committedRootCount: 99);
        var power = new Energy(10);

        Run(job, NewCell(2), NewCell(0), power, maxRoots: 10);

        Assert.That(job.CommittedRootCount, Is.EqualTo(101));
        Assert.That(power.Commits, Is.EqualTo(1), "Root 100 is prepaid; root 101 opens the next block.");
    }

    [Test]
    public void Transfer_StopsAtTheRootLimitWithoutSettling()
    {
        var source = NewCell(5);
        var target = NewCell(0);

        var result = Run(new IoPortTransferJob(), source, target, new Energy(10), maxRoots: 3);

        Assert.That(result.MovedRoots, Is.EqualTo(3));
        Assert.That(result.Status, Is.EqualTo(IoPortRuntimeStatus.Working));
        Assert.That(result.Settled, Is.False);
        Assert.That(source.GetStoredItemCount(), Is.EqualTo(2));
        Assert.That(_service.IsCompletionSatisfied(IoPortCompletionMode.WorkComplete, source, result), Is.False);
    }

    [Test]
    public void Transfer_SpentTimeSliceMovesNothingAndIsNotCompletion()
    {
        var source = NewCell(2);

        var result = Run(new IoPortTransferJob(), source, NewCell(0), new Energy(10), maxRoots: 10, hasTime: () => false);

        Assert.That((result.MovedRoots, result.Status, result.Settled), Is.EqualTo((0, IoPortRuntimeStatus.Working, false)));
        Assert.That(source.GetStoredItemCount(), Is.EqualTo(2));
        Assert.That(_service.IsCompletionSatisfied(IoPortCompletionMode.WorkComplete, source, result), Is.False);
    }

    [TestCase(IoPortRuntimeStatus.Unpowered)]
    [TestCase(IoPortRuntimeStatus.OperationFeeUnavailable)]
    [TestCase(IoPortRuntimeStatus.UnsupportedEnergySource)]
    public void Transfer_PowerBlockLeavesEveryRootAndReservesNothing(IoPortRuntimeStatus block)
    {
        var source = NewCell(2);
        var power = new Energy(10);

        var result = _service.Transfer(new IoPortTransferJob(), source, NewCell(0), Actor, power, _ => block, 10, () => true);

        Assert.That((result.MovedRoots, result.Status, result.Settled), Is.EqualTo((0, block, false)));
        Assert.That(power.Reserves, Is.Zero);
        Assert.That(source.GetStoredItemCount(), Is.EqualTo(2));
    }

    [Test]
    public void Transfer_RefusedReservationStopsBeforeTheRootMoves()
    {
        var source = NewCell(2);
        var target = NewCell(0);
        var power = new Energy(10) { Refuse = true };

        var result = Run(new IoPortTransferJob(), source, target, power, maxRoots: 10);

        Assert.That((result.MovedRoots, result.Status), Is.EqualTo((0, IoPortRuntimeStatus.Unpowered)));
        Assert.That(source.GetStoredItemCount(), Is.EqualTo(2));
        Assert.That(target.GetStoredItemCount(), Is.Zero);
    }

    [Test]
    public void Transfer_UnverifiedDebitFaultsWithoutMoving()
    {
        var source = NewCell(1);
        var power = new Energy(10) { FailReserveAfterDebit = true };

        var result = Run(new IoPortTransferJob(), source, NewCell(0), power, maxRoots: 10);

        Assert.That((result.MovedRoots, result.Status, result.Fault),
            Is.EqualTo((0, IoPortRuntimeStatus.Faulted, (IoPortTransferFault?)IoPortTransferFault.ReservationUnverified)));
        Assert.That(source.GetStoredItemCount(), Is.EqualTo(1));
    }

    [Test]
    public void Transfer_FailedConfirmationKeepsTheVerifiedRootAndStops()
    {
        var job = new IoPortTransferJob();
        var source = NewCell(3);
        var target = NewCell(0);

        var result = Run(job, source, target, new Energy(10) { FailCommit = true }, maxRoots: 10);

        Assert.That((result.MovedRoots, result.Fault), Is.EqualTo((1, (IoPortTransferFault?)IoPortTransferFault.ConfirmationFailed)));
        Assert.That(job.CommittedRootCount, Is.EqualTo(1));
        Assert.That((source.GetStoredItemCount(), target.GetStoredItemCount()), Is.EqualTo((2, 1)));
        Assert.That(_service.IsCompletionSatisfied(IoPortCompletionMode.Empty, source, result), Is.False);
    }

    [Test]
    public void Transfer_TargetThatRefusesEverythingSettlesAsWaitingAndCompletesWork()
    {
        var source = NewCell(1);
        var target = NewCell(0, tier: StorageCellTier.All[0]);
        Fill(target);
        var power = new Energy(10);

        var result = Run(new IoPortTransferJob(), source, target, power, maxRoots: 10);

        Assert.That((result.MovedRoots, result.Status, result.Settled), Is.EqualTo((0, IoPortRuntimeStatus.WaitingForCapacity, true)));
        Assert.That(power.Available, Is.EqualTo(10));
        Assert.That(_service.IsCompletionSatisfied(IoPortCompletionMode.WorkComplete, source, result), Is.True);
        Assert.That(_service.IsCompletionSatisfied(IoPortCompletionMode.Empty, source, result), Is.False);
    }

    [Test]
    public void Transfer_CursorResumesAfterTheLimitAndReadsTheTypesOnce()
    {
        var source = new CountingStorage(NewCell(5));
        var target = NewCell(0);
        var cursor = new IoPortTransferCursor();
        var power = new Energy(10);

        var first = _service.Transfer(new IoPortTransferJob(), source, target, Actor, power, _ => null, 2, () => true, cursor);
        var second = _service.Transfer(new IoPortTransferJob(2), source, target, Actor, power, _ => null, 10, () => true, cursor);

        Assert.That((first.MovedRoots, first.Settled), Is.EqualTo((2, false)));
        Assert.That((second.MovedRoots, second.Settled), Is.EqualTo((3, true)));
        Assert.That(source.PieceReads, Is.EqualTo(1), "The type list is read once per cursor.");
    }

    [Test]
    public void Transfer_CursorNeverRetriesARefusedType()
    {
        var source = new CountingStorage(NewCell(3));
        var target = NewCell(0, tier: StorageCellTier.All[0]);
        Fill(target);
        var cursor = new IoPortTransferCursor();
        var power = new Energy(10);

        var first = _service.Transfer(new IoPortTransferJob(), source, target, Actor, power, _ => null, 10, () => true, cursor);
        var extracts = source.Extracts;
        var second = _service.Transfer(new IoPortTransferJob(), source, target, Actor, power, _ => null, 10, () => true, cursor);

        Assert.That((first.Status, first.Settled), Is.EqualTo((IoPortRuntimeStatus.WaitingForCapacity, true)));
        Assert.That((second.Status, second.Settled), Is.EqualTo((IoPortRuntimeStatus.WaitingForCapacity, true)));
        Assert.That(source.Extracts, Is.EqualTo(extracts), "A refused type is not offered again in the same session.");
    }

    [Test]
    public void IsCompletionSatisfied_ReadsTheLiveCellForEmptyAndFull()
    {
        var empty = NewCell(0);
        var full = NewCell(0, tier: StorageCellTier.All[0]);
        Fill(full);

        Assert.That(_service.IsCompletionSatisfied(IoPortCompletionMode.Empty, empty, null), Is.True);
        Assert.That(_service.IsCompletionSatisfied(IoPortCompletionMode.Full, empty, null), Is.False);
        Assert.That(_service.IsCompletionSatisfied(IoPortCompletionMode.Full, full, null), Is.True);
        Assert.That(_service.IsCompletionSatisfied(IoPortCompletionMode.WorkComplete, empty, null), Is.False,
            "Without a batch nobody checked that nothing is left to move.");
    }

    [Test]
    public void GetControllerBlock_DistinguishesMissingAndConflictingControllers()
    {
        Assert.That(_service.GetControllerBlock(null), Is.EqualTo(IoPortRuntimeStatus.NoController));
        Assert.That(_service.GetControllerBlock(Grid(GridNodeKind.Controller)), Is.Null);
        Assert.That(_service.GetControllerBlock(Grid(GridNodeKind.Controller, GridNodeKind.Controller)),
            Is.EqualTo(IoPortRuntimeStatus.ControllerConflict));
        Assert.That(_service.GetControllerBlock(Grid(GridNodeKind.Terminal)), Is.EqualTo(IoPortRuntimeStatus.NoController));
    }

    private IoPortBatchResult Run(IoPortTransferJob job, BasicCellInventory source, BasicCellInventory target, Energy power,
        int maxRoots, Func<bool>? hasTime = null) =>
        _service.Transfer(job, source, target, Actor, power, fee => power.Available >= fee ? null : IoPortRuntimeStatus.Unpowered,
            maxRoots, hasTime ?? (() => true));

    private static BasicCellInventory NewCell(int roots, int units = 1, StorageCellTier? tier = null)
    {
        var cell = new BasicCellInventory(MemoryCellPages.EmptyView(tier ?? StorageCellTier.All[2]), new MemoryCellPages());
        var source = new MemoryInventory();
        for (var index = 0; index < roots; index++) source.With(Size, "item.a", units);
        Assert.That(cell.Insert(source, Actionable.Modulate, Actor).InsertedUnits, Is.EqualTo(roots * units));
        return cell;
    }

    // Fills the cell with distinct types up to its type limit, then tops it up until no unit fits.
    private static void Fill(BasicCellInventory cell)
    {
        for (var index = 0; index < 100_000; index++)
            if (cell.Insert(new MemoryInventory().With(Size, "filler." + index, 1), Actionable.Modulate, Actor).InsertedUnits == 0) break;
        for (var index = 0; index < 1_000_000; index++)
            if (cell.Insert(new MemoryInventory().With(Size, "filler.0", 64), Actionable.Modulate, Actor).InsertedUnits == 0) break;
        for (var index = 0; index < 1_000_000; index++)
            if (cell.Insert(new MemoryInventory().With(Size, "filler.0", 1), Actionable.Modulate, Actor).InsertedUnits == 0) break;
        Assert.That(cell.GetStatus(), Is.EqualTo(CellState.Full));
    }

    private static Grid Grid(params GridNodeKind[] kinds)
    {
        var nodes = new GridNode[kinds.Length];
        var edges = new GridConnection[Math.Max(0, kinds.Length - 1)];
        for (var index = 0; index < kinds.Length; index++) nodes[index] = new GridNode(index, kinds[index], 0);
        for (var index = 1; index < kinds.Length; index++) edges[index - 1] = new GridConnection(index - 1, index);
        return new GridPropagator().Propagate(nodes, edges)[0];
    }

    private sealed class CountingStorage : IRootStorage
    {
        private readonly IRootStorage _inner;
        internal CountingStorage(IRootStorage inner) => _inner = inner;
        internal int PieceReads { get; private set; }
        internal int Extracts { get; private set; }
        public bool IsPreferredStorageFor(ItemKey what, IActionSource source) => _inner.IsPreferredStorageFor(what, source);
        public InsertResult Insert(IRootSource roots, Actionable mode, IActionSource source) => _inner.Insert(roots, mode, source);

        public ExtractResult Extract(ItemKey what, long amount, IRootSink destination, Actionable mode, IActionSource source)
        {
            Extracts++;
            return _inner.Extract(what, amount, destination, mode, source);
        }

        public void GetAvailableStacks(KeyCounter output) => _inner.GetAvailableStacks(output);

        public void GetAvailablePieces(KeyCounter output)
        {
            PieceReads++;
            _inner.GetAvailablePieces(output);
        }

        public string GetDescription() => _inner.GetDescription();
    }

    private sealed class Energy : ITransferEnergyPort
    {
        internal Energy(int available) => Available = available;
        internal int Available { get; private set; }
        internal int Reserves { get; private set; }
        internal int Commits { get; private set; }
        internal int Cancels { get; private set; }
        internal bool Refuse { get; set; }
        internal bool FailReserveAfterDebit { get; set; }
        internal bool FailCommit { get; set; }

        public bool TryReserve(int amount, [NotNullWhen(true)] out ITransferEnergyReservation? reservation)
        {
            Reserves++;
            reservation = null;
            if (Refuse || Available < amount) return false;
            Available -= amount;
            if (FailReserveAfterDebit) throw new InvalidOperationException("Debit was not confirmed.");
            reservation = new Reservation(this, amount);
            return true;
        }

        private sealed class Reservation : ITransferEnergyReservation
        {
            private readonly Energy _owner;
            private readonly int _amount;
            internal Reservation(Energy owner, int amount) => (_owner, _amount) = (owner, amount);

            public void Commit()
            {
                _owner.Commits++;
                if (_owner.FailCommit) throw new InvalidOperationException("Fee confirmation failed.");
            }

            public bool Cancel()
            {
                _owner.Cancels++;
                _owner.Available += _amount;
                return true;
            }
        }
    }
}
