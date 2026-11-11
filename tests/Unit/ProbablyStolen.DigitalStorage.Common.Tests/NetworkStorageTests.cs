using System;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class NetworkStorageTests
{
    private static readonly BoundingSize OneByOne = new(1, 1);
    private static readonly ItemKey Apple = new("item.apple");
    private static readonly ItemKey Pear = new("item.pear");
    private static readonly IActionSource Player = new PlayerSource("tester");

    private static InventoryHandler Cell(CellPartition partition, params ItemKey[] stored)
    {
        var inventory = new BasicCellInventory(
            MemoryCellPages.EmptyView(StorageCellTier.All[0], partition: partition), new MemoryCellPages());
        foreach (var key in stored)
            inventory.Insert(new MemoryInventory().With(OneByOne, key.Identifier, 1), Actionable.Modulate, Player);
        return new InventoryHandler(inventory, partition);
    }

    private static CellPartition Whitelist(params ItemKey[] keys) => CellPartition.FromUserSubmission(keys, 63);

    private static int[] Order(NetworkStorage network, IStorage[] mounted, ItemKey key) =>
        network.InsertOrder(key, Player).Select(storage => Array.IndexOf(mounted, storage)).ToArray();

    private static (NetworkStorage Network, IStorage[] Cells) Mount(params (int Priority, IStorage Storage)[] cells)
    {
        var network = new NetworkStorage();
        foreach (var (priority, storage) in cells) network.Mount(priority, storage);
        return (network, cells.Select(cell => cell.Storage).ToArray());
    }

    [Test]
    public void PreferredCellsInMountOrderThenOthers()
    {
        // A whitelist listing the type and a cell already holding it are equally preferred; mount order decides.
        var (network, cells) = Mount((0, Cell(CellPartition.Unfiltered)), (0, Cell(CellPartition.Unfiltered, Apple)),
            (0, Cell(Whitelist(Apple))), (0, Cell(CellPartition.Unfiltered)));

        Assert.That(Order(network, cells, Apple), Is.EqualTo(new[] { 1, 2, 0, 3 }));
    }

    [Test]
    public void HigherPriorityGroupsComeFirst_EachWithTwoPasses()
    {
        // Priority groups high to low, the two passes run inside each group.
        var (network, cells) = Mount((0, Cell(CellPartition.Unfiltered)), (5, Cell(CellPartition.Unfiltered)),
            (0, Cell(CellPartition.Unfiltered, Apple)), (5, Cell(Whitelist(Apple))), (-3, Cell(CellPartition.Unfiltered, Apple)));

        Assert.That(Order(network, cells, Apple), Is.EqualTo(new[] { 3, 1, 2, 0, 4 }));
    }

    [Test]
    public void PartitionExcludesACellWhateverItsPriority()
    {
        var (network, cells) = Mount((999, Cell(Whitelist(Pear))), (-999, Cell(CellPartition.Unfiltered)));

        var result = network.Insert(new MemoryInventory().With(OneByOne, "item.apple", 1), Actionable.Modulate, Player);

        Assert.That(result.InsertedUnits, Is.EqualTo(1));
        Assert.That(Units(cells[0], Apple), Is.Zero);
        Assert.That(Units(cells[1], Apple), Is.EqualTo(1));
    }

    [Test]
    public void OnlyMismatchedPartitions_RejectWithPartitionRejected()
    {
        var (network, _) = Mount((0, Cell(Whitelist(Pear))));
        var items = new MemoryInventory().With(OneByOne, "item.apple", 1);

        var result = network.Insert(items, Actionable.Modulate, Player);

        Assert.That(result.StoppedBy.Rejection, Is.EqualTo(StoreRejection.PartitionRejected));
        Assert.That(items.Units("item.apple"), Is.EqualTo(1));
    }

    [Test]
    public void ClearedPartition_AcceptsEverything()
    {
        var (network, _) = Mount((0, Cell(Whitelist())));

        Assert.That(network.Insert(new MemoryInventory().With(OneByOne, "item.apple", 1), Actionable.Modulate, Player)
            .InsertedUnits, Is.EqualTo(1));
    }

    [Test]
    public void BlacklistedCell_IsPreferredOnlyWhenItAlreadyHoldsTheType()
    {
        var blacklist = CellPartition.FromConfiguration(IncludeExclude.Blacklist, new[] { Pear }, false, 128);
        var empty = Cell(blacklist);
        var holding = Cell(blacklist, Apple);

        Assert.That(empty.IsPreferredStorageFor(Apple, Player), Is.False);
        Assert.That(holding.IsPreferredStorageFor(Apple, Player), Is.True);
        Assert.That(empty.IsPreferredStorageFor(Pear, Player), Is.False);
    }

    [Test]
    public void ReadOnlyHandler_RefusesInsertionButStillExtracts()
    {
        var inner = new BasicCellInventory(MemoryCellPages.EmptyView(StorageCellTier.All[0]), new MemoryCellPages());
        inner.Insert(new MemoryInventory().With(OneByOne, "item.apple", 2), Actionable.Modulate, Player);
        var (network, _) = Mount((0, new InventoryHandler(inner, CellPartition.Unfiltered, allowInsertion: false)));
        var bag = new MemoryInventory();

        var inserted = network.Insert(new MemoryInventory().With(OneByOne, "item.apple", 1), Actionable.Modulate, Player);
        var extracted = network.Extract(Apple, 1, bag, Actionable.Modulate, Player);

        Assert.That(inserted.StoppedBy.Rejection, Is.EqualTo(StoreRejection.ReadOnly));
        Assert.That(extracted.ExtractedUnits, Is.EqualTo(2));
        Assert.That(bag.Units("item.apple"), Is.EqualTo(2));
    }

    [Test]
    public void ExtractOrder_LowestPriorityFirst_MountOrderWithin()
    {
        var (network, cells) = Mount((5, Cell(CellPartition.Unfiltered)), (0, Cell(CellPartition.Unfiltered)),
            (-2, Cell(CellPartition.Unfiltered)), (0, Cell(CellPartition.Unfiltered)), (5, Cell(CellPartition.Unfiltered)));

        Assert.That(network.ExtractOrder().Select(storage => Array.IndexOf(cells, storage)), Is.EqualTo(new[] { 2, 1, 3, 0, 4 }));
        Assert.That(new NetworkStorage().ExtractOrder(), Is.Empty);
    }

    [Test]
    public void Extract_TakesFromLowPriorityFirstAndSpansCells()
    {
        var (network, cells) = Mount((5, Cell(CellPartition.Unfiltered, Apple)), (0, Cell(CellPartition.Unfiltered, Apple)));
        var bag = new MemoryInventory();

        var first = network.Extract(Apple, 1, bag, Actionable.Modulate, Player);
        Assert.That(first.ExtractedUnits, Is.EqualTo(1));
        Assert.That(Units(cells[1], Apple), Is.Zero, "The priority 0 cell gives first.");
        Assert.That(Units(cells[0], Apple), Is.EqualTo(1));

        var rest = network.Extract(Apple, 5, bag, Actionable.Modulate, Player);
        Assert.That(rest.ExtractedUnits, Is.EqualTo(1));
        Assert.That(rest.StoppedBy.Outcome, Is.EqualTo(TransferOutcome.SourceEmpty));
        Assert.That(bag.Units("item.apple"), Is.EqualTo(2));
    }

    [Test]
    public void Insert_MovesEveryRootAndMergesStacks()
    {
        var (network, _) = Mount((0, Cell(CellPartition.Unfiltered)), (0, Cell(CellPartition.Unfiltered)));
        var items = new MemoryInventory().With(OneByOne, "item.apple", 3).With(OneByOne, "item.pear", 2);

        var result = network.Insert(items, Actionable.Modulate, Player);
        var counter = new KeyCounter();
        network.GetAvailableStacks(counter);

        Assert.That(result.StoppedBy.Outcome, Is.EqualTo(TransferOutcome.SourceEmpty));
        Assert.That(result.InsertedUnits, Is.EqualTo(5));
        Assert.That(counter.Get(Apple) + counter.Get(Pear), Is.EqualTo(5));
        Assert.That(items.Roots, Is.Empty);
    }

    [Test]
    public void Simulate_ChangesNothing()
    {
        var (network, cells) = Mount((0, Cell(CellPartition.Unfiltered)));
        var items = new MemoryInventory().With(OneByOne, "item.apple", 3);

        var result = network.Insert(items, Actionable.Simulate, Player);

        Assert.That(result.InsertedUnits, Is.EqualTo(3));
        Assert.That(items.Units("item.apple"), Is.EqualTo(3));
        Assert.That(Units(cells[0], Apple), Is.Zero);
    }

    private static long Units(IStorage storage, ItemKey key)
    {
        var counter = new KeyCounter();
        storage.GetAvailableStacks(counter);
        return counter.Get(key);
    }
}
