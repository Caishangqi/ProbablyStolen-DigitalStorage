using System;

namespace ProbablyStolen.DigitalStorage.Client.Workbench;

/// <summary>What a pointer release on the rule grid means.</summary>
public enum GridGesture
{
    /// <summary>Nothing: the release was already handled or no press started it.</summary>
    None = 0,

    /// <summary>A single click.</summary>
    Click = 1,

    /// <summary>The second click on the same slot within the allowed interval.</summary>
    DoubleClick = 2,

    /// <summary>The end of a drag from one slot to another.</summary>
    Drop = 3
}

/// <summary>
/// Binds an editing session to one cell. When the cell leaves the workbench or is replaced, the session ends and every
/// later callback is dropped, so an old window can never write to another cell.
/// </summary>
public sealed class PartitionSession
{
    private readonly object _target;

    /// <summary>Starts a session for a target cell.</summary>
    public PartitionSession(object target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        IsActive = true;
    }

    /// <summary>Gets whether the session still edits its cell.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Ends the session; it never becomes active again.</summary>
    public void End() => IsActive = false;

    /// <summary>Gets whether a callback for this target may still act.</summary>
    public bool Accepts(object? target) => IsActive && ReferenceEquals(target, _target);
}

/// <summary>
/// Turns pointer presses, moves and releases on the grid into one gesture per release. A drag never also counts as a
/// click, and a release is reported only once even when several handlers observe it.
/// </summary>
public sealed class GridGestureArbiter
{
    private readonly double _doubleClickSeconds;
    private int _pressSlot = -1;
    private bool _dragging;
    private bool _pressed;
    private int _lastClickSlot = -1;
    private double _lastClickTime = double.NegativeInfinity;

    /// <summary>Creates an arbiter with an interval for detecting a double click.</summary>
    public GridGestureArbiter(double doubleClickSeconds = 0.35)
    {
        if (doubleClickSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(doubleClickSeconds));
        _doubleClickSeconds = doubleClickSeconds;
    }

    /// <summary>Gets the slot the current press started on, or -1.</summary>
    public int PressSlot => _pressed ? _pressSlot : -1;

    /// <summary>Gets whether the current press has become a drag.</summary>
    public bool IsDragging => _pressed && _dragging;

    /// <summary>Records a press on a slot.</summary>
    public void Press(int slot)
    {
        _pressed = true;
        _pressSlot = slot;
        _dragging = false;
    }

    /// <summary>Records that the pointer moved onto another slot while pressed, which starts a drag.</summary>
    public void Move(int slot)
    {
        if (_pressed && slot != _pressSlot) _dragging = true;
    }

    /// <summary>Gets the gesture of a release at a slot and time; later calls for the same release return None.</summary>
    public GridGesture Release(int slot, double time)
    {
        if (!_pressed) return GridGesture.None;
        _pressed = false;
        if (_dragging)
        {
            _lastClickSlot = -1;
            return slot != _pressSlot ? GridGesture.Drop : GridGesture.None;
        }

        if (slot != _pressSlot) return GridGesture.None;
        if (slot == _lastClickSlot && time - _lastClickTime <= _doubleClickSeconds)
        {
            _lastClickSlot = -1;
            return GridGesture.DoubleClick;
        }

        _lastClickSlot = slot;
        _lastClickTime = time;
        return GridGesture.Click;
    }

    /// <summary>Forgets the current press, as when a drag leaves the grid and is cancelled.</summary>
    public void Cancel()
    {
        _pressed = false;
        _dragging = false;
    }
}
