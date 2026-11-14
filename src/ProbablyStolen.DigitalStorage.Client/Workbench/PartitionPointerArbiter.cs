using System;

namespace ProbablyStolen.DigitalStorage.Client.Workbench;

/// <summary>The physical pointer button used by a partition rule grid.</summary>
public enum PartitionPointerButton
{
    /// <summary>Primary pointer button.</summary>
    Left,
    /// <summary>Secondary pointer button.</summary>
    Right,
    /// <summary>Middle pointer button.</summary>
    Middle
}

/// <summary>The action produced by one validated partition pointer release.</summary>
public enum PartitionPointerGesture
{
    /// <summary>No action.</summary>
    None,
    /// <summary>Open rule configuration.</summary>
    Configure,
    /// <summary>Delete one rule.</summary>
    Delete,
    /// <summary>Copy one rule.</summary>
    Duplicate,
    /// <summary>Move or swap one rule.</summary>
    Move
}

/// <summary>Validates partition pointer presses and produces at most one action per release.</summary>
public sealed class PartitionPointerArbiter
{
    private PartitionPointerButton _button;
    private int _slot = -1;
    private string? _ruleId;
    private int _generation;
    private bool _pressed;
    private bool _dragged;

    /// <summary>Gets whether a pointer press is active.</summary>
    public bool IsPressed => _pressed;

    /// <summary>Gets whether the active press became a drag.</summary>
    public bool IsDragging => _dragged;

    /// <summary>Gets the active button.</summary>
    public PartitionPointerButton Button => _button;

    /// <summary>Gets the source slot of the active press.</summary>
    public int SourceSlot => _slot;

    /// <summary>Gets the identity captured at press time.</summary>
    public string? RuleId => _ruleId;

    /// <summary>Starts a press when the target is actionable and the page is current.</summary>
    public bool Press(PartitionPointerButton button, int slot, string? ruleId, int generation,
        bool visibleAndTopmost, bool sessionActive, bool nativeDrag, bool conflictingButtons)
    {
        Cancel();
        if (!visibleAndTopmost || !sessionActive || nativeDrag || conflictingButtons || slot < 0 ||
            string.IsNullOrEmpty(ruleId)) return false;
        _button = button;
        _slot = slot;
        _ruleId = ruleId;
        _generation = generation;
        _pressed = true;
        return true;
    }

    /// <summary>Marks a primary-button press as a drag after it leaves its source slot.</summary>
    public void Move(int slot)
    {
        if (_pressed && slot != _slot) _dragged = true;
    }

    /// <summary>Completes or cancels the active press after validating identity and page state.</summary>
    public PartitionPointerGesture Release(PartitionPointerButton button, int slot, string? ruleId, int generation,
        bool visibleAndTopmost, bool sessionActive, bool nativeDrag, bool conflictingButtons)
    {
        if (!_pressed) return PartitionPointerGesture.None;
        var sameTarget = _button == button && _generation == generation &&
            (_dragged
                ? _button == PartitionPointerButton.Left && slot >= 0 &&
                  string.Equals(_ruleId, ruleId, StringComparison.Ordinal)
                : _slot == slot && string.Equals(_ruleId, ruleId, StringComparison.Ordinal));
        var valid = sameTarget && visibleAndTopmost && sessionActive && !nativeDrag && !conflictingButtons;
        var gesture = PartitionPointerGesture.None;
        if (valid)
        {
            if (_button == PartitionPointerButton.Left)
                gesture = _dragged ? PartitionPointerGesture.Move : PartitionPointerGesture.Configure;
            else if (!_dragged)
                gesture = _button == PartitionPointerButton.Right ? PartitionPointerGesture.Delete : PartitionPointerGesture.Duplicate;
        }
        Cancel();
        return gesture;
    }

    /// <summary>Cancels an active press when back consumes that interaction layer.</summary>
    public bool CancelForBack()
    {
        var consumed = _pressed;
        Cancel();
        return consumed;
    }

    /// <summary>Cancels the active press and clears its captured identity.</summary>
    public void Cancel()
    {
        _pressed = false;
        _dragged = false;
        _slot = -1;
        _ruleId = null;
    }
}
