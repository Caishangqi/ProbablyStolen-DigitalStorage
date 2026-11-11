using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Application.Storage;

/// <summary>
/// All storage of one network. Each root is offered to priority groups from high to low;
/// inside a group, preferred storage (<see cref="IStorage.IsPreferredStorageFor"/>) comes first, then the rest, in
/// mount order. Extraction walks priority groups from low to high, so high-priority drives keep items longest.
/// </summary>
public sealed class NetworkStorage : IStorage
{
    private readonly List<(int Priority, IStorage Storage)> _mounted = new();

    /// <summary>Mounts a storage at a priority; mount order breaks ties.</summary>
    public void Mount(int priority, IStorage inventory)
    {
        if (inventory == null) throw new ArgumentNullException(nameof(inventory));
        _mounted.Add((priority, inventory));
    }

    /// <summary>Gets the mounted storage in insertion order for one type.</summary>
    public IReadOnlyList<IStorage> InsertOrder(ItemKey key, IActionSource source)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        var result = new List<IStorage>();
        foreach (var group in Groups(descending: true))
        {
            var others = new List<IStorage>();
            foreach (var storage in group)
            {
                if (storage.IsPreferredStorageFor(key, source)) result.Add(storage);
                else others.Add(storage);
            }

            result.AddRange(others);
        }

        return result;
    }

    /// <summary>Gets the mounted storage in extraction order: lowest priority first, mount order within.</summary>
    public IReadOnlyList<IStorage> ExtractOrder() => Groups(descending: false).SelectMany(group => group).ToArray();

    /// <inheritdoc />
    public bool IsPreferredStorageFor(ItemKey what, IActionSource source) => false;

    /// <summary>
    /// Inserts roots one at a time; each goes to the first storage in <see cref="InsertOrder"/> that takes it.
    /// Stops at the first root no storage takes, or at a failed native move.
    /// </summary>
    /// <remarks>
    /// A simulation checks each root against the current contents only; it does not account for earlier roots of the
    /// same call. Terminal checks simulate one item at a time.
    /// </remarks>
    public InsertResult Insert(IRootSource roots, Actionable mode, IActionSource source)
    {
        if (roots == null) throw new ArgumentNullException(nameof(roots));
        if (source == null) throw new ArgumentNullException(nameof(source));

        var changes = new StorageChangeSet();
        long inserted = 0;
        var candidates = mode == Actionable.Simulate ? roots.Snapshot() : Remaining(roots);
        foreach (var root in candidates)
        {
            var step = InsertOne(root, roots, mode, source);
            if (!step.IsMoved) return new InsertResult(inserted, step, changes);
            inserted = checked(inserted + root.Units);
            changes.Add(root.Key, root.Units);
        }

        return new InsertResult(inserted, TransferStep.SourceEmpty, changes);
    }

    /// <inheritdoc />
    public ExtractResult Extract(ItemKey what, long amount, IRootSink destination, Actionable mode, IActionSource source)
    {
        if (what == null) throw new ArgumentNullException(nameof(what));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "Extract at least one unit.");
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        if (source == null) throw new ArgumentNullException(nameof(source));

        var changes = new StorageChangeSet();
        long extracted = 0;
        var last = TransferStep.SourceEmpty;
        foreach (var storage in ExtractOrder())
        {
            var result = storage.Extract(what, checked(amount - extracted), destination, mode, source);
            extracted = checked(extracted + result.ExtractedUnits);
            changes.AddAll(result.Changes);
            last = result.StoppedBy;
            if (extracted >= amount) return new ExtractResult(extracted, TransferStep.Moved, changes);
            // A full destination or a failed move ends the whole extraction; an empty storage moves on.
            if (last.Outcome != TransferOutcome.SourceEmpty) break;
        }

        return new ExtractResult(extracted, last, changes);
    }

    /// <inheritdoc />
    public void GetAvailableStacks(KeyCounter output)
    {
        if (output == null) throw new ArgumentNullException(nameof(output));
        foreach (var (_, storage) in _mounted) storage.GetAvailableStacks(output);
    }

    /// <inheritdoc />
    public string GetDescription() => $"network storage ({_mounted.Count} mounted)";

    private static IEnumerable<RootSnapshot> Remaining(IRootSource roots)
    {
        while (roots.TryPeekFirst(out var root)) yield return root;
    }

    private TransferStep InsertOne(RootSnapshot root, IRootSource roots, Actionable mode, IActionSource source)
    {
        StoreRejection? rejection = null;
        foreach (var storage in InsertOrder(root.Key, source))
        {
            var result = storage.Insert(new FirstRootOnly(roots, root), mode, source);
            if (result.InsertedUnits > 0) return TransferStep.Moved;
            var step = result.StoppedBy;
            if (step.Outcome is TransferOutcome.Returned or TransferOutcome.Unowned) return step;
            // Prefer a capacity-style reason over "excluded by partition" when both occur.
            if (step.Rejection is { } reason && (rejection == null || rejection == StoreRejection.PartitionRejected))
                rejection = reason;
        }

        return TransferStep.Rejected(rejection ?? StoreRejection.ReadOnly);
    }

    private IEnumerable<IEnumerable<IStorage>> Groups(bool descending)
    {
        // OrderBy is stable, so mount order survives inside each priority.
        var groups = _mounted.GroupBy(entry => entry.Priority, entry => entry.Storage);
        return descending ? groups.OrderByDescending(group => group.Key) : groups.OrderBy(group => group.Key);
    }

    /// <summary>Offers only one root of the wrapped source, and only while it is still the first.</summary>
    private sealed class FirstRootOnly : IRootSource
    {
        private readonly IRootSource _roots;
        private readonly RootSnapshot _root;

        internal FirstRootOnly(IRootSource roots, RootSnapshot root)
        {
            _roots = roots;
            _root = root;
        }

        public bool TryPeekFirst(out RootSnapshot root)
        {
            root = _root;
            return _roots.Snapshot().Any(candidate => candidate.Equals(_root));
        }

        public IEnumerable<RootSnapshot> Snapshot()
        {
            if (TryPeekFirst(out var root)) yield return root;
        }
    }
}
