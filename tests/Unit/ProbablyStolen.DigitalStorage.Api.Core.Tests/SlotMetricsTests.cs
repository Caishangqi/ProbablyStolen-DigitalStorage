using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Client;

namespace ProbablyStolen.DigitalStorage.Api.Core.Tests;

[TestFixture]
public sealed class SlotMetricsTests
{
    [TestCase(SizeTier.Default, 40, 36, 12, 3, 2)]
    [TestCase(SizeTier.Small, 48, 44, 14, 4, 2)]
    [TestCase(SizeTier.Medium, 56, 52, 17, 4, 3)]
    [TestCase(SizeTier.Large, 64, 60, 19, 5, 3)]
    public void Size_ScalesTheSlotButtonAndBadge(SizeTier tier, int slot, int button, int font, int badgeX, int badgeY)
    {
        var metrics = SlotMetrics.Create(tier, tier, IconMarginTier.Standard, SizeTier.Default);

        Assert.That(metrics.SlotPixels, Is.EqualTo(slot));
        Assert.That(metrics.ButtonPixels, Is.EqualTo(button).And.EqualTo(slot - 2 * SlotMetrics.BorderPadding));
        Assert.That(metrics.BadgeFontSize, Is.EqualTo(font));
        Assert.That((metrics.BadgeOffsetX, metrics.BadgeOffsetY), Is.EqualTo((badgeX, badgeY)));
        Assert.That(metrics.Scale, Is.EqualTo(slot / 40f));
    }

    [TestCase(SizeTier.Default, IconMarginTier.Compact, 3, 30)]
    [TestCase(SizeTier.Default, IconMarginTier.Standard, 6, 24)]
    [TestCase(SizeTier.Default, IconMarginTier.Loose, 9, 18)]
    [TestCase(SizeTier.Small, IconMarginTier.Compact, 4, 36)]
    [TestCase(SizeTier.Small, IconMarginTier.Standard, 7, 30)]
    [TestCase(SizeTier.Small, IconMarginTier.Loose, 11, 22)]
    [TestCase(SizeTier.Medium, IconMarginTier.Compact, 4, 44)]
    [TestCase(SizeTier.Medium, IconMarginTier.Standard, 8, 36)]
    [TestCase(SizeTier.Medium, IconMarginTier.Loose, 13, 26)]
    [TestCase(SizeTier.Large, IconMarginTier.Compact, 5, 50)]
    [TestCase(SizeTier.Large, IconMarginTier.Standard, 10, 40)]
    [TestCase(SizeTier.Large, IconMarginTier.Loose, 14, 32)]
    public void Margin_ScalesWithTheSlotAndSetsThePictureArea(SizeTier tier, IconMarginTier margin, int inset, int icon)
    {
        var metrics = SlotMetrics.Create(tier, tier, margin, SizeTier.Default);

        Assert.That(metrics.IconInset, Is.EqualTo(inset));
        Assert.That(metrics.IconPixels, Is.EqualTo(icon).And.EqualTo(metrics.ButtonPixels - 2 * inset));
        Assert.That(metrics.MarginTier, Is.EqualTo(margin));
    }

    [TestCase(SizeTier.Default, 40, 32)]
    [TestCase(SizeTier.Small, 48, 38)]
    [TestCase(SizeTier.Medium, 56, 45)]
    [TestCase(SizeTier.Large, 64, 51)]
    public void DockSize_SetsTheDockButtonAndIconWithoutTouchingTheSlot(SizeTier dock, int button, int icon)
    {
        var metrics = SlotMetrics.Create(SizeTier.Small, SizeTier.Small, IconMarginTier.Standard, dock);

        Assert.That((metrics.DockTier, metrics.DockButtonPixels, metrics.DockIconPixels), Is.EqualTo((dock, button, icon)));
        Assert.That((metrics.SlotPixels, metrics.IconPixels, metrics.BadgeFontSize), Is.EqualTo((48, 30, 14)));
    }

