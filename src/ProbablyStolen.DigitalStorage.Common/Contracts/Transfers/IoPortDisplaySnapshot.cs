using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;

namespace ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;

/// <summary>The latest IO Port state for the window; reading it never scans storage.</summary>
public sealed class IoPortDisplaySnapshot
{
    /// <summary>Creates a snapshot.</summary>
    /// <param name="status">The runtime status.</param>
    /// <param name="settings">The saved settings, or null when they cannot be read.</param>
    /// <param name="progress">The progress of the current work.</param>
    public IoPortDisplaySnapshot(IoPortRuntimeStatus status, IoPortSettings? settings, IoPortProgress progress)
    {
        if (!Enum.IsDefined(typeof(IoPortRuntimeStatus), status)) throw new ArgumentOutOfRangeException(nameof(status));
        Status = status;
        Settings = settings;
        Progress = progress ?? throw new ArgumentNullException(nameof(progress));
    }

    /// <summary>A snapshot for a machine whose saved settings cannot be read.</summary>
    public static IoPortDisplaySnapshot SettingsUnavailable { get; } =
        new(IoPortRuntimeStatus.StorageUnavailable, null, IoPortProgress.Unknown);

    /// <summary>Gets the runtime status.</summary>
    public IoPortRuntimeStatus Status { get; }

    /// <summary>Gets the saved settings, or null when they cannot be read.</summary>
    public IoPortSettings? Settings { get; }

    /// <summary>Gets the progress of the current work.</summary>
    public IoPortProgress Progress { get; }
}
