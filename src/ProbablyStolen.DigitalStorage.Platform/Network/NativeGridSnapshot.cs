using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;

namespace ProbablyStolen.DigitalStorage.Platform.Network;

internal sealed class NativeGridSnapshot
{
    private readonly GameItem[] _machines;
    private readonly Grid[] _grids;

    internal NativeGridSnapshot(IReadOnlyList<GameItem> machines, IReadOnlyList<Grid> grids)
    {
        _machines = machines?.ToArray() ?? throw new ArgumentNullException(nameof(machines));
        _grids = grids?.ToArray() ?? throw new ArgumentNullException(nameof(grids));
    }

    internal IReadOnlyList<Grid> Grids => _grids;

    internal Grid FindGrid(GameItem machine)
    {
        if (machine == null)
        {
            throw new ArgumentNullException(nameof(machine));
        }

        var nodeId = FindMachineId(machine);
        return _grids.Single(grid => grid.Nodes.Any(node => node.Id == nodeId));
    }

    internal GridNode FindNode(GameItem machine)
    {
        var nodeId = FindMachineId(machine);
        return _grids.SelectMany(grid => grid.Nodes).Single(node => node.Id == nodeId);
    }

    internal GameItem FindMachine(GridNode node)
    {
        if (node == null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        if (node.Id < 0 || node.Id >= _machines.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(node));
        }

        return _machines[node.Id];
    }

    internal GameItem FindController(Grid grid)
    {
        if (grid == null)
        {
            throw new ArgumentNullException(nameof(grid));
        }

        var controller = grid.Nodes.Single(node => node.Kind == GridNodeKind.Controller);
        return FindMachine(controller);
    }

    private int FindMachineId(GameItem machine)
    {
        for (var index = 0; index < _machines.Length; index++)
        {
            if (ReferenceEquals(_machines[index], machine))
            {
                return index;
            }
        }

        throw new InvalidOperationException("The focused machine is absent from its topology snapshot.");
    }
}
