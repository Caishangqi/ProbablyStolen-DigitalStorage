using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Tooltips;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Tooltips;

[TestFixture]
public sealed class TooltipContentCacheTests
{
    [TestCase(1000, 60)]
    [TestCase(200, 20)]
    public void PointerFrames_UnchangedContentAndMetrics_ReusesNaturalAndFittedContent(double available, int expectedCharacters)
    {
        var builds = 0;
        var cache = new TooltipContentCache<string>(width => { builds++; return $"panel at {width}"; }, StringComparer.Ordinal);
        Assert.That(cache.RefreshNatural(), Is.True);
        cache.RecordNaturalWidth(600);
        var characters = TooltipContentCache<string>.FitCharacters(cache.NaturalWidth, available);
        var fitted = cache.Fitted(characters);
        cache.RecordApplied(fitted, characters);
        var initialBuilds = builds;

        for (var frame = 0; frame < 100; frame++)
        {
            Assert.That(cache.RefreshNatural(), Is.False);
            Assert.That(TooltipContentCache<string>.FitCharacters(cache.NaturalWidth, available), Is.EqualTo(expectedCharacters));
            Assert.That(cache.Fitted(characters), Is.SameAs(fitted));
            Assert.That(cache.NeedsApply(fitted, characters), Is.False);
        }
        Assert.That(builds, Is.EqualTo(initialBuilds));
        Assert.That(initialBuilds, Is.EqualTo(expectedCharacters == 60 ? 1 : 2));
    }

    [Test]
    public void ExpandToContent_MeasuresLongLinesAndOnlyReflowsWhenViewportIsTooNarrow()
    {
        var cache = new TooltipContentCache<string>(width => width.ToString(), StringComparer.Ordinal);
        cache.SetExpandToContent(true);
        Assert.That(cache.RefreshNatural(), Is.True);
        cache.RecordNaturalWidth(812);
        Assert.That(cache.Natural, Is.EqualTo(TooltipContentCache<string>.UnwrappedCharacters.ToString()));
        Assert.That(cache.FitContent(900, 20), Is.EqualTo(792));
        Assert.That(cache.FitContent(600.5, 20), Is.EqualTo(580));

        cache.SetExpandToContent(false);
        Assert.That(cache.RefreshNatural(), Is.True);
        Assert.That(cache.Natural, Is.EqualTo("60"));
    }

    [TestCase(60)]
    [TestCase(20)]
    public void Submit_UnchangedMutableBuilder_DoesNotRequireMeasurementOrNativeApply(int characters)
    {
        var builder = new StringBuilder("unchanged");
        var cache = Cache(builder);
        cache.RefreshNatural();
        cache.RecordNaturalWidth(600);
        cache.RecordApplied(cache.Fitted(characters), characters);

        cache.Submit();

        Assert.That(cache.RefreshNatural(), Is.False);
        Assert.That(cache.NeedsApply(cache.Fitted(characters), characters), Is.False);
        Assert.That(cache.NaturalWidth, Is.EqualTo(600));
    }

    [Test]
    public void Submit_SameBuilderMutatedAfterAnEarlierRender_UsesLatestContentWithoutAFrameSkip()
    {
        var builder = new StringBuilder("first");
        var cache = Cache(builder);
        cache.RefreshNatural();
        cache.RecordNaturalWidth(300);
        var oldRendering = cache.Fitted(20);
        cache.RecordApplied(oldRendering, 20);

        builder.Clear().Append("second");
        cache.Submit();
        builder.Append(" revision");
        cache.Submit();

        Assert.That(cache.RefreshNatural(), Is.True);
        Assert.That(cache.Natural, Is.EqualTo("second revision:60"));
        Assert.That(cache.Fitted(20), Is.EqualTo("second revision:20"));
        Assert.That(cache.NeedsApply(cache.Fitted(20), 20), Is.True);
        Assert.That(oldRendering, Is.EqualTo("first:20"), "previous outputs stay independent of later cache writes");
    }

    [Test]
    public void WidthChange_EqualTextStillRequiresNativeApply_WithoutRebuildingNaturalMeasurement()
    {
        var builds = 0;
        var cache = new TooltipContentCache<string>(_ => { builds++; return "short text"; }, StringComparer.Ordinal);
        cache.RefreshNatural();
        cache.RecordNaturalWidth(600);
        cache.RecordApplied(cache.Natural, 60);

        var narrow = cache.Fitted(20);
        Assert.That(cache.NeedsApply(narrow, 20), Is.True);
        cache.RecordApplied(narrow, 20);
        Assert.That(cache.RefreshNatural(), Is.False);
        Assert.That(cache.NeedsApply(cache.Fitted(20), 20), Is.False);
        Assert.That(cache.NeedsApply(cache.Fitted(60), 60), Is.True);
        Assert.That(builds, Is.EqualTo(2));
    }

    [TestCase("root")]
    [TestCase("font")]
    [TestCase("locale")]
    [TestCase("scale")]
    [TestCase("width")]
    [TestCase("height")]
    [TestCase("size")]
    public void UpdateMetrics_ChangedContext_InvalidatesMeasurementAndNativeApplyEvenForEqualText(string changed)
    {
        var root = new object();
        var font = new object();
        var locale = new object();
        var cache = new TooltipContentCache<string>(_ => "same rendered text", StringComparer.Ordinal);
        cache.UpdateMetrics(root, font, locale, 1, 8, 16, 16);
        cache.RefreshNatural();
        cache.RecordNaturalWidth(600);
        cache.RecordApplied(cache.Fitted(20), 20);
        Assert.That(cache.UpdateMetrics(root, font, locale, 1, 8, 16, 16), Is.False);

        var changedMetrics = cache.UpdateMetrics(changed == "root" ? new object() : root,
            changed == "font" ? new object() : font, changed == "locale" ? new object() : locale,
            changed == "scale" ? 2 : 1, changed == "width" ? 16 : 8,
            changed == "height" ? 20 : 16, changed == "size" ? 20 : 16);

        Assert.That(changedMetrics, Is.True);
        Assert.That(cache.RefreshNatural(), Is.True);
        Assert.That(cache.NeedsApply(cache.Fitted(20), 20), Is.True);
    }

    [Test]
    public void Submit_HashCollision_DoesNotSuppressChangedContent()
    {
        var content = "first";
        var cache = new TooltipContentCache<string>(_ => content, new ConstantHashComparer());
        cache.RefreshNatural();
        cache.RecordNaturalWidth(100);
        cache.RecordApplied(cache.Natural, 60);
        content = "different";
        cache.Submit();

        Assert.That(cache.RefreshNatural(), Is.True);
        Assert.That(cache.NeedsApply(cache.Natural, 60), Is.True);
    }

    private static TooltipContentCache<string> Cache(StringBuilder builder) =>
        new(width => $"{builder}:{width}", StringComparer.Ordinal);

    private sealed class ConstantHashComparer : IEqualityComparer<string>
    {
        public bool Equals(string? left, string? right) => string.Equals(left, right, StringComparison.Ordinal);
        public int GetHashCode(string value) => 1;
    }
}
