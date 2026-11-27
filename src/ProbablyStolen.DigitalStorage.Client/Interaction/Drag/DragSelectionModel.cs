using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Client.Interaction.Drag;

/// <summary>Maintains a revision-bound rectangle selection without reading inventory state.</summary>
public sealed class DragSelectionModel<TId>
{
    private static readonly IReadOnlyList<TId> Empty = Array.Empty<TId>();
    private readonly List<TId> _selected = new();
    private PointerPoint _origin;
    private long _revision;
    private bool _active;

    /// <summary>Gets whether a rectangle gesture is active.</summary>
    public bool IsActive => _active;
    /// <summary>Gets the current selection in reading order.</summary>
    public IReadOnlyList<TId> SelectedIds => new ReadOnlyCollection<TId>(_selected.ToArray());

    /// <summary>
    /// Starts a selection gesture at a finite point and binds it to the revision of the candidates' layout; a candidate
    /// list from another revision cancels the gesture.
    /// </summary>
    public bool Begin(PointerPoint origin, long revision)
    {
        Cancel();
        if (!origin.IsValid) return false;
        _origin = origin;
        _revision = revision;
        _active = true;
        return true;
    }

    /// <summary>
    /// Moves the fixed corner of an active rectangle, for example when the content under it scrolled. The next
    /// <see cref="Update"/> measures from the new corner.
    /// </summary>
    public bool MoveOrigin(PointerPoint origin)
    {
        if (!_active || !origin.IsValid) return false;
        _origin = origin;
        return true;
    }

    /// <summary>Updates the selection against the current candidate page.</summary>
    public IReadOnlyList<TId> Update(PointerPoint current, IReadOnlyList<DragSelectionCandidate<TId>> candidates,
        long revision)
    {
        if (!_active) return Empty;
        if (candidates == null || revision != _revision || !current.IsValid ||
            !PointerRectangle.TryCreate(_origin, current, out var area))
        {
            Cancel();
            return Empty;
        }

        var ordered = candidates.OrderBy(candidate => candidate.Order).ToArray();
        var ids = new HashSet<TId>();
        foreach (var candidate in ordered)
        {
            if (!candidate.Bounds.IsValid || candidate.Id is null || !ids.Add(candidate.Id))
            {
                Cancel();
                return Empty;
            }
        }

        _selected.Clear();
        if (area.HasArea)
        {
            foreach (var candidate in ordered)
            {
                if (area.Intersects(candidate.Bounds)) _selected.Add(candidate.Id);
            }
        }

        return SelectedIds;
    }

    /// <summary>Completes the gesture and keeps the valid selected IDs for a later drag.</summary>
    public IReadOnlyList<TId> Complete(long revision)
    {
        if (!_active || revision != _revision)
        {
            Cancel();
            return Empty;
        }

        _active = false;
        return SelectedIds;
    }

    /// <summary>Clears the active gesture and its selection.</summary>
    public void Cancel()
    {
        _active = false;
        _selected.Clear();
        _origin = default;
        _revision = 0;
    }
}
