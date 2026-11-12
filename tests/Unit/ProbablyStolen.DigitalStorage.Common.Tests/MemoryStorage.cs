using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Ports;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

/// <summary>A container of roots in memory; it is both a source and a destination.</summary>
internal sealed class MemoryInventory : IRootSource, IRootSink
{
    private readonly List<RootSnapshot> _roots = new();
    private int _next;

    internal int Capacity { get; set; } = int.MaxValue;

    internal IReadOnlyList<RootSnapshot> Roots => _roots;

    internal MemoryInventory With(BoundingSize size, string id, long units)
    {
        _roots.Add(new RootSnapshot(0, _next++, 0, size, new ItemKey(id), units, this));
        return this;
    }

    internal long Units(string id) => _roots.Where(root => root.Key.Identifier == id).Sum(root => root.Units);

    internal void Remove(RootSnapshot root) => _roots.Remove(_roots.First(candidate => candidate.Equals(root)));

    public bool TryPeekFirst(out RootSnapshot root)
    {
        var first = Snapshot().Take(1).ToList();
        root = first.FirstOrDefault();
        return first.Count == 1;
    }

    public IEnumerable<RootSnapshot> Snapshot()
    {
        var ordered = _roots.ToList();
        ordered.Sort(RootSnapshot.CompareOrder);
        return ordered;
    }

    public TransferStep Accept(RootSnapshot root, Actionable mode, IActionSource source)
    {
        if (_roots.Count >= Capacity) return TransferStep.Rejected(StoreRejection.PhysicalLimit);
        if (mode == Actionable.Simulate) return TransferStep.Moved;
        Detach(root);
        _roots.Add(new RootSnapshot(0, _next++, 0, root.Size, root.Key, root.Units, this));
        return TransferStep.Moved;
    }

    internal static void Detach(RootSnapshot root)
    {
        switch (root.Origin)
        {
            case MemoryInventory inventory:
                inventory.Remove(root);
                break;
            case MemoryCellPages.StoredSource stored:
                stored.Owner.Remove(root);
                break;
            default:
                throw new InvalidOperationException("Unknown root origin.");
        }
    }
}

/// <summary>An <see cref="ICellPagesPort"/> whose strip roots live in a list in memory.</summary>
internal sealed class MemoryCellPages : ICellPagesPort
{
    private readonly List<RootSnapshot> _roots = new();
    private readonly List<PageGeometry> _pages = new() { PageGeometry.Anchor };

    internal int FailMoveOnCall { get; set; } = -1;
    internal bool RefuseAppend { get; set; }
    internal int MoveCalls { get; private set; }
    internal IReadOnlyList<PageGeometry> Pages => _pages;
    internal IReadOnlyList<RootSnapshot> Roots => _roots;

    internal static CellView EmptyView(StorageCellTier tier, int typeLimit = 128, CellPartition? partition = null,
        ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades.IUpgradeInventory? upgrades = null) =>
        new(CellRules.For(tier), typeLimit, new[] { PageView.Unslotted(PageGeometry.Anchor, 0) }, new CellUsage(),
            partition ?? CellPartition.Unfiltered, upgrades: upgrades);

    internal void Remove(RootSnapshot root) =>
        _roots.Remove(_roots.First(candidate => (candidate.Page, candidate.X, candidate.Y) == (root.Page, root.X, root.Y)));

    public bool TryAppendStripPage(PageGeometry page)
    {
        if (RefuseAppend) return false;
        _pages.Add(page);
        return true;
    }

    public TransferStep MoveInto(RootSnapshot root, int page, int slot)
    {
        if (++MoveCalls == FailMoveOnCall) return TransferStep.Returned;
        MemoryInventory.Detach(root);
        var (x, y) = StripLayout.SlotOrigin(_pages[page], slot);
        _roots.Add(new RootSnapshot(page, x, y, root.Size, root.Key, root.Units));
        return TransferStep.Moved;
    }

    public IRootSource StoredRoots(ItemKey key) => new StoredSource(this, key);

    internal sealed class StoredSource : IRootSource
    {
        private readonly ItemKey _key;

        internal StoredSource(MemoryCellPages owner, ItemKey key)
        {
            Owner = owner;
            _key = key;
        }

        internal MemoryCellPages Owner { get; }

        public bool TryPeekFirst(out RootSnapshot root)
        {
            var first = Snapshot().Take(1).ToList();
            root = first.FirstOrDefault();
            return first.Count == 1;
        }

        public IEnumerable<RootSnapshot> Snapshot()
        {
            var ordered = Owner._roots.Where(root => root.Key.Equals(_key))
                .Select(root => new RootSnapshot(root.Page, root.X, root.Y, root.Size, root.Key, root.Units, this))
                .ToList();
            ordered.Sort(RootSnapshot.CompareOrder);
            return ordered;
        }
    }
}
