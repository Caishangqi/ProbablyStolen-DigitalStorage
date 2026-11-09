namespace ProbablyStolen.DigitalStorage.Common.Domain.Network.Energy;

/// <summary>The power state of a grid, independent of its existence.</summary>
public enum GridPowerStatus
{
    /// <summary>The controller lacks a charged battery or enough energy for this turn.</summary>
    Unpowered,

    /// <summary>The controller supplied the complete configured draw for this turn.</summary>
    Powered
}

/// <summary>Reports the single atomic network draw attempted at one turn boundary.</summary>
public readonly struct GridPowerResult
{
    internal GridPowerResult(GridPowerStatus status, long requestedPower, int drawnPower)
    {
        Status = status;
        RequestedPower = requestedPower;
        DrawnPower = drawnPower;
    }

    /// <summary>Gets whether topology and controller energy allow this turn's draw.</summary>
    public GridPowerStatus Status { get; }

    /// <summary>Gets the configured network draw, even when insufficient energy prevented consumption.</summary>
    public long RequestedPower { get; }

    /// <summary>Gets the amount atomically drawn from the controller battery.</summary>
    public int DrawnPower { get; }
}
