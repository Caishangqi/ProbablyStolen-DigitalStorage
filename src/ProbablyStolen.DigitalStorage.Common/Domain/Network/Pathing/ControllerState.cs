namespace ProbablyStolen.DigitalStorage.Common.Domain.Network.Pathing;

/// <summary>Describes controller structure independently of grid power.</summary>
public enum ControllerState
{
    /// <summary>The grid exists without a controller and has no supported power source.</summary>
    NoController,

    /// <summary>The supported single controller is present. This does not imply power.</summary>
    Online,

    /// <summary>The controller arrangement exceeds the supported single controller model.</summary>
    ControllerConflict
}
