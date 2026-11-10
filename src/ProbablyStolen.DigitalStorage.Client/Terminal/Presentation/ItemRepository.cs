using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>Maintains the client-side view of serial-addressed terminal inventory updates.</summary>
public sealed class ItemRepository : IItemRepository
{
    private readonly Dictionary<long, InventoryEntryUpdate> _entries = new();
    private readonly List<InventoryEntryUpdate> _view = new();
    private bool _paused;
    private TerminalSortOrder _sortOrder = TerminalSortOrder.Name;
    private TerminalSortDirection _sortDirection = TerminalSortDirection.Ascending;

    /// <summary>Applies a complete snapshot or an incremental batch from the menu.</summary>
    public void HandleUpdate(bool fullUpdate, IReadOnlyList<InventoryEntryUpdate> entries)
    {
        if (fullUpdate) Clear();
        foreach (var update in entries) HandleUpdate(update);
        UpdateView();
    }

    private void HandleUpdate(InventoryEntryUpdate update)
    {
        if (!_entries.TryGetValue(update.Serial, out var current))
        {
            if (update.What != null && update.IsMeaningful()) _entries[update.Serial] = update;
            return;
        }

        if (!update.IsMeaningful())
        {
            _entries.Remove(update.Serial);
            return;
        }

        _entries[update.Serial] = update.What == null
            ? new InventoryEntryUpdate(current.Serial, current.What, update.StoredAmount, update.RequestableAmount,
                update.Craftable, current.DisplayName, current.SpriteKey, current.SpriteWidth, current.SpriteHeight, current.SearchDocument)
            : update;
    }

    /// <summary>Rebuilds the ordered view, preserving visible positions while paused.</summary>
    public void UpdateView()
    {
        if (_paused)
        {
            var serials = new HashSet<long>();
            for (var index = 0; index < _view.Count; index++)
            {
                var shown = _view[index];
                if (_entries.TryGetValue(shown.Serial, out var current)) _view[index] = current;
                else _view[index] = new InventoryEntryUpdate(shown.Serial, shown.What, 0, 0, false,
                    shown.DisplayName, shown.SpriteKey, shown.SpriteWidth, shown.SpriteHeight, shown.SearchDocument);
                serials.Add(shown.Serial);
            }
            foreach (var entry in Sort(_entries.Values.Where(entry => !serials.Contains(entry.Serial))))
            {
                var emptyIndex = _view.FindIndex(shown => !shown.IsMeaningful() && Equals(shown.What, entry.What));
                if (emptyIndex >= 0) _view[emptyIndex] = entry;
                else _view.Add(entry);
            }
            return;
        }

        _view.Clear();
        _view.AddRange(Sort(_entries.Values.Where(entry => entry.IsMeaningful())));
    }

    private IOrderedEnumerable<InventoryEntryUpdate> Sort(IEnumerable<InventoryEntryUpdate> entries)
    {
        var descending = _sortDirection == TerminalSortDirection.Descending;
        var ordered = _sortOrder == TerminalSortOrder.Amount
            ? descending ? entries.OrderByDescending(entry => entry.StoredAmount) : entries.OrderBy(entry => entry.StoredAmount)
            : descending
                ? entries.OrderByDescending(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                : entries.OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase);
        // Ties fall back to a fixed name then key order so equal amounts never swap between refreshes.
        return ordered.ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.What);
    }

    /// <summary>Changes the ordering; while paused the visible positions stay until resorting resumes.</summary>
    public void SetSort(TerminalSortOrder order, TerminalSortDirection direction)
    {
        if (_sortOrder == order && _sortDirection == direction) return;
        _sortOrder = order;
        _sortDirection = direction;
        if (!_paused) UpdateView();
    }

    /// <summary>Removes all current entries and visible positions.</summary>
    public void Clear()
    {
        _entries.Clear();
        _view.Clear();
    }

    /// <summary>Pauses resorting until resumed, retaining current visible serial positions.</summary>
    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;
        if (!paused) UpdateView();
    }

    /// <summary>Returns the current meaningful entries keyed by their stable serials.</summary>
    public IReadOnlyCollection<InventoryEntryUpdate> GetAllEntries() => _entries.Values.ToArray();

    /// <summary>Returns the visible ordering, including empty placeholders while paused.</summary>
    public IReadOnlyList<InventoryEntryUpdate> GetView() => _view.ToArray();
}
