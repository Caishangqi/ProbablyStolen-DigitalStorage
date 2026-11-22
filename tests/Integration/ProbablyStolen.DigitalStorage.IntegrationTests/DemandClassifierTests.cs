using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Compat.ClientDemandHighlight;

namespace ProbablyStolen.DigitalStorage.Compat.ClientDemandHighlight.Tests;

[TestFixture]
public sealed class DemandClassifierTests
{
    [TestCase(false, false, false, null)]
    [TestCase(true, false, false, 0x4DE659)]
    [TestCase(false, true, false, 0xFFB31A)]
    [TestCase(false, false, true, 0xF23833)]
    [TestCase(false, true, true, 0xF23833)]
    [TestCase(true, true, true, 0xF23833)]
    public void Buying_UsesConfiguredSeverity(bool normal, bool stolen, bool contraband, int? expected)
    {
        var actual = DemandClassifier.Classify(DemandKind.Buying, buying: true, wanted: true,
            contraband, stolen, normal, stolen, contraband);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void Contraband_DisabledDoesNotFallBackToStolen()
    {
        var actual = DemandClassifier.Classify(DemandKind.Buying, true, true, true, true,
            true, true, false);
        Assert.That(actual, Is.Null);
    }

    [Test]
    public void Inspection_OnlyClassifiesIllegalItems()
    {
        Assert.That(DemandClassifier.Classify(DemandKind.Inspection, false, false, false, false, true, true, true), Is.Null);
        Assert.That(DemandClassifier.Classify(DemandKind.Inspection, false, false, false, true, true, true, true), Is.EqualTo(0xFFB31A));
    }

    [Test]
    public void Buying_RequiresNativeDemandMatch()
    {
        Assert.That(DemandClassifier.Classify(DemandKind.Buying, true, false, false, false, true, true, true), Is.Null);
    }
}
