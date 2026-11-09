using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class CellLayoutTests
{
    private static readonly CellLayout Layout = CellLayout.Cell1K;

    [Test]
    public void Cell1K_UsesApprovedLimits()
    {
        Assert.That(Layout.PageWidth, Is.EqualTo(12));
        Assert.That(Layout.PageHeight, Is.EqualTo(12));
        Assert.That(Layout.MaxPages, Is.EqualTo(16));
        Assert.That(Layout.MaxTypes, Is.EqualTo(63));
        Assert.That(Layout.CellsPerPage, Is.EqualTo(144));
        Assert.That(Layout.TotalCells, Is.EqualTo(2304));
    }

    [TestCase(1, 1, 144)]
    [TestCase(2, 2, 36)]
    [TestCase(3, 4, 12)]
    [TestCase(4, 3, 12)]
    [TestCase(7, 6, 2)]
    [TestCase(12, 2, 6)]
    [TestCase(5, 5, 4)]
    public void SlotCount_TilesBoundingSizeWithoutRotation(int width, int height, int expected)
    {
        Assert.That(Layout.SlotCount(width, height), Is.EqualTo(expected));
    }

    [TestCase(13, 1)]
    [TestCase(1, 13)]
    [TestCase(0, 1)]
    [TestCase(1, 0)]
    public void OversizedOrEmptySize_DoesNotFit(int width, int height)
    {
        Assert.That(Layout.Fits(width, height), Is.False);
        Assert.That(Layout.SlotCount(width, height), Is.Zero);
    }

    [Test]
    public void SlotOrigin_FillsRowsLeftToRight()
    {
        Assert.That(Layout.SlotOrigin(0, 3, 4), Is.EqualTo((0, 0)));
        Assert.That(Layout.SlotOrigin(3, 3, 4), Is.EqualTo((9, 0)));
        Assert.That(Layout.SlotOrigin(4, 3, 4), Is.EqualTo((0, 4)));
        Assert.That(Layout.SlotOrigin(11, 3, 4), Is.EqualTo((9, 8)));
        Assert.That(Layout.SlotOrigin(1, 7, 6), Is.EqualTo((0, 6)));
    }

    [Test]
    public void SlotOrigin_OutsidePage_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => Layout.SlotOrigin(12, 3, 4)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => Layout.SlotOrigin(-1, 3, 4)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => Layout.SlotOrigin(0, 13, 1)));
    }

    [Test]
    public void SlotIndexAt_InvertsSlotOrigin()
    {
        for (var index = 0; index < Layout.SlotCount(5, 5); index++)
        {
            var (x, y) = Layout.SlotOrigin(index, 5, 5);
            Assert.That(Layout.SlotIndexAt(x, y, 5, 5), Is.EqualTo(index));
        }
    }

    [TestCase(1, 0, 2, 2)]
    [TestCase(10, 0, 5, 5)]
    [TestCase(0, 10, 5, 5)]
    [TestCase(-2, 0, 2, 2)]
    [TestCase(0, 0, 13, 1)]
    public void SlotIndexAt_NonSlotPosition_ReturnsMinusOne(int x, int y, int width, int height)
    {
        Assert.That(Layout.SlotIndexAt(x, y, width, height), Is.EqualTo(-1));
    }
}
