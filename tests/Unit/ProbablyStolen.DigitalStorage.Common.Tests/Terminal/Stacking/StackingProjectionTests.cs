using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;
using static ProbablyStolen.DigitalStorage.Common.Tests.Terminal.Stacking.StackingTestItems;

namespace ProbablyStolen.DigitalStorage.Common.Tests.Terminal.Stacking;

[TestFixture]
public sealed class StackingProjectionTests
{
    private static readonly CanonicalIdentityFeature Discount = new("discount", "", valueModifier: -10);

    [Test]
    public void NoIgnoreRule_KeepsEveryEntryExact()
    {
        var config = new StackingConfig(true, new[] { new StackingRule(StackingFieldCatalog.AppraisedValue, StackingMode.Match) });
        var key = Compute(Ingot("FAIR"), config, Value(40));
        Assert.That(key.IsExact, Is.True);
    }

    [Test]
    public void DisabledConfig_KeepsEveryEntryExact()
    {
        var config = Ignore(StackingFieldCatalog.PriceComposition).WithEnabled(false);
        Assert.That(Compute(Ingot("FAIR"), config, Value(40)).IsExact, Is.True);
    }

    [Test]
    public void SameValueDifferentComposition_MergesWhenCompositionIgnored()
    {
        var config = Ignore(StackingFieldCatalog.PriceComposition);
        var a = Compute(Ingot("FAIR", unitValue: 50, extra: new[] { Discount }), config, Value(40));
        var b = Compute(Ingot("FAIR", unitValue: 40), config, Value(40));
        Assert.That(a, Is.EqualTo(b));
        Assert.That(a.IsExact, Is.False);
    }

    [Test]
    public void DifferentAppraisedValue_StaysApartUnlessAppraisedValueIgnored()
    {
        var composition = Ignore(StackingFieldCatalog.PriceComposition);
        Assert.That(Compute(Ingot("FAIR", unitValue: 50), composition, Value(40)),
            Is.Not.EqualTo(Compute(Ingot("FAIR", unitValue: 60), composition, Value(50))));

        var both = Ignore(StackingFieldCatalog.PriceComposition, StackingFieldCatalog.AppraisedValue);
        Assert.That(Compute(Ingot("FAIR", unitValue: 50), both, Value(40)), Is.EqualTo(Compute(Ingot("FAIR", unitValue: 60), both, Value(50))));
    }

    [Test]
    public void PurityIgnored_ButAppraisedValueKept_DifferentValuesStayApart()
    {
        var config = Ignore(StackingFieldCatalog.MetalPurity);
        var low = Compute(Ingot("POOR", purityMod: -40), config, Value(30));
        var high = Compute(Ingot("GOOD", purityMod: -20), config, Value(40));
        var sameValue = Compute(Ingot("GOOD", purityMod: -20), config, Value(30));
        Assert.That(low, Is.Not.EqualTo(high));
        Assert.That(low, Is.EqualTo(sameValue));
    }

    [Test]
    public void ThreeIngotIgnores_MergeDifferentPurityAndValue()
    {
        var config = Ignore(StackingFieldCatalog.AppraisedValue, StackingFieldCatalog.PriceComposition,
            StackingFieldCatalog.MetalPurity);
        Assert.That(Compute(Ingot("POOR", unitValue: 50, purityMod: -40), config, Value(30)),
            Is.EqualTo(Compute(Ingot("GOOD", unitValue: 70, purityMod: 10), config, Value(77))));
    }

    [Test]
    public void UndiscoveredPurity_StaysExactWhenPurityIgnored()
    {
        var config = Ignore(StackingFieldCatalog.MetalPurity, StackingFieldCatalog.AppraisedValue);
        Assert.That(Compute(Ingot("POOR", discovered: false), config, Value(30)).IsExact, Is.True);
    }

    [Test]
    public void MissingDerivedValue_StaysExact()
    {
        var config = Ignore(StackingFieldCatalog.PriceComposition);
        Assert.That(Compute(Ingot("FAIR"), config, StackingAttributes.Empty).IsExact, Is.True);
    }

