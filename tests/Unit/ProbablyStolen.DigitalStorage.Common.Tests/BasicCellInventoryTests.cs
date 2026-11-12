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
public sealed class BasicCellInventoryTests
{
    private static readonly BoundingSize OneByOne = new(1, 1);
    private static readonly BoundingSize TwoByTwo = new(2, 2);
    private static readonly StorageCellTier Tier1K = StorageCellTier.All[0];
    private static readonly IActionSource Player = new PlayerSource("tester");

    private static (BasicCellInventory Cell, MemoryCellPages Pages) NewCell(
        StorageCellTier? tier = null, int typeLimit = 128, CellPartition? partition = null, bool equal = false)
    {
        var pages = new MemoryCellPages();
        var view = MemoryCellPages.EmptyView(tier ?? Tier1K, typeLimit, partition,
            equal ? Partitions.EqualDistribution : null);
        return (new BasicCellInventory(view, pages), pages);
    }

    [Test]
    public void Billing_RoundsAreaUnitsOnceAndChargesPerType()
    {
        var (cell, _) = NewCell();
        var items = new MemoryInventory().With(TwoByTwo, "item.a", 3).With(OneByOne, "item.b", 1);

        cell.Insert(items, Actionable.Modulate, Player);

        Assert.That(cell.GetStoredItemCount(), Is.EqualTo(13));
        Assert.That(cell.GetStoredItemTypes(), Is.EqualTo(2));
        Assert.That(cell.GetUsedBytes(), Is.EqualTo(2 * 4 + 2));
        Assert.That(cell.GetFreeBytes(), Is.EqualTo(1024 - 10));
        Assert.That(cell.GetRemainingItemCount(), Is.EqualTo(1014 * 8 + 3));
        Assert.That(cell.GetRemainingItemTypes(), Is.EqualTo(126));
    }

    [Test]
    public void Extracting_TheLastRootReleasesTheTypeCost()
    {
        var (cell, _) = NewCell();
        cell.Insert(new MemoryInventory().With(OneByOne, "item.a", 2), Actionable.Modulate, Player);
        var bag = new MemoryInventory();

        var result = cell.Extract(new ItemKey("item.a"), 1, bag, Actionable.Modulate, Player);

        Assert.That(result.ExtractedUnits, Is.EqualTo(2), "Whole roots leave, so more than requested can move.");
        Assert.That(cell.GetUsedBytes(), Is.Zero);
        Assert.That(cell.GetStatus(), Is.EqualTo(CellState.Empty));
        Assert.That(bag.Units("item.a"), Is.EqualTo(2));
    }

    [Test]
    public void Tiers_ScaleOneToFourAndFullTypesCostHalf()
    {
        foreach (var tier in StorageCellTier.All)
        {
            var (cell, _) = NewCell(tier);
            Assert.That(cell.GetTotalBytes(), Is.EqualTo(1024L * tier.Multiplier));
            Assert.That(tier.GetBytesPerType() * 128, Is.EqualTo(cell.GetTotalBytes() / 2), tier.Name);
            Assert.That(tier.GetTypeBudget(), Is.EqualTo(cell.GetTotalBytes() / 2), tier.Name);
        }
    }

    [Test]
    public void RaisedLimit_SpreadsTheTypeCostAndKeepsHalfTheBytesForItems()
    {
        var (cell, _) = NewCell(typeLimit: 256);
        Assert.That(cell.GetRemainingItemTypes(), Is.EqualTo(256));
        Assert.That(cell.CanHoldNewItem(), Is.True);

        cell.Insert(new MemoryInventory().With(OneByOne, "item.a", 1), Actionable.Modulate, Player);

        // ceil(512 / 256) = 2 type bytes plus 1 area byte.
        Assert.That(cell.GetUsedBytes(), Is.EqualTo(3));
        Assert.That(cell.GetRemainingItemTypes(), Is.EqualTo(255));
    }

