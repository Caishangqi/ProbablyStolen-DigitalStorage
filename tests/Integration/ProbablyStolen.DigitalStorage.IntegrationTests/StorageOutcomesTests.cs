using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Storage;
using ProbablyStolen.DigitalStorage.Platform.Api;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class StorageOutcomesTests
{
    [TestCase(false, false, true, StorageStatus.WrongThread)]
    [TestCase(false, true, false, StorageStatus.WrongThread)]
    [TestCase(true, true, true, StorageStatus.Unavailable)]
    [TestCase(true, false, false, StorageStatus.Unavailable)]
    public void Refuse_ReportsWhyNothingRan(bool mainThread, bool decoding, bool online, StorageStatus expected)
    {
        var refused = StorageOutcomes.Refuse(mainThread, decoding, online);

        Assert.That(refused, Is.EqualTo(StorageResult.None(expected)));
    }

    [Test]
    public void Refuse_AllowsAMainThreadCallToAnOnlineNetwork()
    {
        Assert.That(StorageOutcomes.Refuse(true, false, true), Is.Null);
    }

    [TestCase(5, 5, true)]
    [TestCase(3, 5, false)]
    [TestCase(0, 5, false)]
    [TestCase(-1, 5, false)]
    [TestCase(6, 5, false)]
    public void IsWholeRoot_NeverSplitsAStack(int units, int rootUnits, bool expected)
    {
        Assert.That(StorageOutcomes.IsWholeRoot(units, rootUnits), Is.EqualTo(expected));
    }

    [TestCase(0, 10, true, StorageStatus.Empty, 0)]
    [TestCase(0, 10, false, StorageStatus.Rejected, 0)]
    [TestCase(4, 10, false, StorageStatus.Partial, 4)]
    [TestCase(12, 10, false, StorageStatus.Done, 12)]
    public void Extracted_ReportsTheUnitsThatMoved(long moved, long requested, bool empty, StorageStatus status, long units)
    {
        Assert.That(StorageOutcomes.Extracted(moved, requested, empty), Is.EqualTo(new StorageResult(units, status)));
    }

    [TestCase(0, 4, true, StorageStatus.NotStored, 0)]
    [TestCase(0, 4, false, StorageStatus.Rejected, 0)]
    [TestCase(1, 4, false, StorageStatus.Partial, 1)]
    [TestCase(4, 4, false, StorageStatus.Done, 4)]
    public void ExtractedRoot_DistinguishesMissingRefusedAndPartial(long moved, int requested, bool missing,
        StorageStatus status, long units)
    {
        Assert.That(StorageOutcomes.ExtractedRoot(moved, requested, missing), Is.EqualTo(new StorageResult(units, status)));
    }

    [TestCase(false, 3, true)]
    [TestCase(false, 0, false)]
    [TestCase(true, 3, false)]
    public void ChangedContents_OnlyForPerformedMoves(bool simulated, long moved, bool expected)
    {
        Assert.That(StorageOutcomes.ChangedContents(simulated, moved), Is.EqualTo(expected));
    }

    [TestCase(5, 5, StorageStatus.Done, 5)]
    [TestCase(0, 5, StorageStatus.Rejected, 0)]
    public void Inserted_IsAllOrNothing(long inserted, int units, StorageStatus status, long reported)
    {
        Assert.That(StorageOutcomes.Inserted(inserted, units), Is.EqualTo(new StorageResult(reported, status)));
    }
}
