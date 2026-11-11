using System;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class CellViewTests
{
    private static readonly ItemKey Apple = new("item.apple");
    private static readonly ItemKey Pear = new("item.pear");
    private static readonly BoundingSize TwoByTwo = new(2, 2);

    private static CellView Empty(params PageView[] extra) => new(
        CellRules.For(StorageCellTier.All[0]), 128,
        new[] { PageView.Unslotted(PageGeometry.Anchor, 0) }.Concat(extra), new CellUsage(), CellPartition.Unfiltered);

    [Test]
    public void Usage_CountsUnitsAndAreaUnitsPerType()
    {
        var usage = new CellUsage();

        usage.Add(Apple, 3, CellUsage.AreaOf(TwoByTwo, 3));
        usage.Add(Apple, 1, CellUsage.AreaOf(TwoByTwo, 1));
        usage.Add(Pear, 5, CellUsage.AreaOf(new BoundingSize(1, 1), 5));

        Assert.That(usage.TypeCount, Is.EqualTo(2));
        Assert.That(usage.Get(Apple).Units, Is.EqualTo(4));
        Assert.That(usage.Get(Apple).AreaUnits, Is.EqualTo(16));
        Assert.That(usage.AreaUnits, Is.EqualTo(21));
    }

    [Test]
    public void Usage_ReleasesATypeWhenItsLastRootLeaves()
    {
        var usage = new CellUsage();
        usage.Add(Apple, 2, 8);
        usage.Add(Apple, 1, 4);

        usage.Remove(Apple, 2, 8);
        Assert.That(usage.Contains(Apple), Is.True);
        usage.Remove(Apple, 1, 4);

        Assert.That(usage.Contains(Apple), Is.False);
        Assert.That(usage.TypeCount, Is.Zero);
        Assert.That(usage.AreaUnits, Is.Zero);
    }

    [Test]
    public void Usage_RejectsRemovingMoreThanStoredAndEmptyRoots()
    {
        var usage = new CellUsage();
        usage.Add(Apple, 1, 4);

        Assert.Throws<InvalidOperationException>(new Action(() => usage.Remove(Apple, 2, 8)));
        Assert.Throws<InvalidOperationException>(new Action(() => usage.Remove(Pear, 1, 1)));
        Assert.Throws<ArgumentException>(new Action(() => usage.Add(Apple, 0, 0)));
        Assert.That(usage.Get(Apple).Units, Is.EqualTo(1));
    }

    [Test]
    public void AreaOf_IsCheckedAndRejectsNegativeUnits()
    {
        Assert.That(CellUsage.AreaOf(new BoundingSize(12, 12), 3), Is.EqualTo(432));
        Assert.Throws<OverflowException>(new Action(() => CellUsage.AreaOf(new BoundingSize(12, 12), long.MaxValue)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => CellUsage.AreaOf(TwoByTwo, -1)));
    }

    [Test]
    public void RecordStoredAndRemoved_UpdateOnlyThatPageAndUsage()
    {
        var strip = PageView.Strip(StripLayout.PageFor(TwoByTwo), new[] { 0 });
        var other = PageView.Strip(StripLayout.PageFor(TwoByTwo), Array.Empty<int>());
        var view = Empty(strip, other);
        view.Usage.Add(Pear, 1, 4);

        view.RecordStored(1, 1, Apple, 2, TwoByTwo);

        Assert.That(strip.IsOccupied(1), Is.True);
        Assert.That(strip.FirstFreeSlot(), Is.EqualTo(2));
        Assert.That(other.IsEmpty, Is.True);
        Assert.That(view.TypeCount, Is.EqualTo(2));
        Assert.That(view.Usage.AreaUnits, Is.EqualTo(12));

        view.RecordRemoved(1, 1, Apple, 2, TwoByTwo);

        Assert.That(strip.IsOccupied(1), Is.False);
        Assert.That(view.Contains(Apple), Is.False);
        Assert.That(view.Usage.AreaUnits, Is.EqualTo(4));
    }

    [Test]
    public void RecordStored_FailsWithoutChangingStateOnAnOccupiedSlot()
    {
        var strip = PageView.Strip(StripLayout.PageFor(TwoByTwo), new[] { 0 });
        var view = Empty(strip);

        Assert.Throws<InvalidOperationException>(new Action(() => view.RecordStored(1, 0, Apple, 1, TwoByTwo)));
        Assert.That(view.Contains(Apple), Is.False);
        Assert.That(strip.RootCount, Is.EqualTo(1));
    }

    [Test]
    public void RecordRemoved_FromALegacyPageCountsRoots()
    {
        var legacy = PageView.Unslotted(PageGeometry.Legacy, 2);
        var view = Empty(legacy);
        view.Usage.Add(Apple, 2, 8);

        view.RecordRemoved(1, -1, Apple, 1, TwoByTwo);

        Assert.That(legacy.RootCount, Is.EqualTo(1));
        Assert.That(view.Usage.Get(Apple).Units, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(new Action(() => view.RecordRemoved(1, 0, Apple, 1, TwoByTwo)));
        Assert.That(view.Usage.Get(Apple).Units, Is.EqualTo(1), "A failed page release restores usage.");
    }

    [Test]
    public void ChainOf_GroupsStripPagesBySlotSizeWhateverTheirSlotCount()
    {
        var view = Empty(
            PageView.Strip(PageGeometry.Strip(TwoByTwo, 8), Array.Empty<int>()),
            PageView.Unslotted(PageGeometry.Legacy, 0),
            PageView.Strip(StripLayout.PageFor(new BoundingSize(1, 1)), Array.Empty<int>()));

        var appended = view.AppendPage(StripLayout.PageFor(TwoByTwo));

        Assert.That(appended, Is.EqualTo(4));
        Assert.That(view.ChainOf(TwoByTwo), Is.EqualTo(new[] { 1, 4 }));
        Assert.That(view.ChainOf(new BoundingSize(1, 1)), Is.EqualTo(new[] { 3 }));
        Assert.That(view.ChainOf(new BoundingSize(3, 3)), Is.Empty);
        Assert.Throws<ArgumentException>(new Action(() => view.AppendPage(PageGeometry.Anchor)));
    }

    [Test]
    public void Constructor_RequiresTheAnchorFirstAndOnlyFirst()
    {
        var rules = CellRules.For(StorageCellTier.All[0]);
        var legacy = PageView.Unslotted(PageGeometry.Legacy, 0);
        var anchor = PageView.Unslotted(PageGeometry.Anchor, 0);

        Assert.Throws<ArgumentException>(new Action(() =>
            _ = new CellView(rules, 128, new[] { legacy }, new CellUsage(), CellPartition.Unfiltered)));
        Assert.Throws<ArgumentException>(new Action(() =>
            _ = new CellView(rules, 128, new[] { anchor, anchor }, new CellUsage(), CellPartition.Unfiltered)));
        Assert.Throws<ArgumentException>(new Action(() =>
            _ = new CellView(rules, 0, new[] { anchor }, new CellUsage(), CellPartition.Unfiltered)));
    }

    [Test]
    public void PageViews_RejectSlotsOutsideThePageAndWrongKinds()
    {
        Assert.Throws<ArgumentException>(new Action(() => PageView.Strip(StripLayout.PageFor(TwoByTwo), new[] { 16 })));
        Assert.Throws<ArgumentException>(new Action(() => PageView.Strip(StripLayout.PageFor(TwoByTwo), new[] { 1, 1 })));
        Assert.Throws<ArgumentException>(new Action(() => PageView.Strip(PageGeometry.Legacy, Array.Empty<int>())));
        Assert.Throws<ArgumentException>(new Action(() => PageView.Unslotted(StripLayout.PageFor(TwoByTwo), 0)));
        Assert.Throws<ArgumentException>(new Action(() => PageView.Unslotted(PageGeometry.Legacy, -1)));
    }

    [Test]
    public void CellRules_EveryTierAppendsUpToTheRestorableLimit()
    {
        Assert.That(StorageCellTier.All.Select(tier => CellRules.For(tier).MaxPages),
            Is.All.EqualTo(LayoutDescriptor.MaxPages));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => _ = new CellRules(StorageCellTier.All[0], 0)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => _ = new CellRules(StorageCellTier.All[0], 32769)));
    }
}
