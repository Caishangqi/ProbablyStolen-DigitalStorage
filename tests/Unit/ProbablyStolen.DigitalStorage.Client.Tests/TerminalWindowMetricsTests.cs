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
    public void DefaultGrid_KeepsThirtySixSlotsAndFitsGridAndScrollbar()
    {
        Assert.That(TerminalWindowMetrics.Columns * TerminalWindowMetrics.Rows, Is.EqualTo(36));
        Assert.That(TerminalWindowMetrics.GridWidth + TerminalWindowMetrics.ScrollbarGap +
            TerminalWindowMetrics.ScrollbarWidth, Is.EqualTo(TerminalWindowMetrics.Width - 32));
        // Search row, grid and status row with the content spacing fill the frame exactly: no extra space below the
        // status row. 74 is the title, rule and the frame padding above and below.
        Assert.That(32 + TerminalWindowMetrics.GetGridHeight(TerminalWindowMetrics.Rows) + 20 + 2 * 6,
            Is.EqualTo(TerminalWindowMetrics.Height - 74));
        Assert.That(TerminalWindowMetrics.GetHeight(TerminalWindowMetrics.Rows), Is.EqualTo(TerminalWindowMetrics.Height));
        Assert.That(TerminalWindowMetrics.GetScale(2560, 1440, 2.67f), Is.EqualTo(0.5f));
    }

    [Test]
    public void Height_FollowsOnlyTheGrid()
    {
        // The function rail sits outside the frame, so short grids are not padded for it.
        Assert.That(TerminalWindowMetrics.GetHeight(TerminalWindowMetrics.MinimumRows),
            Is.EqualTo(TerminalWindowMetrics.Chrome + TerminalWindowMetrics.GetGridHeight(TerminalWindowMetrics.MinimumRows)));
    }

    [Test]
    public void Width_IsTheBodyPlusEqualFramePadding()
    {
        // No button column inside the frame, and 16 units of frame padding on each side.
        Assert.That(TerminalWindowMetrics.Width, Is.EqualTo(16 + 384 + 4 + 12 + 16));
        Assert.That(TerminalWindowMetrics.Width,
            Is.EqualTo(TerminalWindowMetrics.HorizontalChrome + TerminalWindowMetrics.GridWidth +
                TerminalWindowMetrics.ScrollbarGap + TerminalWindowMetrics.ScrollbarWidth));
    }

    [Test]
    public void DockButtons_MatchOneGridSlot()
    {
        // Same side and gap as a grid slot, taken from the grid constants.
        Assert.That(TerminalWindowMetrics.DockButton, Is.EqualTo(TerminalWindowMetrics.CellPixels));
        Assert.That(TerminalWindowMetrics.DockSpacing, Is.EqualTo(TerminalWindowMetrics.CellSpacing));
        Assert.That(TerminalWindowMetrics.DockWidth,
            Is.EqualTo(TerminalWindowMetrics.DockButton + 2 * TerminalWindowMetrics.DockPadding));
    }

    [TestCase(TerminalSize.Small, 5)]
    [TestCase(TerminalSize.Medium, 10)]
    [TestCase(TerminalSize.Tall, 15)]
    [TestCase(TerminalSize.Full, 20)]
    public void Rows_ScaleByQuarterOfTheRowsThatFit(TerminalSize size, int expected)
    {
        // 1080 px at canvas scale 2 is 1080 design units: (1080 - 80 - 138 + 3) / 43 = 20 rows fit.
        Assert.That(TerminalWindowMetrics.GetRows(size, 1080f, 2f), Is.EqualTo(expected));
    }

    [TestCase(1080f, 2f)]
    [TestCase(720f, 1.335f)]
    [TestCase(1440f, 2.67f)]
    [TestCase(2160f, 4f)]
    [TestCase(480f, 2f)]
    public void FullSize_FitsTheViewportWithoutShrinking(float height, float canvas)
    {
        var width = height * 16f / 9f;
        var rows = TerminalWindowMetrics.GetRows(TerminalSize.Full, height, canvas);
        var scale = TerminalWindowMetrics.GetScale(width, height, canvas, TerminalWindowMetrics.GetHeight(rows));
        Assert.That(TerminalWindowMetrics.GetHeight(rows) * scale * canvas, Is.LessThanOrEqualTo(height));
        // The counted rows already fit, so height never shrinks the window more than the width bound does.
        Assert.That(scale, Is.EqualTo(TerminalWindowMetrics.GetScale(width, height, canvas, 1)));
    }

    [Test]
    public void Scale_UsesTheWindowsOwnDesignWidth()
    {
        // 800 px wide at canvas 1: the width bound is 800 / width * 0.4, below the native 0.5 scale.
        Assert.That(TerminalWindowMetrics.GetScale(800f, 4000f, 1f, TerminalWindowMetrics.Width, 100),
            Is.EqualTo(TerminalWindowMetrics.GetScale(800f, 4000f, 1f, 100)), "The old overload is the terminal width.");
        Assert.That(TerminalWindowMetrics.GetScale(800f, 4000f, 1f, 800, 100), Is.EqualTo(0.4f).Within(1e-6f),
            "A wider window shrinks more.");
        Assert.That(TerminalWindowMetrics.GetScale(800f, 4000f, 1f, 0, 100), Is.EqualTo(TerminalWindowMetrics.NativeScale));
    }

    [TestCase(300f, 2f)]
    [TestCase(100f, 1f)]
    public void Rows_RespectTheMinimum(float height, float canvas)
    {
        foreach (TerminalSize size in System.Enum.GetValues(typeof(TerminalSize)))
        {
            Assert.That(TerminalWindowMetrics.GetRows(size, height, canvas), Is.EqualTo(TerminalWindowMetrics.MinimumRows));
        }
    }

    [TestCase(0f, 2f)]
    [TestCase(1080f, 0f)]
    [TestCase(float.NaN, 2f)]
    [TestCase(1080f, float.PositiveInfinity)]
    public void Rows_InvalidViewportFallsBackToTheDefault(float height, float canvas)
    {
        Assert.That(TerminalWindowMetrics.GetRows(TerminalSize.Full, height, canvas), Is.EqualTo(TerminalWindowMetrics.Rows));
    }
}
