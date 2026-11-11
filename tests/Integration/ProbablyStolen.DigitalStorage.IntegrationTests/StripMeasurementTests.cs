using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Ports;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

/// <summary>
/// Synthetic strip-layout measurements. They only print data and
/// never fail on a value. N = 16 runs through the real Common planner and inventory; N = 4, 8, 32 and the old 12×12
/// chains are computed from the same root lists for comparison. No game code or save is involved.
/// </summary>
[TestFixture]
[Category("Measurement")]
public sealed class StripMeasurementTests
{
    // Item sprite sizes from the game asset export (1177 sprites, 16 px per grid cell).
    private static readonly (int W, int H, int Weight)[] Distribution =
    {
        (1, 2, 207), (2, 2, 172), (1, 1, 112), (3, 3, 94), (2, 3, 69), (3, 2, 62), (1, 4, 59), (2, 1, 52),
        (3, 4, 48), (4, 4, 35), (1, 3, 34), (2, 4, 31), (4, 3, 28), (3, 1, 25), (4, 2, 19), (4, 1, 17),
        (5, 4, 14), (6, 4, 14), (4, 7, 14), (5, 3, 6), (4, 5, 6), (6, 9, 5), (5, 5, 4), (2, 5, 4), (5, 1, 4),
        (7, 6, 4), (5, 2, 4), (6, 2, 3), (7, 3, 3), (12, 2, 3), (9, 2, 2), (10, 5, 2), (10, 2, 2), (8, 2, 2)
    };

    private static readonly IActionSource Source = new PlayerSource("measurement");

    [Test]
    public void Measure()
    {
        var report = new StringBuilder();
        report.AppendLine("workload | roots | types | layout | pages | allocated cells | used area | unused slot area | fill % | plan ms");

        Report(report, "1 small", Repeat(3000, 1, 1, "s.a").Concat(Repeat(1500, 1, 2, "s.b")).ToList());
        Report(report, "2 large", new[] { (6, 9), (7, 6), (10, 5), (12, 2) }
            .SelectMany((size, i) => Repeat(8, size.Item1, size.Item2, $"l.{i}")).ToList());
        Report(report, "3 mixed", Mixed(2000, 40, seed: 3));
        Report(report, "4a 127 types", Mixed(127, 127, seed: 4, distinctTypes: true));
        Report(report, "4b 128 types", Mixed(128, 128, seed: 4, distinctTypes: true));
        Report(report, "6 widest", Enumerable.Range(0, 64)
            .Select(i => (10 + i % 3, 1 + i % 5, $"w.{i % 6}")).ToList());

        report.AppendLine();
        report.AppendLine(TypeLimit());
        report.AppendLine(Churn());
        report.AppendLine(Legacy());
        report.AppendLine(DrainTiming(1000));
        TestContext.Out.WriteLine(report.ToString());
        Assert.Pass();
    }

    private static IEnumerable<(int W, int H, string Id)> Repeat(int count, int w, int h, string id) =>
        Enumerable.Repeat((w, h, id), count);

    /// <summary>
    /// Roots drawn from the sprite size distribution. Each type has one size, as real items do; with
    /// <paramref name="distinctTypes"/> every root is its own type.
    /// </summary>
    private static List<(int W, int H, string Id)> Mixed(int count, int types, int seed, bool distinctTypes = false)
    {
        var random = new Random(seed);
        var total = Distribution.Sum(entry => entry.Weight);
        (int W, int H) Draw()
        {
            var pick = random.Next(total);
            var entry = Distribution.First(candidate => (pick -= candidate.Weight) < 0);
            return (entry.W, entry.H);
        }

        var catalog = Enumerable.Range(0, types).Select(_ => Draw()).ToArray();
        var roots = new List<(int, int, string)>();
        for (var i = 0; i < count; i++)
        {
            var type = distinctTypes ? i % types : random.Next(types);
            roots.Add((catalog[type].W, catalog[type].H, $"m.{seed}.{type}"));
        }

        return roots;
    }

