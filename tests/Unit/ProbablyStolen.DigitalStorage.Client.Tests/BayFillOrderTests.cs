using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Machines;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class BayFillOrderTests
{
    [Test]
    public void After_TriesTheFollowingBaysThenWrapsAround() =>
        Assert.That(BayFillOrder.After(2, 5), Is.EqualTo(new[] { 3, 4, 0, 1 }));

    [Test]
    public void After_LeavesOutTheTargetItself()
    {
        Assert.That(BayFillOrder.After(0, 1), Is.Empty);
        Assert.That(BayFillOrder.After(13, 14), Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }));
    }

    [Test]
    public void After_RejectsATargetOutsideTheGrid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => BayFillOrder.After(5, 5)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => BayFillOrder.After(-1, 5)));
    }
}
