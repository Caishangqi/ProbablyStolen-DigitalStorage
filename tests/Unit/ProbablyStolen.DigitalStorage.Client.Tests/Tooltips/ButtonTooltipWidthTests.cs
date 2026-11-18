using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Tooltips;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Tooltips;

[TestFixture]
public sealed class ButtonTooltipWidthTests
{
    [TestCase(0f, 28, 29f)]
    [TestCase(28f, 28, 57f)]
    [TestCase(79.25f, 28, 109f)]
    [TestCase(79.25f, 12, 93f)]
    [TestCase(190.75f, 28, 220f)]
    public void TryFit_MeasuredWidestLine_UsesActualPaddingAndRoundsUp(float widestLine, int padding, float expected)
    {
        var fits = ButtonTooltipWidth.TryFit(widestLine, padding, 1920, 1f, out var width);

        Assert.That(fits, Is.True);
        Assert.That(width, Is.EqualTo(expected));
    }

    [TestCase(1920, 1f, 350f)]
    [TestCase(750, 2f, 350f)]
    [TestCase(699, 2f, 349f)]
    [TestCase(301, 1.25f, 240f)]
    [TestCase(310, 3f, 103f)]
    public void TryFit_LongContent_ClampsWithinPanelAndViewport(int viewportPixels, float canvasScale, float expected)
    {
        var fits = ButtonTooltipWidth.TryFit(float.MaxValue, 28, viewportPixels, canvasScale, out var width);

        Assert.That(fits, Is.True);
        Assert.That(width, Is.EqualTo(expected));
        Assert.That(width * (double)canvasScale, Is.LessThanOrEqualTo(viewportPixels));
    }

    [TestCase(-1f, 28, 200, 1f)]
    [TestCase(float.NaN, 28, 200, 1f)]
    [TestCase(float.PositiveInfinity, 28, 200, 1f)]
    [TestCase(50f, -1, 200, 1f)]
    [TestCase(50f, 350, 1920, 1f)]
    [TestCase(50f, 28, 0, 1f)]
    [TestCase(50f, 28, -1, 1f)]
    [TestCase(50f, 28, 200, 0f)]
    [TestCase(50f, 28, 200, -1f)]
    [TestCase(50f, 28, 200, float.NaN)]
    [TestCase(50f, 28, 200, float.PositiveInfinity)]
    [TestCase(50f, 28, 57, 2f)]
    public void TryFit_InvalidDimensionsOrNoRoomForText_ReturnsNoWidth(float widestLine, int padding,
        int viewportPixels, float canvasScale)
    {
        var fits = ButtonTooltipWidth.TryFit(widestLine, padding, viewportPixels, canvasScale, out var width);

        Assert.That(fits, Is.False);
        Assert.That(width, Is.Zero);
    }
}