    [Test]
    public void ContainerContentAndAppraisedValueIgnored_MergeFullHalfAndEmptyBottles()
    {
        var config = Ignore(StackingFieldCatalog.ContainerContent, StackingFieldCatalog.AppraisedValue);
        var full = Compute(Bottle(9), config, Value(55));
        var half = Compute(Bottle(4), config, Value(25));
        var empty = Compute(Bottle(0), config, Value(1));
        Assert.That(new[] { half, empty }, Is.All.EqualTo(full));
    }

    [Test]
    public void ContainerContentIgnoredAlone_DifferentAppraisedValuesStayApart()
    {
        var config = Ignore(StackingFieldCatalog.ContainerContent);
        Assert.That(Compute(Bottle(9), config, Value(55)), Is.Not.EqualTo(Compute(Bottle(0), config, Value(1))));
    }

    [Test]
    public void DifferentItemIdentifiers_NeverMerge()
    {
        var everything = Ignore(StackingFieldCatalog.FixedFields.Select(field => field.Id).ToArray());
        Assert.That(Compute(Plain("small_bottle"), everything, Value(1)),
            Is.Not.EqualTo(Compute(Plain("large_bottle"), everything, Value(1))));
    }

    [Test]
    public void FieldOnlyOneItemHas_KeepsTheDifference()
    {
        var feature = new CanonicalIdentityFeature("glow", "", isFeatureExposed: true, publicDisplay: "Glowing");
        var config = Ignore(StackingFieldCatalog.FeaturePrefix + "glow", StackingFieldCatalog.AppraisedValue);
        Assert.That(Compute(Plain("lamp", features: new[] { feature }), config, Value(1)),
            Is.Not.EqualTo(Compute(Plain("lamp"), config, Value(1))));
    }

    [Test]
    public void ExposedFeatureIgnored_MergesDifferentFeatureValues()
    {
        var config = Ignore(StackingFieldCatalog.FeaturePrefix + "glow", StackingFieldCatalog.AppraisedValue);
        var dim = Plain("lamp", features: new[] { new CanonicalIdentityFeature("glow", "", isFeatureExposed: true, valueModifier: 1) });
        var bright = Plain("lamp", features: new[] { new CanonicalIdentityFeature("glow", "", isFeatureExposed: true, valueModifier: 9) });
        Assert.That(Compute(dim, config, Value(1)), Is.EqualTo(Compute(bright, config, Value(2))));
    }

    [Test]
    public void HiddenFeature_IsNeverIgnored()
    {
        var config = Ignore(StackingFieldCatalog.FeaturePrefix + "secret", StackingFieldCatalog.AppraisedValue);
        var hidden = Plain("lamp", features: new[] { new CanonicalIdentityFeature("secret", "") });
        Assert.That(Compute(hidden, config, Value(1)).IsExact, Is.True);
    }

    [Test]
    public void UnmaskedDifferences_StayExact()
    {
        var config = Ignore(StackingFieldCatalog.PriceComposition);
        var a = Plain("box", tags: new[] { new CanonicalIdentityTag("COLOR", enabled: true, valueString: "red") });
        var b = Plain("box", tags: new[] { new CanonicalIdentityTag("COLOR", enabled: true, valueString: "blue") });
        Assert.That(Compute(a, config, Value(1)), Is.Not.EqualTo(Compute(b, config, Value(1))));
    }

    [Test]
    public void StolenIgnored_MergesStolenAndClean()
    {
        var config = Ignore(StackingFieldCatalog.Stolen, StackingFieldCatalog.AppraisedValue);
        var stolen = Plain("watch", tags: new[]
        {
            new CanonicalIdentityTag("STOLEN_TAG", enabled: true), new CanonicalIdentityTag("STOLEN_VALUE_INT", enabled: true, valueInt: 40)
        });
        Assert.That(Compute(stolen, config, Value(5)), Is.EqualTo(Compute(Plain("watch"), config, Value(5))));
    }

