using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class CellPlacementPlannerTests
{
    private static readonly ItemKey Apple = new("item.apple");
    private static readonly BoundingSize TwoByTwo = new(2, 2);

    [Test]
    public void EmptyCellWithoutPages_AppendsFirstPage()
    {
        var placement = Plan(Cell(), Item(TwoByTwo));

        Assert.That(placement.IsAccepted, Is.True);
        Assert.That(placement.AppendsPage, Is.True);
        Assert.That(placement.Page, Is.Zero);
        Assert.That(placement.Slot, Is.Zero);
    }

    [Test]
    public void FreeInitialPage_IsUsedBeforeAppending()
    {
        var placement = Plan(Cell(pages: PageView.Free), Item(TwoByTwo));

        Assert.That(placement.AppendsPage, Is.False);
        Assert.That((placement.Page, placement.Slot), Is.EqualTo((0, 0)));
    }

    [Test]
    public void SameSizeChain_FillsFirstFreeSlot()
    {
        var page = new PageView(TwoByTwo, new[] { 0, 1, 3 });

        var placement = Plan(Cell(pages: page), Item(TwoByTwo));

        Assert.That((placement.Page, placement.Slot, placement.AppendsPage), Is.EqualTo((0, 2, false)));
    }

    [Test]
    public void FullChain_ContinuesOnNextPageOfSameSize()
    {
        var full = new PageView(TwoByTwo, Enumerable.Range(0, 36));
        var other = new PageView(new BoundingSize(1, 1), new[] { 0 });
        var next = new PageView(TwoByTwo, new[] { 0 });

        var placement = Plan(Cell(pages: new[] { full, other, next }), Item(TwoByTwo));

        Assert.That((placement.Page, placement.Slot), Is.EqualTo((2, 1)));
    }

    [Test]
    public void FullChain_ReusesFreePageBeforeAppending()
    {
        var full = new PageView(TwoByTwo, Enumerable.Range(0, 36));
        var otherSize = new PageView(new BoundingSize(3, 3), new[] { 0 });

        var placement = Plan(Cell(pages: new[] { full, otherSize, PageView.Free }), Item(TwoByTwo));

        Assert.That((placement.Page, placement.Slot, placement.AppendsPage), Is.EqualTo((2, 0, false)));
    }

    [Test]
    public void FullChainWithoutFreePage_AppendsUntilLimitThenRejects()
    {
        var full = new PageView(TwoByTwo, Enumerable.Range(0, 36));
        var fifteen = Enumerable.Repeat(full, 15).ToArray();
        var sixteen = Enumerable.Repeat(full, 16).ToArray();

        var append = Plan(Cell(pages: fifteen), Item(TwoByTwo));
        var reject = Plan(Cell(pages: sixteen), Item(TwoByTwo));

        Assert.That((append.Page, append.AppendsPage), Is.EqualTo((15, true)));
        Assert.That(reject.IsAccepted, Is.False);
        Assert.That(reject.Rejection, Is.EqualTo(PlacementRejection.NoSpace));
    }

    [Test]
    public void LShapeAndSolid2x2_ShareOneChain()
    {
        // An L-shaped item is classified by its 2×2 bounding box, so it lands next to solid 2×2 items.
        var solidPage = new PageView(TwoByTwo, new[] { 0 });
        var lShape = Item(TwoByTwo, new ItemKey("item.l_bracket"));

        var placement = Plan(Cell(pages: solidPage), lShape);

        Assert.That((placement.Page, placement.Slot), Is.EqualTo((0, 1)));
    }

    [Test]
    public void RotatedSizes_UseSeparateChains()
    {
        var threeByFour = new PageView(new BoundingSize(3, 4), new[] { 0 });

        var same = Plan(Cell(pages: threeByFour), Item(new BoundingSize(3, 4)));
        var rotated = Plan(Cell(pages: threeByFour), Item(new BoundingSize(4, 3)));

        Assert.That((same.Page, same.Slot), Is.EqualTo((0, 1)));
        Assert.That((rotated.Page, rotated.AppendsPage), Is.EqualTo((1, true)));
    }

    [Test]
    public void TypeLimit_AllowsExisting63rdTypeAndRejects64th()
    {
        var types = Enumerable.Range(0, 62).Select(i => new ItemKey($"item.t{i}")).ToList();
        var at62 = Cell(types: types, pages: PageView.Free);
        types.Add(new ItemKey("item.t62"));
        var at63 = Cell(types: types, pages: PageView.Free);

        Assert.That(Plan(at62, Item(TwoByTwo, new ItemKey("item.new"))).IsAccepted, Is.True);
        Assert.That(Plan(at63, Item(TwoByTwo, new ItemKey("item.t5"))).IsAccepted, Is.True);
        var rejected = Plan(at63, Item(TwoByTwo, new ItemKey("item.new")));
        Assert.That(rejected.Rejection, Is.EqualTo(PlacementRejection.TypeLimit));
    }

    [Test]
    public void OversizedItem_IsRejected()
    {
        var placement = Plan(Cell(pages: PageView.Free), Item(new BoundingSize(13, 1)));

        Assert.That(placement.Rejection, Is.EqualTo(PlacementRejection.TooLarge));
    }

    [Test]
    public void EmptiedPage_ReturnsToFreePool()
    {
        var emptied = new PageView(new BoundingSize(3, 3), Array.Empty<int>());

        Assert.That(emptied.IsFree, Is.True);
        Assert.That(emptied.Label, Is.Null);
        var placement = Plan(Cell(pages: emptied), Item(TwoByTwo));
        Assert.That((placement.Page, placement.Slot, placement.AppendsPage), Is.EqualTo((0, 0, false)));
    }

    [Test]
    public void PageViewWithoutLabel_CannotHaveOccupiedSlots()
    {
        Assert.Throws<ArgumentException>(new Action(() => _ = new PageView(null, new[] { 0 })));
    }

    [Test]
    public void CellView_RejectsMorePagesThanLayoutAllows()
    {
        var pages = Enumerable.Repeat(PageView.Free, 17);
        Assert.Throws<ArgumentException>(new Action(() => _ = Cell(pages: pages.ToArray())));
    }

    private static Placement Plan(CellView cell, ItemFootprint item) => CellPlacementPlanner.Plan(cell, item);

    private static ItemFootprint Item(BoundingSize size, ItemKey? key = null) => new(size, key ?? Apple);

    private static CellView Cell(IEnumerable<ItemKey>? types = null, params PageView[] pages) =>
        new(CellLayout.Cell1K, pages, types ?? Array.Empty<ItemKey>(), CellPartition.Unfiltered);

    private static CellView Cell(PageView pages) => Cell(null, pages);
}
