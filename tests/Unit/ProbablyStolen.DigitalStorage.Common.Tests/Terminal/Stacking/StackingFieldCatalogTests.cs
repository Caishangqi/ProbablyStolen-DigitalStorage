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
    public void FixedFields_CoverTheResearchedFieldsButNotItemType()
    {
        var ids = StackingFieldCatalog.FixedFields.Select(field => field.Id).ToArray();
        Assert.That(ids, Is.EquivalentTo(new[]
        {
            "appraised_value", "price_composition", "metal_purity", "container_content", "legality", "stolen", "heat"
        }));
        Assert.That(StackingFieldCatalog.Find("type"), Is.Null);
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
        Assert.That(features, Is.EqualTo(new[] { "feature:glow" }));
        Assert.That(options.All(option => !option.AbsentFromInventory), Is.True);
    }

    [Test]
    public void Discover_KeepsSavedFieldsThatNoItemHasAndMarksThem()
    {
        var options = StackingFieldCatalog.Discover(new ItemKey[0], new[] { "feature:gone", "metal_purity", "unknown" });
        var saved = options.Single(option => option.Field.Id == "feature:gone");
        Assert.That(saved.AbsentFromInventory, Is.True);
        Assert.That(options.Count(option => option.Field.Id == "metal_purity"), Is.EqualTo(1));
        Assert.That(options.Any(option => option.Field.Id == "unknown"), Is.False);
    }

    [Test]
    public void Find_RefusesFeatureFieldsForFeaturesAFixedFieldOwns()
    {
        Assert.That(StackingFieldCatalog.Find("feature:INGOT_PURITY_TAG"), Is.Null);
        Assert.That(StackingFieldCatalog.Find("feature:discount"), Is.Null);
        Assert.That(StackingFieldCatalog.Find("feature:"), Is.Null);
        Assert.That(StackingFieldCatalog.Find("feature:glow")!.FeatureId, Is.EqualTo("glow"));
    }

    [Test]
    public void OwnedPaths_UnionOwnsSharedPathsOnce()
    {
        var stolen = StackingFieldCatalog.Find(StackingFieldCatalog.Stolen)!.OwnedPaths;
        var heat = StackingFieldCatalog.Find(StackingFieldCatalog.Heat)!.OwnedPaths;
        var union = stolen.Union(heat);
        Assert.That(union.TagKeys, Is.EquivalentTo(new[] { "STOLEN_TAG", "STOLEN_VALUE_INT" }));
        Assert.That(StackingOwnedPaths.None.IsEmpty, Is.True);
        Assert.That(StackingFieldCatalog.Find(StackingFieldCatalog.PriceComposition)!.OwnedPaths.ValueFields, Is.True);
        Assert.That(StackingFieldCatalog.Find(StackingFieldCatalog.ContainerContent)!.OwnedPaths.ContentWindow, Is.True);
    }
}
