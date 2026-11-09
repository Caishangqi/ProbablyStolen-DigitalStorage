using System;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Network.Pathing;

/// <summary>Provides controller structure state for one immutable grid snapshot.</summary>
public sealed class PathingService
{
    private readonly ControllerState _controllerState;

    internal PathingService(Grid grid)
    {
        if (grid == null)
        {
            throw new ArgumentNullException(nameof(grid));
        }

        _controllerState = ControllerValidator.CalculateState(grid.Nodes);
    }

    /// <summary>Gets structural controller state without requiring a drive, terminal or power.</summary>
    public ControllerState GetControllerState() => _controllerState;
}
