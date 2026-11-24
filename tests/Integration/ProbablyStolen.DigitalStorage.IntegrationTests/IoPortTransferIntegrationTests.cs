using System;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class IoPortTransferIntegrationTests
{
    [Test]
    public void Forward_OneSecondMovesEveryWholeRootAndChargesOnce()
    {
        using var fixture = new IoPortTransferFixture(IoPortDirection.Forward);
        var source = fixture.Left[0].childItem!;
        var roots = new[] { fixture.AddRoot(source, 51), fixture.AddRoot(source, 52), fixture.AddRoot(source, 53) };
        roots[0].unitCount = 3;

        var display = fixture.Callback();

        Assert.That(display.Status, Is.EqualTo(IoPortRuntimeStatus.Working));
        Assert.That(fixture.Roots(source), Is.Empty);
        Assert.That(fixture.Roots(fixture.DriveCell!), Is.EquivalentTo(roots));
        Assert.That(roots[0].unitCount, Is.EqualTo(3));
        Assert.That((fixture.FeeDraws, fixture.Energy), Is.EqualTo((1, 99)));
        Assert.That(fixture.State.CommittedRoots(0, source.uniqueId), Is.EqualTo(3));
        Assert.That(display.Progress.Value, Is.EqualTo(1d));
    }

    [Test]
    public void Reverse_FillsTheInputCellFromTheDrive()
    {
        using var fixture = new IoPortTransferFixture(IoPortDirection.Reverse, completion: IoPortCompletionMode.Full);
        var target = fixture.Left[0].childItem!;
        var root = fixture.AddRoot(fixture.DriveCell!, 61);

        fixture.Callback();

        Assert.That(fixture.Roots(fixture.DriveCell!), Is.Empty);
        Assert.That(fixture.Roots(target), Is.EqualTo(new[] { root }));
        Assert.That((fixture.FeeDraws, fixture.Energy), Is.EqualTo((1, 99)));
        Assert.That(fixture.Right[0].childItem, Is.Null, "The cell is not full, so it stays for more work.");
    }

    [Test]
    public void Reverse_ProgressCountsTheNetworkRootsTheCellStillAccepts()
    {
        using var fixture = new IoPortTransferFixture(IoPortDirection.Reverse, completion: IoPortCompletionMode.Full);
        fixture.AddRoot(fixture.DriveCell!, 131);
        fixture.AddRoot(fixture.DriveCell!, 132);
        fixture.Tuning.RootsPerSecond = 1;

        var half = fixture.Callback();
        var whole = fixture.Callback();

        Assert.That(half.Progress.Value, Is.EqualTo(0.5d), "One of two eligible network roots has moved.");
        Assert.That(whole.Progress.Value, Is.EqualTo(1d));
    }

    [Test]
    public void WorkComplete_EjectsAfterASecondThatMovesNothingAndEndsTheJob()
    {
        using var fixture = new IoPortTransferFixture(IoPortDirection.Forward);
        var source = fixture.Left[0].childItem!;
        fixture.AddRoot(source, 71);

        fixture.Callback();
        Assert.That(fixture.Left[0].childItem, Is.SameAs(source), "A second that moved items is not proof the work is done.");

        var finished = fixture.Callback();

        Assert.That(fixture.Right[0].childItem, Is.SameAs(source));
        Assert.That(source.parentInventory, Is.SameAs(fixture.Right[0]));
        Assert.That(fixture.Left[0].childItem, Is.Null);
        Assert.That(fixture.State.CommittedRoots(0, source.uniqueId), Is.Zero);
        Assert.That((fixture.FeeDraws, fixture.Energy), Is.EqualTo((1, 99)));
        Assert.That(finished.Status, Is.EqualTo(IoPortRuntimeStatus.Completed));
        Assert.That(fixture.State.Settings.Running, Is.False, "With every input cell processed the machine pauses itself.");

        fixture.AddCellToSlot(fixture.Left[1], 401);
        fixture.Coordinator.Wake(fixture.Machine);
        Assert.That(fixture.Callback().Status, Is.EqualTo(IoPortRuntimeStatus.Completed),
            "A new cell waits for Start; the window keeps showing that the last work finished.");
    }

    [Test]
    public void Empty_EjectsInTheSameSecondThatEmptiesTheCell()
    {
        using var fixture = new IoPortTransferFixture(IoPortDirection.Forward, completion: IoPortCompletionMode.Empty);
        var source = fixture.Left[0].childItem!;
        fixture.AddRoot(source, 72);

        fixture.Callback();

        Assert.That(fixture.Right[0].childItem, Is.SameAs(source));
    }

    [Test]
    public void FullOutputBank_KeepsTheCellAndReportsIt()
    {
        using var fixture = new IoPortTransferFixture(IoPortDirection.Forward, completion: IoPortCompletionMode.Empty);
        var source = fixture.Left[0].childItem!;
        foreach (var slot in fixture.Right) fixture.AddCellToSlot(slot, 300 + System.Array.IndexOf(fixture.Right, slot));

        var display = fixture.Callback();

        Assert.That(display.Status, Is.EqualTo(IoPortRuntimeStatus.OutputFull));
        Assert.That(fixture.Left[0].childItem, Is.SameAs(source));
    }

    [Test]
    public void Paused_DoesNothingUntilStarted()
    {
        using var fixture = new IoPortTransferFixture(IoPortDirection.Forward);
        var source = fixture.Left[0].childItem!;
        var root = fixture.AddRoot(source, 81);
        Assert.That(fixture.Coordinator.TryChangeSettings(fixture.Machine, settings => settings.WithRunning(false)), Is.True);

        var paused = fixture.Callback();

        Assert.That(paused.Status, Is.EqualTo(IoPortRuntimeStatus.Paused));
        Assert.That(fixture.Roots(source), Is.EqualTo(new[] { root }));
        Assert.That(fixture.FeeDraws, Is.Zero);

        Assert.That(fixture.Coordinator.TryChangeSettings(fixture.Machine, settings => settings.WithRunning(true)), Is.True);
        fixture.Callback();

        Assert.That(fixture.Roots(fixture.DriveCell!), Is.EqualTo(new[] { root }));
    }

    [Test]
    public void Cancel_PausesAndRestartsTheFeeBlock()
    {
        using var fixture = new IoPortTransferFixture(IoPortDirection.Forward, completion: IoPortCompletionMode.Full);
        var source = fixture.Left[0].childItem!;
        fixture.AddRoot(source, 91);
        fixture.Callback();
        Assert.That(fixture.State.CommittedRoots(0, source.uniqueId), Is.EqualTo(1));

        Assert.That(fixture.Coordinator.Cancel(fixture.Machine), Is.True);

        Assert.That(fixture.State.Settings.Running, Is.False);
        Assert.That(fixture.State.CommittedRoots(0, source.uniqueId), Is.Zero);
        Assert.That(fixture.Roots(fixture.DriveCell!).Length, Is.EqualTo(1), "Cancelling never moves items back.");
        Assert.That(fixture.Coordinator.ReadDisplay(fixture.Machine).Status, Is.EqualTo(IoPortRuntimeStatus.Paused));

        fixture.Coordinator.TryChangeSettings(fixture.Machine, settings => settings.WithRunning(true));
        fixture.AddRoot(source, 92);
        fixture.Callback();
        Assert.That(fixture.FeeDraws, Is.EqualTo(2), "The next root after a cancel opens a new fee block.");
    }

    [Test]
    public void NothingToMove_WaitsBeforeRetryingAndWakesOnAnEvent()
    {
        using var fixture = new IoPortTransferFixture(IoPortDirection.Forward, completion: IoPortCompletionMode.Full);
        var source = fixture.Left[0].childItem!;

        Assert.That(fixture.Callback().Status, Is.EqualTo(IoPortRuntimeStatus.NoEligibleContent));
        var root = fixture.AddRoot(source, 95);
        fixture.Callback();
        Assert.That(fixture.Roots(source), Is.EqualTo(new[] { root }), "The machine waits before trying again.");

        fixture.Coordinator.Wake(fixture.Machine);
        fixture.Callback();

        Assert.That(fixture.Roots(fixture.DriveCell!), Is.EqualTo(new[] { root }));
    }

    [Test]
    public void SpentFrameBudget_WaitsByDayButNightSimulationStillMoves()
    {
        using var fixture = new IoPortTransferFixture(IoPortDirection.Forward, frameBudget: TimeSpan.Zero);
        var source = fixture.Left[0].childItem!;
        var root = fixture.AddRoot(source, 97);

        fixture.SecondWithoutFrames();
        fixture.Frame();
        fixture.SecondWithoutFrames();
        Assert.That(fixture.Roots(source), Is.EqualTo(new[] { root }), "With no frame time left, a daytime second waits.");

        fixture.CallbackInSameFrame();

        Assert.That(fixture.Roots(fixture.DriveCell!), Is.EqualTo(new[] { root }),
            "A second call in one frame is the night simulation, which has no frame budget.");
    }

    [Test]
    public void NewMachine_WaitsForStart()
    {
        using var fixture = new IoPortTransferFixture(IoPortDirection.Forward);
        var source = fixture.Left[0].childItem!;
        var root = fixture.AddRoot(source, 98);
        Assert.That(IoPortNativeState.TryWrite(fixture.Machine, fixture.State, IoPortNativeState.Default), Is.True);

        Assert.That(fixture.Callback().Status, Is.EqualTo(IoPortRuntimeStatus.Paused));
        Assert.That(fixture.Roots(source), Is.EqualTo(new[] { root }));
    }

    [Test]
    public void Tuning_RateAndFeeApplyFromTheNextSecond()
    {
        using var fixture = new IoPortTransferFixture(IoPortDirection.Forward, completion: IoPortCompletionMode.Full);
        var source = fixture.Left[0].childItem!;
        fixture.AddRoot(source, 101);
        fixture.AddRoot(source, 102);
        fixture.Tuning.RootsPerSecond = 1;
        fixture.Tuning.RootsPerFee = 1;

        fixture.Callback();
        Assert.That((fixture.Roots(source).Length, fixture.FeeDraws), Is.EqualTo((1, 1)));

        fixture.Callback();
        Assert.That((fixture.Roots(source).Length, fixture.FeeDraws), Is.EqualTo((0, 2)));
    }

    [Test]
    public void CellRootSource_LookupAgreesWithTheFullListing()
    {
        using var fixture = new IoPortTransferFixture(IoPortDirection.Forward);
        var cell = fixture.Left[0].childItem!;
        var kept = fixture.AddRoot(cell, 141);
        var leaving = fixture.AddRoot(cell, 142);
        var view = fixture.ReadCell(cell);
        var key = StorageService.Identify(kept)!;
        var source = (CellRootSource)new CellRootPort(cell, StorageService.Identify, view).StoredRoots(key);
        var listed = source.Snapshot().ToArray();
        Assert.That(listed.Length, Is.EqualTo(2));
        Assert.That(listed.All(source.Contains), Is.True);

        var stranger = (CellRootSource)new CellRootPort(cell, StorageService.Identify, view).StoredRoots(key);
        Assert.That(stranger.Contains(listed[0]), Is.False, "A snapshot from another listing is not this source's root.");

        var gone = listed.Single(root => root.X == leaving.modifiedShape.minX);
        new GameGridInventory().Place(leaving);
        Assert.That(source.Contains(gone), Is.False);
        Assert.That(source.Snapshot().Any(root => root.Equals(gone)), Is.False);
    }

    [Test]
    public void CellVersion_ChangesOnlyWhenTheKeptContentsChange()
    {
        using var fixture = new IoPortTransferFixture(IoPortDirection.Forward);
        var cell = fixture.Left[0].childItem!;
        CellInventoryCache.Remember(cell, fixture.ReadCell(cell));
        var kept = CellInventoryCache.Version(cell);

        CellInventoryCache.Remember(cell, fixture.ReadCell(cell));
        Assert.That(CellInventoryCache.Version(cell), Is.EqualTo(kept), "Keeping the same pages again is not a change.");

        fixture.AddRoot(cell, 120);
        CellInventoryCache.Remember(cell, fixture.ReadCell(cell));
        var changed = CellInventoryCache.Version(cell);
        Assert.That(changed, Is.Not.EqualTo(kept));

        CellInventoryCache.Forget(cell);
        Assert.That(CellInventoryCache.Version(cell), Is.Not.EqualTo(changed));
    }

    [Test]
    public void NoInputCell_SleepsAsIdle()
    {
        using var fixture = new IoPortTransferFixture(IoPortDirection.Forward);
        fixture.Left[0].Expel(fixture.Left[0].childItem!);

        Assert.That(fixture.Callback().Status, Is.EqualTo(IoPortRuntimeStatus.Idle));
        Assert.That(fixture.FeeDraws, Is.Zero);
    }

    [Test]
    public void OutputBank_RefusesCellsTheMachineDidNotEject()
    {
        using var fixture = new IoPortTransferFixture(IoPortDirection.Forward);
        var cell = fixture.Left[0].childItem!;

        Assert.That(fixture.Coordinator.IsEjecting(fixture.Machine, cell, fixture.Right[0]), Is.False);
    }

    [Test]
    public void TerminalStore_WarmedDriveWithInvalidPageLayoutRejectsTheItem()
    {
        using var fixture = new IoPortTransferFixture(IoPortDirection.Forward);
        var terminal = fixture.AddTerminal();
        var cell = fixture.DriveCell!;
        var page = fixture.Pages(cell)[1];
        var external = new GameInventory();
        var resident = new GameItem("test.resident");
        external.Place(resident);
        Assert.That(fixture.Store(terminal, resident).Status, Is.EqualTo(TerminalInsertionStatus.Success));
        Assert.That(resident.parentInventory, Is.SameAs(page));
        Assert.That(CellInventoryCache.TryGet(cell, out _, out _), Is.True);
        page.inventoryShape.width--;
        var candidate = new GameItem("test.candidate");
        external.Place(candidate);

        var result = fixture.Store(terminal, candidate);

        Assert.That(result.Status, Is.EqualTo(TerminalInsertionStatus.Rejected));
        Assert.That(result.StoredAmount, Is.Zero);
        Assert.That(candidate.parentInventory, Is.SameAs(external));
        Assert.That(fixture.Roots(cell), Is.EqualTo(new[] { resident }));
        Assert.That(CellHealth.IsUnavailable(cell), Is.True);
    }
}
