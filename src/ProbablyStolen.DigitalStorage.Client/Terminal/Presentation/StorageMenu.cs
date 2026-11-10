using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>Maps current local grid snapshots to the terminal link and incremental repository models.</summary>
public sealed class StorageMenu
{
    private readonly IncrementalUpdateTracker _updateTracker = new();
    private readonly ItemRepository _repository = new();
    private Dictionary<ItemKey, TerminalEntry> _previous = new();
    private LinkState _linkState = LinkState.Disconnected();
    private bool _hasSnapshot;
    private bool _paused;

    /// <summary>Refreshes link state and sends only changed keys to the client repository model.</summary>
    public void BroadcastChanges(TerminalInventorySnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        var nextLinkState = snapshot.Online ? LinkState.Connected() :
            LinkState.Disconnected(snapshot.StorageUnavailable ? "terminal_storage_unavailable" : "terminal_storage_offline");
        var linkChanged = !Equals(_linkState, nextLinkState);
        var fullUpdate = !_hasSnapshot || linkChanged;
        if (fullUpdate) _updateTracker.Clear();

        var current = snapshot.Online
            ? snapshot.Entries.ToDictionary(entry => entry.Key)
            : new Dictionary<ItemKey, TerminalEntry>();
        var updates = new List<InventoryEntryUpdate>();
        var removedKeys = new List<ItemKey>();

        if (fullUpdate)
        {
            foreach (var previous in _previous.Keys)
            {
                if (!current.ContainsKey(previous)) removedKeys.Add(previous);
            }
            foreach (var entry in current.Values)
            {
                var key = entry.Key;
                var serial = _updateTracker.GetOrAssignSerial(key);
                _updateTracker.AddChange(key);
                updates.Add(CreateEntry(serial, entry));
            }
        }
        else
        {
            foreach (var previous in _previous)
            {
                if (current.ContainsKey(previous.Key)) continue;
                var serial = _updateTracker.GetSerial(previous.Key);
                if (!serial.HasValue) continue;
                _updateTracker.AddChange(previous.Key);
                updates.Add(new InventoryEntryUpdate(serial.Value, null, 0));
                removedKeys.Add(previous.Key);
            }

            foreach (var entry in current.Values)
            {
                var key = entry.Key;
                if (_previous.TryGetValue(key, out var old) && Equivalent(old, entry)) continue;
                var serial = _updateTracker.GetOrAssignSerial(key);
                _updateTracker.AddChange(key);
                updates.Add(CreateEntry(serial, entry));
            }
        }

        if (_updateTracker.HasChanges())
        {
            _repository.HandleUpdate(fullUpdate || _updateTracker.IsFullUpdate(), updates);
            _updateTracker.CommitChanges();
            foreach (var key in removedKeys) _updateTracker.RemoveSerial(key);
        }

        _previous = current;
        _linkState = nextLinkState;
        _hasSnapshot = true;
    }

    /// <summary>Resets transient client repository state when the native terminal window closes.</summary>
    public void Reset()
    {
        _updateTracker.Reset();
        _repository.Clear();
        _repository.SetPaused(false);
        _previous.Clear();
        _linkState = LinkState.Disconnected();
        _hasSnapshot = false;
        _paused = false;
    }

    /// <summary>Finds the stable serial currently assigned to a key.</summary>
    public long? GetSerial(ItemKey key) => _updateTracker.GetSerial(key);

    /// <summary>Finds the current key represented by a terminal entry serial.</summary>
    public ItemKey? GetStackBySerial(long serial) => _updateTracker.GetBySerial(serial);

    /// <summary>Returns the current link state. Operation handlers still revalidate the live grid.</summary>
    public LinkState GetLinkState() => _linkState;

    /// <summary>Returns the client-side repository that holds display snapshots only.</summary>
    public IItemRepository GetRepository() => _repository;

    /// <summary>Pauses view resorting while the user holds Shift, preserving item positions.</summary>
    public bool SetPaused(bool paused)
    {
        var changed = _paused != paused;
        _paused = paused;
        _repository.SetPaused(paused);
        return changed;
    }

    /// <summary>Changes the repository ordering used by <see cref="GetEntries"/>.</summary>
    public void SetSort(TerminalSortOrder order, TerminalSortDirection direction) => _repository.SetSort(order, direction);

    /// <summary>Returns the current item entries in repository view order.</summary>
    public IReadOnlyList<TerminalEntry> GetEntries() => _repository.GetView()
        .Where(entry => entry.What is not null)
        .Select(entry => entry.ToTerminalEntry())
        .ToArray();

    /// <summary>Routes an item action through the key currently assigned to a visible repository serial.</summary>
    public TerminalExtractionResult HandleInteraction(long serial, long amount,
        Func<TerminalEntry, long, TerminalExtractionResult> operation)
    {
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        if (!_linkState.IsConnected)
            return TerminalExtractionResult.Create(TerminalExtractionStatus.Offline, amount, 0,
                _linkState.StatusDescription ?? "terminal_storage_offline");

        if (amount <= 0)
            return TerminalExtractionResult.Create(TerminalExtractionStatus.InvalidAmount, amount, 0,
                "terminal_storage_invalid_amount");

        var key = GetStackBySerial(serial);
        var current = _repository.GetAllEntries().FirstOrDefault(entry => entry.Serial == serial);
        if (key == null || current == null || current.What != key || !current.IsMeaningful())
            return TerminalExtractionResult.Create(TerminalExtractionStatus.NoItems, amount, 0,
                "terminal_storage_no_items");

        return operation(current.ToTerminalEntry(), amount);
    }

    /// <summary>Routes an insertion action while connected; the operation rechecks the current grid.</summary>
    public TerminalInsertionResult HandleInsertion(Func<TerminalInsertionResult> operation)
    {
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        return _linkState.IsConnected
            ? operation()
            : TerminalInsertionResult.Create(TerminalInsertionStatus.Offline, 0,
                _linkState.StatusDescription ?? "terminal_storage_offline");
    }

    private static InventoryEntryUpdate CreateEntry(long serial, TerminalEntry entry) =>
        new(serial, entry.Key, entry.Amount, displayName: entry.DisplayName, spriteKey: entry.SpriteKey,
            spriteWidth: entry.SpriteWidth, spriteHeight: entry.SpriteHeight, searchDocument: entry.Document);

    private static bool Equivalent(TerminalEntry left, TerminalEntry right) =>
        left.Amount == right.Amount && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal) &&
        string.Equals(left.SpriteKey, right.SpriteKey, StringComparison.Ordinal) &&
        left.SpriteWidth == right.SpriteWidth && left.SpriteHeight == right.SpriteHeight &&
        left.Document.HasSameContent(right.Document);
}
