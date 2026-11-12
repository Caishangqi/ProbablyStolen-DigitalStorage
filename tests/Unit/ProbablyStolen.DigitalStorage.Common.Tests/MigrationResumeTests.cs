using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Ports;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

/// <summary>
/// The migration drain as Platform <c>LegacyMigration</c> wires it (legacy-page source, <see cref="CellRootSink"/>
/// in <see cref="PlanScope.Relocate"/>), over an in-memory cell whose native move can fail at step k.
/// </summary>
[TestFixture]
public sealed class MigrationResumeTests
{
    [TestCase(1)]
    [TestCase(3)]
    [TestCase(7)]
    public void FailureAtStepK_LeavesItemsInPlaceAndARerunFinishesWithoutDuplicates(int failingMove)
    {
        var cell = MemoryLegacyCell.Create(9, seed: failingMove);
        var before = cell.Inventory();
        cell.FailMoveOnCall = failingMove;

        var first = RootTransfer.Drain(cell, new CellRootSink(cell.ReadView(), cell, PlanScope.Relocate), MigrationSource.Instance);

        Assert.That(first.LastStep.Outcome, Is.EqualTo(TransferOutcome.Returned));
        Assert.That(first.MovedRoots, Is.EqualTo(failingMove - 1));
        Assert.That(cell.Inventory(), Is.EquivalentTo(before), "Every root is somewhere, exactly once.");
        Assert.That(cell.PagesMatchLayout(), Is.True);

        // Next load: the view is read again from the actual pages and the drain continues from there.
        var second = RootTransfer.Drain(cell, new CellRootSink(cell.ReadView(), cell, PlanScope.Relocate), MigrationSource.Instance);

        Assert.That(second.Completed, Is.True);
        Assert.That(first.MovedRoots + second.MovedRoots, Is.EqualTo(9));
        Assert.That(cell.Inventory(), Is.EquivalentTo(before));
        Assert.That(cell.Roots.All(root => cell.Pages[root.Page].Kind == PageKind.Strip), Is.True);
        Assert.That(cell.PagesMatchLayout(), Is.True);
    }

    [Test]
    public void OverCapacityCell_StillCompletes()
    {
        var cell = MemoryLegacyCell.Create(4, seed: 1, units: 5000);

        var view = cell.ReadView();
        Assert.That(view.UsedBytes(), Is.GreaterThan(view.Rules.Tier.GetBytes()));
        var result = RootTransfer.Drain(cell, new CellRootSink(view, cell, PlanScope.Relocate), MigrationSource.Instance);

        Assert.That(result.Completed, Is.True);
        Assert.That(cell.Roots.Sum(root => root.Units), Is.EqualTo(20000));
    }

    /// <summary>A cell with an anchor and legacy pages; it is its own legacy-page source and its own page port.</summary>
    private sealed class MemoryLegacyCell : IRootSource, ICellPagesPort
    {
        private readonly List<RootSnapshot> _roots = new();
        private int _moves;

        private MemoryLegacyCell()
        {
        }

        internal List<PageGeometry> Pages { get; } = new() { PageGeometry.Anchor, PageGeometry.Legacy, PageGeometry.Legacy };
        internal int FailMoveOnCall { get; set; } = -1;
        internal IEnumerable<RootSnapshot> Roots => _roots;

        internal static MemoryLegacyCell Create(int count, int seed, long units = 1)
        {
            var random = new Random(seed);
            var cell = new MemoryLegacyCell();
            for (var i = 0; i < count; i++)
            {
                var size = new BoundingSize(random.Next(1, 4), random.Next(1, 4));
                cell._roots.Add(new RootSnapshot(i % 3, i, 0, size, new ItemKey($"item.t{random.Next(0, 3)}"), units));
            }

            return cell;
        }

        internal CellView ReadView()
        {
            var usage = new CellUsage();
            foreach (var root in _roots) usage.Add(root.Key, root.Units, CellUsage.AreaOf(root.Size, root.Units));
            var pages = Pages.Select((geometry, index) => geometry.Kind == PageKind.Strip
                ? PageView.Strip(geometry, _roots.Where(root => root.Page == index)
                    .Select(root => StripLayout.SlotIndexAt(geometry, root.X, root.Y)))
                : PageView.Unslotted(geometry, _roots.Count(root => root.Page == index)));
            return new CellView(CellRules.For(StorageCellTier.All[0]), 128, pages, usage, CellPartition.Unfiltered);
        }

        internal IEnumerable<(string, long, BoundingSize)> Inventory() =>
            _roots.Select(root => (root.Key.Identifier, root.Units, root.Size));

        internal bool PagesMatchLayout() => _roots.All(root => root.Page < Pages.Count &&
            (Pages[root.Page].Kind != PageKind.Strip || StripLayout.SlotIndexAt(Pages[root.Page], root.X, root.Y) >= 0));

        public bool TryPeekFirst(out RootSnapshot root)
        {
            var first = Snapshot().Take(1).ToList();
            root = first.FirstOrDefault();
            return first.Count == 1;
        }

        public IEnumerable<RootSnapshot> Snapshot()
        {
            var legacy = _roots.Where(root => Pages[root.Page].Kind != PageKind.Strip)
                .Select(root => new RootSnapshot(root.Page, root.X, root.Y, root.Size, root.Key, root.Units, this)).ToList();
            legacy.Sort(RootSnapshot.CompareOrder);
            return legacy;
        }

        public bool TryAppendStripPage(PageGeometry page)
        {
            Pages.Add(page);
            return true;
        }

        public TransferStep MoveInto(RootSnapshot root, int page, int slot)
        {
            if (++_moves == FailMoveOnCall) return TransferStep.Returned;
            var index = _roots.FindIndex(candidate => (candidate.Page, candidate.X, candidate.Y) == (root.Page, root.X, root.Y));
            var (x, y) = StripLayout.SlotOrigin(Pages[page], slot);
            _roots[index] = new RootSnapshot(page, x, y, root.Size, root.Key, root.Units);
            return TransferStep.Moved;
        }

        public IRootSource StoredRoots(ItemKey key) => throw new NotSupportedException();
    }
}