    [Test]
    public void Status_ReflectsCellUsage()
    {
        var (empty, _) = NewCell();
        Assert.That(empty.GetStatus(), Is.EqualTo(CellState.Empty));

        var (partial, _) = NewCell();
        partial.Insert(new MemoryInventory().With(OneByOne, "item.a", 1), Actionable.Modulate, Player);
        Assert.That(partial.GetStatus(), Is.EqualTo(CellState.NotEmpty));

        var (typesFull, _) = NewCell(typeLimit: 1);
        typesFull.Insert(new MemoryInventory().With(OneByOne, "item.a", 1), Actionable.Modulate, Player);
        Assert.That(typesFull.GetStatus(), Is.EqualTo(CellState.TypesFull));

        var (full, _) = NewCell();
        full.Insert(new MemoryInventory().With(OneByOne, "item.a", 8160), Actionable.Modulate, Player);
        Assert.That(full.GetFreeBytes(), Is.Zero);
        Assert.That(full.GetStatus(), Is.EqualTo(CellState.Full));
    }

    [Test]
    public void SimulateAndModulate_Agree()
    {
        var items = new MemoryInventory();
        for (var i = 0; i < 40; i++) items.With(new BoundingSize(1 + i % 3, 1 + i % 2), $"item.t{i % 7}", 1 + i);
        var (cell, pages) = NewCell(typeLimit: 5);

        var simulated = cell.Insert(items, Actionable.Simulate, Player);
        Assert.That(pages.MoveCalls, Is.Zero);
        Assert.That(cell.GetStoredItemCount(), Is.Zero, "Simulation changes nothing.");
        var modulated = cell.Insert(items, Actionable.Modulate, Player);

        Assert.That(modulated.InsertedUnits, Is.EqualTo(simulated.InsertedUnits));
        Assert.That(modulated.StoppedBy.ToString(), Is.EqualTo(simulated.StoppedBy.ToString()));
        Assert.That(modulated.Changes.Changes, Is.EquivalentTo(simulated.Changes.Changes));
    }

    [Test]
    public void ChangeSet_MatchesTheAvailableStacksDifference()
    {
        var (cell, _) = NewCell();
        cell.Insert(new MemoryInventory().With(OneByOne, "item.a", 5), Actionable.Modulate, Player);
        var before = Stacks(cell);

        var inserted = cell.Insert(new MemoryInventory().With(OneByOne, "item.a", 2).With(TwoByTwo, "item.b", 3),
            Actionable.Modulate, Player);
        var afterInsert = Stacks(cell);
        var extracted = cell.Extract(new ItemKey("item.a"), 6, new MemoryInventory(), Actionable.Modulate, Player);
        var afterExtract = Stacks(cell);

        AssertDifference(before, afterInsert, inserted.Changes);
        AssertDifference(afterInsert, afterExtract, extracted.Changes);
    }

    [Test]
    public void Insert_RespectsThePartition()
    {
        var blacklist = Partitions.Blacklist(new ItemKey("item.b"));
        var (cell, _) = NewCell(partition: blacklist);
        var items = new MemoryInventory().With(OneByOne, "item.a", 1).With(OneByOne, "item.b", 1);

        var result = cell.Insert(items, Actionable.Modulate, Player);

        Assert.That(result.InsertedUnits, Is.EqualTo(1));
        Assert.That(result.StoppedBy.Rejection, Is.EqualTo(StoreRejection.PartitionRejected));
        Assert.That(items.Units("item.b"), Is.EqualTo(1));
    }

    [Test]
    public void Insert_StopsAtAFailedMoveWithoutLosingItems()
    {
        var (cell, pages) = NewCell();
        pages.FailMoveOnCall = 2;
        var items = new MemoryInventory().With(OneByOne, "item.a", 1).With(OneByOne, "item.a", 1).With(OneByOne, "item.a", 1);

        var result = cell.Insert(items, Actionable.Modulate, Player);

        Assert.That(result.StoppedBy.Outcome, Is.EqualTo(TransferOutcome.Returned));
        Assert.That(result.InsertedUnits, Is.EqualTo(1));
        Assert.That(cell.GetStoredItemCount() + items.Units("item.a"), Is.EqualTo(3));
        Assert.That(pages.Roots.Count, Is.EqualTo(1));
    }

