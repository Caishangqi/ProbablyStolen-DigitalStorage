using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class CellStorePlannerTests
{
    private static readonly BoundingSize OneByOne = new(1, 1);
    private static readonly BoundingSize TwoByTwo = new(2, 2);
    private static readonly ItemKey Apple = new("item.apple");
    private static readonly StorageCellTier Tier1K = StorageCellTier.All[0];

    private static CellView Cell(
        IEnumerable<PageView>? pages = null, CellUsage? usage = null, int typeLimit = 128, CellRules? rules = null,
        CellPartition? partition = null, CellWriteState state = CellWriteState.Writable, bool equal = false) =>
        new(rules ?? CellRules.For(Tier1K), typeLimit,
            new[] { PageView.Unslotted(PageGeometry.Anchor, 0) }.Concat(pages ?? Array.Empty<PageView>()),
            usage ?? new CellUsage(), partition ?? CellPartition.Unfiltered, writeState: state,
            upgrades: equal ? Partitions.EqualDistribution : null);

    private static PageView Strip(BoundingSize size, params int[] occupied) =>
        PageView.Strip(StripLayout.PageFor(size), occupied);

    private static CellUsage UsageOf(params (ItemKey Key, long Units, BoundingSize Size)[] roots)
    {
        var usage = new CellUsage();
        foreach (var (key, units, size) in roots) usage.Add(key, units, CellUsage.AreaOf(size, units));
        return usage;
    }

    private static StorePlan Plan(CellView cell, BoundingSize size, ItemKey? key = null, long units = 1,
        PlanScope scope = PlanScope.Insert) =>
        CellStorePlanner.Plan(cell, new StoreCandidate(size, key ?? Apple, units), scope);

    [Test]
    public void EmptyCell_AppendsTheFirstStripPage()
    {
        var plan = Plan(Cell(), TwoByTwo);

        Assert.That(plan.IsAccepted, Is.True);
        Assert.That((plan.Page, plan.Slot), Is.EqualTo((1, 0)));
        Assert.That(plan.NewPage, Is.EqualTo(StripLayout.PageFor(TwoByTwo)));
    }

    [Test]
    public void Chain_UsesTheLowestFreeSlotOfTheEarliestPage()
    {
        var full = Strip(TwoByTwo, Enumerable.Range(0, 16).ToArray());
        var cell = Cell(new[] { full, Strip(OneByOne), Strip(TwoByTwo, 0, 2), Strip(TwoByTwo) });

        var plan = Plan(cell, TwoByTwo);

        Assert.That((plan.Page, plan.Slot, plan.NewPage), Is.EqualTo((3, 1, (PageGeometry?)null)));
    }

    [Test]
    public void Chain_IncludesPagesWithADifferentRecordedSlotCount()
    {
        var older = PageView.Strip(PageGeometry.Strip(TwoByTwo, 4), new[] { 0, 1, 2 });

        var plan = Plan(Cell(new[] { older }), TwoByTwo);

        Assert.That((plan.Page, plan.Slot), Is.EqualTo((1, 3)));
    }

    [Test]
    public void LegacyAndAnchorPages_NeverReceiveItems()
    {
        var plan = Plan(Cell(new[] { PageView.Unslotted(PageGeometry.Legacy, 0) }), new BoundingSize(12, 12));

        Assert.That(plan.Page, Is.EqualTo(2));
        Assert.That(plan.NewPage, Is.Not.Null);
    }

    [Test]
    public void PageLimit_RejectsAppendingButKeepsUsingFreeSlots()
    {
        var rules = new CellRules(Tier1K, 2);
        var full = Strip(OneByOne, Enumerable.Range(0, 16).ToArray());

        Assert.That(Plan(Cell(new[] { full }, rules: rules), OneByOne).Rejection, Is.EqualTo(StoreRejection.PhysicalLimit));
        Assert.That(Plan(Cell(new[] { Strip(OneByOne, 0) }, rules: rules), OneByOne).Slot, Is.EqualTo(1));
    }

    [Test]
    public void TooLarge_RejectsItemsOutside12x12InEveryScope()
    {
        Assert.That(Plan(Cell(), new BoundingSize(13, 1)).Rejection, Is.EqualTo(StoreRejection.TooLarge));
        Assert.That(Plan(Cell(), new BoundingSize(1, 13), scope: PlanScope.Relocate).Rejection,
            Is.EqualTo(StoreRejection.TooLarge));
    }

    [Test]
    public void TypeLimit_AboveLimitAllowsExistingTypesAndRejectsNewOnes()
    {
        var types = Enumerable.Range(0, 3).Select(i => new ItemKey($"item.t{i}")).ToArray();
        var usage = UsageOf(types.Select(key => (key, 1L, OneByOne)).ToArray());
        var cell = Cell(new[] { Strip(OneByOne, 0, 1, 2) }, usage, typeLimit: 2);

        Assert.That(Plan(cell, OneByOne, types[2]).IsAccepted, Is.True);
        Assert.That(Plan(cell, OneByOne, new ItemKey("item.new")).Rejection, Is.EqualTo(StoreRejection.TypeLimit));
    }

    [Test]
    public void Capacity_BillsTypesAndRoundedAreaUnits()
    {
        // 1k: 1024 bytes, 4 per type. One type leaves 1020 bytes = 8160 area units.
        Assert.That(Plan(Cell(), OneByOne, units: 8160).IsAccepted, Is.True);
        Assert.That(Plan(Cell(), OneByOne, units: 8161).Rejection, Is.EqualTo(StoreRejection.Capacity));

        var usage = UsageOf((Apple, 8159, OneByOne));
        var cell = Cell(new[] { Strip(OneByOne, 0) }, usage);
        Assert.That(Plan(cell, OneByOne).IsAccepted, Is.True);
        Assert.That(Plan(cell, OneByOne, new ItemKey("item.pear")).Rejection, Is.EqualTo(StoreRejection.Capacity));
    }

    [Test]
    public void DistributionQuota_LimitsEachTypeToItsShare()
    {
        var two = new[] { Apple, new ItemKey("item.pear") };
        // The share follows the type limit, never the rule count: (1024 - 512) * 8 / 128 = 32 area units per type.
        var cell = Cell(partition: Partitions.Whitelist(two), equal: true);

        Assert.That(cell.EqualShareQuota(), Is.EqualTo(32));
        Assert.That(Plan(cell, OneByOne, units: 32).IsAccepted, Is.True);
        Assert.That(Plan(cell, OneByOne, units: 33).Rejection, Is.EqualTo(StoreRejection.DistributionQuota));
        Assert.That(Cell(partition: Partitions.Whitelist(two)).EqualDistribution, Is.False,
            "Only the equal distribution module enables the share.");
    }

    [Test]
    public void Rejections_FollowTheDesignOrder()
    {
        var usage = UsageOf((new ItemKey("item.other"), 8160, OneByOne));
        var full = Strip(OneByOne, Enumerable.Range(0, 16).ToArray());

        Assert.That(Plan(Cell(state: CellWriteState.ExtractOnly), new BoundingSize(13, 13)).Rejection,
            Is.EqualTo(StoreRejection.ReadOnly));
        Assert.That(Plan(Cell(usage: usage, typeLimit: 1), new BoundingSize(13, 13)).Rejection,
            Is.EqualTo(StoreRejection.TooLarge));
        Assert.That(Plan(Cell(new[] { full }, usage, typeLimit: 1), OneByOne).Rejection,
            Is.EqualTo(StoreRejection.TypeLimit));
        Assert.That(Plan(Cell(new[] { full }, usage, equal: true), OneByOne).Rejection,
            Is.EqualTo(StoreRejection.Capacity));
        Assert.That(Plan(Cell(new[] { full }, rules: new CellRules(Tier1K, 2), equal: true), OneByOne,
            units: 5000).Rejection, Is.EqualTo(StoreRejection.DistributionQuota));
        Assert.That(Plan(Cell(new[] { full }, rules: new CellRules(Tier1K, 2)), OneByOne).Rejection,
            Is.EqualTo(StoreRejection.PhysicalLimit));
    }

    [Test]
    public void Relocate_IgnoresQuotasPartitionAndTierPageLimit()
    {
        var types = Enumerable.Range(0, 5).Select(i => new ItemKey($"item.t{i}")).ToArray();
        var usage = UsageOf(types.Select(key => (key, 9000L, OneByOne)).ToArray());
        var invalid = CellPartition.Invalid(PartitionMode.Whitelist, CellPartitionFailure.UnknownMode);
        var full = Strip(OneByOne, Enumerable.Range(0, 16).ToArray());
        var cell = Cell(new[] { full }, usage, typeLimit: 1, rules: new CellRules(Tier1K, 2), partition: invalid,
            state: CellWriteState.ExtractOnly);

        var plan = Plan(cell, OneByOne, types[0], 9000, PlanScope.Relocate);

        Assert.That(plan.IsAccepted, Is.True);
        Assert.That(plan.Page, Is.EqualTo(2));
        Assert.That(plan.NewPage, Is.Not.Null);
    }

    [Test]
    public void Relocate_StopsAtTheRestorablePageLimit()
    {
        var full = Strip(OneByOne, Enumerable.Range(0, 16).ToArray());
        var cell = Cell(Enumerable.Repeat(full, LayoutDescriptor.MaxPages - 1));

        Assert.That(Plan(cell, OneByOne, scope: PlanScope.Relocate).Rejection, Is.EqualTo(StoreRejection.PhysicalLimit));
    }

    [Test]
    public void UsedBytes_RoundsAreaOnceForTheWholeCell()
    {
        Assert.That(CellView.UsedBytes(0, 0, 512, 128), Is.Zero);
        Assert.That(CellView.UsedBytes(2, 1, 512, 128), Is.EqualTo(9));
        Assert.That(CellView.UsedBytes(2, 16, 512, 128), Is.EqualTo(10));
        Assert.That(CellView.UsedBytes(2, 17, 512, 128), Is.EqualTo(11));
        Assert.Throws<OverflowException>(new Action(() => CellView.UsedBytes(int.MaxValue, 0, long.MaxValue, 1)));
    }

    [Test]
    public void EqualShareQuota_RoundsUpPerShareOfTheFreeBytes()
    {
        var keys = Enumerable.Range(0, 128).Select(i => new ItemKey($"item.t{i}")).ToArray();
        // The type budget (512) is paid for the whole limit (192), and the rest is shared over the limit:
        // (1024 - 512) * 8 / 192 = 21.3, rounded up to 22, however many rules list types.
        Assert.That(Cell(typeLimit: 192, partition: Partitions.Whitelist(keys), equal: true).EqualShareQuota(), Is.EqualTo(22));
        Assert.That(Cell(typeLimit: 192, rules: new CellRules(Tier1K, 64), equal: true).EqualShareQuota(), Is.EqualTo(22));
    }

    [Test]
    public void UsedBytes_AtTheBaseLimitMatchesTheFormerPerTypeCost()
    {
        foreach (var tier in StorageCellTier.All)
        {
            for (var types = 0; types <= 128; types += 7)
            {
                Assert.That(CellView.UsedBytes(types, 13, tier.GetTypeBudget(), 128),
                    Is.EqualTo(types * tier.GetBytesPerType() + 2), tier.Name);
            }
        }
    }

    [Test]
    public void UsedBytes_SpreadsTheTypeBudgetOverAHigherLimit()
    {
        // 1k: budget 512. A full cell's types always cost half the capacity, whatever the limit.
        Assert.That(CellView.UsedBytes(282, 0, 512, 282), Is.EqualTo(512));
        Assert.That(CellView.UsedBytes(1120, 0, 512, 1120), Is.EqualTo(512));
        // One type at limit 300 costs ceil(512 / 300) = 2 bytes; 150 types cost ceil(76800 / 300) = 256.
        Assert.That(CellView.TypeBytes(1, 512, 300), Is.EqualTo(2));
        Assert.That(CellView.TypeBytes(150, 512, 300), Is.EqualTo(256));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => CellView.TypeBytes(1, 512, 0)));
    }

    [Test]
    public void Planner_AcceptsMoreTypesBecauseEachCostsLess()
    {
        // Limit 256 on 1k: each type costs 2 bytes. 255 types using one unit each cost 510 + 32 bytes; the 256th still fits,
        // where the former fixed 4 bytes per type would already need 1020 + 32 > 1024.
        var roots = Enumerable.Range(0, 255).Select(i => (new ItemKey($"item.many{i}"), 1L, OneByOne)).ToArray();
        var cell = Cell(usage: UsageOf(roots), typeLimit: 256);

        Assert.That(cell.UsedBytes(), Is.EqualTo(510 + 32));
        Assert.That(Plan(cell, OneByOne, new ItemKey("item.new")).Rejection, Is.Null);
        Assert.That(Plan(Cell(usage: UsageOf(roots.Concat(new[] { (new ItemKey("item.last"), 1L, OneByOne) }).ToArray()),
            typeLimit: 256), OneByOne, new ItemKey("item.new")).Rejection, Is.EqualTo(StoreRejection.TypeLimit));
    }
}
