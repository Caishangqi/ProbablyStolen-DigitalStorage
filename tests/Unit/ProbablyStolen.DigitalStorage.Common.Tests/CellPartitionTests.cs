using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class CellPartitionTests
{
    private static readonly ItemKey Apple = new("item.apple");
    private static readonly ItemKey Pear = new("item.pear");

    private sealed class CountingSource : IItemAttributeSource
    {
        private readonly Func<ItemKey, ItemAttributeSet> _read;
        internal CountingSource(Func<ItemKey, ItemAttributeSet> read) => _read = read;
        internal int Reads { get; private set; }

        public ItemAttributeSet Read(ItemKey key)
        {
            Reads++;
            return _read(key);
        }
    }

    private static ItemKey Canonical(string identifier, IReadOnlyList<string>? types = null,
        IReadOnlyList<CanonicalIdentityTag>? tags = null, IReadOnlyList<CanonicalIdentityFeature>? features = null) =>
        CanonicalIdentityEncoder.Encode(new CanonicalIdentityInput(identifier, types ?? Array.Empty<string>(),
            tags ?? Array.Empty<CanonicalIdentityTag>(), features ?? Array.Empty<CanonicalIdentityFeature>()));

    private static PartitionRule HeatBelow(string id, int slot, int heat) => new(id, slot, null, null, new[]
    {
        new PartitionCondition(PartitionFieldRegistry.Heat, PartitionOperator.Lt, PartitionValue.Of(heat))
    });

    [Test]
    public void Unfiltered_AdmitsEveryKeyButNull()
    {
        var partition = CellPartition.Unfiltered;

        Assert.That(partition.IsValid, Is.True);
        Assert.That(partition.Mode, Is.EqualTo(PartitionMode.Whitelist));
        Assert.That(partition.Rejection(Apple), Is.Null);
        Assert.That(partition.Rejection(null), Is.EqualTo(StoreRejection.PartitionRejected));
    }

    [TestCase(PartitionMode.Whitelist)]
    [TestCase(PartitionMode.Blacklist)]
    public void EmptyRules_AdmitEverythingInEitherMode(PartitionMode mode)
    {
        var partition = Partitions.Of(mode);

        Assert.That(partition.Mode, Is.EqualTo(mode), "The mode is kept even without rules.");
        Assert.That(partition.Rejection(Apple), Is.Null);
    }

    [Test]
    public void Whitelist_AdmitsMatchesAndBlacklistRefusesThem()
    {
        Assert.That(Partitions.Whitelist(Apple).Rejection(Apple), Is.Null);
        Assert.That(Partitions.Whitelist(Apple).Rejection(Pear), Is.EqualTo(StoreRejection.PartitionRejected));
        Assert.That(Partitions.Blacklist(Apple).Rejection(Apple), Is.EqualTo(StoreRejection.PartitionRejected));
        Assert.That(Partitions.Blacklist(Apple).Rejection(Pear), Is.Null);
    }

    [Test]
    public void Unknown_IsRefusedInBothModesWithItsOwnReason()
    {
        // A legacy identity records no heat, so a heat rule cannot be decided.
        var rules = new[] { HeatBelow("r0", 0, 40) };
        var whitelist = CellPartition.From(new PartitionConfig(PartitionMode.Whitelist, rules), CanonicalAttributeSource.Instance);
        var blacklist = CellPartition.From(new PartitionConfig(PartitionMode.Blacklist, rules), CanonicalAttributeSource.Instance);

        Assert.That(whitelist.Rejection(Apple), Is.EqualTo(StoreRejection.PartitionUnknown));
        Assert.That(blacklist.Rejection(Apple), Is.EqualTo(StoreRejection.PartitionUnknown),
            "A blacklist never admits an item because a rule could not be decided.");
    }

    [Test]
    public void KnownResult_DecidesDespiteAnotherUnknownRule()
    {
        // Rule 0 cannot be decided for a legacy key, but rule 1 matches it, so the whitelist admits it.
        var rules = new[] { HeatBelow("r0", 0, 40), PartitionRule.ForSample("r1", 1, Apple) };
        var partition = CellPartition.From(new PartitionConfig(PartitionMode.Whitelist, rules), CanonicalAttributeSource.Instance);

        Assert.That(partition.Rejection(Apple), Is.Null);
        Assert.That(partition.Rejection(Pear), Is.EqualTo(StoreRejection.PartitionUnknown));
    }

    [Test]
    public void InvalidConfiguration_RefusesEveryNewItem()
    {
        var unknownField = new PartitionConfig(PartitionMode.Whitelist, new[]
        {
            new PartitionRule("r0", 0, null, null, new[] { new PartitionCondition("price", PartitionOperator.Is, PartitionValue.Of("1")) })
        });
        var empty = new PartitionConfig(PartitionMode.Blacklist, new[] { new PartitionRule("r0", 0, null, null, Array.Empty<PartitionCondition>()) });

        var first = CellPartition.From(unknownField, CanonicalAttributeSource.Instance);
        var second = CellPartition.From(empty, CanonicalAttributeSource.Instance);

        Assert.That((first.IsValid, first.Failure), Is.EqualTo((false, CellPartitionFailure.UnknownField)));
        Assert.That(second.Failure, Is.EqualTo(CellPartitionFailure.MalformedRule));
        Assert.That(first.Rejection(Apple), Is.EqualTo(StoreRejection.PartitionInvalid));
        Assert.Throws<ArgumentException>(new Action(() => CellPartition.Invalid(PartitionMode.Whitelist, CellPartitionFailure.None)));
    }

    [Test]
    public void Preference_RequiresACertainWhitelistMatch()
    {
        Assert.That(Partitions.Whitelist(Apple).IsPreferred(Apple), Is.True);
        Assert.That(Partitions.Whitelist(Apple).IsPreferred(Pear), Is.False);
        Assert.That(Partitions.Blacklist(Apple).IsPreferred(Pear), Is.False, "Blacklist admission is not a preference.");
        Assert.That(Partitions.Whitelist().IsPreferred(Apple), Is.False);
        Assert.That(CellPartition.Invalid(PartitionMode.Whitelist, CellPartitionFailure.MalformedRule).IsPreferred(Apple), Is.False);
    }

    [Test]
    public void RulePartitionList_CachesEachIdentityOnce()
    {
        var source = new CountingSource(key => CanonicalAttributeSource.Instance.Read(key));
        var list = new RulePartitionList(new[] { PartitionRule.ForSample("r0", 0, Apple) }, source);

        Assert.That(list.Evaluate(Apple), Is.EqualTo(PartitionMatch.Match));
        Assert.That(list.Evaluate(Apple), Is.EqualTo(PartitionMatch.Match));
        Assert.That(list.Evaluate(Pear), Is.EqualTo(PartitionMatch.NoMatch));
        Assert.That(source.Reads, Is.EqualTo(2));
    }

    [Test]
    public void CanonicalAttributes_ReadTypesLegalityStolenHeatAndExposedFeatures()
    {
        var key = Canonical("item.poison", new[] { "POISON" }, new[]
        {
            new CanonicalIdentityTag("CONTRABAND_ITEM_TAG", enabled: true),
            new CanonicalIdentityTag("CONTRABAND_LEVEL", valueString: "CONTRABAND_LEVEL_HIGH"),
            new CanonicalIdentityTag("STOLEN_TAG", enabled: true),
            new CanonicalIdentityTag("STOLEN_VALUE_INT", enabled: true, valueInt: 35)
        }, new[]
        {
            new CanonicalIdentityFeature("shiny", "quality", isFeatureExposed: true)
        });

        var attributes = CanonicalAttributeSource.Instance.Read(key);

        Assert.That(attributes.Get("item").Text, Is.EqualTo("item.poison"));
        Assert.That(attributes.Get("type").Texts, Is.EquivalentTo(new[] { "POISON" }));
        Assert.That(attributes.Get("legality").Text, Is.EqualTo("high"));
        Assert.That(attributes.Get("stolen").Flag, Is.True);
        Assert.That(attributes.Get("heat").Number, Is.EqualTo(35));
        Assert.That(attributes.Get("feature:shiny").Flag, Is.True);
        Assert.That(attributes.Get("feature:cursed").IsKnown, Is.True, "No hidden feature exists.");
        Assert.That(attributes.Get("feature:cursed").Flag, Is.False);
    }

    [Test]
    public void CanonicalAttributes_KnownLegalItemHasHeatZeroAndHiddenFeaturesStayUnknown()
    {
        var key = Canonical("item.apple", features: new[]
        {
            new CanonicalIdentityFeature("secret", "quality", isFeatureExposed: false),
            new CanonicalIdentityFeature("old", "quality", isFeatureExposed: true, isDisabled: true)
        });

        var attributes = CanonicalAttributeSource.Instance.Read(key);

        Assert.That(attributes.Get("legality").Text, Is.EqualTo("none"));
        Assert.That(attributes.Get("stolen").Flag, Is.False);
        Assert.That(attributes.Get("heat").IsKnown, Is.True);
        Assert.That(attributes.Get("heat").Number, Is.Zero);
        Assert.That(attributes.Get("feature:secret").IsKnown, Is.False, "An unexposed feature is never revealed.");
        Assert.That(attributes.Get("feature:anything").IsKnown, Is.False, "Hidden features make absence unknown.");
        Assert.That(attributes.Get("feature:old").IsKnown, Is.False, "A disabled feature does not count as exposed.");
    }

    [Test]
    public void FeatureConditions_NotShownByTheRuleSampleMakeThePartitionInvalid()
    {
        var plain = Canonical("item.plain");
        var rule = new PartitionRule("feature", 0, null, plain, new[]
        {
            new PartitionCondition("feature:no_such_feature", PartitionOperator.Is)
        });
        var blacklist = new PartitionConfig(PartitionMode.Blacklist, new[] { rule });

        Assert.That(rule.Validate(), Is.EqualTo(PartitionIssue.UnknownField));
        Assert.That(new PartitionRule("bare", 0, null, null, rule.Conditions).Validate(), Is.EqualTo(PartitionIssue.UnknownField),
            "A feature condition without a sample cannot be checked either.");
        var partition = CellPartition.From(blacklist, CanonicalAttributeSource.Instance);
        Assert.That(partition.IsValid, Is.False);
        Assert.That(partition.Rejection(plain), Is.Not.Null, "An item without the feature is not admitted as known false.");
    }

    [Test]
    public void DynamicFeatureConditions_UseExposedIdentityAndUnknownHiddenAbsenceInBothModes()
    {
        var id = "feature:third_party_effect";
        var exposed = Canonical("item.exposed", features: new[]
        {
            new CanonicalIdentityFeature("third_party_effect", "quality", isFeatureExposed: true)
        });
        var rule = new PartitionRule("feature", 0, null, exposed, new[]
        {
            new PartitionCondition(id, PartitionOperator.Is)
        });
        var noHidden = Canonical("item.plain");
        var hidden = Canonical("item.hidden", features: new[]
        {
            new CanonicalIdentityFeature("secret", "quality", isFeatureExposed: false)
        });

        var whitelist = new PartitionConfig(PartitionMode.Whitelist, new[] { rule });
        var blacklist = new PartitionConfig(PartitionMode.Blacklist, new[] { rule });
        Assert.That(CellPartition.From(whitelist, CanonicalAttributeSource.Instance).Rejection(exposed), Is.Null);
        Assert.That(CellPartition.From(whitelist, CanonicalAttributeSource.Instance).Rejection(noHidden),
            Is.EqualTo(StoreRejection.PartitionRejected));
        Assert.That(CellPartition.From(whitelist, CanonicalAttributeSource.Instance).Rejection(hidden),
            Is.EqualTo(StoreRejection.PartitionUnknown));
        Assert.That(CellPartition.From(blacklist, CanonicalAttributeSource.Instance).Rejection(exposed),
            Is.EqualTo(StoreRejection.PartitionRejected));
        Assert.That(CellPartition.From(blacklist, CanonicalAttributeSource.Instance).Rejection(hidden),
            Is.EqualTo(StoreRejection.PartitionUnknown));
    }

    [Test]
    public void CanonicalAttributes_UnrecognisedContrabandLevelIsUnknown()
    {
        var key = Canonical("item.x", tags: new[]
        {
            new CanonicalIdentityTag("CONTRABAND_ITEM_TAG", enabled: true),
            new CanonicalIdentityTag("CONTRABAND_LEVEL", valueString: "CONTRABAND_LEVEL_EXTREME")
        });

        Assert.That(CanonicalAttributeSource.Instance.Read(key).Get("legality").IsKnown, Is.False);
        Assert.That(CanonicalAttributeSource.Instance.Read(Apple).Get("item").Text, Is.EqualTo("item.apple"));
        Assert.That(CanonicalAttributeSource.Instance.Read(Apple).Get("stolen").IsKnown, Is.False);
    }

    [Test]
    public void MatchesFilter_TurnsBlacklistResultsAroundButNeverUnknown()
    {
        IPartitionList list = new RulePartitionList(new[] { HeatBelow("r0", 0, 40), PartitionRule.ForSample("r1", 1, Pear) },
            CanonicalAttributeSource.Instance);

        Assert.That(list.MatchesFilter(Pear, PartitionMode.Blacklist), Is.EqualTo(PartitionMatch.NoMatch));
        Assert.That(list.MatchesFilter(Apple, PartitionMode.Blacklist), Is.EqualTo(PartitionMatch.Unknown));
    }
}
