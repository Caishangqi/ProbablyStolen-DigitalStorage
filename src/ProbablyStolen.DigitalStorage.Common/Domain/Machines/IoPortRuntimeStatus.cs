namespace ProbablyStolen.DigitalStorage.Common.Domain.Machines;

/// <summary>The current operating condition reported by an IO Port.</summary>
public enum IoPortRuntimeStatus
{
    /// <summary>No input cell is waiting.</summary>
    Idle = 0,

    /// <summary>Items moved in the last batch.</summary>
    Working = 1,

    /// <summary>Nothing is left to move for the current settings.</summary>
    NoEligibleContent = 2,

    /// <summary>No controller is connected to the machine.</summary>
    NoController = 3,

    /// <summary>The connected network has conflicting controllers.</summary>
    ControllerConflict = 4,

    /// <summary>The connected network cannot meet its power demand.</summary>
    Unpowered = 5,

    /// <summary>The network service is active but cannot pay the IO Port operation fee.</summary>
    OperationFeeUnavailable = 6,

    /// <summary>The target cannot accept the remaining items.</summary>
    WaitingForCapacity = 7,

    /// <summary>A cell or the machine's saved settings could not be read.</summary>
    StorageUnavailable = 8,

    /// <summary>The power source cannot pay metered IO Port work.</summary>
    UnsupportedEnergySource = 9,

    /// <summary>A finished cell is waiting for a free output slot.</summary>
    OutputFull = 10,

    /// <summary>A move could not be verified; the machine retries later.</summary>
    Faulted = 11,

    /// <summary>The player paused the machine.</summary>
    Paused = 12,

    /// <summary>Every input cell was processed and ejected, so the machine paused itself.</summary>
    Completed = 13
}
