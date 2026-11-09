using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class DrivePriorityTests
{
    [TestCase(0, 0)]
    [TestCase(999, 999)]
    [TestCase(-999, -999)]
    [TestCase(1000, 999)]
    [TestCase(-1000, -999)]
    [TestCase(long.MaxValue, 999)]
    [TestCase(long.MinValue, -999)]
    public void Clamp_KeepsRange(long value, int expected)
    {
        Assert.That(DrivePriority.Clamp(value), Is.EqualTo(expected));
    }

    [TestCase("0", 0)]
    [TestCase(" 42 ", 42)]
    [TestCase("-7", -7)]
    [TestCase("+7", 7)]
    [TestCase("5000", 999)]
    [TestCase("-5000", -999)]
    [TestCase("99999999999999999999999", 999)]
    [TestCase("-99999999999999999999999", -999)]
    public void TryParse_WholeNumbers_AreClamped(string text, int expected)
    {
        Assert.That(DrivePriority.TryParse(text, out var priority), Is.True);
        Assert.That(priority, Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("-")]
    [TestCase("1.5")]
    [TestCase("abc")]
    [TestCase("1e3")]
    public void TryParse_RejectsNonIntegers(string? text)
    {
        Assert.That(DrivePriority.TryParse(text, out var priority), Is.False);
        Assert.That(priority, Is.EqualTo(DrivePriority.Default));
    }

    [Test]
    public void CellView_DefaultsToZeroAndClamps()
    {
        var partition = CellPartition.Unfiltered;
        var pages = new[] { PageView.Free };
        Assert.That(new CellView(CellLayout.Cell1K, pages, new ItemKey[0], partition).Priority, Is.EqualTo(0));
        Assert.That(new CellView(CellLayout.Cell1K, pages, new ItemKey[0], partition, 4000).Priority, Is.EqualTo(999));
    }
}
