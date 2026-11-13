using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Storage;

namespace ProbablyStolen.DigitalStorage.Api.Tests;

[TestFixture]
public sealed class StorageResultTests
{
    [Test]
    public void Constructor_NonnegativeUnits_PreservesValues()
    {
        foreach (var units in new[] { 0L, 1L, long.MaxValue })
        {
            var result = new StorageResult(units, StorageStatus.Partial);
            Assert.That(result.Units, Is.EqualTo(units));
            Assert.That(result.Status, Is.EqualTo(StorageStatus.Partial));
        }
    }

    [Test]
    public void Constructor_NegativeUnits_Rejects()
    {
        foreach (var units in new[] { -1L, long.MinValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>((Action)(() => new StorageResult(units, StorageStatus.Done)));
        }
    }

    [Test]
    public void None_PreservesStatusAndHasZeroUnits()
    {
        Assert.That(StorageResult.None(StorageStatus.Unavailable), Is.EqualTo(new StorageResult(0, StorageStatus.Unavailable)));
    }

    [Test]
    public void Equality_UsesUnitsAndStatusAndRejectsOtherObjects()
    {
        var value = new StorageResult(2, StorageStatus.Partial);
        var equal = new StorageResult(2, StorageStatus.Partial);
        Assert.That(value.Equals(equal), Is.True);
        Assert.That(value.Equals((object)equal), Is.True);
        Assert.That(value.GetHashCode(), Is.EqualTo(equal.GetHashCode()));
        Assert.That(value.Equals(new StorageResult(3, StorageStatus.Partial)), Is.False);
        Assert.That(value.Equals(new StorageResult(2, StorageStatus.Done)), Is.False);
        Assert.That(value.Equals(null), Is.False);
        Assert.That(value.Equals((object)2), Is.False);
    }
}
