using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class PartitionFieldTests
{
    [Test]
    public void Registry_ListsTheFirstReleaseFieldsWithTheirOperators()
    {
        Assert.That(PartitionFieldRegistry.Find("item")!.Operators, Is.EqualTo(new[] { PartitionOperator.Is, PartitionOperator.IsNot }));
        Assert.That(PartitionFieldRegistry.Find("type")!.Operators,
            Is.EqualTo(new[] { PartitionOperator.Contains, PartitionOperator.NotContains }));
        Assert.That(PartitionFieldRegistry.Find("legality")!.EnumValues,
            Is.EqualTo(new[] { "none", "low", "mid", "high", "critical" }));
        Assert.That(PartitionFieldRegistry.Find("stolen")!.HasTarget, Is.False);
        var heat = PartitionFieldRegistry.Find("heat")!;
        Assert.That(heat.UnitKey, Is.EqualTo("partition_unit_percent"));
        Assert.That(heat.Operators[0], Is.EqualTo(PartitionOperator.Eq), "Numbers default to equal.");
        Assert.That(heat.Supports(PartitionOperator.Range), Is.True);
        Assert.That(heat.Supports(PartitionOperator.Contains), Is.False);
    }

    [Test]
    public void Registry_ResolvesExposedFeatureFieldsButNoInternalData()
    {
        var feature = PartitionFieldRegistry.Find(PartitionFieldRegistry.FeatureField("rare_find"));
        Assert.That(feature!.Kind, Is.EqualTo(PartitionFieldKind.Bool));
        Assert.That(feature.Id, Is.EqualTo("feature:rare_find"));
        Assert.That(PartitionFieldRegistry.Find("feature:"), Is.Null);
        Assert.That(new PartitionCondition("feature:", PartitionOperator.Is, PartitionValue.Of("x")).Validate(),
            Is.EqualTo(PartitionIssue.UnknownField));
        Assert.That(PartitionFieldRegistry.Find("unitValue"), Is.Null);
        Assert.That(PartitionFieldRegistry.Find(null), Is.Null);
        Assert.Throws<ArgumentException>(new Action(() => PartitionFieldRegistry.FeatureField("")));
    }

    [Test]
    public void AttributeSet_TreatsUnlistedFeaturesAsUnknownOnlyWhenHiddenFeaturesExist()
    {
        var values = new Dictionary<string, AttributeValue> { ["feature:shiny"] = AttributeValue.Of(true) };
        var noneHidden = new ItemAttributeSet(values, hasHiddenFeatures: false);
        var someHidden = new ItemAttributeSet(values, hasHiddenFeatures: true);

        Assert.That(noneHidden.Get("feature:shiny").Flag, Is.True);
        Assert.That(noneHidden.Get("feature:cursed").IsKnown, Is.True);
        Assert.That(noneHidden.Get("feature:cursed").Flag, Is.False);
        Assert.That(someHidden.Get("feature:cursed").IsKnown, Is.False);
        var dynamicFeature = "feature:third_party_effect";
        Assert.That(PartitionFieldRegistry.Find(dynamicFeature)!.Id, Is.EqualTo(dynamicFeature));
        Assert.That(someHidden.Get(dynamicFeature).IsKnown, Is.False);
        Assert.That(PartitionFieldRegistry.Find("feature:"), Is.Null);
        Assert.That(noneHidden.Get("heat").IsKnown, Is.False, "A missing non-feature value is unknown, never zero.");
        Assert.That(ItemAttributeSet.Unreadable.Get("feature:shiny").IsKnown, Is.False);
    }

    [Test]
    public void Values_CompareByKindAndContent()
    {
        Assert.That(PartitionValue.Of("a"), Is.EqualTo(PartitionValue.Of("a")));
        Assert.That(PartitionValue.Of(40), Is.EqualTo(PartitionValue.Of(40L)));
        Assert.That(PartitionValue.Of("40"), Is.Not.EqualTo(PartitionValue.Of(40)));
        Assert.Throws<ArgumentException>(new Action(() => PartitionValue.Of("")));
        Assert.That(AttributeValue.OfSet(new[] { "POISON", "", "POISON" }).Texts, Is.EquivalentTo(new[] { "POISON" }));
    }
}
