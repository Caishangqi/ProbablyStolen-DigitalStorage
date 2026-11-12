using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class StorageChangeSetTests
{
    private static readonly ItemKey Apple = new("item.apple");

    [Test]
    public void Add_KeepsNetChangesAndDropsZero()
    {
        var changes = new StorageChangeSet();

        changes.Add(Apple, 5);
        changes.Add(Apple, -2);
        Assert.That(changes.Get(Apple), Is.EqualTo(3));

        changes.Add(Apple, -3);
        Assert.That(changes.IsEmpty, Is.True);
        Assert.Throws<OverflowException>(new Action(() =>
        {
            changes.Add(Apple, long.MaxValue);
            changes.Add(Apple, 1);
        }));
    }

    [Test]
    public void AddAll_MergesSets()
    {
        var first = new StorageChangeSet();
        var second = new StorageChangeSet();
        first.Add(Apple, 2);
        second.Add(Apple, -2);
        second.Add(new ItemKey("item.pear"), 1);

        first.AddAll(second);

        Assert.That(first.Changes.Count, Is.EqualTo(1));
        Assert.That(first.Get(new ItemKey("item.pear")), Is.EqualTo(1));
    }

    [Test]
    public void SourcesAndSteps_CarryTheirIdentity()
    {
        Assert.That(new PlayerSource("Ada").PlayerName, Is.EqualTo("Ada"));
        Assert.That(new MachineSource("drive:1").MachineId, Is.EqualTo("drive:1"));
        Assert.That(MigrationSource.Instance.PlayerName, Is.Null);
        Assert.Throws<ArgumentException>(new Action(() => _ = new PlayerSource("")));
        Assert.That(TransferStep.Rejected(StoreRejection.Capacity).Rejection, Is.EqualTo(StoreRejection.Capacity));
        Assert.That(TransferStep.Moved.IsMoved, Is.True);
        Assert.That(TransferStep.Returned.IsMoved, Is.False);
    }

    [Test]
    public void RootSnapshot_OrdersByPageThenRowThenColumn()
    {
        var size = new BoundingSize(1, 1);
        var a = new RootSnapshot(1, 5, 0, size, Apple, 1);
        var b = new RootSnapshot(1, 0, 1, size, Apple, 1);
        var c = new RootSnapshot(2, 0, 0, size, Apple, 1);

        Assert.That(RootSnapshot.CompareOrder(a, b), Is.LessThan(0));
        Assert.That(RootSnapshot.CompareOrder(b, c), Is.LessThan(0));
        Assert.That(RootSnapshot.CompareOrder(a, a), Is.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => _ = new RootSnapshot(0, 0, 0, size, Apple, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => _ = new RootSnapshot(-1, 0, 0, size, Apple, 1)));
    }
}
