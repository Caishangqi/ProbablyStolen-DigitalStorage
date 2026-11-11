using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Interaction;

/// <summary>Supplies a validated, current cell and accepts one detached partition submission.</summary>
public interface IPartitionWindowSource : IDisposable
{
    /// <summary>Checks that the original session, bay and cell remain current.</summary>
    bool IsCurrent { get; }
    /// <summary>Reads a finalized partition; missing or untrusted data returns false.</summary>
    bool TryRead(out CellPartition? partition);
    /// <summary>Reads detached identities of all trustworthy current contents.</summary>
    bool TryReadContents(out IReadOnlyList<ItemKey>? keys);
    /// <summary>Submits the candidate, revalidating the same cell at the publication boundary.</summary>
    bool TrySubmit(CellPartition candidate);
}

/// <summary>Owns a bounded draft without retaining native items.</summary>
public sealed class PartitionWindowController : IDisposable
{
    private static readonly IReadOnlyList<ItemKey> NoKeys = Array.Empty<ItemKey>();
    private readonly PartitionEditor _editor = new();
    private IPartitionWindowSource? _source;
    private int _sourceVersion;
    private bool _busy;
    private bool _closeRequested;

    /// <summary>Reports the most recent action outcome.</summary>
    public string Status { get; private set; } = "unavailable";
    /// <summary>Whether a validated draft is open.</summary>
    public bool IsEditing => _editor.IsEditing;
    /// <summary>Read-only identities in the draft, hidden when there is no validated draft.</summary>
    public IReadOnlyList<ItemKey> Keys => _editor.IsEditing ? _editor.DraftKeys : NoKeys;

    /// <summary>Starts from the source's validated current partition only.</summary>
    public bool Open(IPartitionWindowSource source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (_busy)
        {
            _closeRequested = true;
            DisposeRejected(source);
            return false;
        }

        _busy = true;
        _closeRequested = false;
        ClearDraft();
        ReleaseSource(out var oldDisposeFailed);
        var opened = false;
        var sourceInstalled = false;
        try
        {
            _source = source;
            sourceInstalled = true;
            var version = ++_sourceVersion;
            if (_closeRequested) return Fail("unavailable");
            if (!ReadCurrent(source, version) || _closeRequested ||
                !source.TryRead(out var partition) || !Owns(source, version) || _closeRequested ||
                partition == null || !partition.IsValid || !AllCanonical(partition.List.GetItems()) ||
                !ReadCurrent(source, version) || _closeRequested)
                return Fail("unavailable");

            _editor.Begin(partition);
            Status = oldDisposeFailed ? "cleanup_failed" : "ready";
            opened = true;
            return true;
        }
        catch (Exception)
        {
            return Fail("unavailable");
        }
        finally
        {
            if (!opened)
            {
                if (sourceInstalled && ReferenceEquals(_source, source)) ReleaseSource(out _);
                else if (!sourceInstalled) DisposeRejected(source);
            }
            _busy = false;
            if (_closeRequested) FinishDeferredClose();
        }
    }

    /// <summary>Adds a detached identity without modifying its physical source.</summary>
    public bool AddGhost(ItemKey? key)
    {
        if (!RunCurrent(() => key != null && key.IsCanonical && AddKey(key), "invalid")) return false;
        Status = "ready";
        return true;
    }

    /// <summary>Removes a single identity.</summary>
    public bool Remove(ItemKey key)
    {
        var removed = false;
        if (!RunCurrent(() => removed = _editor.Remove(key), "unavailable")) return false;
        Status = "ready";
        return removed;
    }

    /// <summary>Clears all draft identities.</summary>
    public bool Clear()
    {
        if (!RunCurrent(() => _editor.Clear(), "unavailable")) return false;
        Status = "ready";
        return true;
    }

    /// <summary>Replaces the draft only after validating the complete content snapshot.</summary>
    public bool SetFromContents()
    {
        if (!BeginOperation(out var source, out var version)) return false;
        try
        {
            if (!ReadCurrent(source, version) || _closeRequested ||
                !source.TryReadContents(out var keys) || !Owns(source, version) || _closeRequested ||
                keys == null || !AllCanonical(keys)) return Fail("invalid");
            var candidate = CellPartition.FromUserSubmission(keys, PartitionEditor.MaxKeys);
            if (!ReadCurrent(source, version) || _closeRequested) return Fail("unavailable");
            _editor.Clear();
            foreach (var key in candidate.List.GetItems()) _editor.Add(key);
            Status = "ready";
            return true;
        }
        catch (Exception) { return Fail("invalid"); }
        finally { EndOperation(); }
    }

