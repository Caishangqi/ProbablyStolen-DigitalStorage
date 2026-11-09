using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Network.Pathing;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Network.Energy;
using ProbablyStolen.DigitalStorage.Common.Ports;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Network;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

/// <summary>
/// Checks that network topology, drive occupancy, and controller energy stay independent of the retired
/// storage backend. Doubles only; no game assembly, native inventory, or saved item is involved.
/// </summary>
[TestFixture]
public sealed class NetworkIndependenceTests
{
    [Test]
    public void Scanner_MountedCell_KeepsTopologyOccupancyAndPower()
    {
        var grid = new GameGridInventory();
        var controller = new GameItem(ControllerItem.ItemId);
        var drive = new GameItem(DriveItem.ItemId);
        grid.Place(controller);
        grid.Place(drive);
        grid.Touch(controller, drive);
        var bay = NativeCellResolverTests.Bay();
        bay.childItem = NativeCellResolverTests.CompleteCell();
        drive.Slots.Add(bay);
        var scanner = new NativeNetworkScanner(new PowerSettings(), null, NativeCellResolver.Get);

        var snapshot = scanner.Capture(drive);
        var component = snapshot.FindGrid(drive);
        var node = snapshot.FindNode(drive);

        Assert.That(component.GetPathingService().GetControllerState(), Is.EqualTo(ControllerState.Online));
        Assert.That(node.OccupiedDriveSlots, Is.EqualTo(1));
        Assert.That(node.DriveSlotCapacity, Is.EqualTo(14));
        Assert.That(node.StorageCells, Has.Count.EqualTo(1));
        Assert.That(node.StorageCells[0].GetStatus(), Is.EqualTo(CellState.Empty));

        var energy = new EnergyService();
        var source = new TestEnergySource(10);
        var power = energy.OnTurnEnd(component, source);

        Assert.That(power.Status, Is.EqualTo(GridPowerStatus.Powered));
        Assert.That(power.RequestedPower, Is.EqualTo(5));
        Assert.That(power.DrawnPower, Is.EqualTo(5));
        Assert.That(energy.IsNetworkPowered(component, source), Is.True);
        Assert.That(bay.childItem, Is.Not.Null);
    }

    [Test]
    public void Scanner_EmptyNetwork_ReportsNoUnavailableCell()
    {
        var grid = new GameGridInventory();
        var controller = new GameItem(ControllerItem.ItemId);
        var drive = new GameItem(DriveItem.ItemId);
        grid.Place(controller);
        grid.Place(drive);
        grid.Touch(controller, drive);
        var scanner = new NativeNetworkScanner(new PowerSettings(), null, NativeCellResolver.Get);

        var node = scanner.Capture(drive).FindNode(drive);

        Assert.That(node.OccupiedDriveSlots, Is.Zero);
        Assert.That(node.StorageCells, Is.Empty);
    }

    private sealed class PowerSettings : IGridPowerSettingsPort
    {
        public int GetPowerUsage(GridNodeKind kind) => kind switch
        {
            GridNodeKind.Controller => 3,
            GridNodeKind.Drive => 2,
            GridNodeKind.Terminal => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private sealed class TestEnergySource : IEnergySourcePort
    {
        private int _available;

        internal TestEnergySource(int available)
        {
            _available = available;
        }

        public bool HasSource => true;

        public bool CanDraw(int amount) => amount <= _available;

        public bool TryDraw(int amount)
        {
            if (amount > _available)
            {
                return false;
            }

            _available -= amount;
            return true;
        }
    }
}
