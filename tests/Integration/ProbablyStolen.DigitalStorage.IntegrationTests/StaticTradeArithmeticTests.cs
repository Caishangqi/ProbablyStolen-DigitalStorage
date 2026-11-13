using System;
using NUnit.Framework;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

/// <summary>
/// Detached arithmetic fixtures based on the target build's directly observed quote and customer-budget order.
/// These tests do not call the game methods or claim to reproduce all item-feature and child-item behavior.
/// </summary>
[TestFixture]
internal sealed class StaticTradeArithmeticTests
{
    [TestCase("base", 100, 0, 0, 0, 100)]
    [TestCase("retail markup", 100, 0, 15, 0, 115)]
    [TestCase("upper-level tip", 100, 0, 15, 0, 115)]
    [TestCase("retail and tip", 100, 0, 30, 0, 130)]
    [TestCase("bargain markup", 100, 0, 0, 25, 125)]
    [TestCase("bargain discount", 100, 0, 0, -25, 75)]
    [TestCase("market and final modifiers", 100, 0, 15, -25, 86)]
    [TestCase("1k component with retail", 100, 0, 15, 0, 115)]
    [TestCase("4k component with retail", 200, 0, 15, 0, 230)]
    [TestCase("16k component with retail", 400, 0, 15, 0, 460)]
    [TestCase("64k component with retail", 800, 0, 15, 0, 920)]
    [TestCase("256k component with retail", 1600, 0, 15, 0, 1840)]
    public void QuoteMatrix_AppliesSeparatedNativeStages(string scenario, long unitValue, int innatePercent,
        int marketPercent, int finalPercent, long expected)
    {
        Assert.That(Quote(unitValue, innatePercent, marketPercent, finalPercent), Is.EqualTo(expected), scenario);
    }

    [TestCase(0, 0)]
    [TestCase(100, 100)]
    [TestCase(288, 288)]
    [TestCase(500, 288)]
    public void CustomerBudget_CapsPaymentAfterQuote(long budget, long expectedPayment)
    {
        var quote = Quote(250, innatePercent: 0, marketPercent: 15, finalPercent: 0);
        Assert.That(Math.Min(quote, budget), Is.EqualTo(expectedPayment));
    }

    private static long Quote(long unitValue, int innatePercent, int marketPercent, int finalPercent)
    {
        var innateValue = Math.Max(0d, unitValue * (1d + innatePercent / 100d));
        var marketValue = innateValue * Math.Max(0d, 1d + marketPercent / 100d);
        var finalValue = marketValue * Math.Max(0d, 1d + finalPercent / 100d);
        return checked((long)Math.Round(Math.Max(0d, finalValue), MidpointRounding.ToEven));
    }
}
