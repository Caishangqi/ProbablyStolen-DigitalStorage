using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

/// <summary>Reads one named number from a stored sample for an active projector.</summary>
internal interface IEntryAttributeCapture
{
    /// <summary>Gets the attribute name a projector asks for.</summary>
    string Name { get; }

    /// <summary>Reads the value; false when it cannot be read, which keeps the entry exact.</summary>
    bool TryRead(GameItem sample, out long value);
}

/// <summary>
/// Reads the attributes an active projector needs, once per stored type until the inventory changes. Nothing is read
/// while no projector asks for attributes.
/// </summary>
internal sealed class EntryAttributeReader
{
    private readonly IReadOnlyList<IEntryAttributeCapture> _captures;
    private readonly Func<ItemKey, GameItem?> _findSample;
    private readonly Action<string> _diagnostic;
    private readonly Dictionary<ItemKey, IReadOnlyDictionary<string, long>> _cache = new();

    internal EntryAttributeReader(IReadOnlyList<IEntryAttributeCapture> captures, Func<ItemKey, GameItem?> findSample,
        Action<string> diagnostic)
    {
        _captures = captures ?? throw new ArgumentNullException(nameof(captures));
        _findSample = findSample ?? throw new ArgumentNullException(nameof(findSample));
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    /// <summary>Forgets read values, for example after a refresh where values may have changed.</summary>
    internal void Clear() => _cache.Clear();

    /// <summary>Reads the requested attributes of every entry.</summary>
    internal EntryAttributes Read(IEnumerable<TerminalEntry> entries, IReadOnlyCollection<string> names)
    {
        if (names.Count == 0) return EntryAttributes.Empty;
        var captures = _captures.Where(capture => names.Contains(capture.Name)).ToArray();
        var values = new Dictionary<ItemKey, IReadOnlyDictionary<string, long>>();
        foreach (var entry in entries)
        {
            if (!_cache.TryGetValue(entry.Key, out var read))
            {
                read = ReadOne(entry.Key, captures);
                // A value that could not be read yet, for example before the store loads, is read again next time.
                if (read.Count == captures.Length) _cache[entry.Key] = read;
            }

            values[entry.Key] = read;
        }

        return new EntryAttributes(values);
    }

    private IReadOnlyDictionary<string, long> ReadOne(ItemKey key, IReadOnlyList<IEntryAttributeCapture> captures)
    {
        var read = new Dictionary<string, long>(StringComparer.Ordinal);
        GameItem? sample;
        try
        {
            sample = _findSample(key);
        }
        catch (Exception error)
        {
            _diagnostic($"entry_attributes failed=sample error={error.GetType().Name}");
            return read;
        }

        if (sample == null) return read;
        foreach (var capture in captures)
        {
            try
            {
                if (capture.TryRead(sample, out var value)) read[capture.Name] = value;
            }
            catch (Exception error)
            {
                _diagnostic($"entry_attributes failed={capture.Name} error={error.GetType().Name}");
            }
        }

        return read;
    }
}
