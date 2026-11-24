using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class IoPortDisplaySnapshotTests
{
    [TestCase(0, 0, 1d)]
    [TestCase(0, 4, 0d)]
    [TestCase(1, 3, 0.25d)]
    [TestCase(4, 0, 1d)]
    public void FromRoots_IsMovedOverMovedPlusRemaining(long moved, long remaining, double expected) =>
        Assert.That(IoPortProgress.FromRoots(moved, remaining).Value, Is.EqualTo(expected));

    [Test]
    public void Progress_RejectsNegativeCountsAndUnknownHasNoValue()
    {
        Assert.That(IoPortProgress.Unknown.Value, Is.Null);
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => IoPortProgress.FromRoots(-1, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => IoPortProgress.FromRoots(0, -1)));
    }

    [Test]
    public void Snapshot_KeepsStatusSettingsAndProgress()
    {
        var progress = IoPortProgress.FromRoots(1, 1);
        var snapshot = new IoPortDisplaySnapshot(IoPortRuntimeStatus.Paused, IoPortSettings.Default, progress);

        Assert.That(snapshot.Status, Is.EqualTo(IoPortRuntimeStatus.Paused));
        Assert.That(snapshot.Settings, Is.SameAs(IoPortSettings.Default));
        Assert.That(snapshot.Progress, Is.SameAs(progress));
        Assert.That(IoPortDisplaySnapshot.SettingsUnavailable.Settings, Is.Null);
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
            _ = new IoPortDisplaySnapshot((IoPortRuntimeStatus)99, null, IoPortProgress.Unknown)));
    }
}
