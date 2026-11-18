using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Interaction;

[TestFixture]
internal sealed class GridDropAnchorTests
{
    [TestCase(0f, 0f, 0, 0)]
    [TestCase(15.999f, 15.999f, 0, 0)]
    [TestCase(16f, 16f, 1, 1)]
    [TestCase(63.999f, 47.999f, 3, 2)]
    public void TryFromLocal_UsesHalfOpenTopLeftCells(float x, float y, int expectedX, int expectedY)
    {
        Assert.That(GridDropAnchor.TryFromLocal(new PointerPoint(x, y), 16, 4, 3, out var anchor), Is.True);
        Assert.That(anchor, Is.EqualTo(new GridDropAnchor(expectedX, expectedY)));
    }

    [TestCase(-0.001f, 0f)]
    [TestCase(0f, -0.001f)]
    [TestCase(64f, 0f)]
    [TestCase(0f, 48f)]
    [TestCase(float.NaN, 0f)]
    [TestCase(0f, float.PositiveInfinity)]
    [TestCase(float.MaxValue, float.MaxValue)]
    public void TryFromLocal_RejectsOutsideAndNonFiniteCoordinates(float x, float y)
    {
        Assert.That(GridDropAnchor.TryFromLocal(new PointerPoint(x, y), 16, 4, 3, out _), Is.False);
    }

    [TestCase(0f, 4, 3)]
    [TestCase(-1f, 4, 3)]
    [TestCase(float.NaN, 4, 3)]
    [TestCase(float.PositiveInfinity, 4, 3)]
    [TestCase(16f, 0, 3)]
    [TestCase(16f, 4, -1)]
    public void TryFromLocal_RejectsInvalidGrid(float size, int columns, int rows)
    {
        Assert.That(GridDropAnchor.TryFromLocal(new PointerPoint(0, 0), size, columns, rows, out _), Is.False);
    }

    [Test]
    public void TryFromLocal_UsesCurrentLocalCellSizeWithoutScreenCoordinates()
    {
        Assert.That(GridDropAnchor.TryFromLocal(new PointerPoint(20, 10), 8, 8, 8, out var small), Is.True);
        Assert.That(GridDropAnchor.TryFromLocal(new PointerPoint(40, 20), 16, 8, 8, out var large), Is.True);
        Assert.That(small, Is.EqualTo(large));
        Assert.That(small, Is.EqualTo(new GridDropAnchor(2, 1)));
    }

    [Test]
    public void FootprintCenter_UsesTheHitCellForAllFiveRejectedCounterDrops()
    {
        var releases = new[] { new PointerPoint(5.79f, 0.65f), new PointerPoint(7.20f, 0.67f),
            new PointerPoint(3.48f, 0.74f), new PointerPoint(6.36f, 0.35f), new PointerPoint(0.74f, 0.28f) };
        foreach (var release in releases)
        {
            Assert.That(GridDropAnchor.TryFromLocal(release, 1, 18, 4, out var anchor), Is.True);
            var center = anchor.FootprintCenter(1, 1, 4);
            Assert.That(center, Is.EqualTo(new PointerPoint(anchor.X + 0.5f, 2f)));
            Assert.That(Math.Round(center.Y - 2f), Is.Zero, "Native snapping must start the bottle on row zero.");
        }
    }

    [TestCase(1, 1, 56f, 40f)]
    [TestCase(4, 1, 80f, 40f)]
    [TestCase(2, 3, 64f, 56f)]
    public void FootprintCenter_UsesOrientedSizeFromTopLeft(int width, int height, float x, float y)
    {
        Assert.That(new GridDropAnchor(3, 2).FootprintCenter(16, width, height), Is.EqualTo(new PointerPoint(x, y)));
    }

    [Test]
    public void TryFromLocal_RejectsOverflowBeforeConvertingToInteger()
    {
        Assert.That(GridDropAnchor.TryFromLocal(new PointerPoint(float.MaxValue, 0), float.Epsilon,
            int.MaxValue, 1, out _), Is.False);
    }

    [Test]
    public void Constructor_RejectsNegativeCells()
    {
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => new GridDropAnchor(-1, 0)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => new GridDropAnchor(0, -1)));
    }
}
