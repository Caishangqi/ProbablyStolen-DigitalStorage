using System;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;

namespace ProbablyStolen.DigitalStorage.Client.Machines.IoPort;

/// <summary>What the IO Port window shows for one display snapshot; holds no game objects.</summary>
public sealed class IoPortPanelState
{
    private IoPortPanelState(IoPortDisplaySnapshot snapshot)
    {
        var settings = snapshot.Settings;
        Status = snapshot.Status;
        Progress = snapshot.Progress.Value;
        StatusTextKey = settings == null ? "io_status_settings_unavailable" : StatusKey(snapshot.Status);
        if (settings == null) return;

        SettingsKnown = true;
        Running = settings.Running;
        ArrowPointsRight = settings.Direction == IoPortDirection.Forward;
        DirectionTextKey = ArrowPointsRight ? "io_direction_cell_to_network" : "io_direction_network_to_cell";
        CompletionTextKey = CompletionKey(settings.CompletionMode);
        CompletionIconKey = CompletionIcon(settings.CompletionMode);
    }

    /// <summary>Projects a display snapshot.</summary>
    public static IoPortPanelState From(IoPortDisplaySnapshot snapshot) =>
        new(snapshot ?? throw new ArgumentNullException(nameof(snapshot)));

    /// <summary>Gets whether the saved settings could be read; the controls are disabled otherwise.</summary>
    public bool SettingsKnown { get; }

    /// <summary>Gets whether the machine runs; Start is offered when false and Pause when true.</summary>
    public bool Running { get; }

    /// <summary>Gets whether the central arrow points from the input cells to the network.</summary>
    public bool ArrowPointsRight { get; }

    /// <summary>Gets the direction text key, or null when the settings are unknown.</summary>
    public string? DirectionTextKey { get; }

    /// <summary>Gets the completion text key, or null when the settings are unknown.</summary>
    public string? CompletionTextKey { get; }

    /// <summary>Gets the completion icon key, or the placeholder when the settings are unknown.</summary>
    public string CompletionIconKey { get; } = "button_placeholder";

    /// <summary>Gets the runtime status.</summary>
    public IoPortRuntimeStatus Status { get; }

    /// <summary>Gets the status text key.</summary>
    public string StatusTextKey { get; }

    /// <summary>Gets the completed fraction, or null when unknown.</summary>
    public double? Progress { get; }

    /// <summary>Gets the icon key of a completion mode.</summary>
    public static string CompletionIcon(IoPortCompletionMode mode) => mode switch
    {
        IoPortCompletionMode.Empty => "button_io_finish_empty",
        IoPortCompletionMode.Full => "button_io_finish_full",
        IoPortCompletionMode.WorkComplete => "button_io_finish_done",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    /// <summary>Gets the text key of a completion mode.</summary>
    public static string CompletionKey(IoPortCompletionMode mode) => mode switch
    {
        IoPortCompletionMode.Empty => "io_finish_empty",
        IoPortCompletionMode.Full => "io_finish_full",
        IoPortCompletionMode.WorkComplete => "io_finish_done",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    /// <summary>Gets the completion mode after <paramref name="mode"/> in the rail button's cycle.</summary>
    public static IoPortCompletionMode NextCompletion(IoPortCompletionMode mode) => mode switch
    {
        IoPortCompletionMode.WorkComplete => IoPortCompletionMode.Empty,
        IoPortCompletionMode.Empty => IoPortCompletionMode.Full,
        _ => IoPortCompletionMode.WorkComplete
    };

    private static string StatusKey(IoPortRuntimeStatus status) => status switch
    {
        IoPortRuntimeStatus.Idle => "io_status_idle",
        IoPortRuntimeStatus.Working => "io_status_working",
        IoPortRuntimeStatus.NoEligibleContent => "io_status_no_eligible_content",
        IoPortRuntimeStatus.NoController => "io_status_no_controller",
        IoPortRuntimeStatus.ControllerConflict => "io_status_controller_conflict",
        IoPortRuntimeStatus.Unpowered => "io_status_unpowered",
        IoPortRuntimeStatus.OperationFeeUnavailable => "io_status_operation_fee_unavailable",
        IoPortRuntimeStatus.WaitingForCapacity => "io_status_waiting_for_capacity",
        IoPortRuntimeStatus.StorageUnavailable => "io_status_storage_unavailable",
        IoPortRuntimeStatus.UnsupportedEnergySource => "io_status_unsupported_energy_source",
        IoPortRuntimeStatus.OutputFull => "io_status_output_full",
        IoPortRuntimeStatus.Faulted => "io_status_faulted",
        IoPortRuntimeStatus.Paused => "io_status_paused",
        IoPortRuntimeStatus.Completed => "io_status_completed",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
