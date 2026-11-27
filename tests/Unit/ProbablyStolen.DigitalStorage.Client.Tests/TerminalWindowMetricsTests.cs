using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Client;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class TerminalWindowMetricsTests
{
    private static readonly SlotMetrics Default = SlotMetrics.Default;

    private static SlotMetrics At(SizeTier tier) => SlotMetrics.Create(tier, tier, IconMarginTier.Standard, SizeTier.Default);

    [TestCase(960, 540, 1f)]
    [TestCase(1280, 720, 1.335f)]
    [TestCase(1920, 1080, 2f)]
    [TestCase(2560, 1440, 2.67f)]
    [TestCase(3840, 2160, 4f)]
    [TestCase(640, 480, 2f)]
    [TestCase(300, 200, 1f)]
    public void Scale_RespectsViewportBoundsWithoutUpscaling(float width, float height, float canvas)
    {
        var windowWidth = TerminalWindowMetrics.GetWidth(Default);
        var windowHeight = TerminalWindowMetrics.GetHeight(Default, TerminalWindowMetrics.Rows);
        var scale = TerminalWindowMetrics.GetScale(width, height, canvas, windowWidth, windowHeight);
        Assert.That(scale, Is.GreaterThan(0).And.LessThanOrEqualTo(0.5f));
        Assert.That(windowWidth * scale * canvas,
            Is.LessThanOrEqualTo(width * TerminalWindowMetrics.MaximumWidthFraction + 0.01f));
        Assert.That(windowHeight * scale * canvas,
            Is.LessThanOrEqualTo(height * TerminalWindowMetrics.MaximumHeightFraction + 0.01f));
    }

    [TestCase(0, 540, 1)]
    [TestCase(960, -1, 1)]
    [TestCase(960, 540, 0)]
    [TestCase(float.NaN, 540, 1)]
    [TestCase(960, float.PositiveInfinity, 1)]
    public void InvalidViewport_ReturnsSafeNativeScale(float width, float height, float canvas)
    {
        Assert.That(TerminalWindowMetrics.GetScale(width, height, canvas, 432, 310), Is.EqualTo(0.5f));
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
        Assert.That(TerminalWindowMetrics.GetGridWidth(Default) + TerminalWindowMetrics.ScrollbarGap +
            TerminalWindowMetrics.ScrollbarWidth, Is.EqualTo(TerminalWindowMetrics.GetWidth(Default) - 32));
        // Search row, grid and status row with the content spacing fill the frame exactly: no extra space below the
        // status row. 74 is the title, rule and the frame padding above and below.
        Assert.That(32 + TerminalWindowMetrics.GetGridHeight(Default, TerminalWindowMetrics.Rows) + 20 + 2 * 6,
            Is.EqualTo(TerminalWindowMetrics.GetHeight(Default, TerminalWindowMetrics.Rows) - 74));
    }

    [Test]
    public void Height_FollowsOnlyTheGrid()
    {
        // The function rail sits outside the frame, so short grids are not padded for it.
        Assert.That(TerminalWindowMetrics.GetHeight(Default, TerminalWindowMetrics.MinimumRows),
            Is.EqualTo(TerminalWindowMetrics.GetChrome(Default) + TerminalWindowMetrics.GetGridHeight(Default, TerminalWindowMetrics.MinimumRows)));
    }

    [TestCase(SizeTier.Default, 32, 20, 14, 0, 138)]
    [TestCase(SizeTier.Small, 34, 21, 15, 1, 141)]
    [TestCase(SizeTier.Medium, 36, 22, 16, 2, 144)]
    [TestCase(SizeTier.Large, 40, 24, 17, 3, 150)]
    public void TextRows_GrowBySmallerStepsThanTheSlots(SizeTier tier, int search, int status, int statusFont, int step,
        int chrome)
    {
        var slots = At(tier);
        Assert.That(TerminalWindowMetrics.GetSearchHeight(slots), Is.EqualTo(search));
        Assert.That(TerminalWindowMetrics.GetStatusHeight(slots), Is.EqualTo(status));
        Assert.That(TerminalWindowMetrics.GetStatusFontSize(slots), Is.EqualTo(statusFont));
        Assert.That(TerminalWindowMetrics.TextStep(slots), Is.EqualTo(step));
        // Title 40, rule 2, padding 16 twice and two 6-unit gaps around the search and status rows.
        Assert.That(TerminalWindowMetrics.GetChrome(slots), Is.EqualTo(40 + 2 + 32 + 12 + search + status).And.EqualTo(chrome));
        Assert.That(TerminalWindowMetrics.GetHeight(slots, 3), Is.EqualTo(chrome + TerminalWindowMetrics.GetGridHeight(slots, 3)));
    }

    [Test]
    public void TextRows_FollowTheSlotSizeInUseNotTheChoiceOrTheDock()
    {
        var reduced = SlotMetrics.Create(SizeTier.Large, SizeTier.Small, IconMarginTier.Standard, SizeTier.Large);
        Assert.That(TerminalWindowMetrics.GetSearchHeight(reduced), Is.EqualTo(34));
        Assert.That(TerminalWindowMetrics.TextStep(reduced), Is.EqualTo(1));
    }

    [TestCase(SizeTier.Default, 384, 432)]
    [TestCase(SizeTier.Small, 456, 504)]
    [TestCase(SizeTier.Medium, 528, 576)]
    [TestCase(SizeTier.Large, 600, 648)]
    public void Width_IsNineWholeSlotsPlusEqualFramePadding(SizeTier tier, int grid, int window)
    {
        var slots = At(tier);
        Assert.That(TerminalWindowMetrics.GetGridWidth(slots), Is.EqualTo(grid));
        // No button column inside the frame, and 16 units of frame padding on each side.
        Assert.That(TerminalWindowMetrics.GetWidth(slots), Is.EqualTo(16 + grid + 4 + 12 + 16).And.EqualTo(window));
    }

    [TestCase(SizeTier.Default, 40, 52, 32)]
    [TestCase(SizeTier.Small, 48, 60, 38)]
    [TestCase(SizeTier.Medium, 56, 68, 45)]
    [TestCase(SizeTier.Large, 64, 76, 51)]
    public void DockButtons_FollowTheirOwnSize(SizeTier tier, int button, int dock, int icon)
    {
        // The default and the large slot give the same dock: the dock never reads the slot size.
        foreach (var slotTier in new[] { SizeTier.Default, SizeTier.Large })
        {
            var slots = SlotMetrics.Create(slotTier, slotTier, IconMarginTier.Standard, tier);
            Assert.That(TerminalWindowMetrics.GetDockButton(slots), Is.EqualTo(button));
            Assert.That(TerminalWindowMetrics.GetDockWidth(slots),
                Is.EqualTo(button + 2 * TerminalWindowMetrics.DockPadding).And.EqualTo(dock));
            Assert.That(TerminalWindowMetrics.GetDockIcon(slots), Is.EqualTo(icon));
        }

        Assert.That(TerminalWindowMetrics.DockSpacing, Is.EqualTo(TerminalWindowMetrics.CellSpacing));
    }

    [Test]
    public void DockButtons_DoNotChangeTheWindowOrItsSizeFit()
    {
        var plain = SlotMetrics.Create(SizeTier.Large, SizeTier.Large, IconMarginTier.Standard, SizeTier.Default);
        var largeDock = SlotMetrics.Create(SizeTier.Large, SizeTier.Large, IconMarginTier.Standard, SizeTier.Large);
        Assert.That(TerminalWindowMetrics.GetWidth(largeDock), Is.EqualTo(TerminalWindowMetrics.GetWidth(plain)));
        Assert.That(TerminalWindowMetrics.GetHeight(largeDock, 4), Is.EqualTo(TerminalWindowMetrics.GetHeight(plain, 4)));
        Assert.That(TerminalWindowMetrics.Resolve(SizeTier.Large, IconMarginTier.Standard, SizeTier.Large, 700f, 4000f, 1f)
            .EffectiveTier, Is.EqualTo(SizeTier.Small), "Only the slot size steps down; the dock is outside the frame.");
    }

    [TestCase(TerminalSize.Small, 5)]
    [TestCase(TerminalSize.Medium, 10)]
    [TestCase(TerminalSize.Tall, 15)]
    [TestCase(TerminalSize.Full, 20)]
    public void Rows_ScaleByQuarterOfTheRowsThatFit(TerminalSize size, int expected)
    {
        // 1080 px at canvas scale 2 is 1080 design units: (1080 - 80 - 138 + 3) / 43 = 20 rows fit.
        Assert.That(TerminalWindowMetrics.GetRows(size, Default, 1080f, 2f), Is.EqualTo(expected));
    }

    [TestCase(SizeTier.Small, 16)]
    [TestCase(SizeTier.Medium, 14)]
    [TestCase(SizeTier.Large, 12)]
    public void Rows_CountWholeSlotsOfTheTier(SizeTier tier, int expected)
    {
        // (1080 - 80 - chrome + 3) / (slot + 3) whole rows, the chrome growing with the text rows; never a partial row.
        Assert.That(TerminalWindowMetrics.GetRows(TerminalSize.Full, At(tier), 1080f, 2f), Is.EqualTo(expected));
    }

    [TestCase(1080f, 2f)]
    [TestCase(720f, 1.335f)]
    [TestCase(1440f, 2.67f)]
    [TestCase(2160f, 4f)]
    [TestCase(480f, 2f)]
    public void FullSize_FitsTheViewportWithoutShrinking(float height, float canvas)
    {
        var width = height * 16f / 9f;
        foreach (var tier in new[] { SizeTier.Default, SizeTier.Small, SizeTier.Medium, SizeTier.Large })
        {
            var slots = At(tier);
            var rows = TerminalWindowMetrics.GetRows(TerminalSize.Full, slots, height, canvas);
            var windowHeight = TerminalWindowMetrics.GetHeight(slots, rows);
            var windowWidth = TerminalWindowMetrics.GetWidth(slots);
            var scale = TerminalWindowMetrics.GetScale(width, height, canvas, windowWidth, windowHeight);
            Assert.That(windowHeight * scale * canvas, Is.LessThanOrEqualTo(height), tier.ToString());
            // The counted rows already fit, so height never shrinks the window more than the width bound does.
            Assert.That(scale, Is.EqualTo(TerminalWindowMetrics.GetScale(width, height, canvas, windowWidth, 1)), tier.ToString());
        }
    }

    [Test]
    public void Scale_UsesTheWindowsOwnDesignWidth()
    {
        // 800 px wide at canvas 1: the width bound is 800 / width * 0.4, below the native 0.5 scale.
        Assert.That(TerminalWindowMetrics.GetScale(800f, 4000f, 1f, 800, 100), Is.EqualTo(0.4f).Within(1e-6f),
            "A wider window shrinks more.");
        Assert.That(TerminalWindowMetrics.GetScale(800f, 4000f, 1f, 0, 100), Is.EqualTo(TerminalWindowMetrics.NativeScale));
    }

    [TestCase(300f, 2f)]
    [TestCase(100f, 1f)]
    public void Rows_RespectTheMinimum(float height, float canvas)
    {
        foreach (TerminalSize size in Enum.GetValues(typeof(TerminalSize)))
        {
            Assert.That(TerminalWindowMetrics.GetRows(size, Default, height, canvas), Is.EqualTo(TerminalWindowMetrics.MinimumRows));
        }
    }

    [TestCase(0f, 2f)]
    [TestCase(1080f, 0f)]
    [TestCase(float.NaN, 2f)]
    [TestCase(1080f, float.PositiveInfinity)]
    public void Rows_InvalidViewportFallsBackToTheDefault(float height, float canvas)
    {
        Assert.That(TerminalWindowMetrics.GetRows(TerminalSize.Full, Default, height, canvas), Is.EqualTo(TerminalWindowMetrics.Rows));
    }

    [TestCase(SizeTier.Default)]
    [TestCase(SizeTier.Small)]
    [TestCase(SizeTier.Medium)]
    [TestCase(SizeTier.Large)]
    public void ResolveTier_KeepsTheRequestWhenItFits(SizeTier tier)
    {
        Assert.That(TerminalWindowMetrics.ResolveTier(tier, 1920f, 1080f, 2f), Is.EqualTo(tier));
    }

    [Test]
    public void ResolveTier_StepsDownToTheLargestSizeThatFitsTheWidth()
    {
        // Canvas 1 and the 0.4 width bound: a window fits at native scale when 0.8 * viewport >= its design width.
        Assert.That(TerminalWindowMetrics.ResolveTier(SizeTier.Large, 810f, 4000f, 1f), Is.EqualTo(SizeTier.Large),
            "648 / 0.8 = 810 holds the large window exactly.");
        Assert.That(TerminalWindowMetrics.ResolveTier(SizeTier.Large, 809f, 4000f, 1f), Is.EqualTo(SizeTier.Medium));
        Assert.That(TerminalWindowMetrics.ResolveTier(SizeTier.Large, 630f, 4000f, 1f), Is.EqualTo(SizeTier.Small));
        Assert.That(TerminalWindowMetrics.ResolveTier(SizeTier.Large, 629f, 4000f, 1f), Is.EqualTo(SizeTier.Default));
    }

    [Test]
    public void ResolveTier_StepsDownForTheMinimumRowsHeight()
    {
        // Two large rows: 150 + 2 * 64 + 3 = 281 units; at canvas 1 that needs 281 * 0.5 / 0.95 = 147.9 px.
        Assert.That(TerminalWindowMetrics.ResolveTier(SizeTier.Large, 4000f, 148f, 1f), Is.EqualTo(SizeTier.Large));
        Assert.That(TerminalWindowMetrics.ResolveTier(SizeTier.Large, 4000f, 147f, 1f), Is.EqualTo(SizeTier.Medium));
    }

    [Test]
    public void ResolveTier_FallsBackToTheDefaultWhenNothingFits()
    {
        Assert.That(TerminalWindowMetrics.ResolveTier(SizeTier.Large, 100f, 100f, 1f), Is.EqualTo(SizeTier.Default));
    }

    [TestCase(0f, 1080f, 2f)]
    [TestCase(1920f, float.NaN, 2f)]
    [TestCase(1920f, 1080f, 0f)]
    public void ResolveTier_UnknownViewportKeepsTheRequest(float width, float height, float canvas)
    {
        Assert.That(TerminalWindowMetrics.ResolveTier(SizeTier.Large, width, height, canvas), Is.EqualTo(SizeTier.Large));
    }

    [Test]
    public void ResolveTier_RejectsAnUndefinedRequest()
    {
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => TerminalWindowMetrics.ResolveTier((SizeTier)9, 1920f, 1080f, 2f)));
    }

    [Test]
    public void Resolve_KeepsTheRequestAndMarginBesideTheSizeInUse()
    {
        var slots = TerminalWindowMetrics.Resolve(SizeTier.Large, IconMarginTier.Compact, SizeTier.Medium, 700f, 4000f, 1f);
        Assert.That((slots.RequestedTier, slots.EffectiveTier, slots.MarginTier, slots.IsReduced, slots.DockTier),
            Is.EqualTo((SizeTier.Large, SizeTier.Small, IconMarginTier.Compact, true, SizeTier.Medium)));
    }
}
