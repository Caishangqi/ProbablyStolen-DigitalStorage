namespace ProbablyStolen.DigitalStorage.Api.Config;

/// <summary>Whether a storage call changes anything.</summary>
public enum Actionable
{
    /// <summary>Performs the operation.</summary>
    Modulate = 0,

    /// <summary>Only reports what the operation would do.</summary>
    Simulate = 1
}
