using System;
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
    public void RawTagIgnored_MergesOnlyItsPresenceAndValueDifferences()
    {
        var config = Ignore(StackingFieldCatalog.TagFieldId("AGRI_PERFUME_RECORDED"), StackingFieldCatalog.AppraisedValue);
        var tagged = Plain("scrap_metal", tags: new[]
        {
            new CanonicalIdentityTag("AGRI_PERFUME_RECORDED", enabled: true, valueString: "one")
        });
        var untagged = Plain("scrap_metal");
        var differentOtherTag = Plain("scrap_metal", tags: new[]
        {
            new CanonicalIdentityTag("OTHER", enabled: true, valueInt: 1)
        });

        Assert.That(Compute(tagged, config, StackingAttributes.Empty), Is.EqualTo(Compute(untagged, config, StackingAttributes.Empty)));
        Assert.That(Compute(tagged, config, StackingAttributes.Empty),
            Is.Not.EqualTo(Compute(differentOtherTag, config, StackingAttributes.Empty)));
    }

    [Test]
    public void PriceCompositionOnlyIgnoresPriceValuesAndEconomicFeatures()
    {
        var bought = Plain("powerblock", tags: new[] { new CanonicalIdentityTag("BOUGHT_PRICE_TAG", enabled: true, valueInt: 120) });
        var clean = Plain("powerblock");
        var ignorePrice = Ignore(StackingFieldCatalog.PriceComposition, StackingFieldCatalog.AppraisedValue);
        var ignorePurchaseTag = ignorePrice.WithAddedRule("tag:BOUGHT_PRICE_TAG", StackingMode.Ignore);

        Assert.That(Compute(bought, ignorePrice, StackingAttributes.Empty), Is.Not.EqualTo(Compute(clean, ignorePrice, StackingAttributes.Empty)));
        Assert.That(Compute(bought, ignorePurchaseTag, StackingAttributes.Empty),
            Is.EqualTo(Compute(clean, ignorePurchaseTag, StackingAttributes.Empty)));
    }

    [Test]
    public void Differences_ReportsOnlyRemainingFieldNamesWithoutValues()
    {
        var config = Ignore(StackingFieldCatalog.PriceComposition, StackingFieldCatalog.AppraisedValue);
        var first = Plain("scrap_metal", tags: new[]
        {
            new CanonicalIdentityTag("AGRI_PERFUME_RECORDED", enabled: true, valueString: "courrierMining|155|2444")
        });
        var second = Plain("scrap_metal", tags: new[]
        {
            new CanonicalIdentityTag("AGRI_PERFUME_RECORDED", enabled: true, valueString: "courrierMining|150|2345")
        });

        var differences = StackingProjection.Differences(first, StackingAttributes.Empty, second, StackingAttributes.Empty, config);

        Assert.That(differences, Is.EqualTo(new[] { "tag:AGRI_PERFUME_RECORDED" }));
        Assert.That(string.Join(" ", differences), Does.Not.Contain("155"));
    }

    [Test]
    public void Differences_ReportsEstimatedValueWhenPriceCompositionIsIgnored()
    {
        var config = Ignore(StackingFieldCatalog.PriceComposition);
        var differences = StackingProjection.Differences(Plain("ore", unitValue: 15), Value(10),
            Plain("ore", unitValue: 20), Value(11), config);
        Assert.That(differences, Is.EqualTo(new[] { StackingFieldCatalog.AppraisedValue }));
    }

    [Test]
    public void Differences_NameHiddenFeaturesOnlyWithTheAdvancedOption()
    {
        var first = Plain("lamp", features: new[] { new CanonicalIdentityFeature("secret", "hidden") });
        var second = Plain("lamp");
        var basic = StackingProjection.Differences(first, StackingAttributes.Empty,
            second, StackingAttributes.Empty, StackingConfig.Default.WithAdvanced(false));
        Assert.That(basic, Is.EqualTo(new[] { StackingFieldCatalog.HiddenFeature }));
        Assert.That(string.Join(" ", basic), Does.Not.Contain("secret"));

        var advanced = StackingProjection.Differences(first, StackingAttributes.Empty,
            second, StackingAttributes.Empty, StackingConfig.Default);
        Assert.That(advanced, Is.EqualTo(new[] { StackingFieldCatalog.FeaturePrefix + "secret" }));
    }

    [Test]
    public void MatchOnDisjointUndiscoveredField_DoesNotBlockAnUnrelatedMerge()
    {
        var config = Ignore(StackingFieldCatalog.PriceComposition, StackingFieldCatalog.AppraisedValue)
            .WithAddedRule("feature:INGOT_PURITY_TAG", StackingMode.Match);
        var first = Ingot("FAIR", discovered: false, unitValue: 50);
        var second = Ingot("FAIR", discovered: false, unitValue: 60);

        Assert.That(Compute(first, config, StackingAttributes.Empty), Is.EqualTo(Compute(second, config, StackingAttributes.Empty)));
    }

    [Test]
    public void TryGetDifferences_SeparatesIncompleteReadsFromKnownIdentityDifferences()
    {
        var config = Ignore(StackingFieldCatalog.PriceComposition);
        var first = Plain("scrap_metal", tags: new[] { new CanonicalIdentityTag("COLOR", enabled: true, valueString: "red") });
        var second = Plain("scrap_metal", tags: new[] { new CanonicalIdentityTag("COLOR", enabled: true, valueString: "green") });
        var third = Plain("scrap_metal", tags: new[] { new CanonicalIdentityTag("COLOR", enabled: true, valueString: "blue") });

        var complete = StackingProjection.TryGetDifferences(second, Value(10), third, Value(20), config, out var known);
        var incomplete = StackingProjection.TryGetDifferences(first, StackingAttributes.Empty,
            second, Value(10), config, out var unknown);

        Assert.That(complete, Is.True);
        Assert.That(known, Does.Contain("tag:COLOR"));
        Assert.That(incomplete, Is.False);
        Assert.That(unknown, Is.EqualTo(new[] { "item_identity" }));
    }

    [Test]
    public void NoIgnoreRule_KeepsEveryEntryExact()
    {
        var config = Rules(new[] { new StackingRule(StackingFieldCatalog.AppraisedValue, StackingMode.Match) });
        var key = Compute(Ingot("FAIR"), config, Value(40));
        Assert.That(key.IsExact, Is.True);
    }

    [Test]
    public void DisabledConfig_KeepsEveryEntryExact()
    {
        var config = Ignore(StackingFieldCatalog.PriceComposition).WithAllEnabled(false);
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
    public void PurityTagRules_RequireAnExplicitRuleForTheFeature()
    {
        var tags = Ignore("tag:INGOT_PURITY_TAG", "tag:INGOT_PURITY_FAKE_TAG", StackingFieldCatalog.AppraisedValue);
        var withFeature = tags.WithAddedRule("feature:INGOT_PURITY_TAG", StackingMode.Ignore);
        var low = Ingot("POOR", purityMod: -40);
        var high = Ingot("GOOD", purityMod: -20);

        Assert.That(Compute(low, tags, Value(30)), Is.Not.EqualTo(Compute(high, tags, Value(30))));
        Assert.That(Compute(low, withFeature, Value(30)), Is.EqualTo(Compute(high, withFeature, Value(40))));
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

    [TestCase(true)]
    [TestCase(false)]
    public void ExposedFeatureIgnored_MergesPresenceAndReportsTheContributingRule(bool advanced)
    {
        var feature = new CanonicalIdentityFeature("glow", "", isFeatureExposed: true, publicDisplay: "Glowing");
        const string fieldId = StackingFieldCatalog.FeaturePrefix + "glow";
        var config = Ignore(fieldId, StackingFieldCatalog.AppraisedValue).WithAdvanced(advanced);
        var present = Plain("lamp", features: new[] { feature });
        var absent = Plain("lamp");
        var value = Value(1);

        Assert.That(Compute(present, config, value), Is.EqualTo(Compute(absent, config, value)));
        Assert.That(StackingProjection.AppliedIgnoredFields(new[] { (absent, value), (present, value) }, config)
            .Select(field => field.Id), Is.EqualTo(new[] { fieldId }));
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
    public void HiddenFeatureRow_PausesWithoutTheAdvancedOptionAndAppliesWithIt()
    {
        var config = Ignore(StackingFieldCatalog.FeaturePrefix + "secret", StackingFieldCatalog.AppraisedValue);
        var hidden = Plain("lamp", features: new[] { new CanonicalIdentityFeature("secret", "", customIntValue1: 1) });
        var plain = Plain("lamp", features: new[] { new CanonicalIdentityFeature("secret", "", customIntValue1: 2) });

        Assert.That(Compute(hidden, config, Value(1)), Is.EqualTo(Compute(plain, config, Value(1))));
        var basic = config.WithAdvanced(false);
        Assert.That(Compute(hidden, basic, Value(1)), Is.Not.EqualTo(Compute(Plain("lamp"), basic, Value(1))));
        Assert.That(Compute(hidden, basic, Value(1)), Is.Not.EqualTo(Compute(plain, basic, Value(1))),
            "The paused row keeps the hidden feature compared.");
        Assert.That(Compute(hidden, basic, Value(1)).IsExact, Is.False, "Other rows of the rule still apply.");
    }

    [Test]
    public void WaterPurity_DisabledOrUnexposed_MergesWithTheAdvancedOption()
    {
        var config = Ignore(StackingFieldCatalog.FeaturePrefix + "CATEGORY_WATER_PURITY", StackingFieldCatalog.AppraisedValue);
        var untested = Plain("bottled_water", features: new[]
        {
            new CanonicalIdentityFeature("CATEGORY_WATER_PURITY", "CATEGORY_WATER_PURITY", customIntValue1: 9800)
        });
        var tested = Plain("bottled_water", features: new[]
        {
            new CanonicalIdentityFeature("CATEGORY_WATER_PURITY", "CATEGORY_WATER_PURITY", customIntValue1: 9900,
                isFeatureExposed: true)
        });
        var empty = Plain("bottled_water", features: new[]
        {
            new CanonicalIdentityFeature("CATEGORY_WATER_PURITY", "CATEGORY_WATER_PURITY", isDisabled: true)
        });

        Assert.That(new[] { untested, tested, empty }.Select(key => Compute(key, config, Value(5))).Distinct().Count(),
            Is.EqualTo(1));
    }

    [Test]
    public void Discover_ListsHiddenFeaturesOnlyWhenAsked()
    {
        var key = Plain("lamp", features: new[] { new CanonicalIdentityFeature("secret", "") });

        Assert.That(StackingFieldCatalog.Discover(new[] { key }, Array.Empty<string>())
            .Select(option => option.Field.Id), Does.Not.Contain("feature:secret"));
        var option = StackingFieldCatalog.Discover(new[] { key }, Array.Empty<string>(), includeHidden: true)
            .Single(item => item.Field.Id == "feature:secret");
        Assert.That(option.Hidden, Is.True);
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
    public void StolenAndHeatTags_AreIndependent()
    {
        var config = Ignore("tag:STOLEN_TAG", StackingFieldCatalog.AppraisedValue);
        var stolen = Plain("watch", tags: new[]
        {
            new CanonicalIdentityTag("STOLEN_TAG", enabled: true), new CanonicalIdentityTag("STOLEN_VALUE_INT", enabled: true, valueInt: 40)
        });
        var clean = Plain("watch");
        Assert.That(Compute(stolen, config, Value(5)), Is.Not.EqualTo(Compute(clean, config, Value(5))));
        var ignoreBoth = config.WithAddedRule("tag:STOLEN_VALUE_INT", StackingMode.Ignore);
        Assert.That(Compute(stolen, ignoreBoth, Value(5)), Is.EqualTo(Compute(clean, ignoreBoth, Value(5))));
    }

    [Test]
    public void Grouping_IsAnEquivalenceIndependentOfOrder()
    {
        var config = Ignore(StackingFieldCatalog.PriceComposition, StackingFieldCatalog.AppraisedValue, "tag:INGOT_PURITY_TAG",
            "tag:INGOT_PURITY_FAKE_TAG", "feature:INGOT_PURITY_TAG");
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
    public void NonCanonicalKey_StaysExact()
    {
        var key = new ItemKey("legacy");
        Assert.That(Compute(key, Ignore(StackingFieldCatalog.PriceComposition), Value(1)).IsExact, Is.True);
    }

    private static StackingGroupKey Compute(ItemKey key, StackingConfig config, StackingAttributes attributes) =>
        StackingProjection.Compute(key, config, attributes);

    [Test]
    public void AppliedIgnoredFields_ListsOnlyFieldsThatDifferWithinTheGroupInRuleOrder()
    {
        var stableTag = new CanonicalIdentityTag("CHEMISTRY", enabled: true, valueInt: 1);
        var secondTag = new CanonicalIdentityTag("VARIANT", enabled: true, valueInt: 2);
        var config = Ignore("tag:CHEMISTRY", StackingFieldCatalog.PriceComposition, "tag:UNRELATED", "tag:VARIANT",
            StackingFieldCatalog.AppraisedValue, "unknown_field");
        var first = Plain("fruit", tags: new[] { stableTag });
        var members = new[] { (first, Value(20)), (Plain("fruit", 11, new[] { stableTag, secondTag }), Value(20)) };

        Assert.That(members.Select(member => Compute(member.Item1, config, member.Item2)).Distinct().Count(), Is.EqualTo(1));
        Assert.That(StackingProjection.AppliedIgnoredFields(members, config).Select(field => field.Id),
            Is.EqualTo(new[] { StackingFieldCatalog.PriceComposition, "tag:VARIANT" }));
        Assert.That(StackingProjection.AppliedIgnoredFields(members.Reverse().ToArray(), config).Select(field => field.Id),
            Is.EqualTo(new[] { StackingFieldCatalog.PriceComposition, "tag:VARIANT" }));
        Assert.That(StackingProjection.AppliedIgnoredFields(members, config.WithAllEnabled(false)), Is.Empty);
    }

    [Test]
    public void AppliedIgnoredFields_UsesKnownAppraisalsEvenWhenTheFirstIsMissing()
    {
        var config = Ignore(StackingFieldCatalog.PriceComposition, StackingFieldCatalog.AppraisedValue);
        var members = new[] { (Plain("fruit", 10), StackingAttributes.Empty), (Plain("fruit", 11), Value(20)),
            (Plain("fruit", 12), Value(30)) };

        Assert.That(StackingProjection.AppliedIgnoredFields(members.Take(2).ToArray(), config).Select(field => field.Id),
            Is.EqualTo(new[] { StackingFieldCatalog.PriceComposition }));
        Assert.That(StackingProjection.AppliedIgnoredFields(members, config).Select(field => field.Id),
            Is.EqualTo(new[] { StackingFieldCatalog.PriceComposition, StackingFieldCatalog.AppraisedValue }));
    }

    [Test]
    public void AppliedIgnoredFields_OmitsMatchRulesEvenWhenAnotherFieldDiffers()
    {
        var config = Ignore(StackingFieldCatalog.PriceComposition, StackingFieldCatalog.AppraisedValue)
            .WithAddedRule("tag:STOLEN_VALUE_INT", StackingMode.Match);
        var heat = new CanonicalIdentityTag("STOLEN_VALUE_INT", enabled: true, valueInt: 3);
        var members = new[] { (Plain("fruit", 10, new[] { heat }), Value(20)), (Plain("fruit", 11, new[] { heat }), Value(20)) };

        Assert.That(members.Select(member => Compute(member.Item1, config, member.Item2)).Distinct().Count(), Is.EqualTo(1));
        Assert.That(StackingProjection.AppliedIgnoredFields(members, config).Select(field => field.Id),
            Is.EqualTo(new[] { StackingFieldCatalog.PriceComposition }));
    }
    [Test]
    public void ItemRule_GroupsOnlyItsOwnItem()
    {
        var price = new[] { new StackingRule(StackingFieldCatalog.PriceComposition, StackingMode.Ignore),
            new StackingRule(StackingFieldCatalog.AppraisedValue, StackingMode.Ignore) };
        var config = new StackingConfig(new[] { new StackingItemRule("fruit", true, price) });

        Assert.That(Compute(Plain("fruit", 10), config, Value(20)), Is.EqualTo(Compute(Plain("fruit", 11), config, Value(21))));
        Assert.That(Compute(Plain("box", 10), config, Value(20)).IsExact, Is.True,
            "An item without a rule of its own keeps its exact identity.");
        Assert.That(Compute(Plain("box", 10), config, Value(20)), Is.Not.EqualTo(Compute(Plain("box", 11), config, Value(21))));
    }

    [Test]
    public void SwitchedOffItemRule_KeepsItsItemExactWhileOthersGroup()
    {
        var price = new[] { new StackingRule(StackingFieldCatalog.PriceComposition, StackingMode.Ignore),
            new StackingRule(StackingFieldCatalog.AppraisedValue, StackingMode.Ignore) };
        var config = new StackingConfig(new[]
        {
            new StackingItemRule("fruit", false, price), new StackingItemRule("box", true, price)
        });

        Assert.That(config.HasActiveRule, Is.True);
        Assert.That(Compute(Plain("fruit", 10), config, Value(20)).IsExact, Is.True);
        Assert.That(Compute(Plain("box", 10), config, Value(20)), Is.EqualTo(Compute(Plain("box", 11), config, Value(21))));
    }

    [Test]
    public void Differences_UseTheItemRuleAndFallBackToExactWithoutOne()
    {
        var withoutRule = StackingConfig.Default;
        var withRule = new StackingConfig(new[] { new StackingItemRule("fruit", true,
            new[] { new StackingRule(StackingFieldCatalog.PriceComposition, StackingMode.Ignore) }) });

        StackingProjection.TryGetDifferences(Plain("fruit", 10), Value(20), Plain("fruit", 11), Value(21), withoutRule,
            out var raw);
        StackingProjection.TryGetDifferences(Plain("fruit", 10), Value(20), Plain("fruit", 11), Value(21), withRule,
            out var remaining);

        Assert.That(raw, Is.EqualTo(new[] { StackingFieldCatalog.PriceComposition }));
        Assert.That(remaining, Is.EqualTo(new[] { StackingFieldCatalog.AppraisedValue }),
            "Once raw data is ignored, the estimated value takes part in the comparison.");
    }
}
