namespace ProbablyStolen.DigitalStorage.Platform.Network;

/// <summary>The player-adjustable IO Port rates, read on every batch so a changed setting applies at once.</summary>
internal interface IIoPortTuning
{
    /// <summary>Gets the most roots one machine moves per game second, at least 1.</summary>
    int RootsPerSecond { get; }

    /// <summary>Gets the roots one power unit pays for, at least 1.</summary>
    int RootsPerFee { get; }
}
