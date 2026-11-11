using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Common.Contracts.Storage;

/// <summary>Net unit change per type caused by one storage operation; zero entries are dropped.</summary>
/// <remarks>This is the data a later public notification would carry; no event is raised.</remarks>
public sealed class StorageChangeSet
{
    private readonly Dictionary<ItemKey, long> _changes = new();

    /// <summary>Gets the net changes; positive values entered storage.</summary>
    public IReadOnlyDictionary<ItemKey, long> Changes => _changes;

    /// <summary>Gets whether nothing changed.</summary>
    public bool IsEmpty => _changes.Count == 0;

    /// <summary>Gets the net change of a type.</summary>
    public long Get(ItemKey key) => key != null && _changes.TryGetValue(key, out var delta) ? delta : 0;

    /// <summary>Adds a signed unit change.</summary>
    public void Add(ItemKey key, long delta)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        var total = checked(Get(key) + delta);
        if (total == 0) _changes.Remove(key);
        else _changes[key] = total;
    }

    /// <summary>Adds every change of another set.</summary>
    public void AddAll(StorageChangeSet other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        foreach (var change in other._changes) Add(change.Key, change.Value);
    }
}
