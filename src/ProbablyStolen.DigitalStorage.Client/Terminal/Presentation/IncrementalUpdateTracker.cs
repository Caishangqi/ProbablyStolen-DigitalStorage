using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>Assigns stable client serials and collects key changes for one inventory update batch.</summary>
public sealed class IncrementalUpdateTracker
{
    private readonly Dictionary<ItemKey, long> _serials = new();
    private readonly Dictionary<long, ItemKey> _keys = new();
    private readonly HashSet<ItemKey> _changes = new();
    private long _serial;
    private bool _fullUpdate = true;

    /// <summary>Finds the stable serial assigned to a key, if it has one.</summary>
    public long? GetSerial(ItemKey key) => _serials.TryGetValue(key, out var serial) ? serial : null;

    /// <summary>Gets or assigns the serial used by the client repository for a key.</summary>
    public long GetOrAssignSerial(ItemKey key)
    {
        if (_serials.TryGetValue(key, out var serial)) return serial;
        serial = checked(++_serial);
        _serials.Add(key, serial);
        _keys.Add(serial, key);
        return serial;
    }

    /// <summary>Finds the key represented by a serial, if it remains assigned.</summary>
    public ItemKey? GetBySerial(long serial) => _keys.TryGetValue(serial, out var key) ? key : null;

    /// <summary>Clears pending key changes and requests a full client update.</summary>
    public void Clear()
    {
        _changes.Clear();
        _fullUpdate = true;
    }

    /// <summary>Clears pending changes and all key-to-serial assignments.</summary>
    public void Reset()
    {
        Clear();
        _serials.Clear();
        _keys.Clear();
        _serial = 0;
    }

    /// <summary>Marks a key as changed in the pending update batch.</summary>
    public void AddChange(ItemKey key)
    {
        _changes.Remove(key);
        _changes.Add(key);
    }

    /// <summary>Removes a key's serial assignment after its removal update is committed.</summary>
    public void RemoveSerial(ItemKey key)
    {
        if (!_serials.Remove(key, out var serial)) return;
        _keys.Remove(serial);
    }

    /// <summary>Commits the current change batch and clears the full-update flag.</summary>
    public void CommitChanges()
    {
        _changes.Clear();
        _fullUpdate = false;
    }

    /// <summary>Returns whether a full update or at least one key change is pending.</summary>
    public bool HasChanges() => _fullUpdate || _changes.Count != 0;

    /// <summary>Returns whether the pending batch replaces the full client repository.</summary>
    public bool IsFullUpdate() => _fullUpdate;

    /// <summary>Returns a snapshot of keys changed in the pending batch.</summary>
    public IReadOnlyList<ItemKey> ChangedKeys() => _changes.ToArray();
}
