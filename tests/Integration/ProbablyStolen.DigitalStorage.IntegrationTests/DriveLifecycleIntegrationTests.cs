using System;
using System.Linq;
using NUnit.Framework;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class DriveLifecycleIntegrationTests
{
    [Test]
    public void Drive_FillFourteenIndependentBaysAndRejectFifteenth_ConservesEveryCell()
    {
        var drive = new NativeDriveBoundary();
        var originals = Enumerable.Range(0, 14).Select(_ => NativeCellBoundary.Assembled()).ToArray();
        for (var index = 0; index < originals.Length; index++)
        {
            Assert.That(drive.Insert(index, originals[index]), Is.True);
        }

        var extra = NativeCellBoundary.Assembled();
        for (var index = 0; index < originals.Length; index++)
        {
            Assert.That(drive.Insert(index, extra), Is.False);
            Assert.That(drive.Bays[index].Item, Is.SameAs(originals[index].Host));
            Assert.That(originals[index].Host.Parent, Is.SameAs(drive.Bays[index]));
        }

        Assert.That(extra.Host.Parent, Is.Null);
        Assert.That(drive.Bays.Select(slot => slot.Item).Distinct().Count(), Is.EqualTo(14));
        Assert.That(originals.Sum(cell => cell.Component.Item!.Count) + extra.Component.Item!.Count, Is.EqualTo(15));
    }

    [Test]
    public void TwoDrives_MovingSameOriginalBetweenThem_ChangesOnlyActualSourceAndTarget()
    {
        var first = new NativeDriveBoundary();
        var second = new NativeDriveBoundary();
        var cell = NativeCellBoundary.Assembled();
        var originalComponent = cell.Component.Item;
        var other = NativeCellBoundary.Assembled();
        first.Insert(0, cell);
        first.Insert(13, other);
        cell.Component.Item!.Fixed = false;

        Assert.That(cell.Extract(new PhysicalTestSlot()), Is.False);
        Assert.That(second.Insert(7, cell), Is.True);
        Assert.That(first.Bays[0].Item, Is.Null);
        Assert.That(second.Bays[7].Item, Is.SameAs(cell.Host));
        Assert.That(first.Bays[13].Item, Is.SameAs(other.Host));
        Assert.That(second.Bays[13].Item, Is.Null);
        Assert.That(cell.Component.Item, Is.SameAs(originalComponent));
        Assert.That(cell.Extract(new PhysicalTestSlot()), Is.False);
        var inventory = new PhysicalTestSlot();
        inventory.TransferHere(cell.Host);
        Assert.That(cell.Extract(new PhysicalTestSlot()), Is.True);
    }

    [Test]
    public void Drive_RejectsHousingAndCancelledPlacement_PreservesAllOwners()
    {
        var drive = new NativeDriveBoundary();
        var housing = new NativeCellBoundary();
        var cell = NativeCellBoundary.Assembled();
        var source = new PhysicalTestSlot();
        source.TransferHere(cell.Host);

        Assert.That(drive.Insert(0, housing), Is.False);
        Assert.That(drive.Insert(0, cell, true), Is.False);
        Assert.That(drive.Bays.All(slot => slot.Item == null), Is.True);
        Assert.That(source.Item, Is.SameAs(cell.Host));
    }

    [Test]
    public void Window_CloseAndReopen_RetainsBaysModulesBatteryAndSingleRestoration()
    {
        var drive = new NativeDriveBoundary();
        var cell = NativeCellBoundary.Assembled();
        drive.Insert(13, cell);
        var module = new PhysicalTestItem { Id = "native.module" };
        var battery = new PhysicalTestItem { Id = "native.battery" };
        drive.Modules.TransferHere(module);
        drive.Battery.TransferHere(battery);
        var window = drive.Window;
        var slot = drive.Bays[13];

        drive.Open();
        for (var iteration = 0; iteration < 3; iteration++)
        {
            drive.Close();
            Assert.That(drive.IsOpen, Is.False);
            drive.Open();
        }

        Assert.That(drive.IsOpen, Is.True);
        Assert.That(drive.RestoreCalls, Is.EqualTo(1));
        Assert.That(drive.Window, Is.SameAs(window));
        Assert.That(drive.Bays[13], Is.SameAs(slot));
        Assert.That(slot.Item, Is.SameAs(cell.Host));
        Assert.That(drive.Modules.Item, Is.SameAs(module));
        Assert.That(drive.Battery.Item, Is.SameAs(battery));
    }

    [Test]
    public void Lifecycle_DecodingThenIndependentOpen_RestoresOnlyReadyMachine()
    {
        var first = new NativeDriveBoundary { Decoding = true };
        var second = new NativeDriveBoundary();
        first.Open();
        second.Open();
        Assert.That(first.RestoreCalls, Is.Zero);
        Assert.That(second.RestoreCalls, Is.EqualTo(1));

        first.Decoding = false;
        first.Open();
        first.Lifecycle.RestoreIfReady(false);
        Assert.That(first.RestoreCalls, Is.EqualTo(1));
        Assert.That(second.RestoreCalls, Is.EqualTo(1));
    }

    [Test]
    public void Lifecycle_FailedRestore_RetriesSameWindowWithoutLosingItems()
    {
        var drive = new NativeDriveBoundary { FailRestore = true };
        var cell = NativeCellBoundary.Assembled();
        drive.Insert(4, cell);
        var window = drive.Window;

        Assert.Throws<InvalidOperationException>(new Action(() => drive.Open()));
        Assert.That(drive.Bays[4].Item, Is.SameAs(cell.Host));
        drive.FailRestore = false;
        drive.Close();
        drive.Open();
        Assert.That(drive.RestoreCalls, Is.EqualTo(2));
        Assert.That(drive.Window, Is.SameAs(window));
        Assert.That(drive.Bays[4].Item, Is.SameAs(cell.Host));
    }
}
