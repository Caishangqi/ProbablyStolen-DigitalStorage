using System;

namespace ProbablyStolen.DigitalStorage.Client.Workbench;

/// <summary>Coordinates cancellation of the two rule-grid press trackers.</summary>
public sealed class PartitionPointerBackCoordinator
{
    private readonly GridGestureArbiter _legacy;
    private readonly PartitionPointerArbiter _pointer;

    /// <summary>Creates a coordinator for one partition grid.</summary>
    public PartitionPointerBackCoordinator(GridGestureArbiter legacy, PartitionPointerArbiter pointer)
    {
        _legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));
        _pointer = pointer ?? throw new ArgumentNullException(nameof(pointer));
    }

    /// <summary>Gets whether either tracker has a press that back should consume.</summary>
    public bool HasActivePress => _legacy.PressSlot >= 0 || _pointer.IsPressed;

    /// <summary>Cancels both trackers and reports whether back consumed a press.</summary>
    public bool Cancel()
    {
        if (!HasActivePress) return false;
        _legacy.Cancel();
        _pointer.CancelForBack();
        return true;
    }
}
