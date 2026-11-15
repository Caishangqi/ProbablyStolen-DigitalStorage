using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class SubScreenMetricsTests
{
    [TestCase(true, 288f)]
    [TestCase(false, 454f)]
    public void SmallContent_OnlyFittedPageShrinks(bool fit, float expected)
        => Assert.That(SubScreenMetrics.GetLength(454, 256, 32, fit), Is.EqualTo(expected));

    [TestCase(true)]
    [TestCase(false)]
    public void LargeContent_GrowsWithItsChrome(bool fit)
        => Assert.That(SubScreenMetrics.GetLength(454, 576, 32, fit), Is.EqualTo(608));

    [Test]
    public void InvalidMeasurement_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => SubScreenMetrics.GetLength(0, 200, 32, true)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => SubScreenMetrics.GetLength(454, float.NaN, 32, true)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => SubScreenMetrics.GetLength(454, 200, -1, true)));
    }
}