    [Test]
    public void Grouping_IsAnEquivalenceIndependentOfOrder()
    {
        var config = Ignore(StackingFieldCatalog.PriceComposition, StackingFieldCatalog.MetalPurity, StackingFieldCatalog.AppraisedValue);
        var keys = new[] { Ingot("POOR"), Ingot("GOOD", unitValue: 60), Ingot("FAIR", extra: new[] { Discount }) };
        var forward = keys.Select(key => Compute(key, config, Value(1))).ToList();
        var backward = keys.Reverse().Select(key => Compute(key, config, Value(1))).Reverse().ToList();
        Assert.That(forward, Is.EqualTo(backward));
        Assert.That(forward.Distinct().Count(), Is.EqualTo(1));
        Assert.That(forward[0].ToStableId(), Is.EqualTo(backward[2].ToStableId()));
    }

    [Test]
    public void Explain_NamesTheTagsThatStillDifferWithoutTheirValues()
    {
        var config = Ignore(StackingFieldCatalog.ContainerContent, StackingFieldCatalog.AppraisedValue);
        var a = Plain("bottled_water", tags: new[] { new CanonicalIdentityTag("WATER_PART", enabled: true, valueInt: 750) });
        var b = Plain("bottled_water", tags: new[] { new CanonicalIdentityTag("WATER_PART", enabled: true, valueInt: 300) });

        var reason = StackingProjection.Explain(a, Value(1), b, Value(1), config);

        Assert.That(reason, Is.EqualTo("tags[WATER_PART]"));
        Assert.That(reason, Does.Not.Contain("750"));
        Assert.That(StackingProjection.Explain(Bottle(9), Value(1), Bottle(0), Value(1), config), Is.EqualTo("none"));
        Assert.That(StackingProjection.Explain(a, StackingAttributes.Empty, b, Value(1), Ignore(StackingFieldCatalog.ContainerContent)),
            Does.StartWith("exact:unread:"));
    }

    [Test]
    public void BoughtPrice_IsPartOfPriceComposition()
    {
        var config = Ignore(StackingFieldCatalog.PriceComposition);
        var bought = Plain("powerblock", tags: new[] { new CanonicalIdentityTag("BOUGHT_PRICE_TAG", enabled: true, valueInt: 120) });
        Assert.That(Compute(bought, config, Value(80)), Is.EqualTo(Compute(Plain("powerblock"), config, Value(80))));
    }

    [Test]
    public void NonCanonicalKey_StaysExact()
    {
        var key = new ItemKey("legacy");
        Assert.That(Compute(key, Ignore(StackingFieldCatalog.PriceComposition), Value(1)).IsExact, Is.True);
    }

    private static StackingGroupKey Compute(ItemKey key, StackingConfig config, StackingAttributes attributes) =>
        StackingProjection.Compute(key, config, attributes);

    [Test]
    public void AppliedIgnoredFields_ListsIgnoreRulesThatDescribeTheItemInRuleOrder()
    {
        var config = Ignore(StackingFieldCatalog.ContainerContent, StackingFieldCatalog.MetalPurity,
            StackingFieldCatalog.AppraisedValue).WithAddedRule(StackingFieldCatalog.PriceComposition, StackingMode.Match);
        var fields = StackingProjection.AppliedIgnoredFields(Bottle(4), config).Select(field => field.Id);
        Assert.That(fields, Is.EqualTo(new[] { StackingFieldCatalog.ContainerContent, StackingFieldCatalog.AppraisedValue }));
    }

    [Test]
    public void AppliedIgnoredFields_SkipsHiddenAndUnknownFieldsAndOffConfigs()
    {
        var config = Ignore(StackingFieldCatalog.MetalPurity, "unknown_field");
        Assert.That(StackingProjection.AppliedIgnoredFields(Ingot("Pure", discovered: false), config), Is.Empty);
        Assert.That(StackingProjection.AppliedIgnoredFields(Ingot("Pure"), config).Select(field => field.Id),
            Is.EqualTo(new[] { StackingFieldCatalog.MetalPurity }));
        Assert.That(StackingProjection.AppliedIgnoredFields(Ingot("Pure"), config.WithEnabled(false)), Is.Empty);
    }
}
