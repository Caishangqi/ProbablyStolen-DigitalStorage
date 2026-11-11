using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class KeyCounterTests
{
    [Test]
    public void Add_AggregatesPerKeyAndIgnoresZero()
    {
        var counter = new KeyCounter();
        var key = new ItemKey("item.apple");

        counter.Add(key, 2);
        counter.Add(key, 3);
        counter.Add(new ItemKey("item.pear"), 0);

        Assert.That(counter.Get(key), Is.EqualTo(5));
        Assert.That(counter.Size(), Is.EqualTo(1));
        Assert.That(counter.Get(new ItemKey("item.pear")), Is.Zero);
    }

    [Test]
    public void Add_RejectsNullNegativeAndOverflow()
    {
        var counter = new KeyCounter();
        var key = new ItemKey("item.apple");
        counter.Add(key, 1);

        Assert.Throws<ArgumentNullException>(new Action(() => counter.Add(null!, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => counter.Add(key, -1)));
        Assert.Throws<OverflowException>(new Action(() => counter.Add(key, long.MaxValue)));
        Assert.That(counter.Get(key), Is.EqualTo(1));
    }

    [Test]
    public void Snapshot_IsADetachedCopy()
    {
        var counter = new KeyCounter();
        var key = new ItemKey("item.apple");
        counter.Add(key, 4);
        var snapshot = counter.Snapshot();

        counter.Add(key, 6);

        Assert.That(snapshot[key], Is.EqualTo(4));
        Assert.That(counter.Get(key), Is.EqualTo(10));
        Assert.That(snapshot, Is.Not.SameAs(counter.Snapshot()));
    }

    [Test]
    public void Clear_RemovesAllAggregates()
    {
        var counter = new KeyCounter();
        var key = new ItemKey("item.apple");
        counter.Add(key, 4);

        counter.Clear();

        Assert.That(counter.Size(), Is.Zero);
        Assert.That(counter.Get(key), Is.Zero);
    }
}
