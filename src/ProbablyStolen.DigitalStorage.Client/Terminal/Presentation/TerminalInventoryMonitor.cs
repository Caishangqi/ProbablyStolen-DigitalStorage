using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>Why a terminal should read a fresh inventory snapshot this frame.</summary>
public enum TerminalRefreshReason
{
    /// <summary>Nothing requires a read.</summary>
    None,

    /// <summary>A network event or a reopen marked the view stale.</summary>
    Dirty,

    /// <summary>A diagnostic audit is due; a change found here means a network event was missed.</summary>
    Audit
}

/// <summary>Decides when a terminal rereads inventory and detects display changes without owning inventory state.</summary>
/// <remarks>
/// Network events mark the view dirty and the next frame reads once, mirroring AE2's dirty-cache-then-tick refresh.
/// There is no fixed polling; a periodic audit runs only while diagnostics are enabled.
/// </remarks>
public sealed class TerminalInventoryMonitor
{
    /// <summary>The default delay between diagnostic audits, in seconds.</summary>
    public const float DefaultAuditIntervalSeconds = 5f;
    private readonly float _auditIntervalSeconds;
    private float _nextAudit;
    private bool _dirty = true;
    private TerminalInventorySnapshot? _current;

    /// <summary>Creates a monitor with a fixed interval between diagnostic audits.</summary>
    public TerminalInventoryMonitor(float auditIntervalSeconds = DefaultAuditIntervalSeconds)
    {
        if (!float.IsFinite(auditIntervalSeconds) || auditIntervalSeconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(auditIntervalSeconds));
        _auditIntervalSeconds = auditIntervalSeconds;
    }

    /// <summary>Requests a read on the next active frame.</summary>
    public void MarkDirty() => _dirty = true;

    /// <summary>Consumes a pending dirty mark, or reserves a due audit when diagnostics are enabled.</summary>
    public TerminalRefreshReason BeginRefresh(float now, bool active, bool auditEnabled)
    {
        if (!active || !float.IsFinite(now)) return TerminalRefreshReason.None;
        if (_dirty)
        {
            _dirty = false;
            _nextAudit = now + _auditIntervalSeconds;
            return TerminalRefreshReason.Dirty;
        }

        if (!auditEnabled || now < _nextAudit) return TerminalRefreshReason.None;
        _nextAudit = now + _auditIntervalSeconds;
        return TerminalRefreshReason.Audit;
    }

    /// <summary>Checks a snapshot against the last accepted inventory and link state.</summary>
    public bool HasChanged(TerminalInventorySnapshot snapshot) => _current == null || !Equivalent(_current, snapshot);

    /// <summary>Stores a fresh snapshot as the comparison baseline; it satisfies any pending dirty mark.</summary>
    public void Accept(TerminalInventorySnapshot snapshot)
    {
        _current = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _dirty = false;
    }

    /// <summary>Clears the comparison baseline and makes the next active frame read immediately.</summary>
    public void Reset(float now = 0f)
    {
        _current = null;
        _dirty = true;
        _nextAudit = now;
    }

    private static bool Equivalent(TerminalInventorySnapshot left, TerminalInventorySnapshot right)
    {
        if (left.Online != right.Online || left.StorageUnavailable != right.StorageUnavailable ||
            left.Entries.Count != right.Entries.Count) return false;

        var rightByKey = right.Entries.ToDictionary(entry => entry.Key);
        foreach (var entry in left.Entries)
        {
            if (!rightByKey.TryGetValue(entry.Key, out var other) || entry.Amount != other.Amount ||
                !string.Equals(entry.DisplayName, other.DisplayName, StringComparison.Ordinal) ||
                !string.Equals(entry.SpriteKey, other.SpriteKey, StringComparison.Ordinal) ||
                entry.SpriteWidth != other.SpriteWidth || entry.SpriteHeight != other.SpriteHeight) return false;
        }
        return true;
    }
}
