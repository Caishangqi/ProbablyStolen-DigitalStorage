using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Network.Pathing;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class GridPropagatorTests
{
    private readonly GridPropagator _propagator = new();

    [Test]
    public void Propagate_AllThreeMachineOrders_FormsOneConnectedGrid()
    {
        var nodes = StandardNodes();
        var orders = new[]
        {
            new[] { 0, 1, 2 },
            new[] { 0, 2, 1 },
            new[] { 2, 0, 1 }
        };

        foreach (var order in orders)
        {
            var grids = _propagator.Propagate(nodes, new[]
            {
                new GridConnection(order[0], order[1]),
                new GridConnection(order[1], order[2])
            });

            Assert.That(grids, Has.Count.EqualTo(1));
            Assert.That(grids[0].GetPathingService().GetControllerState(), Is.EqualTo(ControllerState.Online));
            Assert.That(grids[0].ControllerCount, Is.EqualTo(1));
            Assert.That(grids[0].DriveCount, Is.EqualTo(1));
            Assert.That(grids[0].TerminalCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void Propagate_BranchAndMultipleDrivesAndTerminals_FormsOneConnectedGrid()
    {
        var nodes = new[]
        {
            new GridNode(0, GridNodeKind.Controller, 3),
            new GridNode(1, GridNodeKind.Drive, 2, 4, 14),
            new GridNode(2, GridNodeKind.Drive, 2, 14, 14),
            new GridNode(3, GridNodeKind.Terminal, 1),
            new GridNode(4, GridNodeKind.Terminal, 1)
        };
        var edges = new[]
        {
            new GridConnection(0, 1),
            new GridConnection(0, 2),
            new GridConnection(1, 3),
            new GridConnection(2, 4)
        };

        var grid = _propagator.Propagate(nodes, edges).Single();

        Assert.That(grid.GetPathingService().GetControllerState(), Is.EqualTo(ControllerState.Online));
        Assert.That(grid.TotalPowerUsage, Is.EqualTo(9));
        Assert.That(grid.Nodes.Single(node => node.Id == 1).OccupiedDriveSlots, Is.EqualTo(4));
        Assert.That(grid.Nodes.Single(node => node.Id == 1).DriveSlotCapacity, Is.EqualTo(14));
    }

    [Test]
    public void Propagate_GappedDrive_KeepsSeparateGridsWithoutInvalidatingController()
    {
        var grids = _propagator.Propagate(StandardNodes(), new[] { new GridConnection(0, 1) });

        Assert.That(grids, Has.Count.EqualTo(2));
        Assert.That(grids.Single(grid => grid.Nodes.Any(node => node.Kind == GridNodeKind.Drive)).GetPathingService().GetControllerState(),
            Is.EqualTo(ControllerState.NoController));
        Assert.That(grids.Single(grid => grid.Nodes.Any(node => node.Kind == GridNodeKind.Controller)).GetPathingService().GetControllerState(),
            Is.EqualTo(ControllerState.Online));
    }

    [Test]
    public void Propagate_MissingController_KeepsGridWithoutController()
    {
        var nodes = new[]
        {
            new GridNode(1, GridNodeKind.Drive, 2, 0, 14),
            new GridNode(2, GridNodeKind.Terminal, 1)
        };

        var grid = _propagator.Propagate(nodes, new[] { new GridConnection(1, 2) }).Single();

        Assert.That(grid.GetPathingService().GetControllerState(), Is.EqualTo(ControllerState.NoController));
    }

    [Test]
    public void Propagate_WithoutTerminal_ControllerRemainsValid()
    {
        var nodes = new[]
        {
            new GridNode(0, GridNodeKind.Controller, 3),
            new GridNode(1, GridNodeKind.Drive, 2, 0, 14)
        };

        var grid = _propagator.Propagate(nodes, new[] { new GridConnection(0, 1) }).Single();

        Assert.That(grid.GetPathingService().GetControllerState(), Is.EqualTo(ControllerState.Online));
    }

    [Test]
    public void Propagate_MultipleControllersInOneComponent_MarksConflict()
    {
        var nodes = new[]
        {
            new GridNode(0, GridNodeKind.Controller, 3),
            new GridNode(1, GridNodeKind.Controller, 3),
            new GridNode(2, GridNodeKind.Drive, 2, 0, 14),
            new GridNode(3, GridNodeKind.Terminal, 1)
        };
        var edges = new[]
        {
            new GridConnection(0, 1),
            new GridConnection(1, 2),
            new GridConnection(2, 3)
        };

        var grid = _propagator.Propagate(nodes, edges).Single();

        Assert.That(grid.GetPathingService().GetControllerState(), Is.EqualTo(ControllerState.ControllerConflict));
    }

    [Test]
    public void Propagate_IsolatedNodes_IncludesEachNodeAsItsOwnGrid()
    {
        var grids = _propagator.Propagate(StandardNodes(), Array.Empty<GridConnection>());

        Assert.That(grids, Has.Count.EqualTo(3));
        Assert.That(grids.All(grid => grid.Nodes.Count == 1), Is.True);
        Assert.That(grids.Single(grid => grid.Nodes[0].Kind == GridNodeKind.Controller).GetPathingService().GetControllerState(),
            Is.EqualTo(ControllerState.Online));
        Assert.That(grids.Where(grid => grid.Nodes[0].Kind != GridNodeKind.Controller)
            .All(grid => grid.GetPathingService().GetControllerState() == ControllerState.NoController), Is.True);
    }

    [Test]
    public void Propagate_RejectsDuplicateIdsUnknownEndpointsAndDuplicateEdges()
    {
        var nodes = StandardNodes();
        Assert.Throws<ArgumentException>(new Action(() => _propagator.Propagate(
            new[] { nodes[0], new GridNode(0, GridNodeKind.Terminal, 1) }, Array.Empty<GridConnection>())));
        Assert.Throws<ArgumentException>(new Action(() => _propagator.Propagate(nodes,
            new[] { new GridConnection(9, 10) })));
        Assert.Throws<ArgumentException>(new Action(() => _propagator.Propagate(nodes,
            new[] { new GridConnection(0, 1), new GridConnection(1, 0) })));
    }

    [Test]
    public void GridNode_RejectsInvalidPowerAndDriveSlotFacts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => new GridNode(0, GridNodeKind.Controller, -1)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => new GridNode(0, GridNodeKind.Drive, 1, 15, 14)));
        Assert.Throws<ArgumentException>(new Action(() => new GridNode(0, GridNodeKind.Terminal, 1, 1, 14)));
    }

    private static IReadOnlyList<GridNode> StandardNodes() => new[]
    {
        new GridNode(0, GridNodeKind.Controller, 3),
        new GridNode(1, GridNodeKind.Terminal, 1),
        new GridNode(2, GridNodeKind.Drive, 2, 0, 14)
    };
}