    [Test]
    public void Insert_ReportsARefusedPageAppendAsPhysicalLimit()
    {
        var (cell, pages) = NewCell();
        pages.RefuseAppend = true;

        var result = cell.Insert(new MemoryInventory().With(OneByOne, "item.a", 1), Actionable.Modulate, Player);

        Assert.That(result.StoppedBy.Rejection, Is.EqualTo(StoreRejection.PhysicalLimit));
        Assert.That(cell.View.Pages.Count, Is.EqualTo(1));
    }

    [Test]
    public void Extract_StopsAtTheDestinationAndKeepsTheRest()
    {
        var (cell, _) = NewCell();
        cell.Insert(new MemoryInventory().With(OneByOne, "item.a", 1).With(OneByOne, "item.a", 1), Actionable.Modulate, Player);
        var bag = new MemoryInventory { Capacity = 1 };

        var result = cell.Extract(new ItemKey("item.a"), 2, bag, Actionable.Modulate, Player);

        Assert.That(result.ExtractedUnits, Is.EqualTo(1));
        Assert.That(result.StoppedBy.Rejection, Is.EqualTo(StoreRejection.PhysicalLimit));
        Assert.That(Stacks(cell).Get(new ItemKey("item.a")), Is.EqualTo(1));
    }

    [Test]
    public void ExtractRoot_MovesOnlyTheAddressedRoot()
    {
        var (cell, pages) = NewCell();
        cell.Insert(new MemoryInventory().With(OneByOne, "item.a", 3).With(OneByOne, "item.a", 5), Actionable.Modulate, Player);
        var second = pages.Roots.Single(root => root.Units == 5);
        var bag = new MemoryInventory();

        var result = cell.ExtractRoot(second.Key, second.Page, second.X, second.Y, bag, Actionable.Modulate, Player);

        Assert.That(result.ExtractedUnits, Is.EqualTo(5));
        Assert.That(result.StoppedBy.IsMoved, Is.True);
        Assert.That(result.Changes.Get(second.Key), Is.EqualTo(-5));
        Assert.That(bag.Units("item.a"), Is.EqualTo(5));
        Assert.That(Stacks(cell).Get(second.Key), Is.EqualTo(3), "The other root of the same type stays.");
        Assert.That(cell.GetStoredItemCount() + bag.Units("item.a"), Is.EqualTo(8), "No unit appears or disappears.");
    }

    [Test]
    public void ExtractRoot_UnknownPositionOrType_ReportsSourceEmpty()
    {
        var (cell, pages) = NewCell();
        cell.Insert(new MemoryInventory().With(OneByOne, "item.a", 2), Actionable.Modulate, Player);
        var stored = pages.Roots.Single();
        var bag = new MemoryInventory();

        var elsewhere = cell.ExtractRoot(stored.Key, stored.Page, stored.X + 1, stored.Y, bag, Actionable.Modulate, Player);
        var otherType = cell.ExtractRoot(new ItemKey("item.b"), stored.Page, stored.X, stored.Y, bag, Actionable.Modulate, Player);

        Assert.That(elsewhere.StoppedBy.Outcome, Is.EqualTo(TransferOutcome.SourceEmpty));
        Assert.That(otherType.StoppedBy.Outcome, Is.EqualTo(TransferOutcome.SourceEmpty));
        Assert.That(elsewhere.ExtractedUnits + otherType.ExtractedUnits, Is.Zero);
        Assert.That(Stacks(cell).Get(stored.Key), Is.EqualTo(2));
    }

    [Test]
    public void ExtractRoot_Simulate_ChangesNothing()
    {
        var (cell, pages) = NewCell();
        cell.Insert(new MemoryInventory().With(OneByOne, "item.a", 4), Actionable.Modulate, Player);
        var stored = pages.Roots.Single();
        var bag = new MemoryInventory();
        var movesBefore = pages.MoveCalls;

        var result = cell.ExtractRoot(stored.Key, stored.Page, stored.X, stored.Y, bag, Actionable.Simulate, Player);

        Assert.That(result.ExtractedUnits, Is.EqualTo(4));
        Assert.That(bag.Units("item.a"), Is.Zero);
        Assert.That(Stacks(cell).Get(stored.Key), Is.EqualTo(4));
        Assert.That(pages.MoveCalls, Is.EqualTo(movesBefore));
        Assert.That(pages.Roots.Single(), Is.EqualTo(stored));
    }

