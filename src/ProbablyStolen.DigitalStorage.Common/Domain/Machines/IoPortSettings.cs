using System;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Machines;

/// <summary>Immutable IO Port settings shared by the engine-free transfer and the native adapter.</summary>
public sealed class IoPortSettings
{
    /// <summary>The number of cell slots in each bank.</summary>
    public const int SlotsPerBank = 6;

    /// <summary>Creates validated settings.</summary>
    public IoPortSettings(IoPortDirection direction, IoPortCompletionMode completionMode, bool running)
    {
        if (!Enum.IsDefined(typeof(IoPortDirection), direction))
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown IO Port direction.");
        if (!Enum.IsDefined(typeof(IoPortCompletionMode), completionMode))
            throw new ArgumentOutOfRangeException(nameof(completionMode), completionMode, "Unknown IO Port completion mode.");

        Direction = direction;
        CompletionMode = completionMode;
        Running = running;
    }

    /// <summary>Settings of a new IO Port: it waits for the player to press Start.</summary>
    public static IoPortSettings Default { get; } = new(IoPortDirection.Forward, IoPortCompletionMode.WorkComplete, false);

    /// <summary>Gets whether items move from the cell to the network (forward) or from the network into the cell.</summary>
    public IoPortDirection Direction { get; }

    /// <summary>Gets when a processed cell moves to the output bank.</summary>
    public IoPortCompletionMode CompletionMode { get; }

    /// <summary>Gets whether the machine works; false after the player pauses or cancels.</summary>
    public bool Running { get; }

    /// <summary>Returns settings with the opposite direction.</summary>
    public IoPortSettings ReverseDirection() =>
        new(Direction == IoPortDirection.Forward ? IoPortDirection.Reverse : IoPortDirection.Forward, CompletionMode, Running);

    /// <summary>Returns settings with another completion condition.</summary>
    public IoPortSettings WithCompletionMode(IoPortCompletionMode completionMode) => new(Direction, completionMode, Running);

    /// <summary>Returns settings that run or pause the machine.</summary>
    public IoPortSettings WithRunning(bool running) => new(Direction, CompletionMode, running);

    /// <summary>Gets whether two settings are equal.</summary>
    public bool SameAs(IoPortSettings other) => other != null && Direction == other.Direction &&
        CompletionMode == other.CompletionMode && Running == other.Running;
}
