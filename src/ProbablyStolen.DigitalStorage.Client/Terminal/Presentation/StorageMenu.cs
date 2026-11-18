using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;

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

    /// <summary>Replaces how entries are grouped; the view is rebuilt unless paused.</summary>
    public void SetProjection(Func<IReadOnlyList<TerminalEntry>, IReadOnlyList<ProjectedGroup>> project) =>
        _repository.SetProjection(project);

    /// <summary>Returns the current display groups in view order, placeholders included while paused.</summary>
    public IReadOnlyList<DisplayEntry> GetEntries() => _repository.GetView();

    /// <summary>
    /// Validates a captured page against the current final page and expands selected groups into ordered member steps.
    /// A null result means the captured selection is stale, hidden, duplicated, or absent from the current repository.
    /// </summary>
    /// <param name="shownPage">The page captured when the drag began.</param>
    /// <param name="currentPage">The latest page at release time.</param>
    /// <param name="selectedDisplayIds">The group IDs captured by the selection model.</param>
    /// <param name="currentPresentationRevision">The latest pipeline revision observed by the caller.</param>
    /// <param name="requestedPieces">A single-group request, or null for full groups.</param>
    public TerminalDragBatch? CreateDragBatch(StorageScreenPage shownPage, StorageScreenPage currentPage,
        IEnumerable<string> selectedDisplayIds, long currentPresentationRevision, long? requestedPieces = null)
    {
        if (shownPage == null) throw new ArgumentNullException(nameof(shownPage));
        if (currentPage == null) throw new ArgumentNullException(nameof(currentPage));
        if (selectedDisplayIds == null) throw new ArgumentNullException(nameof(selectedDisplayIds));
        if (!_linkState.IsConnected || shownPage.ViewRevision != currentPage.ViewRevision ||
            shownPage.PresentationRevision != currentPage.PresentationRevision ||
            currentPage.PresentationRevision != currentPresentationRevision)
            return null;

        var selected = selectedDisplayIds.ToArray();
        if (selected.Length == 0 || selected.Any(string.IsNullOrEmpty) ||
            selected.Length != selected.Distinct(StringComparer.Ordinal).Count()) return null;
        if (requestedPieces.HasValue && (selected.Length != 1 || requestedPieces.Value <= 0)) return null;

        if (!TryGetVisibleGroups(shownPage, out var shownGroups) ||
            !TryGetVisibleGroups(currentPage, out var currentGroups)) return null;
        var shownById = shownGroups.ToDictionary(pair => pair.Id, StringComparer.Ordinal);
        var currentById = currentGroups.ToDictionary(pair => pair.Id, StringComparer.Ordinal);
        var repositoryById = GetEntries().Where(group => group != null)
            .GroupBy(group => group.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

        var orderedSelected = currentGroups.Where(group => selected.Contains(group.Id, StringComparer.Ordinal)).ToArray();
        if (orderedSelected.Length != selected.Length) return null;

        var steps = new List<TerminalDragStep>();
        long totalRequested = 0;
        foreach (var currentGroup in orderedSelected)
        {
            if (!shownById.TryGetValue(currentGroup.Id, out var shownGroup) ||
                !SameMembersInOrder(shownGroup, currentGroup) ||
                !repositoryById.TryGetValue(currentGroup.Id, out var repositoryGroup) ||
                repositoryGroup.Total <= 0 || !SameMembersInOrder(currentGroup, repositoryGroup) ||
                currentGroup.Total <= 0) return null;
            if (currentGroup.Members.Any(member => !Equals(GetStackBySerial(member.Serial), member.Key))) return null;

            var request = requestedPieces ?? currentGroup.Total;
            request = Math.Min(request, currentGroup.Total);
            if (request <= 0) return null;
            foreach (var step in GroupExtractionPlan.Steps(currentGroup, request))
            {
                steps.Add(new TerminalDragStep(currentGroup.Id, step.Member.Serial, step.Member.Key, step.Pieces));
                totalRequested = checked(totalRequested + step.Pieces);
            }
        }

        return steps.Count == 0
            ? null
            : new TerminalDragBatch(currentPage.ViewRevision, currentPage.PresentationRevision,
                orderedSelected.Select(group => group.Id).ToArray(), steps, totalRequested);
    }

    /// <summary>
    /// Takes pieces from a shown slot: members in group order, each by its real key, until the request is met or a
    /// member cannot be taken completely. The group must still be shown with the same members; otherwise nothing moves
    /// and the caller refreshes. Moved pieces stay moved when a later member fails.
    /// </summary>
    /// <param name="shown">The group the player clicked, as captured by the shown page.</param>
    /// <param name="requested">The pieces to take.</param>
    /// <param name="extract">Takes up to the given pieces of one member and reports how many moved.</param>
    public TerminalExtractionResult HandleGroupInteraction(DisplayEntry shown, long requested,
        Func<TerminalEntry, long, TerminalExtractionResult> extract)
    {
        if (shown == null) throw new ArgumentNullException(nameof(shown));
        if (extract == null) throw new ArgumentNullException(nameof(extract));
        if (!_linkState.IsConnected)
            return TerminalExtractionResult.Create(TerminalExtractionStatus.Offline, requested, 0,
                _linkState.StatusDescription ?? "terminal_storage_offline");
        if (requested <= 0)
            return TerminalExtractionResult.Create(TerminalExtractionStatus.InvalidAmount, requested, 0,
                "terminal_storage_invalid_amount");

        var current = _repository.GetView().FirstOrDefault(group => group.Id == shown.Id && group.Total > 0);
        if (current == null || !SameMembers(shown, current))
            return NoItems(requested);

        long moved = 0;
        TerminalExtractionResult? last = null;
        foreach (var step in GroupExtractionPlan.Steps(current, requested))
        {
            var member = step.Member;
            if (GetStackBySerial(member.Serial) != member.Key) break;
            last = extract(member, step.Pieces);
            moved = checked(moved + Math.Max(0, last.ExtractedAmount));
            if (last.Status == TerminalExtractionStatus.Failed)
                return TerminalExtractionResult.Create(last.Status, requested, moved, last.MessageKey);
            if (last.ExtractedAmount < step.Pieces) break;
        }

        if (moved >= requested)
            return TerminalExtractionResult.Create(TerminalExtractionStatus.Success, requested, moved, "terminal_storage_extracted");
        if (moved > 0)
            return TerminalExtractionResult.Create(TerminalExtractionStatus.Partial, requested, moved, "terminal_storage_extracted");
        return last ?? NoItems(requested);
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
            spriteWidth: entry.SpriteWidth, spriteHeight: entry.SpriteHeight, searchDocument: entry.Document,
            totalUnits: entry.TotalUnits);

    private static TerminalExtractionResult NoItems(long requested) =>
        TerminalExtractionResult.Create(TerminalExtractionStatus.NoItems, requested, 0, "terminal_storage_no_items");

    private static bool TryGetVisibleGroups(StorageScreenPage page, out IReadOnlyList<DisplayEntry> groups)
    {
        var result = new List<DisplayEntry>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in page.Slots)
        {
            if (slot.Group == null || slot.Group.Total <= 0) continue;
            if (!ids.Add(slot.Group.Id))
            {
                groups = Array.Empty<DisplayEntry>();
                return false;
            }

            result.Add(slot.Group);
        }

        groups = result;
        return true;
    }

    private static bool SameMembersInOrder(DisplayEntry left, DisplayEntry right) =>
        left.Members.Count == right.Members.Count && left.Members.Zip(right.Members,
            (leftMember, rightMember) => leftMember.Serial == rightMember.Serial &&
                Equals(leftMember.Key, rightMember.Key)).All(match => match);

    private static bool SameMembers(DisplayEntry shown, DisplayEntry current) =>
        shown.Members.Select(member => member.Serial).OrderBy(serial => serial)
            .SequenceEqual(current.Members.Select(member => member.Serial).OrderBy(serial => serial));

    private static bool Equivalent(TerminalEntry left, TerminalEntry right) =>
        left.Amount == right.Amount && left.TotalUnits == right.TotalUnits && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal) &&
        string.Equals(left.SpriteKey, right.SpriteKey, StringComparison.Ordinal) &&
        left.SpriteWidth == right.SpriteWidth && left.SpriteHeight == right.SpriteHeight &&
        left.Document.HasSameContent(right.Document);
}