    private static void Report(StringBuilder report, string workload, List<(int W, int H, string Id)> roots)
    {
        var types = roots.Select(root => root.Id).Distinct().Count();
        var used = roots.Sum(root => (long)root.W * root.H);
        foreach (var n in new[] { 4, 8, 16, 32 })
        {
            var (pages, allocated) = Strip(roots, n);
            var ms = n == 16 ? RunStrip16(roots, pages) : double.NaN;
            Row(report, workload, roots.Count, types, $"N={n}", pages, allocated, used, ms);
        }

        var (oldPages, oldAllocated) = Square(roots);
        Row(report, workload, roots.Count, types, "12x12", oldPages, oldAllocated, used, double.NaN);
    }

    private static void Row(StringBuilder report, string workload, int roots, int types, string layout, int pages,
        long allocated, long used, double ms) =>
        report.AppendLine(string.Join(" | ", workload, roots, types, layout, pages, allocated, used, allocated - used,
            (100.0 * used / allocated).ToString("0.0", CultureInfo.InvariantCulture),
            double.IsNaN(ms) ? "-" : ms.ToString("0.0", CultureInfo.InvariantCulture)));

    // Strip pages plus the empty 12×12 anchor.
    private static (int Pages, long Allocated) Strip(IEnumerable<(int W, int H, string Id)> roots, int n)
    {
        var pages = 1;
        long allocated = 144;
        foreach (var group in roots.GroupBy(root => (root.W, root.H)))
        {
            var count = (group.Count() + n - 1) / n;
            pages += count;
            allocated += (long)count * group.Key.W * n * group.Key.H;
        }

        return (pages, allocated);
    }

    // The schema 1 layout: one size per 12×12 page, floor(12/w)·floor(12/h) slots.
    private static (int Pages, long Allocated) Square(IEnumerable<(int W, int H, string Id)> roots)
    {
        var pages = 0;
        foreach (var group in roots.GroupBy(root => (root.W, root.H)))
        {
            var slots = 12 / group.Key.W * (12 / group.Key.H);
            pages += (group.Count() + slots - 1) / slots;
        }

        return (Math.Max(1, pages), Math.Max(1, pages) * 144L);
    }

    /// <summary>Stores every root through BasicCellInventory with N = 16 and returns the elapsed planning time.</summary>
    private static double RunStrip16(List<(int W, int H, string Id)> roots, int expectedPages)
    {
        var pages = new MemoryPages();
        var view = new CellView(new CellRules(StorageCellTier.All[4], LayoutDescriptor.MaxPages), CellTypeLimit.Max,
            new[] { PageView.Unslotted(PageGeometry.Anchor, 0) }, new CellUsage(), CellPartition.Unfiltered);
        var inventory = new BasicCellInventory(view, pages);
        var source = new MemorySource(roots);
        var clock = Stopwatch.StartNew();
        var result = inventory.Insert(source, Actionable.Modulate, Source);
        clock.Stop();
        if (result.StoppedBy.Outcome != TransferOutcome.SourceEmpty || view.Pages.Count != expectedPages)
            TestContext.Out.WriteLine($"note: N=16 run stopped at {result.StoppedBy} with {view.Pages.Count} pages (formula {expectedPages}).");
        return clock.Elapsed.TotalMilliseconds;
    }

    private static string TypeLimit()
    {
        var roots = Mixed(129, 129, seed: 4, distinctTypes: true);
        var view = new CellView(CellRules.For(StorageCellTier.All[0]), 128,
            new[] { PageView.Unslotted(PageGeometry.Anchor, 0) }, new CellUsage(), CellPartition.Unfiltered);
        var result = new BasicCellInventory(view, new MemoryPages()).Insert(new MemorySource(roots), Actionable.Modulate, Source);
        return $"4 type limit (1k, limit 128): stored {view.TypeCount} types, stopped by {result.StoppedBy}, " +
            $"used {view.UsedBytes()}/1024 bytes, type cost {view.TypeCount * 4} bytes";
    }

