using System;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;
using static ProbablyStolen.DigitalStorage.Common.Tests.Terminal.Stacking.StackingTestItems;

namespace ProbablyStolen.DigitalStorage.Common.Tests.Terminal.Stacking;

[TestFixture]
public sealed class StackingFieldCatalogTests
{
    [Test]
    public void FixedFields_ListTheBuiltInAttributes()
    {
        var ids = StackingFieldCatalog.FixedFields.Select(field => field.Id).ToArray();
        Assert.That(ids, Is.EquivalentTo(new[]
        {
            "appraised_value", "price_composition", "container_content"
        }));
    }

    [Test]
    public void Discover_ListsEveryCanonicalRootTagWithoutItsState()
    {
        var keys = new[]
        {
            Plain("scrap_metal", tags: new[]
            {
                new CanonicalIdentityTag("AGRI_PERFUME_RECORDED", enabled: true, valueString: "private-value"),
                new CanonicalIdentityTag("DISABLED_TAG", enabled: false, valueString: "retained")
            })
        };

        var options = StackingFieldCatalog.Discover(keys, Array.Empty<string>());
        var tags = options.Where(option => option.Field.Kind == StackingFieldKind.Raw && option.Field.TagKey != null)
            .Select(option => option.Field.Id).ToArray();

        Assert.That(tags, Is.EqualTo(new[] { "tag:AGRI_PERFUME_RECORDED", "tag:DISABLED_TAG" }));
        Assert.That(options.All(option => !option.AbsentFromInventory), Is.True);
    }

    [Test]
    public void Discover_KeepsSavedTagFieldsAndSkipsIdentifiersThatCannotBeStored()
    {
        var tooLong = new string('x', StackingRule.MaxFieldIdLength - StackingFieldCatalog.TagPrefix.Length + 1);
        var options = StackingFieldCatalog.Discover(
            new[] { Plain("lamp", tags: new[] { new CanonicalIdentityTag(tooLong) }) },
            new[] { "tag:AGRI_PERFUME_RECORDED", "tag:" });

        Assert.That(options.Single(option => option.Field.Id == "tag:AGRI_PERFUME_RECORDED").AbsentFromInventory, Is.True);
        Assert.That(options.Any(option => option.Field.TagKey == tooLong), Is.False);
        Assert.That(StackingFieldCatalog.Find("tag:"), Is.Null);
        Assert.That(StackingFieldCatalog.Find("tag:" + tooLong), Is.Null);
        Assert.That(StackingFieldCatalog.Find("tag:bad\nkey"), Is.Null);
    }

    [Test]
    public void Discover_AddsExposedFeaturesOnceAndSkipsHiddenDisabledAndOwnedOnes()
    {
        var keys = new[]
        {
            Plain("lamp", features: new[]
            {
                new CanonicalIdentityFeature("glow", "", isFeatureExposed: true),
                new CanonicalIdentityFeature("secret", ""),
                new CanonicalIdentityFeature("broken", "", isFeatureExposed: true, isDisabled: true),
                new CanonicalIdentityFeature("discount", "", isFeatureExposed: true)
            }),
            Plain("lamp", 5, features: new[] { new CanonicalIdentityFeature("glow", "", isFeatureExposed: true) }),
            Ingot("FAIR")
        };

        var options = StackingFieldCatalog.Discover(keys, new string[0]);

        var features = options.Where(option => option.Field.Kind == StackingFieldKind.Feature).Select(option => option.Field.Id);
        Assert.That(features, Is.EqualTo(new[] { "feature:INGOT_PURITY_TAG", "feature:glow" }));
        Assert.That(options.All(option => !option.AbsentFromInventory), Is.True);
    }

    [Test]
    public void Discover_KeepsSavedFieldsThatNoItemHasAndMarksThem()
    {
        var options = StackingFieldCatalog.Discover(Array.Empty<ItemKey>(),
            new[] { "feature:gone", "container_content", "unknown_field" }, includeHidden: true);
        var saved = options.Single(option => option.Field.Id == "feature:gone");
        Assert.That(saved.AbsentFromInventory, Is.True);
        Assert.That(options.Count(option => option.Field.Id == "container_content"), Is.EqualTo(1));
        Assert.That(options.Any(option => option.Field.Id == "unknown_field"), Is.False);
    }

    [Test]
    public void Find_ExcludesBoundPriceFeaturesAndKeepsIndependentTagsAndFeatures()
    {
        Assert.That(StackingFieldCatalog.Find("feature:INGOT_PURITY_TAG")!.FeatureId, Is.EqualTo("INGOT_PURITY_TAG"));
        Assert.That(StackingFieldCatalog.Find("feature:discount"), Is.Null);
        Assert.That(StackingFieldCatalog.Find("feature:"), Is.Null);
        Assert.That(StackingFieldCatalog.Find("feature:glow")!.FeatureId, Is.EqualTo("glow"));
        Assert.That(StackingFieldCatalog.Find("tag:AGRI_PERFUME_RECORDED")!.TagKey, Is.EqualTo("AGRI_PERFUME_RECORDED"));
    }

    [Test]
    public void Discover_UsesRawIdentifiersForGameTags()
    {
        var keys = new[] { "STOLEN_TAG", "STOLEN_VALUE_INT", "INGOT_PURITY_TAG", "CONTRABAND_LEVEL", "BOUGHT_PRICE_TAG" };
        var item = Plain("watch", tags: keys.Select(key => new CanonicalIdentityTag(key, enabled: true)));
        var tags = StackingFieldCatalog.Discover(new[] { item }, Array.Empty<string>())
            .Where(option => option.Field.TagKey != null).Select(option => option.Field).ToArray();
        Assert.That(tags.Select(field => field.TagKey), Is.EquivalentTo(keys));
        Assert.That(tags.All(field => field.NameKey.Length == 0 && field.DescriptionKey.Length == 0), Is.True);
    }

    [Test]
    public void OwnedPaths_UnionOwnsSharedPathsOnce()
    {
        var stolen = StackingFieldCatalog.Find("tag:STOLEN_TAG")!.OwnedPaths;
        var heat = StackingFieldCatalog.Find("tag:STOLEN_VALUE_INT")!.OwnedPaths;
        var union = stolen.Union(heat).Union(heat);
        Assert.That(union.TagKeys, Is.EquivalentTo(new[] { "STOLEN_TAG", "STOLEN_VALUE_INT" }));
        Assert.That(StackingOwnedPaths.None.IsEmpty, Is.True);
        Assert.That(StackingFieldCatalog.Find(StackingFieldCatalog.PriceComposition)!.OwnedPaths.ValueFields, Is.True);
        Assert.That(StackingFieldCatalog.Find(StackingFieldCatalog.ContainerContent)!.OwnedPaths.ContentWindow, Is.True);
        Assert.That(StackingFieldCatalog.Find("tag:COLOR")!.OwnedPaths.TagKeys, Is.EquivalentTo(new[] { "COLOR" }));
        Assert.That(new StackingOwnedPaths(valueFields: true, tagKeys: new[] { "A", "B" })
            .Without(new StackingOwnedPaths(tagKeys: new[] { "A" })).TagKeys, Is.EquivalentTo(new[] { "B" }));
    }
}
