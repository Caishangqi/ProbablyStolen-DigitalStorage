using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Network.Pathing;
using ProbablyStolen.DigitalStorage.Common.Ports;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Network.Energy;

/// <summary>Calculates native cycle demand and reads or consumes controller energy.</summary>
public sealed class EnergyService
{
    /// <summary>Gets configured demand without using device roles as a network prerequisite.</summary>
    public long GetIdlePowerUsage(Grid grid)
    {
        if (grid == null)
        {
            throw new ArgumentNullException(nameof(grid));
        }

        return grid.TotalPowerUsage;
    }

    /// <summary>Checks power independently of grid existence, without consuming energy.</summary>
    public bool IsNetworkPowered(Grid grid, IEnergySourcePort? source)
    {
        if (grid == null)
        {
            throw new ArgumentNullException(nameof(grid));
        }

        if (grid.GetPathingService().GetControllerState() != ControllerState.Online ||
            source == null || !source.HasSource)
        {
            return false;
        }

        var demand = GetIdlePowerUsage(grid);
        return demand <= int.MaxValue && source.CanDraw(demand == 0 ? 1 : (int)demand);
    }

    /// <summary>Settles one complete native cycle, leaving insufficient sources unchanged.</summary>
    public GridPowerResult OnTurnEnd(Grid grid, IEnergySourcePort? source)
    {
        var demand = GetIdlePowerUsage(grid);
        if (!IsNetworkPowered(grid, source))
        {
            return new GridPowerResult(GridPowerStatus.Unpowered, demand, 0);
        }

        if (demand == 0)
        {
            return new GridPowerResult(GridPowerStatus.Powered, 0, 0);
        }

        var draw = (int)demand;
        return source!.TryDraw(draw)
            ? new GridPowerResult(GridPowerStatus.Powered, demand, draw)
            : new GridPowerResult(GridPowerStatus.Unpowered, demand, 0);
    }
}