    /// <summary>
    /// Stores, removes 30 % at random and stores again, through the real planner; after each round empty pages are
    /// removed as PageCompactionPlanner does (partly filled pages are never repacked).
    /// </summary>
    private static string Churn()
    {
        var random = new Random(5);
        var rules = new CellRules(StorageCellTier.All[4], LayoutDescriptor.MaxPages);
        var pages = new List<PageView> { PageView.Unslotted(PageGeometry.Anchor, 0) };
        var live = new List<(int Page, int Slot, BoundingSize Size, ItemKey Key)>();
        var usage = new CellUsage();
        int peak = 0, removedPages = 0;
        var catalog = Mixed(30, 30, seed: 6, distinctTypes: true);
        for (var round = 0; round < 21; round++)
        {
            var view = new CellView(rules, CellTypeLimit.Max, pages, usage, CellPartition.Unfiltered);
            foreach (var _ in Enumerable.Range(0, round == 0 ? 1000 : 300))
            {
                var type = catalog[random.Next(catalog.Count)];
                var size = new BoundingSize(type.W, type.H);
                var key = new ItemKey(type.Id);
                var plan = CellStorePlanner.Plan(view, new StoreCandidate(size, key, 1), PlanScope.Insert);
                if (plan.NewPage is { } geometry) view.AppendPage(geometry);
                view.RecordStored(plan.Page, plan.Slot, key, 1, size);
                live.Add((plan.Page, plan.Slot, size, key));
            }

            peak = Math.Max(peak, view.Pages.Count);
            foreach (var root in live.Where(_ => random.NextDouble() < 0.3).ToList())
            {
                view.RecordRemoved(root.Page, root.Slot, root.Key, 1, root.Size);
                live.Remove(root);
            }

            // Remove empty pages and renumber the survivors, keeping their order.
            var kept = Enumerable.Range(0, view.Pages.Count).Where(page => page == 0 || !view.Pages[page].IsEmpty).ToList();
            removedPages += view.Pages.Count - kept.Count;
            var map = kept.Select((old, index) => (old, index)).ToDictionary(pair => pair.old, pair => pair.index);
            pages = kept.Select(page => view.Pages[page]).ToList();
            live = live.Select(root => (map[root.Page], root.Slot, root.Size, root.Key)).ToList();
            usage = view.Usage;
        }

        var minimum = Strip(live.Select(root => (root.Size.Width, root.Size.Height, root.Key.Identifier)), 16).Pages;
        return $"5 churn (N=16, 30 types, 1000 roots then 20 rounds of 30% out / 300 in): peak {peak} pages, " +
            $"{removedPages} empty pages removed in total, final {pages.Count} pages for {live.Count} roots " +
            $"(perfect repacking would need {minimum})";
    }

    private static string Legacy()
    {
        // A full old 1k cell: 16 pages of 12×12, each page filled with one mixed size.
        var random = new Random(7);
        var roots = new List<RootSnapshot>();
        var total = Distribution.Sum(entry => entry.Weight);
        for (var page = 0; page < 16; page++)
        {
            var pick = random.Next(total);
            var size = Distribution.First(candidate => (pick -= candidate.Weight) < 0);
            var columns = 12 / size.W;
            var rows = 12 / size.H;
            for (var slot = 0; slot < columns * rows; slot++)
                roots.Add(new RootSnapshot(page, slot % columns * size.W, slot / columns * size.H,
                    new BoundingSize(size.W, size.H), new ItemKey($"old.{size.W}x{size.H}"), 1));
        }

        var usage = new CellUsage();
        foreach (var root in roots) usage.Add(root.Key, root.Units, CellUsage.AreaOf(root.Size, root.Units));
        var view = new CellView(CellRules.For(StorageCellTier.All[0]), 128,
            Enumerable.Range(0, 16).Select(page => PageView.Unslotted(page == 0 ? PageGeometry.Anchor : PageGeometry.Legacy,
                roots.Count(root => root.Page == page))), usage, CellPartition.Unfiltered);
        var clock = Stopwatch.StartNew();
        var forecast = RootDrainSimulator.Forecast(view, roots);
        clock.Stop();
        var stripCells = forecast.After.Pages.Skip(16).Sum(page => (long)page.Geometry.Width * page.Geometry.Height);
        return $"7 legacy migration (full 1k, 16×144 cells, {roots.Count} roots, {roots.Select(r => r.Key).Distinct().Count()} types): " +
            $"accepted={forecast.Accepted}, appended {forecast.AppendedPages} strip pages ({stripCells} cells), " +
            $"over capacity={forecast.OverCapacity} (used {forecast.After.UsedBytes()}/1024 bytes), forecast {clock.Elapsed.TotalMilliseconds:0.0} ms";
    }

