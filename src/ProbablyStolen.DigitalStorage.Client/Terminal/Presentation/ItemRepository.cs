using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>
/// The client-side display snapshot of one terminal: exact entries by serial, grouped by the current projection and
/// sorted by group. Members inside a group follow the same sort, so the first member is the piece a click takes.
/// While paused (Shift held), shown groups keep their slots and groups that disappeared leave empty placeholders.
/// </summary>
public sealed class ItemRepository : IItemRepository
{
    private readonly Dictionary<long, InventoryEntryUpdate> _entries = new();
    private readonly List<DisplayEntry> _view = new();
    private Func<IReadOnlyList<TerminalEntry>, IReadOnlyList<ProjectedGroup>> _project = IdentityProjection.Project;
    private bool _paused;
    private TerminalSortOrder _sortOrder = TerminalSortOrder.Name;
    private TerminalSortDirection _sortDirection = TerminalSortDirection.Ascending;

    /// <inheritdoc />
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
                update.Craftable, current.DisplayName, current.SpriteKey, current.SpriteWidth, current.SpriteHeight,
                current.SearchDocument, update.TotalUnits)
            : update;
    }

    /// <summary>
    /// Replaces the projection and rebuilds the view, even while paused: groups of another projection have other
    /// identifiers, so kept positions would only leave empty duplicates.
    /// </summary>
    public void SetProjection(Func<IReadOnlyList<TerminalEntry>, IReadOnlyList<ProjectedGroup>> project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _view.Clear();
        _view.AddRange(Sort(BuildGroups()));
    }

    /// <summary>Rebuilds the grouped, sorted view; while paused, shown groups keep their positions.</summary>
    public void UpdateView()
    {
        var groups = BuildGroups();
        if (_paused)
        {
            var current = groups.ToDictionary(group => group.Id, StringComparer.Ordinal);
            var shownIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < _view.Count; index++)
            {
                var shown = _view[index];
                _view[index] = current.TryGetValue(shown.Id, out var group) ? group : Placeholder(shown);
                shownIds.Add(shown.Id);
            }

            // A group that comes back keeps its identifier, so it already took its old slot above.
            _view.AddRange(Sort(groups.Where(group => !shownIds.Contains(group.Id))));

            return;
        }

        _view.Clear();
        _view.AddRange(Sort(groups));
    }

    /// <inheritdoc cref="SetSort" />
    public void SetSort(TerminalSortOrder order, TerminalSortDirection direction)
    {
        if (_sortOrder == order && _sortDirection == direction) return;
        _sortOrder = order;
        _sortDirection = direction;
        if (!_paused) UpdateView();
    }

    /// <summary>Removes every entry and the view.</summary>
    public void Clear()
    {
        _entries.Clear();
        _view.Clear();
    }

    /// <summary>Pauses or resumes resorting; resuming rebuilds the view.</summary>
    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;
        if (!paused) UpdateView();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<InventoryEntryUpdate> GetAllEntries() => _entries.Values.ToArray();

    /// <summary>Gets the groups in view order, placeholders included while paused.</summary>
    public IReadOnlyList<DisplayEntry> GetView() => _view.ToArray();

    private IReadOnlyList<DisplayEntry> BuildGroups()
    {
        var entries = _entries.Values.Where(entry => entry.What != null && entry.IsMeaningful())
            .Select(entry => entry.ToTerminalEntry()).ToArray();
        var groups = _project(entries);
        return groups.Select(group => new DisplayEntry(group.Id, OrderMembers(group.Members))).ToArray();
    }

    private IEnumerable<TerminalEntry> OrderMembers(IEnumerable<TerminalEntry> members)
    {
        var descending = _sortDirection == TerminalSortDirection.Descending;
        var ordered = _sortOrder == TerminalSortOrder.Amount
            ? descending ? members.OrderByDescending(member => member.Amount) : members.OrderBy(member => member.Amount)
            : descending
                ? members.OrderByDescending(member => member.DisplayName, StringComparer.OrdinalIgnoreCase)
                : members.OrderBy(member => member.DisplayName, StringComparer.OrdinalIgnoreCase);
        // Ties follow identity order, so the first member stays the same between refreshes.
        return ordered.ThenBy(member => member.GetIdentitySortKey(), StringComparer.Ordinal);
    }

    private IEnumerable<DisplayEntry> Sort(IEnumerable<DisplayEntry> groups)
    {
        var descending = _sortDirection == TerminalSortDirection.Descending;
        var ordered = _sortOrder == TerminalSortOrder.Amount
            ? descending ? groups.OrderByDescending(group => group.Total) : groups.OrderBy(group => group.Total)
            : descending
                ? groups.OrderByDescending(group => group.Representative.DisplayName, StringComparer.OrdinalIgnoreCase)
                : groups.OrderBy(group => group.Representative.DisplayName, StringComparer.OrdinalIgnoreCase);
        // Ties fall back to a fixed name then key order so equal amounts never swap between refreshes.
        return ordered.ThenBy(group => group.Representative.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Representative.Key).ThenBy(group => group.Id, StringComparer.Ordinal);
    }

    private static DisplayEntry Placeholder(DisplayEntry shown) =>
        shown.Total == 0 ? shown : new DisplayEntry(shown.Id, new[] { shown.Representative.WithAmount(0) });
}