    [Test]
    public void ExtractRoot_RefusedByDestination_KeepsTheRoot()
    {
        var (cell, pages) = NewCell();
        cell.Insert(new MemoryInventory().With(OneByOne, "item.a", 4), Actionable.Modulate, Player);
        var stored = pages.Roots.Single();
        var full = new MemoryInventory { Capacity = 0 };

        var result = cell.ExtractRoot(stored.Key, stored.Page, stored.X, stored.Y, full, Actionable.Modulate, Player);

        Assert.That(result.ExtractedUnits, Is.Zero);
        Assert.That(result.StoppedBy.Rejection, Is.EqualTo(StoreRejection.PhysicalLimit));
        Assert.That(Stacks(cell).Get(stored.Key), Is.EqualTo(4));
        Assert.That(cell.GetStoredItemCount(), Is.EqualTo(4));
    }

    [Test]
    public void ExtractRoot_PartOfARoot_CountsOnlyTheUnitsThatLeft()
    {
        var (cell, pages) = NewCell();
        cell.Insert(new MemoryInventory().With(OneByOne, "item.a", 10), Actionable.Modulate, Player);
        var stored = pages.Roots.Single();
        var sink = new SplittingSink(pages, 4);

        var result = cell.ExtractRoot(stored.Key, stored.Page, stored.X, stored.Y, sink, Actionable.Modulate, Player);

        Assert.That(result.ExtractedUnits, Is.EqualTo(4));
        Assert.That(result.StoppedBy.Outcome, Is.EqualTo(TransferOutcome.Returned), "Part of the root stays.");
        Assert.That(result.Changes.Get(stored.Key), Is.EqualTo(-4));
        Assert.That(pages.Roots.Single().Units, Is.EqualTo(6));
        Assert.That(cell.GetStoredItemCount(), Is.EqualTo(6), "Usage follows the units that left; the slot stays taken.");
        Assert.That(cell.GetStoredItemCount() + sink.Taken, Is.EqualTo(10));
    }

    // A destination that takes only part of a root, leaving the rest in its slot like a native split.
    private sealed class SplittingSink : IRootSink
    {
        private readonly MemoryCellPages _pages;
        private readonly long _units;

        internal SplittingSink(MemoryCellPages pages, long units)
        {
            _pages = pages;
            _units = units;
        }

        internal long Taken { get; private set; }

        public TransferStep Accept(RootSnapshot root, Actionable mode, IActionSource source)
        {
            if (mode == Actionable.Simulate) return TransferStep.Moved;
            _pages.KeepUnits(root, root.Units - _units);
            Taken += _units;
            return TransferStep.Moved;
        }
    }

    [Test]
    public void EqualShareQuota_AndPreference()
    {
        var keys = new[] { new ItemKey("item.a"), new ItemKey("item.b") };
        var (cell, _) = NewCell(partition: Partitions.Whitelist(keys), equal: true);
        cell.Insert(new MemoryInventory().With(OneByOne, "item.a", 1), Actionable.Modulate, Player);

        // The share follows the effective type limit, not the rule count: (1024 - 512) * 8 / 128 = 32.
        Assert.That(cell.GetEqualShareQuota(), Is.EqualTo(32));
        Assert.That(cell.IsPreferredStorageFor(keys[0], Player), Is.True);
        Assert.That(cell.IsPreferredStorageFor(keys[1], Player), Is.False);
    }

    private static KeyCounter Stacks(IRootStorage storage)
    {
        var counter = new KeyCounter();
        storage.GetAvailableStacks(counter);
        return counter;
    }

    private static void AssertDifference(KeyCounter before, KeyCounter after, StorageChangeSet changes)
    {
        var keys = before.Snapshot().Keys.Union(after.Snapshot().Keys).Union(changes.Changes.Keys);
        foreach (var key in keys)
            Assert.That(after.Get(key) - before.Get(key), Is.EqualTo(changes.Get(key)), key.ToString());
    }
}
