using System;
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
public sealed class RootStorageSinkTests
{
    private static readonly IActionSource Player = new PlayerSource("tester");

    [Test]
    public void Simulate_DoesNotMoveTheRootOrChangeDestination()
    {
        var pages = new MemoryCellPages();
        var destination = NewCell(pages);
        var source = new MemoryInventory().With(new BoundingSize(2, 2), "item.stack", 100);
        source.TryPeekFirst(out var root);
        var movesBefore = pages.MoveCalls;

        var step = new RootStorageSink(destination).Accept(root, Actionable.Simulate, Player);

        Assert.That(step.Outcome, Is.EqualTo(TransferOutcome.Moved));
        Assert.That(source.Roots.Count, Is.EqualTo(1));
        Assert.That(source.Roots[0].Units, Is.EqualTo(100));
        Assert.That(destination.GetStoredItemCount(), Is.Zero);
        Assert.That(pages.MoveCalls, Is.EqualTo(movesBefore));
    }

    [Test]
    public void Modulate_UsesRootTransferToMoveTheWholeStackAsOneRoot()
    {
        var pages = new MemoryCellPages();
        var destination = NewCell(pages);
        var source = new MemoryInventory().With(new BoundingSize(1, 1), "item.stack", 100);
        var sink = new RootStorageSink(destination);

        var step = RootTransfer.MoveFirst(source, sink, Player);

        Assert.That(step.Outcome, Is.EqualTo(TransferOutcome.Moved));
        Assert.That(source.Roots, Is.Empty);
        Assert.That(pages.Roots.Count, Is.EqualTo(1));
        Assert.That(pages.Roots[0].Units, Is.EqualTo(100));
        Assert.That(destination.View.Usage.Get(new ItemKey("item.stack")).Pieces, Is.EqualTo(1));
    }

    [TestCase(TransferOutcome.Rejected)]
    [TestCase(TransferOutcome.Returned)]
    [TestCase(TransferOutcome.Unowned)]
    public void Accept_PreservesDestinationFailureOutcome(TransferOutcome outcome)
    {
        var source = new MemoryInventory().With(new BoundingSize(1, 1), "item.a", 3);
        source.TryPeekFirst(out var root);
        var expected = outcome switch
        {
            TransferOutcome.Rejected => TransferStep.Rejected(StoreRejection.PhysicalLimit),
            TransferOutcome.Returned => TransferStep.Returned,
            TransferOutcome.Unowned => TransferStep.Unowned,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
        var destination = new FixedResultStorage(new InsertResult(0, expected, new StorageChangeSet()));

        var actual = new RootStorageSink(destination).Accept(root, Actionable.Modulate, Player);

        Assert.That(actual.Outcome, Is.EqualTo(outcome));
        if (outcome == TransferOutcome.Rejected)
            Assert.That(actual.Rejection, Is.EqualTo(StoreRejection.PhysicalLimit));
        Assert.That(source.Roots.Count, Is.EqualTo(1));
    }

    [TestCase(TransferOutcome.Returned)]
    [TestCase(TransferOutcome.Unowned)]
    public void Accept_RejectsInsertedUnitsWithExplicitReturnOrUnknownOwnership(TransferOutcome outcome)
    {
        var source = new MemoryInventory().With(new BoundingSize(1, 1), "item.a", 3);
        source.TryPeekFirst(out var root);
        var lastStep = outcome == TransferOutcome.Returned ? TransferStep.Returned : TransferStep.Unowned;
        var destination = new FixedResultStorage(new InsertResult(1, lastStep, new StorageChangeSet()));

        Assert.Throws<InvalidOperationException>(new Action(() =>
            new RootStorageSink(destination).Accept(root, Actionable.Modulate, Player)));
    }

    [TestCase(TransferOutcome.SourceEmpty, 1)]
    [TestCase(TransferOutcome.Moved, 1)]
    [TestCase(TransferOutcome.Rejected, 1)]
    public void Accept_RejectsPartialInsertionWithoutOwnershipEvidence(TransferOutcome outcome, long insertedUnits)
    {
        var source = new MemoryInventory().With(new BoundingSize(1, 1), "item.a", 3);
        source.TryPeekFirst(out var root);
        var lastStep = outcome == TransferOutcome.Rejected
            ? TransferStep.Rejected(StoreRejection.PhysicalLimit)
            : outcome == TransferOutcome.Moved ? TransferStep.Moved : TransferStep.SourceEmpty;
        var destination = new FixedResultStorage(new InsertResult(insertedUnits, lastStep, new StorageChangeSet()));

        Assert.Throws<InvalidOperationException>(new Action(() =>
            new RootStorageSink(destination).Accept(root, Actionable.Modulate, Player)));
    }

    [Test]
    public void DetachedRoot_CanBeSimulatedButCannotBeModulatedWithoutAnOrigin()
    {
        var root = new RootSnapshot(0, 0, 0, new BoundingSize(1, 1), new ItemKey("item.a"), 3);
        var pages = new MemoryCellPages();
        var destination = NewCell(pages);
        var sink = new RootStorageSink(destination);

        Assert.That(sink.Accept(root, Actionable.Simulate, Player).IsMoved, Is.True);
        Assert.That(sink.Accept(root, Actionable.Modulate, Player).Outcome, Is.EqualTo(TransferOutcome.Unowned));
        Assert.That(destination.GetStoredItemCount(), Is.Zero);
        Assert.That(pages.MoveCalls, Is.Zero);
    }

    private static BasicCellInventory NewCell(MemoryCellPages pages) =>
        new(MemoryCellPages.EmptyView(StorageCellTier.All[0]), pages);

    private sealed class FixedResultStorage : IRootStorage
    {
        private readonly InsertResult _insertResult;

        internal FixedResultStorage(InsertResult insertResult) => _insertResult = insertResult;

        public bool IsPreferredStorageFor(ItemKey what, IActionSource source) => false;

        public InsertResult Insert(IRootSource roots, Actionable mode, IActionSource source)
        {
            var candidates = roots.Snapshot().ToArray();
            Assert.That(candidates.Length, Is.EqualTo(1));
            return _insertResult;
        }

        public ExtractResult Extract(ItemKey what, long amount, IRootSink destination, Actionable mode, IActionSource source) =>
            throw new NotSupportedException();

        public void GetAvailableStacks(KeyCounter output) => throw new NotSupportedException();

        public void GetAvailablePieces(KeyCounter output) => throw new NotSupportedException();

        public string GetDescription() => "fixed result";
    }
}
