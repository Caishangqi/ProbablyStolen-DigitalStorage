using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class CellPartitionTests
{
    [Test]
    public void Unfiltered_AcceptsEverySuppliedKeyAndRejectsNull()
    {
        var partition = CellPartition.Unfiltered;

        Assert.That(partition.IsValid, Is.True);
        Assert.That(partition.Failure, Is.EqualTo(CellPartitionFailure.None));
        Assert.That(partition.Mode, Is.EqualTo(IncludeExclude.Whitelist));
        Assert.That(partition.List.GetItems(), Is.Empty);
        Assert.That(partition.MatchesFilter(new ItemKey("item.apple")), Is.True);
        Assert.That(partition.MatchesFilter(ItemKey.Of("item.tool", "durability", "1")), Is.True);
        Assert.That(partition.MatchesFilter(null), Is.False);
    }

    [Test]
    public void Whitelist_MatchesExactIdentifierAndCompleteStateOnly()
    {
        var listed = ItemKey.Of("item.tool", "durability", "1");
        var partition = CellPartition.FromUserSubmission(new[] { listed }, 63);

        Assert.That(partition.Mode, Is.EqualTo(IncludeExclude.Whitelist));
        Assert.That(partition.IsValid, Is.True);
        Assert.That(partition.MatchesFilter(ItemKey.Of("item.tool", "durability", "1")), Is.True);
        Assert.That(partition.MatchesFilter(ItemKey.Of("item.tool", "durability", "2")), Is.False);
        Assert.That(partition.MatchesFilter(new ItemKey("item.tool")), Is.False);
        Assert.That(partition.MatchesFilter(new ItemKey("item.other")), Is.False);
        Assert.That(partition.MatchesFilter(null), Is.False);
    }

    [Test]
    public void FromUserSubmission_CollapsesDuplicateKeys()
    {
        var first = ItemKey.Of("item.tool", "durability", "1");
        var second = new ItemKey("item.apple");

        var partition = CellPartition.FromUserSubmission(new[] { first, first, second, first }, 63);

        Assert.That(partition.List.GetItems().Count, Is.EqualTo(2));
        Assert.That(partition.List.GetItems(), Is.EqualTo(new[] { first, second }));
    }

    [Test]
    public void FromUserSubmission_EmptyEditClearsBackToUnfiltered()
    {
        var fromEmptyList = CellPartition.FromUserSubmission(Array.Empty<ItemKey>(), 63);
        var fromEmptyItems = CellPartition.FromUserSubmission(Array.Empty<ItemKey?>(), 63);

        Assert.That(fromEmptyList.Mode, Is.EqualTo(IncludeExclude.Whitelist));
        Assert.That(fromEmptyList.IsValid, Is.True);
        Assert.That(fromEmptyList.List.GetItems(), Is.Empty);
        Assert.That(fromEmptyList.MatchesFilter(new ItemKey("item.apple")), Is.True);
        Assert.That(fromEmptyItems.Mode, Is.EqualTo(IncludeExclude.Whitelist));
        Assert.That(fromEmptyItems.IsValid, Is.True);
    }

    [Test]
    public void FromUserSubmission_Accepts63UniqueKeysAndRejects64()
    {
        var sixtyThree = Enumerable.Range(0, 63).Select(index => new ItemKey($"item.synthetic_{index}")).ToArray();
        var sixtyFour = Enumerable.Range(0, 64).Select(index => new ItemKey($"item.synthetic_{index}")).ToArray();

        var accepted = CellPartition.FromUserSubmission(sixtyThree, 63);

        Assert.That(accepted.IsValid, Is.True);
        Assert.That(accepted.List.GetItems().Count, Is.EqualTo(63));
        Assert.That(accepted.MatchesFilter(sixtyThree[62]), Is.True);
        Assert.Throws<ArgumentException>(new Action(() => CellPartition.FromUserSubmission(sixtyFour, 63)));
    }

    [Test]
    public void FromUserSubmission_DeduplicatesBeforeApplyingTheUniqueLimit()
    {
        var key = new ItemKey("item.synthetic_repeated");
        var repeated = Enumerable.Range(0, 64).Select(_ => key).ToArray();

        var partition = CellPartition.FromUserSubmission(repeated, 63);

        Assert.That(partition.IsValid, Is.True);
        Assert.That(partition.List.GetItems().Count, Is.EqualTo(1));
    }

    [Test]
    public void FromUserSubmission_RejectsNullSubmissionAndNullKey()
    {
        Assert.Throws<ArgumentNullException>(new Action(() => CellPartition.FromUserSubmission(null!, 63)));
        Assert.Throws<ArgumentException>(new Action(() => CellPartition.FromUserSubmission(new ItemKey?[]
        {
            new ItemKey("item.apple"), null
        }, 63)));
    }

    [Test]
    public void FromConfiguration_EmptyListAcceptsEverythingInBothModes()
    {
        var whitelist = CellPartition.FromConfiguration(IncludeExclude.Whitelist, Array.Empty<ItemKey>(), false, 63);
        var blacklist = CellPartition.FromConfiguration(IncludeExclude.Blacklist, Array.Empty<ItemKey>(), false, 63);

        Assert.That(ReferenceEquals(whitelist, CellPartition.Unfiltered), Is.True);
        Assert.That(blacklist.IsValid, Is.True);
        Assert.That(blacklist.Mode, Is.EqualTo(IncludeExclude.Blacklist));
        Assert.That(blacklist.List.IsEmpty(), Is.True);
        Assert.That(whitelist.MatchesFilter(new ItemKey("item.apple")), Is.True);
        Assert.That(blacklist.MatchesFilter(new ItemKey("item.apple")), Is.True);
    }

    [Test]
    public void FromConfiguration_UsableConfigurationMatchesSuppliedKeys()
    {
        var listed = new ItemKey("item.apple");

        var whitelist = CellPartition.FromConfiguration(IncludeExclude.Whitelist, new[] { listed }, false, 63);
        var blacklist = CellPartition.FromConfiguration(IncludeExclude.Blacklist, new[] { listed }, false, 63);

        Assert.That(whitelist.IsValid, Is.True);
        Assert.That(whitelist.Mode, Is.EqualTo(IncludeExclude.Whitelist));
        Assert.That(whitelist.MatchesFilter(listed), Is.True);
        Assert.That(whitelist.MatchesFilter(new ItemKey("item.pear")), Is.False);
        Assert.That(blacklist.IsValid, Is.True);
        Assert.That(blacklist.MatchesFilter(listed), Is.False);
        Assert.That(blacklist.MatchesFilter(new ItemKey("item.pear")), Is.True);
        Assert.That(blacklist.MatchesFilter(null), Is.False);
    }

    [TestCase(IncludeExclude.Whitelist)]
    [TestCase(IncludeExclude.Blacklist)]
    public void FromConfiguration_MissingKeyCollectionIsInvalidAndNeverMatches(IncludeExclude mode)
    {
        var listed = new ItemKey("item.apple");

        var partition = CellPartition.FromConfiguration(mode, null, false, 63);

        Assert.That(partition.Mode, Is.EqualTo(mode));
        Assert.That(partition.IsValid, Is.False);
        Assert.That(partition.Failure, Is.EqualTo(CellPartitionFailure.MissingKeys));
        Assert.That(partition.List.IsEmpty(), Is.True);
        Assert.That(partition.MatchesFilter(listed), Is.False);
        Assert.That(partition.MatchesFilter(null), Is.False);
    }

    [Test]
    public void FromConfiguration_MalformedInputIsInvalidAndNeverFallsBackToUnfiltered()
    {
        var unknownMode = (IncludeExclude)99;
        var listed = new ItemKey("item.apple");

        var unknown = CellPartition.FromConfiguration(unknownMode, new[] { listed }, false, 63);
        var unknownWithoutKeys = CellPartition.FromConfiguration(unknownMode, null, false, 63);
        var withNullKey = CellPartition.FromConfiguration(IncludeExclude.Whitelist, new ItemKey?[] { listed, null }, false, 63);
        var blacklistWithNullKey = CellPartition.FromConfiguration(IncludeExclude.Blacklist, new ItemKey?[] { null }, false, 63);
        var tooMany = CellPartition.FromConfiguration(
            IncludeExclude.Whitelist,
            Enumerable.Range(0, 64).Select(index => new ItemKey($"item.synthetic_{index}")).ToArray(), false, 63);

        Assert.That(unknown.IsValid, Is.False);
        Assert.That(unknown.Failure, Is.EqualTo(CellPartitionFailure.UnknownMode));
        Assert.That(unknown.MatchesFilter(listed), Is.False);

        Assert.That(unknownWithoutKeys.IsValid, Is.False);
        Assert.That(unknownWithoutKeys.Failure, Is.EqualTo(CellPartitionFailure.UnknownMode));
        Assert.That(unknownWithoutKeys.MatchesFilter(listed), Is.False);

        Assert.That(withNullKey.IsValid, Is.False);
        Assert.That(withNullKey.Failure, Is.EqualTo(CellPartitionFailure.NullKey));
        Assert.That(withNullKey.MatchesFilter(listed), Is.False);

        Assert.That(blacklistWithNullKey.IsValid, Is.False);
        Assert.That(blacklistWithNullKey.Failure, Is.EqualTo(CellPartitionFailure.NullKey));
        Assert.That(blacklistWithNullKey.MatchesFilter(new ItemKey("item.pear")), Is.False);

        Assert.That(tooMany.IsValid, Is.False);
        Assert.That(tooMany.Failure, Is.EqualTo(CellPartitionFailure.TooManyKeys));
        Assert.That(tooMany.MatchesFilter(new ItemKey("item.synthetic_0")), Is.False);
    }

    [Test]
    public void PartitionList_MatchesFilterFollowsAe2Rules()
    {
        IPartitionList empty = PrecisePriorityList.Empty;
        IPartitionList listed = new PrecisePriorityList(new[] { new ItemKey("item.apple") });
        var apple = new ItemKey("item.apple");
        var pear = new ItemKey("item.pear");

        Assert.That(empty.MatchesFilter(apple, IncludeExclude.Whitelist), Is.True);
        Assert.That(empty.MatchesFilter(apple, IncludeExclude.Blacklist), Is.True);
        Assert.That(listed.MatchesFilter(apple, IncludeExclude.Whitelist), Is.True);
        Assert.That(listed.MatchesFilter(pear, IncludeExclude.Whitelist), Is.False);
        Assert.That(listed.MatchesFilter(apple, IncludeExclude.Blacklist), Is.False);
        Assert.That(listed.MatchesFilter(pear, IncludeExclude.Blacklist), Is.True);
    }

    [Test]
    public void PrecisePriorityList_RejectsNullAndRepeatedKeys()
    {
        var apple = new ItemKey("item.apple");

        Assert.Throws<ArgumentNullException>(new Action(() => _ = new PrecisePriorityList(null!)));
        Assert.Throws<ArgumentException>(new Action(() => _ = new PrecisePriorityList(new ItemKey[] { null! })));
        Assert.Throws<ArgumentException>(new Action(() => _ = new PrecisePriorityList(new[] { apple, apple })));
        Assert.That(new PrecisePriorityList(new[] { apple }).IsListed(null!), Is.False);
    }

    [Test]
    public void Keys_AreDefensiveCopiesThatCannotChangeThePartition()
    {
        var mutable = new List<ItemKey?> { new ItemKey("item.apple") };

        var partition = CellPartition.FromUserSubmission(mutable, 63);
        mutable.Add(new ItemKey("item.pear"));
        mutable.Clear();

        Assert.That(partition.List.GetItems().Count, Is.EqualTo(1));
        Assert.That(partition.MatchesFilter(new ItemKey("item.apple")), Is.True);
        Assert.That(partition.MatchesFilter(new ItemKey("item.pear")), Is.False);
        Assert.That(partition.List.GetItems(), Is.Not.AssignableTo<List<ItemKey>>());
        Assert.Throws<NotSupportedException>(new Action(() =>
            ((IList<ItemKey>)partition.List.GetItems()).Add(new ItemKey("item.plum"))));
    }

    [Test]
    public void Partitions_AreIndependentImmutableValues()
    {
        var first = CellPartition.FromUserSubmission(new[] { new ItemKey("item.apple") }, 63);
        var second = CellPartition.FromUserSubmission(new[] { new ItemKey("item.pear") }, 63);
        var cleared = CellPartition.FromUserSubmission(Array.Empty<ItemKey>(), 63);

        Assert.That(first.MatchesFilter(new ItemKey("item.apple")), Is.True);
        Assert.That(first.MatchesFilter(new ItemKey("item.pear")), Is.False);
        Assert.That(second.MatchesFilter(new ItemKey("item.pear")), Is.True);
        Assert.That(second.MatchesFilter(new ItemKey("item.apple")), Is.False);
        Assert.That(cleared.MatchesFilter(new ItemKey("item.apple")), Is.True);
        Assert.That(cleared.MatchesFilter(new ItemKey("item.pear")), Is.True);
        Assert.That(ReferenceEquals(first, second), Is.False);
        Assert.That(ReferenceEquals(cleared, CellPartition.Unfiltered), Is.True);
    }

    [Test]
    public void PartitionAccepts63Keys()
    {
        var keys = Enumerable.Range(0, 63).Select(index => new ItemKey($"item.synthetic_{index}")).ToArray();
        var partition = CellPartition.FromUserSubmission(keys, 63);

        Assert.That(partition.List.GetItems().Count, Is.EqualTo(63));
    }

    [Test]
    public void FromConfiguration_KeyLimitComesFromTheCaller()
    {
        var keys = Enumerable.Range(0, 128).Select(index => new ItemKey($"item.synthetic_{index}")).ToArray();

        var atLimit = CellPartition.FromConfiguration(IncludeExclude.Blacklist, keys, false, 128);
        var overLimit = CellPartition.FromConfiguration(IncludeExclude.Blacklist, keys, false, 127);

        Assert.That(atLimit.IsValid, Is.True);
        Assert.That(atLimit.List.GetItems().Count, Is.EqualTo(128));
        Assert.That(overLimit.Failure, Is.EqualTo(CellPartitionFailure.TooManyKeys));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
            CellPartition.FromConfiguration(IncludeExclude.Whitelist, keys, false, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => CellPartition.FromUserSubmission(keys, 0)));
    }

    [Test]
    public void EqualDistribution_IsKeptAndAlsoOnInvalidConfigurations()
    {
        var listed = new ItemKey("item.apple");

        var whitelist = CellPartition.FromConfiguration(IncludeExclude.Whitelist, new[] { listed }, true, 63);
        var emptyEqual = CellPartition.FromConfiguration(IncludeExclude.Whitelist, Array.Empty<ItemKey>(), true, 63);
        var invalid = CellPartition.FromConfiguration((IncludeExclude)7, new[] { listed }, true, 63);

        Assert.That(whitelist.EqualDistribution, Is.True);
        Assert.That(emptyEqual.EqualDistribution, Is.True);
        Assert.That(emptyEqual.MatchesFilter(new ItemKey("item.pear")), Is.True);
        Assert.That(ReferenceEquals(emptyEqual, CellPartition.Unfiltered), Is.False);
        Assert.That(invalid.EqualDistribution, Is.True);
        Assert.That(invalid.MatchesFilter(listed), Is.False);
        Assert.That(CellPartition.Unfiltered.EqualDistribution, Is.False);
    }

    [Test]
    public void EqualShareBase_UsesTheWhitelistCountCappedByTheTypeLimit()
    {
        var three = Enumerable.Range(0, 3).Select(index => new ItemKey($"item.synthetic_{index}")).ToArray();
        var whitelist = CellPartition.FromConfiguration(IncludeExclude.Whitelist, three, true, 63);
        var blacklist = CellPartition.FromConfiguration(IncludeExclude.Blacklist, three, true, 63);

        Assert.That(whitelist.EqualShareBase(128), Is.EqualTo(3));
        Assert.That(whitelist.EqualShareBase(2), Is.EqualTo(2));
        Assert.That(blacklist.EqualShareBase(128), Is.EqualTo(128));
        Assert.That(CellPartition.Unfiltered.EqualShareBase(150), Is.EqualTo(150));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => whitelist.EqualShareBase(0)));
    }
}