    [Test]
    public void Default_IsTheOriginalSlot()
    {
        var metrics = SlotMetrics.Default;

        Assert.That((metrics.RequestedTier, metrics.EffectiveTier, metrics.MarginTier, metrics.IsReduced),
            Is.EqualTo((SizeTier.Default, SizeTier.Default, IconMarginTier.Standard, false)));
        Assert.That((metrics.SlotPixels, metrics.ButtonPixels, metrics.IconInset, metrics.IconPixels, metrics.BadgeFontSize),
            Is.EqualTo((40, 36, 6, 24, 12)));
        Assert.That((metrics.DockTier, metrics.DockButtonPixels, metrics.DockIconPixels),
            Is.EqualTo((SizeTier.Default, 40, 32)));
    }

    [Test]
    public void ReducedSize_MeasuresTheSizeInUseAndKeepsTheChoice()
    {
        var metrics = SlotMetrics.Create(SizeTier.Large, SizeTier.Small, IconMarginTier.Compact, SizeTier.Default);

        Assert.That(metrics.IsReduced, Is.True);
        Assert.That(metrics.RequestedTier, Is.EqualTo(SizeTier.Large));
        Assert.That(metrics.SlotPixels, Is.EqualTo(48));
        Assert.That(metrics.IconInset, Is.EqualTo(4));
    }

    [TestCase(0, 0)]
    [TestCase(1, 2)]
    [TestCase(3, 5)]
    [TestCase(32, 51)]
    public void Scaled_RoundsToTheNearestUnit(int baseUnits, int large)
    {
        Assert.That(SlotMetrics.Create(SizeTier.Large, SizeTier.Large, IconMarginTier.Standard, SizeTier.Default).Scaled(baseUnits),
            Is.EqualTo(large));
        Assert.That(SlotMetrics.Default.Scaled(baseUnits), Is.EqualTo(baseUnits));
    }

    [Test]
    public void Create_RejectsUndefinedValuesAndASizeAboveTheChoice()
    {
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            SlotMetrics.Create((SizeTier)4, SizeTier.Default, IconMarginTier.Standard, SizeTier.Default)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            SlotMetrics.Create(SizeTier.Large, (SizeTier)(-1), IconMarginTier.Standard, SizeTier.Default)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            SlotMetrics.Create(SizeTier.Small, SizeTier.Medium, IconMarginTier.Standard, SizeTier.Default)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            SlotMetrics.Create(SizeTier.Small, SizeTier.Small, (IconMarginTier)3, SizeTier.Default)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            SlotMetrics.Create(SizeTier.Small, SizeTier.Small, IconMarginTier.Standard, (SizeTier)4)));
    }

    [Test]
    public void Equality_ComparesTheChoiceTheSizeInUseAndTheMargin()
    {
        var a = SlotMetrics.Create(SizeTier.Large, SizeTier.Medium, IconMarginTier.Loose, SizeTier.Default);
        var b = SlotMetrics.Create(SizeTier.Large, SizeTier.Medium, IconMarginTier.Loose, SizeTier.Default);

        Assert.That(a, Is.EqualTo(b));
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        Assert.That(a, Is.Not.EqualTo(SlotMetrics.Create(SizeTier.Medium, SizeTier.Medium, IconMarginTier.Loose, SizeTier.Default)));
        Assert.That(a, Is.Not.EqualTo(SlotMetrics.Create(SizeTier.Large, SizeTier.Large, IconMarginTier.Loose, SizeTier.Default)));
        Assert.That(a, Is.Not.EqualTo(SlotMetrics.Create(SizeTier.Large, SizeTier.Medium, IconMarginTier.Compact, SizeTier.Default)));
        Assert.That(a, Is.Not.EqualTo(SlotMetrics.Create(SizeTier.Large, SizeTier.Medium, IconMarginTier.Loose, SizeTier.Small)));
        Assert.That(a.Equals(null), Is.False);
    }

    [Test]
    public void ChangedEventArgs_RequireBothValues()
    {
        Assert.Throws<ArgumentNullException>((Action)(() => new SlotMetricsChangedEventArgs(null!, SlotMetrics.Default)));
        Assert.Throws<ArgumentNullException>((Action)(() => new SlotMetricsChangedEventArgs(SlotMetrics.Default, null!)));
    }
}
