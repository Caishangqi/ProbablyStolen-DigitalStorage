using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Interaction;

/// <summary>Pure draft editor for a cell partition; it never writes persistence or moves items.</summary>
public sealed class PartitionEditor
{
    private readonly List<ItemKey> _baseline = new();
    private readonly List<ItemKey> _draft = new();
    private CellPartitionMode _baselineMode;
    private bool _submitting;

    /// <summary>Starts a draft from the current validated partition.</summary>
    public void Begin(CellPartition current)
    {
        EnsureNotSubmitting();
        if (current == null)
        {
            throw new ArgumentNullException(nameof(current));
        }

        _baseline.Clear();
        _draft.Clear();
        _baselineMode = current.Mode;
        foreach (var key in current.Keys)
        {
            _baseline.Add(key);
            _draft.Add(key);
        }

        IsEditing = true;
        IsDirty = false;
    }

    /// <summary>Gets whether a draft is active.</summary>
    public bool IsEditing { get; private set; }

    /// <summary>Gets whether the draft differs from its baseline.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Gets the current draft keys without exposing a mutable list.</summary>
    public IReadOnlyList<ItemKey> DraftKeys => new ReadOnlyCollection<ItemKey>(_draft);

    /// <summary>Adds one exact identity to the draft whitelist.</summary>
    public bool Add(ItemKey key)
    {
        EnsureEditing();
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (_draft.Contains(key))
        {
            return false;
        }

        if (_draft.Count >= 63)
        {
            throw new InvalidOperationException("A partition draft accepts at most 63 identities.");
        }

        _draft.Add(key);
        UpdateDirty();
        return true;
    }

    /// <summary>Removes one exact identity from the draft.</summary>
    public bool Remove(ItemKey key)
    {
        EnsureEditing();
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        var removed = _draft.Remove(key);
        if (removed)
        {
            UpdateDirty();
        }

        return removed;
    }

    /// <summary>Clears the draft; applying an empty draft means the unfiltered user edit.</summary>
    public bool Clear()
    {
        EnsureEditing();
        if (_draft.Count == 0)
        {
            return false;
        }

        _draft.Clear();
        UpdateDirty();
        return true;
    }

    /// <summary>Returns the pure partition value and closes the draft without submitting it.</summary>
    public CellPartition Apply()
    {
        EnsureEditing();
        var result = CellPartition.FromUserSubmission(_draft);
        CompleteApply(result);
        return result;
    }

    /// <summary>Closes the draft only if the supplied callback accepts its detached value.</summary>
    /// <remarks>Callback exceptions propagate with the draft unchanged. This guard does not establish publication atomicity.</remarks>
    public bool TryApply(Func<CellPartition, bool> submit, out CellPartition? applied)
    {
        EnsureEditing();
        if (submit == null) throw new ArgumentNullException(nameof(submit));

        var candidate = CellPartition.FromUserSubmission(_draft);
        applied = null;
        bool accepted;
        _submitting = true;
        try
        {
            accepted = submit(candidate);
        }
        finally
        {
            _submitting = false;
        }

        if (!accepted) return false;
        CompleteApply(candidate);
        applied = candidate;
        return true;
    }

    private void CompleteApply(CellPartition result)
    {
        _baseline.Clear();
        _baseline.AddRange(_draft);
        _baselineMode = result.Mode;
        IsDirty = false;
        IsEditing = false;
    }

    /// <summary>Discards the draft and restores the baseline without persistence or inventory mutation.</summary>
    public void Cancel()
    {
        EnsureEditing();
        _draft.Clear();
        _draft.AddRange(_baseline);
        IsDirty = false;
        IsEditing = false;
    }

    private void EnsureEditing()
    {
        EnsureNotSubmitting();
        if (!IsEditing)
        {
            throw new InvalidOperationException("No partition draft is active.");
        }
    }

    private void EnsureNotSubmitting()
    {
        if (_submitting) throw new InvalidOperationException("A partition submission is already in progress.");
    }

    private void UpdateDirty()
    {
        IsDirty = _baselineMode != (_draft.Count == 0 ? CellPartitionMode.Unfiltered : CellPartitionMode.Whitelist) ||
            _baseline.Count != _draft.Count;
        if (!IsDirty)
        {
            for (var index = 0; index < _baseline.Count; index++)
            {
                if (!_baseline[index].Equals(_draft[index]))
                {
                    IsDirty = true;
                    break;
                }
            }
        }
    }
}
