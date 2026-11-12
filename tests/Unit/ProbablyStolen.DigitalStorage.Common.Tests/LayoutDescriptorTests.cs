using System;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class LayoutDescriptorTests
{
    [Test]
    public void TryParse_ReadsTheDesignExample()
    {
        Assert.That(LayoutDescriptor.TryParse("A;S2x2x16*3;L*4;S1x1x16;S12x2x16", out var descriptor), Is.True);

        var pages = descriptor!.Pages;
        Assert.That(pages.Count, Is.EqualTo(1 + 3 + 4 + 1 + 1));
        Assert.That(pages[0], Is.EqualTo(PageGeometry.Anchor));
        Assert.That(pages.Skip(1).Take(3), Is.All.EqualTo(StripLayout.PageFor(new BoundingSize(2, 2))));
        Assert.That(pages.Skip(4).Take(4), Is.All.EqualTo(PageGeometry.Legacy));
        Assert.That(pages[8], Is.EqualTo(StripLayout.PageFor(new BoundingSize(1, 1))));
        Assert.That(pages[9].Width, Is.EqualTo(192));
    }

    [TestCase("A")]
    [TestCase("A;L")]
    [TestCase("A;L*15")]
    [TestCase("A;S2x2x16*3;L*4;S1x1x16;S12x2x16")]
    [TestCase("A;S4x4x8;S4x4x16")]
    public void FormatAndTryParse_RoundTripCanonicalText(string text)
    {
        Assert.That(LayoutDescriptor.TryParse(text, out var descriptor), Is.True);
        Assert.That(descriptor!.Format(), Is.EqualTo(text));
        Assert.That(LayoutDescriptor.TryParse(descriptor.Format(), out var again), Is.True);
        Assert.That(again, Is.EqualTo(descriptor));
    }

    [Test]
    public void Format_CollapsesRunsWrittenOneByOne()
    {
        Assert.That(LayoutDescriptor.TryParse("A;L;L;S1x1x16;S1x1x16;L", out var descriptor), Is.True);

        Assert.That(descriptor!.Format(), Is.EqualTo("A;L*2;S1x1x16*2;L"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("2|A")]
    [TestCase("L")]
    [TestCase("S1x1x16")]
    [TestCase("A;A")]
    [TestCase("L;A")]
    [TestCase("A*2")]
    [TestCase("A;")]
    [TestCase(";A")]
    [TestCase("A;;L")]
    [TestCase("a")]
    [TestCase("A;X")]
    [TestCase("A;L*1")]
    [TestCase("A;L*0")]
    [TestCase("A;L*")]
    [TestCase("A;L*02")]
    [TestCase("A;L*+2")]
    [TestCase("A;L*-2")]
    [TestCase("A;L*2*2")]
    [TestCase("A;S0x1x16")]
    [TestCase("A;S1x0x16")]
    [TestCase("A;S13x1x16")]
    [TestCase("A;S1x13x16")]
    [TestCase("A;S1x1x0")]
    [TestCase("A;S1x1x17")]
    [TestCase("A;S1x1")]
    [TestCase("A;S1x1x16x1")]
    [TestCase("A;S01x1x16")]
    [TestCase("A;S 1x1x16")]
    [TestCase("A;S1x1x16 ")]
    [TestCase("A;S")]
    [TestCase("A;L*32768")]
    [TestCase("A;L*99999")]
    [TestCase("A;L*100000")]
    public void TryParse_RejectsInvalidText(string? text)
    {
        Assert.That(LayoutDescriptor.TryParse(text, out var descriptor), Is.False);
        Assert.That(descriptor, Is.Null);
    }

    [Test]
    public void TryParse_AcceptsExactlyThePageLimit()
    {
        Assert.That(LayoutDescriptor.TryParse("A;L*32767", out var descriptor), Is.True);
        Assert.That(descriptor!.Pages.Count, Is.EqualTo(LayoutDescriptor.MaxPages));
        Assert.That(LayoutDescriptor.TryParse("A;L*32767;L", out _), Is.False);
        Assert.That(LayoutDescriptor.TryParse("A;L*20000;L*20000", out _), Is.False);
    }

    [Test]
    public void TryParse_RejectsTextLongerThanTheLimit()
    {
        var text = "A" + string.Concat(Enumerable.Repeat(";L", LayoutDescriptor.MaxTextLength));

        Assert.That(text.Length, Is.GreaterThan(LayoutDescriptor.MaxTextLength));
        Assert.That(LayoutDescriptor.TryParse(text, out _), Is.False);
    }

    [Test]
    public void Legacy_IsAnchorFollowedByLegacyPages()
    {
        Assert.That(LayoutDescriptor.AnchorOnly().Format(), Is.EqualTo("A"));
        Assert.That(LayoutDescriptor.AnchorOnly(), Is.EqualTo(LayoutDescriptor.Legacy(1)),
            "A new cell records the same layout an empty schema 1 cell gets from migration.");
        Assert.That(LayoutDescriptor.Legacy(1).Format(), Is.EqualTo("A"));
        Assert.That(LayoutDescriptor.Legacy(2).Format(), Is.EqualTo("A;L"));
        Assert.That(LayoutDescriptor.Legacy(16).Format(), Is.EqualTo("A;L*15"));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => LayoutDescriptor.Legacy(0)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => LayoutDescriptor.Legacy(32769)));
    }

    [Test]
    public void WithAppended_AddsAtTheEndAndNeverMutates()
    {
        var original = LayoutDescriptor.Legacy(1);
        var strip = StripLayout.PageFor(new BoundingSize(3, 1));

        var appended = original.WithAppended(strip);

        Assert.That(original.Format(), Is.EqualTo("A"));
        Assert.That(appended.Format(), Is.EqualTo("A;S3x1x16"));
        Assert.Throws<ArgumentException>(new Action(() => original.WithAppended(PageGeometry.Anchor)));
        Assert.Throws<InvalidOperationException>(new Action(() =>
            LayoutDescriptor.Legacy(LayoutDescriptor.MaxPages).WithAppended(strip)));
    }

    [Test]
    public void Without_RemovesPagesKeepingOrderButNeverTheAnchor()
    {
        Assert.That(LayoutDescriptor.TryParse("A;L*2;S1x1x16;S2x2x16", out var descriptor), Is.True);

        var removed = descriptor!.Without(new[] { 1, 2, 2 });

        Assert.That(removed.Format(), Is.EqualTo("A;S1x1x16;S2x2x16"));
        Assert.That(descriptor.Format(), Is.EqualTo("A;L*2;S1x1x16;S2x2x16"));
        Assert.Throws<ArgumentException>(new Action(() => descriptor.Without(new[] { 0 })));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => descriptor.Without(new[] { 5 })));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => descriptor.Without(new[] { -1 })));
        Assert.Throws<ArgumentNullException>(new Action(() => descriptor.Without(null!)));
    }

    [Test]
    public void Equality_ComparesPages()
    {
        Assert.That(LayoutDescriptor.TryParse("A;L;L", out var first), Is.True);
        Assert.That(LayoutDescriptor.TryParse("A;L*2", out var second), Is.True);

        Assert.That(first, Is.EqualTo(second));
        Assert.That(first!.GetHashCode(), Is.EqualTo(second!.GetHashCode()));
        Assert.That(first, Is.Not.EqualTo(LayoutDescriptor.Legacy(2)));
    }
}
