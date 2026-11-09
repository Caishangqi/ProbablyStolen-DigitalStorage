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
    public void PartitionedThenHoldingThenOtherUnfiltered()
    {
        var cells = new[]
        {
            Cell(CellPartition.Unfiltered),
            Cell(CellPartition.Unfiltered, Apple),
            Cell(CellPartition.FromUserSubmission(new[] { Apple })),
            Cell(CellPartition.Unfiltered)
        };

        Assert.That(StorageRouter.Order(cells, Apple), Is.EqualTo(new[] { 2, 1, 0, 3 }));
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

    private static CellView Cell(CellPartition partition, params ItemKey[] types) =>
        new(CellLayout.Cell1K, new[] { PageView.Free }, types, partition);
}
