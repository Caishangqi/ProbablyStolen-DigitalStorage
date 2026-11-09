using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Network.Pathing;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Network.Energy;
using ProbablyStolen.DigitalStorage.Common.Ports;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class EnergyServiceTests
{
    private readonly GridPropagator _propagator = new();
    private readonly EnergyService _service = new();

    [Test]
    public void OnTurnEnd_ConnectedGridDrawsConfiguredTotalOnce()
    {
        var grid = ConnectedGrid();
        var source = new TestEnergySource(10);

        var result = _service.OnTurnEnd(grid, source);

        Assert.That(result.Status, Is.EqualTo(GridPowerStatus.Powered));
        Assert.That(result.RequestedPower, Is.EqualTo(6));
        Assert.That(result.DrawnPower, Is.EqualTo(6));
        Assert.That(source.AvailableEnergy, Is.EqualTo(4));
        Assert.That(source.DrawCalls, Is.EqualTo(1));
        Assert.That(source.LastDraw, Is.EqualTo(6));
    }

    [Test]
    public void OnTurnEnd_InsufficientEnergyLeavesBatteryUnchanged()
    {
        var grid = ConnectedGrid();
        var source = new TestEnergySource(5);

        var result = _service.OnTurnEnd(grid, source);

        Assert.That(result.Status, Is.EqualTo(GridPowerStatus.Unpowered));
        Assert.That(result.RequestedPower, Is.EqualTo(6));
        Assert.That(result.DrawnPower, Is.Zero);
        Assert.That(source.AvailableEnergy, Is.EqualTo(5));
        Assert.That(source.DrawCalls, Is.Zero);
    }

    [Test]
    public void OnTurnEnd_MissingBatteryLeavesValidGridUnpowered()
    {
        var grid = ConnectedGrid();
        var source = new TestEnergySource(100) { HasSource = false };

        var result = _service.OnTurnEnd(grid, source);

        Assert.That(grid.GetPathingService().GetControllerState(), Is.EqualTo(ControllerState.Online));
        Assert.That(result.Status, Is.EqualTo(GridPowerStatus.Unpowered));
        Assert.That(source.DrawCalls, Is.Zero);
    }

    [Test]
    public void OnTurnEnd_WithoutDrive_StillPowersControllerAndTerminal()
    {
        var grid = _propagator.Propagate(new[]
        {
            new GridNode(0, GridNodeKind.Controller, 3),
            new GridNode(1, GridNodeKind.Terminal, 1)
        }, new[] { new GridConnection(0, 1) })[0];
        var source = new TestEnergySource(100);

        var result = _service.OnTurnEnd(grid, source);

        Assert.That(result.Status, Is.EqualTo(GridPowerStatus.Powered));
        Assert.That(result.RequestedPower, Is.EqualTo(4));
        Assert.That(source.AvailableEnergy, Is.EqualTo(96));
        Assert.That(source.DrawCalls, Is.EqualTo(1));
    }

    [Test]
    public void OnTurnEnd_ControllerConflictNeverConsumesEnergy()
    {
        var nodes = new[]
        {
            new GridNode(0, GridNodeKind.Controller, 3),
            new GridNode(1, GridNodeKind.Drive, 2, 0, 14),
            new GridNode(2, GridNodeKind.Terminal, 1),
            new GridNode(3, GridNodeKind.Controller, 3)
        };
        var grid = _propagator.Propagate(nodes,
            new[] { new GridConnection(0, 1), new GridConnection(1, 2), new GridConnection(2, 3) })[0];
        var source = new TestEnergySource(100);

        var result = _service.OnTurnEnd(grid, source);

        Assert.That(grid.GetPathingService().GetControllerState(), Is.EqualTo(ControllerState.ControllerConflict));
        Assert.That(result.Status, Is.EqualTo(GridPowerStatus.Unpowered));
        Assert.That(source.DrawCalls, Is.Zero);
    }

    [Test]
    public void OnTurnEnd_ZeroDrawWithoutSourceRemainsUnpowered()
    {
        var nodes = new[]
        {
            new GridNode(0, GridNodeKind.Controller, 0),
            new GridNode(1, GridNodeKind.Drive, 0, 0, 14),
            new GridNode(2, GridNodeKind.Terminal, 0)
        };
        var grid = _propagator.Propagate(nodes,
            new[] { new GridConnection(0, 1), new GridConnection(1, 2) })[0];
        var source = new TestEnergySource(0) { HasSource = false };

        var result = _service.OnTurnEnd(grid, source);

        Assert.That(result.Status, Is.EqualTo(GridPowerStatus.Unpowered));
        Assert.That(result.RequestedPower, Is.Zero);
        Assert.That(result.DrawnPower, Is.Zero);
        Assert.That(source.DrawCalls, Is.Zero);
    }

    [Test]
    public void OnTurnEnd_DrawRaceFailureDoesNotReportPowered()
    {
        var grid = ConnectedGrid();
        var source = new TestEnergySource(100) { FailDraw = true };

        var result = _service.OnTurnEnd(grid, source);

        Assert.That(result.Status, Is.EqualTo(GridPowerStatus.Unpowered));
        Assert.That(result.DrawnPower, Is.Zero);
        Assert.That(source.AvailableEnergy, Is.EqualTo(100));
        Assert.That(source.DrawCalls, Is.EqualTo(1));
    }

    [Test]
    public void IsNetworkPowered_IsReadOnly()
    {
        var source = new TestEnergySource(6);

        Assert.That(_service.IsNetworkPowered(ConnectedGrid(), source), Is.True);
        Assert.That(source.AvailableEnergy, Is.EqualTo(6));
        Assert.That(source.DrawCalls, Is.Zero);
    }

    [Test]
    public void IsNetworkPowered_OversizedAggregateIsRejectedWithoutOverflow()
    {
        var nodes = new[]
        {
            new GridNode(0, GridNodeKind.Controller, int.MaxValue),
            new GridNode(1, GridNodeKind.Drive, int.MaxValue, 0, 14),
            new GridNode(2, GridNodeKind.Terminal, 1)
        };
        var grid = _propagator.Propagate(nodes,
            new[] { new GridConnection(0, 1), new GridConnection(1, 2) })[0];
        var source = new TestEnergySource(int.MaxValue);

        var result = _service.OnTurnEnd(grid, source);

        Assert.That(grid.TotalPowerUsage, Is.EqualTo((long)int.MaxValue * 2 + 1));
        Assert.That(result.Status, Is.EqualTo(GridPowerStatus.Unpowered));
        Assert.That(result.DrawnPower, Is.Zero);
        Assert.That(source.DrawCalls, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OnTurnEnd_ControllerWithoutTerminal_IsPoweredAndBilled(bool includeDrive)
    {
        var nodes = new List<GridNode> { new(0, GridNodeKind.Controller, 3) };
        var edges = new List<GridConnection>();
        if (includeDrive)
        {
            nodes.Add(new GridNode(1, GridNodeKind.Drive, 2, 0, 14));
            edges.Add(new GridConnection(0, 1));
        }

        var grid = _propagator.Propagate(nodes, edges)[0];
        var source = new TestEnergySource(100);
        var result = _service.OnTurnEnd(grid, source);
        Assert.That(result.Status, Is.EqualTo(GridPowerStatus.Powered));
        Assert.That(result.DrawnPower, Is.EqualTo(includeDrive ? 5 : 3));
        Assert.That(grid.GetPathingService().GetControllerState(), Is.EqualTo(ControllerState.Online));
    }

    [Test]
    public void OnTurnEnd_NoController_GridExistsButCannotUseAnUnrelatedSource()
    {
        var grid = _propagator.Propagate(
            new[] { new GridNode(0, GridNodeKind.Terminal, 1) }, Array.Empty<GridConnection>())[0];
        var source = new TestEnergySource(100);

        var result = _service.OnTurnEnd(grid, source);

        Assert.That(grid.Nodes, Has.Count.EqualTo(1));
        Assert.That(grid.GetPathingService().GetControllerState(), Is.EqualTo(ControllerState.NoController));
        Assert.That(result.Status, Is.EqualTo(GridPowerStatus.Unpowered));
        Assert.That(source.DrawCalls, Is.Zero);
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    public void OnTurnEnd_ZeroDemand_RequiresLiveSourceButConsumesNothing(int energy, bool powered)
    {
        var grid = _propagator.Propagate(
            new[] { new GridNode(0, GridNodeKind.Controller, 0) }, Array.Empty<GridConnection>())[0];
        var source = new TestEnergySource(energy);

        var result = _service.OnTurnEnd(grid, source);

        Assert.That(result.Status, Is.EqualTo(powered ? GridPowerStatus.Powered : GridPowerStatus.Unpowered));
        Assert.That(result.DrawnPower, Is.Zero);
        Assert.That(source.AvailableEnergy, Is.EqualTo(energy));
        Assert.That(source.DrawCalls, Is.Zero);
    }

    [Test]
    public void IsNetworkPowered_PowerLossDoesNotChangeGridOrControllerStructure()
    {
        var grid = ConnectedGrid();
        var source = new TestEnergySource(6);
        var nodes = grid.Nodes;
        var edges = grid.Connections;
        Assert.That(_service.IsNetworkPowered(grid, source), Is.True);
        _service.OnTurnEnd(grid, source);

        Assert.That(_service.IsNetworkPowered(grid, source), Is.False);
        Assert.That(grid.GetPathingService().GetControllerState(), Is.EqualTo(ControllerState.Online));
        Assert.That(grid.Nodes, Is.SameAs(nodes));
        Assert.That(grid.Connections, Is.SameAs(edges));
        Assert.That(source.AvailableEnergy, Is.Zero);
    }

    private Grid ConnectedGrid()
    {
        var nodes = new[]
        {
            new GridNode(0, GridNodeKind.Controller, 3),
            new GridNode(1, GridNodeKind.Drive, 2, 0, 14),
            new GridNode(2, GridNodeKind.Terminal, 1)
        };
        return _propagator.Propagate(nodes,
            new[] { new GridConnection(0, 1), new GridConnection(1, 2) })[0];
    }

    private sealed class TestEnergySource : IEnergySourcePort
    {
        internal TestEnergySource(long availableEnergy)
        {
            AvailableEnergy = availableEnergy;
            HasSource = true;
        }

        public bool HasSource { get; set; }

        public long AvailableEnergy { get; private set; }

        internal bool FailDraw { get; set; }

        internal int DrawCalls { get; private set; }

        internal int LastDraw { get; private set; }

        public bool CanDraw(int amount) => HasSource && amount >= 0 && amount <= AvailableEnergy;

        public bool TryDraw(int amount)
        {
            DrawCalls++;
            LastDraw = amount;
            if (FailDraw || amount < 0 || amount > AvailableEnergy)
            {
                return false;
            }

            AvailableEnergy -= amount;
            return true;
        }
    }
}
