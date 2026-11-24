using System;

namespace ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;

/// <summary>How far the current IO Port work is, or unknown.</summary>
public sealed class IoPortProgress
{
    private IoPortProgress(double? value) => Value = value;

    /// <summary>Progress that cannot be shown, such as while storage is unreadable.</summary>
    public static IoPortProgress Unknown { get; } = new(null);

    /// <summary>Gets the completed fraction from 0 to 1, or null when unknown.</summary>
    public double? Value { get; }

    /// <summary>Progress as roots moved against roots still to move: moved / (moved + remaining).</summary>
    public static IoPortProgress FromRoots(long moved, long remaining)
    {
        if (moved < 0) throw new ArgumentOutOfRangeException(nameof(moved));
        if (remaining < 0) throw new ArgumentOutOfRangeException(nameof(remaining));
        var total = moved + remaining;
        return new IoPortProgress(total == 0 ? 1d : (double)moved / total);
    }
}
