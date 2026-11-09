using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class TerminalClickAmountTests
{
    [TestCase(TerminalClick.Stack, 2L, 4L, 2L)]
    [TestCase(TerminalClick.Stack, 10L, 4L, 4L)]
    [TestCase(TerminalClick.Stack, 3L, 1L, 1L)]
    [TestCase(TerminalClick.Stack, 3L, 0L, 1L)]
    [TestCase(TerminalClick.Stack, 3L, long.MaxValue, 3L)]
    [TestCase(TerminalClick.Half, 10L, 4L, 2L)]
    [TestCase(TerminalClick.Half, 10L, 5L, 3L)]
    [TestCase(TerminalClick.Half, 2L, 4L, 1L)]
    [TestCase(TerminalClick.Half, 1L, 4L, 1L)]
    [TestCase(TerminalClick.All, 12345L, 4L, 12345L)]
    public void For_MapsAe2ClicksToTheRequestedAmount(TerminalClick click, long stored, long stackLimit, long expected)
    {
        Assert.That(TerminalClickAmount.For(click, stored, stackLimit), Is.EqualTo(expected));
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public void For_RejectsAnEmptyEntry(long stored)
    {
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => TerminalClickAmount.For(TerminalClick.All, stored, 4)));
    }

    [Test]
    public void Plan_CombinesSingleUnitStacksUpToOneFullStack()
    {
        // Three rolls stored one at a time, stack limit 4: a left click asks for min(3, 4) = 3 and gets all three.
        Assert.That(TerminalExtractionPlan.Select(new long[] { 1, 1, 1 }, 3), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(TerminalExtractionPlan.Select(new long[] { 1, 1, 1 }, 2), Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void Plan_SkipsStacksThatWouldPassTheAmountButAlwaysTakesTheFirst()
    {
        Assert.That(TerminalExtractionPlan.Select(new long[] { 3, 3, 1 }, 4), Is.EqualTo(new[] { 0, 2 }));
        Assert.That(TerminalExtractionPlan.Select(new long[] { 3, 1 }, 2), Is.EqualTo(new[] { 0 }));
        Assert.That(TerminalExtractionPlan.Select(new long[] { 2, 0, 2 }, 10), Is.EqualTo(new[] { 0, 2 }));
        Assert.That(TerminalExtractionPlan.Select(Array.Empty<long>(), 3), Is.Empty);
    }
}