    /// <summary>Submits once; rejection or exception leaves the draft open unless close was requested.</summary>
    public bool Apply()
    {
        if (!BeginOperation(out var source, out var version)) return false;
        var accepted = false;
        var submitFailed = false;
        try
        {
            if (!ReadCurrent(source, version) || _closeRequested) return Fail("unavailable");
            accepted = _editor.TryApply(candidate =>
            {
                if (!ReadCurrent(source, version) || _closeRequested) return false;
                var result = source.TrySubmit(candidate);
                return Owns(source, version) && result;
            }, out _);
            submitFailed = !accepted;
            Status = accepted ? "applied" : "failed";
            if (accepted) _closeRequested = true;
            return accepted;
        }
        catch (Exception)
        {
            submitFailed = true;
            Status = "failed";
            return false;
        }
        finally
        {
            EndOperation();
            if (accepted)
                Status = Status == "cleanup_failed" ? "applied_cleanup_failed" : "applied";
            else if (submitFailed && Status == "cleanup_failed") Status = "failed_cleanup_failed";
        }
    }

    /// <summary>Checks current source ownership before a native drag item is inspected.</summary>
    public bool ValidateSourceForGesture() => RunCurrent(() => true, "unavailable");

    /// <summary>Handles a ghost drag over this window; every targeted active drag is consumed.</summary>
    public PartitionGhostGestureResult HandleGhostGesture(bool pointerTarget, bool dragging,
        Func<ItemKey?> capture, Func<bool> cancelWithoutPlacement)
    {
        if (!pointerTarget || !dragging)
            return PartitionGhostGestureResult.NotTargeted;
        if (capture == null) throw new ArgumentNullException(nameof(capture));
        if (cancelWithoutPlacement == null) throw new ArgumentNullException(nameof(cancelWithoutPlacement));

        if (!BeginOperation(out var source, out var version))
            return PartitionGhostGesture.Execute(true, true, () => false, capture, cancelWithoutPlacement, _ => { });
        try
        {
            return PartitionGhostGesture.Execute(true, true,
                () => ReadCurrent(source, version) && !_closeRequested,
                capture,
                cancelWithoutPlacement,
                key =>
                {
                    if (Owns(source, version) && !_closeRequested) _editor.Add(key);
                });
        }
        finally { EndOperation(); }
    }

    /// <summary>Discards an unsubmitted draft and releases its source.</summary>
    public void Close()
    {
        if (_busy)
        {
            _closeRequested = true;
            return;
        }
        _busy = true;
        _closeRequested = false;
        CleanupClosedDraft();
        _busy = false;
        _closeRequested = false;
    }

    /// <inheritdoc />
    public void Dispose() => Close();

    private bool RunCurrent(Func<bool> action, string failure)
    {
        if (!BeginOperation(out var source, out var version)) return false;
        try
        {
            if (!ReadCurrent(source, version) || _closeRequested) return Fail("unavailable");
            if (!action() || !Owns(source, version) || _closeRequested) return Fail(failure);
            return true;
        }
        catch (InvalidOperationException) { return Fail("limit"); }
        catch (Exception) { return Fail(failure); }
        finally { EndOperation(); }
    }

    private bool BeginOperation(out IPartitionWindowSource source, out int version)
    {
        source = _source!;
        version = _sourceVersion;
        if (_busy || !_editor.IsEditing || source == null)
        {
            Fail("unavailable");
            return false;
        }
        _busy = true;
        _closeRequested = false;
        return true;
    }

    private void EndOperation()
    {
        _busy = false;
        if (_closeRequested) FinishDeferredClose();
    }

    private void FinishDeferredClose()
    {
        _closeRequested = false;
        _busy = true;
        CleanupClosedDraft();
        _busy = false;
        _closeRequested = false;
    }

    private bool ReadCurrent(IPartitionWindowSource source, int version)
    {
        if (!Owns(source, version) || _closeRequested) return false;
        bool current;
        try { current = source.IsCurrent; }
        catch
        {
            if (Owns(source, version)) _closeRequested = true;
            throw;
        }
        if (!current || !Owns(source, version) || _closeRequested)
        {
            if (Owns(source, version)) _closeRequested = true;
            return false;
        }
        return true;
    }

    private bool Owns(IPartitionWindowSource source, int version) =>
        ReferenceEquals(_source, source) && _sourceVersion == version;

    private bool AddKey(ItemKey key)
    {
        _editor.Add(key);
        return true;
    }

