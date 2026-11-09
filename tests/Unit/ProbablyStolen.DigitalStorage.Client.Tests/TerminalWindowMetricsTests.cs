using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class TerminalWindowMetricsTests
{
    [TestCase(960, 540, 1f)]
    [TestCase(1280, 720, 1.335f)]
    [TestCase(1920, 1080, 2f)]
    [TestCase(2560, 1440, 2.67f)]
    [TestCase(3840, 2160, 4f)]
    [TestCase(640, 480, 2f)]
    [TestCase(300, 200, 1f)]
    public void Scale_RespectsViewportBoundsWithoutUpscaling(float width, float height, float canvas)
    {
        var scale = TerminalWindowMetrics.GetScale(width, height, canvas);
        Assert.That(scale, Is.GreaterThan(0).And.LessThanOrEqualTo(0.5f));
        Assert.That(TerminalWindowMetrics.Width * scale * canvas,
            Is.LessThanOrEqualTo(width * TerminalWindowMetrics.MaximumWidthFraction + 0.01f));
        Assert.That(TerminalWindowMetrics.Height * scale * canvas,
            Is.LessThanOrEqualTo(height * TerminalWindowMetrics.MaximumHeightFraction + 0.01f));
    }

    [TestCase(0, 540, 1)]
    [TestCase(960, -1, 1)]
    [TestCase(960, 540, 0)]
    [TestCase(float.NaN, 540, 1)]
    [TestCase(960, float.PositiveInfinity, 1)]
    public void InvalidViewport_ReturnsSafeNativeScale(float width, float height, float canvas)
    {
        Assert.That(TerminalWindowMetrics.GetScale(width, height, canvas), Is.EqualTo(0.5f));
    }

    [TestCase(-1000, 960, 176, -392)]
    [TestCase(1000, 960, 176, 392)]
    [TestCase(0, 960, 176, 0)]
    [TestCase(25, 960, 176, 25)]
    [TestCase(1000, 540, 188, 176)]
    [TestCase(-1000, 540, 188, -176)]
    [TestCase(1000, 100, 188, 0)]
    public void WindowOffset_KeepsTheEntireWindowInsideTheViewport(float requested, float viewport, float window, float expected)
    {
        Assert.That(TerminalWindowMetrics.GetClampedOffset(requested, viewport, window), Is.EqualTo(expected));
    }

    [Test]
    public void WindowOffset_HandlesMarginsAndInvalidGeometry()
    {
        Assert.That(TerminalWindowMetrics.GetClampedOffset(1000, 960, 176, 8), Is.EqualTo(384));
        Assert.That(TerminalWindowMetrics.GetClampedOffset(float.NaN, 960, 176), Is.Zero);
        Assert.That(TerminalWindowMetrics.GetClampedOffset(100, 0, 176), Is.Zero);
    }

    [Test]
    public void CompactGrid_KeepsAllTwentyEightSlotsAndFitsTheContent()
    {
        Assert.That(TerminalWindowMetrics.Columns * TerminalWindowMetrics.Rows, Is.EqualTo(28));
        var gridWidth = TerminalWindowMetrics.Columns * TerminalWindowMetrics.CellPixels +
            (TerminalWindowMetrics.Columns - 1) * TerminalWindowMetrics.CellSpacing;
        var gridHeight = TerminalWindowMetrics.Rows * TerminalWindowMetrics.CellPixels +
            (TerminalWindowMetrics.Rows - 1) * TerminalWindowMetrics.CellSpacing;
        Assert.That(gridWidth, Is.LessThanOrEqualTo(TerminalWindowMetrics.Width - 32));
        Assert.That(32 + 32 + gridHeight + 40 + 3 * 6,
            Is.LessThanOrEqualTo(TerminalWindowMetrics.Height - 74));
        Assert.That(TerminalWindowMetrics.GetScale(2560, 1440, 2.67f), Is.EqualTo(0.5f));
    }
}
