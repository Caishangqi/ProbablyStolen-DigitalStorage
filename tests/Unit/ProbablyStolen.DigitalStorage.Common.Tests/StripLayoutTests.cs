using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class StripLayoutTests
{
    [Test]
    public void PageFor_EverySizeFrom1x1To12x12IsOneRowOf16Slots()
    {
        for (var width = 1; width <= 12; width++)
            for (var height = 1; height <= 12; height++)
            {
                var page = StripLayout.PageFor(new BoundingSize(width, height));

                Assert.That(page.Kind, Is.EqualTo(PageKind.Strip));
                Assert.That(page.Width, Is.EqualTo(width * 16), $"{width}x{height}");
                Assert.That(page.Height, Is.EqualTo(height), $"{width}x{height}");
                Assert.That(page.SlotCount, Is.EqualTo(16));
                Assert.That(page.SlotSize, Is.EqualTo(new BoundingSize(width, height)));
            }
    }

    [Test]
    public void PageFor_WidestPageIs192Cells()
    {
        Assert.That(StripLayout.PageFor(new BoundingSize(12, 1)).Width, Is.EqualTo(192));
    }

    [TestCase(0, 1)]
    [TestCase(1, 0)]
    [TestCase(13, 1)]
    [TestCase(1, 13)]
    [TestCase(-1, 5)]
    [TestCase(int.MaxValue, 1)]
    public void Fits_RejectsSidesOutside1To12(int width, int height)
    {
        var size = new BoundingSize(width, height);

        Assert.That(StripLayout.Fits(size), Is.False);
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => StripLayout.PageFor(size)));
    }

    [Test]
    public void SlotOriginAndSlotIndexAt_AreInverse()
    {
        for (var width = 1; width <= 12; width++)
        {
            var page = StripLayout.PageFor(new BoundingSize(width, 3));
            for (var slot = 0; slot < page.SlotCount; slot++)
            {
                var (x, y) = StripLayout.SlotOrigin(page, slot);

                Assert.That((x, y), Is.EqualTo((slot * width, 0)));
                Assert.That(StripLayout.SlotIndexAt(page, x, y), Is.EqualTo(slot));
            }
        }
    }

    [Test]
    public void SlotIndexAt_ReturnsMinusOneOffSlotOrigins()
    {
        var page = StripLayout.PageFor(new BoundingSize(3, 2));

        Assert.That(StripLayout.SlotIndexAt(page, 1, 0), Is.EqualTo(-1));
        Assert.That(StripLayout.SlotIndexAt(page, 3, 1), Is.EqualTo(-1));
        Assert.That(StripLayout.SlotIndexAt(page, -3, 0), Is.EqualTo(-1));
        Assert.That(StripLayout.SlotIndexAt(page, 48, 0), Is.EqualTo(-1));
    }

    [Test]
    public void SlotOrigin_RejectsSlotsOutsideThePage()
    {
        var page = StripLayout.PageFor(new BoundingSize(2, 2));

        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => StripLayout.SlotOrigin(page, -1)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => StripLayout.SlotOrigin(page, 16)));
    }

    [Test]
    public void SlotArithmetic_RejectsAnchorAndLegacyPages()
    {
        Assert.Throws<ArgumentException>(new Action(() => StripLayout.SlotOrigin(PageGeometry.Anchor, 0)));
        Assert.Throws<ArgumentException>(new Action(() => StripLayout.SlotIndexAt(PageGeometry.Legacy, 0, 0)));
    }

    [Test]
    public void Strip_KeepsARecordedSlotCountBelowN()
    {
        var page = PageGeometry.Strip(new BoundingSize(4, 4), 8);

        Assert.That(page.Width, Is.EqualTo(32));
        Assert.That(StripLayout.SlotIndexAt(page, 28, 0), Is.EqualTo(7));
        Assert.That(StripLayout.SlotIndexAt(page, 32, 0), Is.EqualTo(-1));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => PageGeometry.Strip(new BoundingSize(4, 4), 0)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => PageGeometry.Strip(new BoundingSize(4, 4), 17)));
    }

    [Test]
    public void AnchorAndLegacyPages_Are12x12WithoutSlots()
    {
        foreach (var page in new[] { PageGeometry.Anchor, PageGeometry.Legacy })
        {
            Assert.That((page.Width, page.Height, page.SlotCount), Is.EqualTo((12, 12, 0)));
        }

        Assert.That(PageGeometry.Anchor, Is.Not.EqualTo(PageGeometry.Legacy));
    }
}
