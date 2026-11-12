using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class RootDrainSimulatorTests
{
    private static readonly StorageCellTier Tier1K = StorageCellTier.All[0];

    [Test]
    public void Forecast_PlansEveryRootWithoutChangingTheInput()
    {
        var cell = new LegacyCell(Tier1K, 3,
            (0, 0, 0, new BoundingSize(2, 2), "item.a", 1),
            (1, 0, 0, new BoundingSize(2, 2), "item.a", 5),
            (2, 4, 4, new BoundingSize(1, 1), "item.b", 2));
        var input = cell.View;
        var before = input.Pages.Select(page => page.RootCount).ToArray();

        var forecast = RootDrainSimulator.Forecast(input, cell.Snapshot());

        Assert.That(forecast.Accepted, Is.True);
        Assert.That(forecast.AppendedPages, Is.EqualTo(2));
        Assert.That(forecast.Placements.Select(p => (p.Page, p.Slot)), Is.EqualTo(new[] { (3, 0), (3, 1), (4, 0) }));
        Assert.That(input.Pages.Select(page => page.RootCount), Is.EqualTo(before));
        Assert.That(input.Pages.Count, Is.EqualTo(3));
        Assert.That(forecast.After.Pages.Take(3).All(page => page.IsEmpty), Is.True);
        Assert.That(forecast.After.Usage.AreaUnits, Is.EqualTo(input.Usage.AreaUnits), "Relocating keeps usage.");
    }

    [Test]
    public void Forecast_OverCapacityCellsStillMigrate()
    {
        // 9000 1x1 units exceed the 1k capacity of 8160 area units for one type.
        var cell = new LegacyCell(Tier1K, 2, (1, 0, 0, new BoundingSize(1, 1), "item.a", 9000));

        var forecast = RootDrainSimulator.Forecast(cell.View, cell.Snapshot());

        Assert.That(forecast.Accepted, Is.True);
        Assert.That(forecast.OverCapacity, Is.True);
    }

    [Test]
    public void Forecast_RejectsOnlyBeyondTheRestorablePageLimit()
    {
        var roots = Enumerable.Range(0, 16)
            .Select(i => (1, i % 12, i / 12, new BoundingSize(1, 1), $"item.t{i}", 1L)).ToArray();
        var cell = new LegacyCell(Tier1K, 2, roots);
        // Every other page is a full 1x1 strip page, up to the restorable limit.
        var full = cell.View;
        var filled = new CellView(full.Rules, full.TypeLimit,
            full.Pages.Concat(Enumerable.Range(0, LayoutDescriptor.MaxPages - 2)
                .Select(_ => PageView.Strip(StripLayout.PageFor(new BoundingSize(1, 1)), Enumerable.Range(0, 16)))),
            full.Usage, full.Partition);

        var forecast = RootDrainSimulator.Forecast(filled, cell.Snapshot());

        Assert.That(forecast.Accepted, Is.False);
        Assert.That(forecast.Rejection, Is.EqualTo(StoreRejection.PhysicalLimit));
        Assert.That(forecast.RejectedRoot, Is.EqualTo(cell.Snapshot().First()));
    }

    [Test]
    public void Forecast_MatchesARealDrainOfTheSameInput()
    {
        var random = new Random(20261110);
        for (var round = 0; round < 50; round++)
        {
            var roots = new List<(int, int, int, BoundingSize, string, long)>();
            var count = random.Next(1, 60);
            for (var i = 0; i < count; i++)
            {
                var size = new BoundingSize(random.Next(1, 13), random.Next(1, 13));
                roots.Add((random.Next(0, 4), i, random.Next(0, 3), size, $"item.t{random.Next(0, 8)}", random.Next(1, 20)));
            }

            var simulated = new LegacyCell(Tier1K, 4, roots.ToArray());
            var drained = new LegacyCell(Tier1K, 4, roots.ToArray());
            var forecast = RootDrainSimulator.Forecast(simulated.View, simulated.Snapshot());
            var unitsBefore = drained.TotalUnits();

            var result = RootTransfer.Drain(drained, drained, MigrationSource.Instance);

            Assert.That(result.Completed, Is.EqualTo(forecast.Accepted), $"round {round}");
            Assert.That(drained.Placements, Is.EqualTo(forecast.Placements.Select(p => (p.Root, p.Page, p.Slot))),
                $"round {round}");
            Assert.That(drained.TotalUnits(), Is.EqualTo(unitsBefore), $"round {round}");
            Assert.That(drained.View.Pages.Count, Is.EqualTo(forecast.After.Pages.Count), $"round {round}");
        }
    }

    /// <summary>A cell whose roots sit on the anchor and legacy pages; it drains into its own strip pages.</summary>
    private sealed class LegacyCell : IRootSource, IRootSink
    {
        private readonly List<RootSnapshot> _roots = new();

        internal LegacyCell(StorageCellTier tier, int pageCount, params (int Page, int X, int Y, BoundingSize Size, string Id, long Units)[] roots)
        {
            var usage = new CellUsage();
            var counts = new int[pageCount];
            foreach (var (page, x, y, size, id, units) in roots)
            {
                var root = new RootSnapshot(page, x, y, size, new ItemKey(id), units);
                _roots.Add(root);
                usage.Add(root.Key, units, CellUsage.AreaOf(size, units));
                counts[page]++;
            }

            View = new CellView(CellRules.For(tier), 128,
                Enumerable.Range(0, pageCount).Select(page =>
                    PageView.Unslotted(page == 0 ? PageGeometry.Anchor : PageGeometry.Legacy, counts[page])),
                usage, CellPartition.Unfiltered);
        }

        internal CellView View { get; }

        internal List<(RootSnapshot, int, int)> Placements { get; } = new();

        internal long TotalUnits() => _roots.Sum(root => root.Units);

        public bool TryPeekFirst(out RootSnapshot root)
        {
            var first = Snapshot().Take(1).ToList();
            root = first.FirstOrDefault();
            return first.Count == 1;
        }

        public IEnumerable<RootSnapshot> Snapshot()
        {
            var unslotted = _roots.Where(root => View.Pages[root.Page].Kind != PageKind.Strip).ToList();
            unslotted.Sort(RootSnapshot.CompareOrder);
            return unslotted;
        }

        public TransferStep Accept(RootSnapshot root, Actionable mode, IActionSource source)
        {
            var plan = CellStorePlanner.Plan(View, root.ToCandidate(), PlanScope.Relocate);
            if (!plan.IsAccepted) return TransferStep.Rejected(plan.Rejection!.Value);
            if (mode == Actionable.Simulate) return TransferStep.Moved;
            if (plan.NewPage is { } geometry) View.AppendPage(geometry);
            View.RecordRelocated(root.Page, View.SlotAt(root.Page, root.X, root.Y), plan.Page, plan.Slot);
            var (x, y) = StripLayout.SlotOrigin(View.Pages[plan.Page].Geometry, plan.Slot);
            _roots.Remove(root);
            _roots.Add(new RootSnapshot(plan.Page, x, y, root.Size, root.Key, root.Units));
            Placements.Add((root, plan.Page, plan.Slot));
            return TransferStep.Moved;
        }
    }
}