    private static string DrainTiming(int count)
    {
        var roots = Mixed(count, 40, seed: 8);
        var cell = new SameCell(roots);
        var view = cell.ReadView();
        var clock = Stopwatch.StartNew();
        var result = RootTransfer.Drain(cell, new CellRootSink(view, cell, PlanScope.Relocate), MigrationSource.Instance);
        clock.Stop();
        return $"7 drain timing: {result.MovedRoots} roots relocated in {clock.Elapsed.TotalMilliseconds:0.0} ms " +
            $"(Common rules and in-memory moves only; native ItemMove cost not included), {view.Pages.Count} pages after, legacy pages not yet removed";
    }

    private sealed class MemorySource : IRootSource
    {
        private readonly List<RootSnapshot> _roots;
        private int _first;

        internal MemorySource(IEnumerable<(int W, int H, string Id)> roots) => _roots = roots
            .Select((root, i) => new RootSnapshot(0, i, 0, new BoundingSize(root.W, root.H), new ItemKey(root.Id), 1, null))
            .ToList();

        internal void Take() => _first++;

        public bool TryPeekFirst(out RootSnapshot root)
        {
            root = _first < _roots.Count ? Rebind(_roots[_first]) : default;
            return _first < _roots.Count;
        }

        public IEnumerable<RootSnapshot> Snapshot() => _roots.Skip(_first).Select(Rebind);

        private RootSnapshot Rebind(RootSnapshot root) => new(root.Page, root.X, root.Y, root.Size, root.Key, root.Units, this);
    }

    private sealed class MemoryPages : ICellPagesPort
    {
        public bool TryAppendStripPage(PageGeometry page) => true;

        public TransferStep MoveInto(RootSnapshot root, int page, int slot)
        {
            ((MemorySource)root.Origin!).Take();
            return TransferStep.Moved;
        }

        public IRootSource StoredRoots(ItemKey key) => throw new NotSupportedException();
    }

    /// <summary>A cell whose roots start on 12×12 legacy pages and move to its own strip pages.</summary>
    private sealed class SameCell : IRootSource, ICellPagesPort
    {
        private readonly List<RootSnapshot> _legacy;
        private readonly List<PageGeometry> _pages = new() { PageGeometry.Anchor };
        private int _first;

        internal SameCell(IEnumerable<(int W, int H, string Id)> roots)
        {
            var list = roots.ToList();
            var legacyPages = Math.Max(1, (list.Count + 3) / 4);
            for (var i = 0; i < legacyPages; i++) _pages.Add(PageGeometry.Legacy);
            // Four roots per legacy page at fixed corners; the measurement only needs a stable order.
            _legacy = list.Select((root, i) => new RootSnapshot(1 + i / 4, i % 4 * 3, 0,
                new BoundingSize(root.W, root.H), new ItemKey(root.Id), 1, this)).ToList();
        }

        internal CellView ReadView()
        {
            var usage = new CellUsage();
            foreach (var root in _legacy) usage.Add(root.Key, root.Units, CellUsage.AreaOf(root.Size, root.Units));
            return new CellView(new CellRules(StorageCellTier.All[4], LayoutDescriptor.MaxPages), CellTypeLimit.Max,
                _pages.Select((page, index) => PageView.Unslotted(page, _legacy.Count(root => root.Page == index))),
                usage, CellPartition.Unfiltered);
        }

        public bool TryPeekFirst(out RootSnapshot root)
        {
            root = _first < _legacy.Count ? _legacy[_first] : default;
            return _first < _legacy.Count;
        }

        public IEnumerable<RootSnapshot> Snapshot() => _legacy.Skip(_first);

        public bool TryAppendStripPage(PageGeometry page)
        {
            _pages.Add(page);
            return true;
        }

        public TransferStep MoveInto(RootSnapshot root, int page, int slot)
        {
            _first++;
            return TransferStep.Moved;
        }

        public IRootSource StoredRoots(ItemKey key) => throw new NotSupportedException();
    }
}
