using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class RootTransferTests
{
    private static readonly BoundingSize OneByOne = new(1, 1);
    private static readonly ItemKey Apple = new("item.apple");
    private static readonly ItemKey Pear = new("item.pear");

    [Test]
    public void Drain_MovesRootsInPageRowColumnOrderAndConservesUnits()
    {
        var source = new MemorySource(
            Root(2, 0, 0, Apple, 1), Root(1, 3, 0, Pear, 4), Root(1, 0, 2, Apple, 2), Root(1, 1, 0, Apple, 3));
        var sink = new MemorySink(source);

        var result = RootTransfer.Drain(source, sink, MigrationSource.Instance);

        Assert.That(result.Completed, Is.True);
        Assert.That(result.MovedRoots, Is.EqualTo(4));
        Assert.That(sink.Received.Select(root => (root.Page, root.X, root.Y)),
            Is.EqualTo(new[] { (1, 1, 0), (1, 3, 0), (1, 0, 2), (2, 0, 0) }));
        Assert.That(result.MovedUnits.Get(Apple), Is.EqualTo(6));
        Assert.That(result.MovedUnits.Get(Pear), Is.EqualTo(4));
        Assert.That(source.Snapshot(), Is.Empty);
    }

    [Test]
    public void MoveFirst_EmptySourceTouchesNothing()
    {
        var source = new MemorySource();
        var sink = new MemorySink(source);

        Assert.That(RootTransfer.MoveFirst(source, sink, MigrationSource.Instance).Outcome, Is.EqualTo(TransferOutcome.SourceEmpty));
        Assert.That(sink.Calls, Is.Empty);
    }

    [Test]
    public void MoveFirst_RejectedSimulationNeverModulates()
    {
        var source = new MemorySource(Root(1, 0, 0, Apple, 1));
        var sink = new MemorySink(source) { Reject = StoreRejection.Capacity };

        var step = RootTransfer.MoveFirst(source, sink, MigrationSource.Instance);

        Assert.That(step.Rejection, Is.EqualTo(StoreRejection.Capacity));
        Assert.That(sink.Calls, Is.EqualTo(new[] { Actionable.Simulate }));
        Assert.That(source.Snapshot().Count(), Is.EqualTo(1));
    }

    [Test]
    public void MoveFirst_RejectedModulationRestoresRoot()
    {
        var source = new MemorySource(Root(1, 0, 0, Apple, 4));
        var sink = new MemorySink(source) { RejectAfterSimulation = true };

        var step = RootTransfer.MoveFirst(source, sink, MigrationSource.Instance);

        Assert.That(step.Outcome, Is.EqualTo(TransferOutcome.Rejected));
        Assert.That(sink.Calls, Is.EqualTo(new[] { Actionable.Simulate, Actionable.Modulate }));
        Assert.That(source.Snapshot().Single().Units, Is.EqualTo(4));
        Assert.That(sink.AcceptedUnits, Is.Zero);
    }

    [Test]
    public void MoveFirst_PartialReceiveRestoresCurrentRoot()
    {
        var source = new MemorySource(Root(1, 0, 0, Apple, 4));
        var sink = new MemorySink(source) { PartialOnCall = 1 };

        var step = RootTransfer.MoveFirst(source, sink, MigrationSource.Instance);

        Assert.That(step.Outcome, Is.EqualTo(TransferOutcome.Returned));
        Assert.That(source.Snapshot().Single().Units, Is.EqualTo(4));
        Assert.That(sink.AcceptedUnits, Is.Zero, "A partial root is not a destination success.");
    }

    [Test]
    public void MoveFirst_UnknownOwnershipNeverReportsSuccess()
    {
        var source = new MemorySource(Root(1, 0, 0, Apple, 4));
        var sink = new MemorySink(source) { FailOnCall = 1, Failure = TransferOutcome.Unowned };

        var step = RootTransfer.MoveFirst(source, sink, MigrationSource.Instance);

        Assert.That(step.Outcome, Is.EqualTo(TransferOutcome.Unowned));
        Assert.That(step.IsMoved, Is.False);
        Assert.That(sink.AcceptedUnits, Is.Zero);
    }

    [Test]
    public void MoveFirst_RepeatedAttemptsConserveUnits()
    {
        var source = new MemorySource(Root(1, 0, 0, Apple, 4));
        var sink = new MemorySink(source) { FailOnCall = 1, Failure = TransferOutcome.Returned };

        Assert.That(RootTransfer.MoveFirst(source, sink, MigrationSource.Instance).Outcome,
            Is.EqualTo(TransferOutcome.Returned));
        Assert.That(RootTransfer.MoveFirst(source, sink, MigrationSource.Instance).Outcome,
            Is.EqualTo(TransferOutcome.Moved));

        Assert.That(source.Snapshot().Sum(root => root.Units) + sink.AcceptedUnits, Is.EqualTo(4));
        Assert.That(sink.Received, Has.Count.EqualTo(1));
    }

    [TestCase(TransferOutcome.Returned)]
    [TestCase(TransferOutcome.Unowned)]
    public void Drain_StopsAtAFailedNativeMove(TransferOutcome failure)
    {
        var source = new MemorySource(Root(1, 0, 0, Apple, 1), Root(1, 1, 0, Apple, 1), Root(1, 2, 0, Apple, 1));
        var sink = new MemorySink(source) { FailOnCall = 2, Failure = failure };

        var result = RootTransfer.Drain(source, sink, MigrationSource.Instance);

        Assert.That(result.LastStep.Outcome, Is.EqualTo(failure));
        Assert.That(result.MovedRoots, Is.EqualTo(1));
        Assert.That(result.Completed, Is.False);
        Assert.That(source.Snapshot().Count() + sink.Received.Count, Is.EqualTo(3), "No root is lost or duplicated.");
    }

    [Test]
    public void Drain_StopsAfterMaxSteps()
    {
        var source = new MemorySource(Root(1, 0, 0, Apple, 1), Root(1, 1, 0, Apple, 1), Root(1, 2, 0, Apple, 1));
        var sink = new MemorySink(source);

        var result = RootTransfer.Drain(source, sink, MigrationSource.Instance, 2);

        Assert.That(result.MovedRoots, Is.EqualTo(2));
        Assert.That(result.LastStep.IsMoved, Is.True);
        Assert.That(result.Completed, Is.False);
        Assert.That(RootTransfer.Drain(source, sink, MigrationSource.Instance).Completed, Is.True);
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => RootTransfer.Drain(source, sink, MigrationSource.Instance, 0)));
    }

    [Test]
    public void MoveFirst_FailsLoudlyWhenTheSinkLiesAboutMoving()
    {
        var source = new MemorySource(Root(1, 0, 0, Apple, 1));
        var sink = new MemorySink(source) { KeepInSource = true };

        Assert.Throws<InvalidOperationException>(new Action(() => RootTransfer.MoveFirst(source, sink, MigrationSource.Instance)));
    }

    private static RootSnapshot Root(int page, int x, int y, ItemKey key, long units) => new(page, x, y, OneByOne, key, units);

    private sealed class MemorySource : IRootSource
    {
        private readonly List<RootSnapshot> _roots;

        internal MemorySource(params RootSnapshot[] roots) => _roots = roots.ToList();

        internal void Remove(RootSnapshot root) => _roots.Remove(root);

        public bool TryPeekFirst(out RootSnapshot root)
        {
            var ordered = Snapshot().ToList();
            root = ordered.FirstOrDefault();
            return ordered.Count > 0;
        }

        public IEnumerable<RootSnapshot> Snapshot()
        {
            var ordered = _roots.ToList();
            ordered.Sort(RootSnapshot.CompareOrder);
            return ordered;
        }
    }

    private sealed class MemorySink : IRootSink
    {
        private readonly MemorySource _source;
        private int _modulations;

        internal MemorySink(MemorySource source) => _source = source;

        internal List<Actionable> Calls { get; } = new();
        internal List<RootSnapshot> Received { get; } = new();
        internal long AcceptedUnits { get; private set; }
        internal StoreRejection? Reject { get; set; }
        internal bool RejectAfterSimulation { get; set; }
        internal int PartialOnCall { get; set; } = -1;
        internal int FailOnCall { get; set; } = -1;
        internal TransferOutcome Failure { get; set; }
        internal bool KeepInSource { get; set; }

        public TransferStep Accept(RootSnapshot root, Actionable mode, IActionSource source)
        {
            Calls.Add(mode);
            if (Reject is { } reason) return TransferStep.Rejected(reason);
            if (mode == Actionable.Simulate) return TransferStep.Moved;
            if (RejectAfterSimulation) return TransferStep.Rejected(StoreRejection.PhysicalLimit);
            var modulation = ++_modulations;
            if (modulation == PartialOnCall) return TransferStep.Returned;
            if (modulation == FailOnCall)
                return Failure == TransferOutcome.Returned ? TransferStep.Returned : TransferStep.Unowned;
            if (!KeepInSource) _source.Remove(root);
            Received.Add(root);
            AcceptedUnits += root.Units;
            return TransferStep.Moved;
        }
    }
}
