using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Network.Pathing;

namespace ProbablyStolen.DigitalStorage.Client.Network;

/// <summary>Describes the grid state shown to a player.</summary>
public enum GridNetworkDisplayStatus
{
    /// <summary>The grid has no supported controller power source.</summary>
    NoController,

    /// <summary>The grid exceeds the supported single controller arrangement.</summary>
    ControllerConflict,

    /// <summary>The controller structure is valid but the grid is not powered.</summary>
    Unpowered,

    /// <summary>The controller structure is valid and the grid is powered.</summary>
    Online
}

/// <summary>Maps controller structure and power to display without changing either.</summary>
public static class GridNetworkStatusPresenter
{
    /// <summary>Resolves a display state independently of terminal and drive presence.</summary>
    public static GridNetworkDisplayStatus Resolve(ControllerState controllerState, bool isPowered)
    {
        return controllerState switch
        {
            ControllerState.NoController => GridNetworkDisplayStatus.NoController,
            ControllerState.ControllerConflict => GridNetworkDisplayStatus.ControllerConflict,
            ControllerState.Online => isPowered
                ? GridNetworkDisplayStatus.Online
                : GridNetworkDisplayStatus.Unpowered,
            _ => throw new ArgumentOutOfRangeException(nameof(controllerState), controllerState,
                "Unsupported controller state.")
        };
    }
}
