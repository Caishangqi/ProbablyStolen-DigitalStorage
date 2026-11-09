using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Network.Pathing;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Application.Queries;

/// <summary>Aggregates the storage cells attached to one current connected grid snapshot.</summary>
public sealed class GridStorage
{
    private readonly IReadOnlyList<IStorageCell> _cells;
    private readonly GridStorageStatus _status;

    /// <summary>Creates a storage view over a topology snapshot and one power observation.</summary>
    public GridStorage(Grid grid, bool powered)
    {
        if (grid == null)
        {
            throw new ArgumentNullException(nameof(grid));
        }

        _cells = CollectCells(grid);
        var status = ResolveStatus(grid, powered);
        _status = status == GridStorageStatus.Online && _cells.Any(cell => cell.GetStatus() == CellState.Absent)
            ? GridStorageStatus.StorageUnavailable : status;
    }

    /// <summary>Gets the current availability status.</summary>
    public GridStorageStatus GetStatus() => _status;

    /// <summary>Gets whether this view may expose stored contents.</summary>
    public bool IsAvailable() => _status == GridStorageStatus.Online;

    private static GridStorageStatus ResolveStatus(Grid grid, bool powered)
    {
        return grid.GetPathingService().GetControllerState() switch
        {
            ControllerState.NoController => GridStorageStatus.NoController,
            ControllerState.ControllerConflict => GridStorageStatus.ControllerConflict,
            ControllerState.Online when powered => GridStorageStatus.Online,
            _ => GridStorageStatus.Unpowered
        };
    }

    private static IReadOnlyList<IStorageCell> CollectCells(Grid grid)
    {
        var cells = new List<IStorageCell>();
        foreach (var node in grid.Nodes.Where(node => node.Kind == GridNodeKind.Drive))
        {
            foreach (var cell in node.StorageCells)
            {
                if (cell == null)
                {
                    throw new ArgumentException("A grid drive contains a null storage cell.", nameof(grid));
                }

                if (cells.Any(existing => ReferenceEquals(existing, cell)))
                {
                    throw new ArgumentException("A storage cell cannot be mounted twice in one grid.", nameof(grid));
                }

                cells.Add(cell);
            }
        }

        return cells;
    }
}
