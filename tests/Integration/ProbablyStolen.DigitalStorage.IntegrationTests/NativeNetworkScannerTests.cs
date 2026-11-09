using System;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Network.Pathing;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Ports;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Network;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class NativeNetworkScannerTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void Capture_StoreGridWithoutShip_AllMachinesResolveSameCompleteComponent(int focusIndex)
    {
        var (grid, machines) = CreateChain();
        var scanner = new NativeNetworkScanner(new PowerSettings());

        var snapshot = scanner.Capture(machines[focusIndex]);
        var component = snapshot.FindGrid(machines[focusIndex]);

        Assert.That(snapshot.Grids, Has.Count.EqualTo(1));
        Assert.That(component.GetPathingService().GetControllerState(), Is.EqualTo(ControllerState.Online));
        Assert.That(component.Nodes, Has.Count.EqualTo(3));
        Assert.That(component.Connections, Has.Count.EqualTo(2));
        Assert.That(component.TotalPowerUsage, Is.EqualTo(6));
        Assert.That(snapshot.FindController(component), Is.SameAs(machines[0]));
        Assert.That(grid.childItems, Has.Count.EqualTo(3));
        Assert.That(machines.All(machine => ReferenceEquals(machine.parentInventory, grid)), Is.True);
    }

    [Test]
    public void Capture_MovementAndReconnection_UsesCurrentContactsWithoutCachedMembership()
    {
        var (grid, machines) = CreateChain();
        var scanner = new NativeNetworkScanner(new PowerSettings());
        var before = scanner.Capture(machines[0]);

        grid.Separate(machines[2]);
        var split = scanner.Capture(machines[0]);
        Assert.That(split.FindGrid(machines[0]).GetPathingService().GetControllerState(), Is.EqualTo(ControllerState.Online));
        Assert.That(split.FindGrid(machines[2]).GetPathingService().GetControllerState(), Is.EqualTo(ControllerState.NoController));
        Assert.That(before.FindGrid(machines[0]).GetPathingService().GetControllerState(), Is.EqualTo(ControllerState.Online));

        grid.Touch(machines[1], machines[2]);
        var joined = scanner.Capture(machines[2]);
        Assert.That(joined.FindGrid(machines[2]).GetPathingService().GetControllerState(), Is.EqualTo(ControllerState.Online));
    }

    [Test]
    public void Capture_MachineMovedToAnotherParent_OldContactsCannotBridgeInventories()
    {
        var (_, machines) = CreateChain();
        var destination = new GameGridInventory();
        destination.Place(machines[2]);
        var scanner = new NativeNetworkScanner(new PowerSettings());

        Assert.That(scanner.Capture(machines[0]).FindGrid(machines[0]).GetPathingService().GetControllerState(),
            Is.EqualTo(ControllerState.Online));
        Assert.That(scanner.Capture(machines[2]).FindGrid(machines[2]).GetPathingService().GetControllerState(),
            Is.EqualTo(ControllerState.NoController));
    }

    [Test]
    public void Capture_OtherInventoryAndUnrelatedItems_DoNotCreateMembersOrBridges()
    {
        var (grid, machines) = CreateChain();
        var unrelated = new GameItem("unrelated");
        grid.Place(unrelated);
        grid.Touch(machines[0], unrelated);
        var otherGrid = new GameGridInventory();
        otherGrid.Place(new GameItem(ControllerItem.ItemId));

        var snapshot = new NativeNetworkScanner(new PowerSettings()).Capture(machines[0]);
        var component = snapshot.FindGrid(machines[0]);

        Assert.That(component.ControllerCount, Is.EqualTo(1));
        Assert.That(component.Nodes, Has.Count.EqualTo(3));
        Assert.That(snapshot.Grids, Has.Count.EqualTo(1));
    }

    [Test]
    public void Capture_SecondController_ReportsConflictFromEveryFocus()
    {
        var (grid, machines) = CreateChain();
        var second = new GameItem(ControllerItem.ItemId);
        grid.Place(second);
        grid.Touch(machines[2], second);
        var scanner = new NativeNetworkScanner(new PowerSettings());

        foreach (var focus in grid.childItems)
        {
            Assert.That(scanner.Capture(focus).FindGrid(focus).GetPathingService().GetControllerState(),
                Is.EqualTo(ControllerState.ControllerConflict));
        }
    }

    [Test]
    public void Capture_NonGridParent_LeavesMachinesIsolated()
    {
        var parent = new GameInventory();
        var controller = new GameItem(ControllerItem.ItemId);
        var terminal = new GameItem(TerminalItem.ItemId);
        parent.Place(controller);
        parent.Place(terminal);
        var scanner = new NativeNetworkScanner(new PowerSettings());

        Assert.That(scanner.Capture(controller).FindGrid(controller).GetPathingService().GetControllerState(),
            Is.EqualTo(ControllerState.Online));
        Assert.That(scanner.Capture(terminal).FindGrid(terminal).GetPathingService().GetControllerState(),
            Is.EqualTo(ControllerState.NoController));
    }

    [Test]
    public void Capture_DriveOccupancyAndConfigChanges_AreReadFreshWithoutMutatingPreviousSnapshot()
    {
        var (_, machines) = CreateChain();
        var settings = new PowerSettings();
        var scanner = new NativeNetworkScanner(settings);
        var drive = machines[2];
        var bay = new GameSlotInventory { identifier = CellContentWindow.BayPrefix + "0" };
        drive.Slots.Add(bay);
        drive.Slots.Add(new GameSlotInventory { identifier = "module", childItem = new GameItem("module") });
        var before = scanner.Capture(drive);
        bay.childItem = new GameItem("digital_storage.item_storage_cell_1k");
        settings.DriveUsage = 7;

        var after = scanner.Capture(drive);

        Assert.That(before.FindNode(drive).OccupiedDriveSlots, Is.Zero);
        Assert.That(before.FindGrid(drive).TotalPowerUsage, Is.EqualTo(6));
        Assert.That(after.FindNode(drive).OccupiedDriveSlots, Is.EqualTo(1));
        Assert.That(after.FindNode(drive).DriveSlotCapacity, Is.EqualTo(14));
        Assert.That(after.FindGrid(drive).TotalPowerUsage, Is.EqualTo(11));
        Assert.That(bay.childItem, Is.Not.Null);
    }

    private static (GameGridInventory Grid, GameItem[] Machines) CreateChain()
    {
        var grid = new GameGridInventory();
        var machines = new[]
        {
            new GameItem(ControllerItem.ItemId),
            new GameItem(TerminalItem.ItemId),
            new GameItem(DriveItem.ItemId)
        };
        foreach (var machine in machines)
        {
            grid.Place(machine);
        }

        grid.Touch(machines[0], machines[1]);
        grid.Touch(machines[1], machines[2]);
        return (grid, machines);
    }

    private sealed class PowerSettings : IGridPowerSettingsPort
    {
        internal int DriveUsage = 2;

        public int GetPowerUsage(GridNodeKind kind) => kind switch
        {
            GridNodeKind.Controller => 3,
            GridNodeKind.Drive => DriveUsage,
            GridNodeKind.Terminal => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
