using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>Captures presentation data only when a new inventory snapshot is accepted.</summary>
public sealed class TerminalSnapshotCache<T> where T : class
{
    private readonly Func<TerminalEntry, T> _capture;
    private readonly Func<TerminalEntry, Exception, T>? _fallback;
    private Dictionary<ItemKey, T> _values = new();

    /// <summary>Creates a cache with an explicit snapshot-time capture port.</summary>
    public TerminalSnapshotCache(Func<TerminalEntry, T> capture, Func<TerminalEntry, Exception, T>? fallback = null)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _fallback = fallback;
    }

    /// <summary>Replaces the cache, removing data from the previous snapshot.</summary>
    public void Accept(IEnumerable<TerminalEntry> entries)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        var values = new Dictionary<ItemKey, T>();
        foreach (var entry in entries)
        {
            if (entry.Amount <= 0) continue;
            T value;
            try { value = _capture(entry); }
            catch (Exception error) when (_fallback != null) { value = _fallback(entry, error); }
            values.Add(entry.Key, value);
        }
        _values = values;
    }

    /// <summary>Reads captured data without invoking the capture port on a cache miss.</summary>
    public T? Get(ItemKey key) => _values.TryGetValue(key, out var value) ? value : null;

    /// <summary>Releases the current snapshot's presentation data.</summary>
    public void Clear() => _values.Clear();
}
