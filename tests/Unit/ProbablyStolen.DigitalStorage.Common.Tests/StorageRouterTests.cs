using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class StorageRouterTests
{
    private static readonly ItemKey Apple = new("item.apple");
    private static readonly ItemKey Pear = new("item.pear");

    [Test]
    public void PreferredCellsInMountOrderThenOthers()
    {
        // AE2: a whitelist listing the type and a cell already holding it are equally preferred; mount order decides.
        var cells = new[]
        {
            Cell(CellPartition.Unfiltered),
            Cell(CellPartition.Unfiltered, Apple),
            Cell(CellPartition.FromUserSubmission(new[] { Apple })),
            Cell(CellPartition.Unfiltered)
        };

        Assert.That(StorageRouter.Order(cells, Apple), Is.EqualTo(new[] { 1, 2, 0, 3 }));
    }

    [Test]
    public void PartitionWithoutType_IsExcluded()
    {
        var cells = new[]
        {
            Cell(CellPartition.FromUserSubmission(new[] { Pear })),
            Cell(CellPartition.Unfiltered)
        };

        Assert.That(StorageRouter.Order(cells, Apple), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void OnlyMismatchedPartitions_YieldNoCandidate()
    {
        var cells = new[] { Cell(CellPartition.FromUserSubmission(new[] { Pear })) };

        Assert.That(StorageRouter.Order(cells, Apple), Is.Empty);
    }

    [Test]
    public void ClearedPartition_AcceptsEverything()
    {
        var cleared = CellPartition.FromUserSubmission(Array.Empty<ItemKey>());

        Assert.That(StorageRouter.Order(new[] { Cell(cleared) }, Apple), Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void HigherPriorityGroupsComeFirst_EachWithTwoPasses()
    {
        // AE2 NetworkStorage.insert: priority groups high to low, the two passes run inside each group.
        var cells = new[]
        {
            Cell(CellPartition.Unfiltered, priority: 0),
            Cell(CellPartition.Unfiltered, priority: 5),
            Cell(CellPartition.Unfiltered, priority: 0, Apple),
            Cell(CellPartition.FromUserSubmission(new[] { Apple }), priority: 5),
            Cell(CellPartition.Unfiltered, priority: -3, Apple)
        };

        Assert.That(StorageRouter.Order(cells, Apple), Is.EqualTo(new[] { 3, 1, 2, 0, 4 }));
    }

    [Test]
    public void PriorityDoesNotOverridePartition()
    {
        var cells = new[]
        {
            Cell(CellPartition.FromUserSubmission(new[] { Pear }), priority: 999),
            Cell(CellPartition.Unfiltered, priority: -999)
        };

        Assert.That(StorageRouter.Order(cells, Apple), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void ExtractOrder_LowestPriorityFirst_MountOrderWithin()
    {
        Assert.That(StorageRouter.ExtractOrder(new[] { 5, 0, -2, 0, 5 }), Is.EqualTo(new[] { 2, 1, 3, 0, 4 }));
    }

    [Test]
    public void ExtractOrder_EqualPriorities_KeepMountOrder()
    {
        Assert.That(StorageRouter.ExtractOrder(new[] { 0, 0, 0 }), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(StorageRouter.ExtractOrder(Array.Empty<int>()), Is.Empty);
    }

    private static CellView Cell(CellPartition partition, params ItemKey[] types) =>
        Cell(partition, DrivePriority.Default, types);

    private static CellView Cell(CellPartition partition, int priority, params ItemKey[] types) =>
        new(CellLayout.Cell1K, new[] { PageView.Free }, types, partition, priority);
}
