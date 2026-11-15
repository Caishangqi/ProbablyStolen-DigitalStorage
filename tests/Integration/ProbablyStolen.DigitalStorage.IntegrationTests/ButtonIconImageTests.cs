using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class ButtonIconImageTests
{
    [Test]
    public void TransparentImage_IsRejected() =>
        Assert.That(ButtonIconImage.HasVisiblePixels(16, 16, new byte[256]), Is.False);

    [TestCase(0, 0)]
    [TestCase(15, 15)]
    public void VisibleEdge_IsAccepted(int x, int y)
    {
        var alpha = new byte[256];
        alpha[y * 16 + x] = 1;
        Assert.That(ButtonIconImage.HasVisiblePixels(16, 16, alpha), Is.True);
    }

    [TestCase(48, 16, 768)]
    [TestCase(16, 48, 768)]
    [TestCase(16, 16, 255)]
    [TestCase(16, 16, 257)]
    [TestCase(0, 0, 0)]
    public void WrongDimensionsOrPixelCount_AreRejected(int width, int height, int pixelCount)
    {
        var alpha = new byte[pixelCount];
        if (alpha.Length > 0) alpha[0] = 255;
        Assert.That(ButtonIconImage.HasVisiblePixels(width, height, alpha), Is.False);
    }
}