    private bool Fail(string status) { Status = status; return false; }

    private void ClearDraft()
    {
        if (_editor.IsEditing) _editor.Cancel();
    }

    private void CleanupClosedDraft()
    {
        ClearDraft();
        ReleaseSource(out var disposeFailed);
        Status = disposeFailed ? "cleanup_failed" : "unavailable";
    }

    private void ReleaseSource(out bool disposeFailed)
    {
        var source = _source;
        _source = null;
        _sourceVersion++;
        disposeFailed = false;
        if (source == null) return;
        try { source.Dispose(); }
        catch (Exception) { disposeFailed = true; }
    }

    private void DisposeRejected(IPartitionWindowSource source)
    {
        try { source.Dispose(); }
        catch (Exception) { Status = "cleanup_failed"; }
    }

    private static bool AllCanonical(IReadOnlyList<ItemKey> keys)
    {
        foreach (var key in keys) if (key == null || !key.IsCanonical) return false;
        return true;
    }
}

/// <summary>Pure release ordering for a ghost drag over the partition window.</summary>
public static class PartitionGhostGesture
{
    /// <summary>Captures only for the current source, always attempts cancellation, and accepts only after confirmation.</summary>
    public static PartitionGhostGestureResult Execute(bool pointerTarget, bool dragging,
        Func<bool> sourceCurrent, Func<ItemKey?> capture, Func<bool> cancelWithoutPlacement,
        Action<ItemKey> accept)
    {
        if (!pointerTarget || !dragging) return PartitionGhostGestureResult.NotTargeted;
        ItemKey? key = null;
        var captured = false;
        var cancelled = false;
        try
        {
            if (sourceCurrent())
            {
                key = capture();
                captured = key?.IsCanonical == true;
            }
        }
        catch (Exception) { }
        finally
        {
            try { cancelled = cancelWithoutPlacement(); }
            catch (Exception) { cancelled = false; }
        }

        var accepted = false;
        if (cancelled && captured && key != null)
        {
            try
            {
                if (sourceCurrent())
                {
                    accept(key);
                    accepted = true;
                }
            }
            catch (Exception) { }
        }
        return new PartitionGhostGestureResult(true, cancelled, accepted);
    }
}

/// <summary>Reports whether a targeted native drag was consumed, cancelled and accepted as a ghost key.</summary>
public readonly struct PartitionGhostGestureResult
{
    internal PartitionGhostGestureResult(bool consumed, bool cancellationConfirmed, bool accepted)
    { Consumed = consumed; CancellationConfirmed = cancellationConfirmed; Accepted = accepted; }
    /// <summary>Whether the pointer release belongs to the partition window.</summary>
    public bool Consumed { get; }
    /// <summary>Whether native drag cancellation completed without placement.</summary>
    public bool CancellationConfirmed { get; }
    /// <summary>Whether one validated detached key entered the draft.</summary>
    public bool Accepted { get; }
    internal static PartitionGhostGestureResult NotTargeted => new(false, false, false);
}

/// <summary>Creates a stable, bounded label that distinguishes complete canonical identities without exposing their payload.</summary>
public static class PartitionIdentityLabel
{
    /// <summary>Combines the readable item identifier with a SHA-256 fingerprint of its complete identity.</summary>
    public static string Format(ItemKey key)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (!key.IsCanonical) return key.Identifier;
        var identifier = System.Text.Encoding.UTF8.GetBytes(key.Identifier);
        var state = key.CopyCanonicalState();
        var value = new byte[4 + identifier.Length + state.Length];
        value[0] = (byte)(identifier.Length >> 24);
        value[1] = (byte)(identifier.Length >> 16);
        value[2] = (byte)(identifier.Length >> 8);
        value[3] = (byte)identifier.Length;
        Buffer.BlockCopy(identifier, 0, value, 4, identifier.Length);
        Buffer.BlockCopy(state, 0, value, 4 + identifier.Length, state.Length);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(value);
        var chars = new char[hash.Length * 2];
        const string hex = "0123456789abcdef";
        for (var index = 0; index < hash.Length; index++)
        {
            chars[index * 2] = hex[hash[index] >> 4];
            chars[index * 2 + 1] = hex[hash[index] & 15];
        }
        const int maxIdentifierLength = 64;
        var shownIdentifier = key.Identifier.Length <= maxIdentifierLength
            ? key.Identifier : key.Identifier.Substring(0, maxIdentifierLength - 3) + "...";
        return shownIdentifier + " [" + new string(chars) + "]";
    }
}
