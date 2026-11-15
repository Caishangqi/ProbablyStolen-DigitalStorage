using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using static ProbablyStolen.DigitalStorage.Common.Tests.Terminal.Stacking.StackingTestItems;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class ItemIdentityDumpTests
{
    [Test]
    public void Dump_ShowsValuesTagsFeaturesAndContentChildren()
    {
        var bottle = Plain("bottled_water", 5, tags: new[]
        {
            new CanonicalIdentityTag("CURRENT_PART_WATER", enabled: true, valueInt: 750000),
            new CanonicalIdentityTag("NOTE", enabled: true, valueString: "say \"hi\"\n")
        }, features: new[] { new CanonicalIdentityFeature("CATEGORY_WATER_PURITY", "water", valueModifier: -3, isFeatureExposed: true) });

        var json = ItemIdentityDump.ToJson(bottle);

        Assert.That(json, Does.Contain("\"identifier\": \"bottled_water\""));
        Assert.That(json, Does.Contain("\"unitValue\": 5"));
        Assert.That(json, Does.Contain("\"key\": \"CURRENT_PART_WATER\"").And.Contain("\"int\": 750000"));
        Assert.That(json, Does.Contain("say \\\"hi\\\"\\n"));
        Assert.That(json, Does.Contain("\"identifier\": \"CATEGORY_WATER_PURITY\"").And.Contain("\"valueModifier\": -3"));
        Assert.That(ItemIdentityDump.ToJson(Bottle(2)), Does.Contain("\"kind\": \"grid\"").And.Contain("\"identifier\": \"water_tablet\""));
    }

    [Test]
    public void Dump_IsCutAtTheLimitAndHandlesPlainKeys()
    {
        Assert.That(ItemIdentityDump.ToJson(Bottle(9), 40), Does.EndWith("(cut at 40 characters)"));
        Assert.That(ItemIdentityDump.ToJson(new ItemKey("legacy")), Is.EqualTo("{\"identifier\":\"legacy\",\"canonical\":false}"));
    }
}
