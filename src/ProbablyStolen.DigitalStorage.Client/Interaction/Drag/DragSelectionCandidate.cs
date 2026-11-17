using System;

namespace ProbablyStolen.DigitalStorage.Client.Interaction.Drag;

/// <summary>A selectable display item with stable identity, screen bounds and reading order.</summary>
public readonly struct DragSelectionCandidate<TId>
{
    /// <summary>Creates a selection candidate.</summary>
    public DragSelectionCandidate(TId id, PointerRectangle bounds, int order)
    {
        if (id is null) throw new ArgumentNullException(nameof(id));
        Id = id;
        Bounds = bounds;
        Order = order;
    }

    /// <summary>Gets the stable display identity.</summary>
    public TId Id { get; }
    /// <summary>Gets the candidate's screen rectangle.</summary>
    public PointerRectangle Bounds { get; }
    /// <summary>Gets the reading order used to return selected IDs.</summary>
    public int Order { get; }
}
