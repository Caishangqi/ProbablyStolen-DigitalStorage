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
        Assert.That(partition.Mode, Is.EqualTo(CellPartitionMode.Unfiltered));
        Assert.That(partition.Keys, Is.Empty);
        Assert.That(partition.Matches(new ItemKey("item.apple")), Is.True);
        Assert.That(partition.Matches(ItemKey.Of("item.tool", "durability", "1")), Is.True);
        Assert.That(partition.Matches(null), Is.False);
    }

    [Test]
    public void Whitelist_MatchesExactIdentifierAndCompleteStateOnly()
    {
        var listed = ItemKey.Of("item.tool", "durability", "1");
        var partition = CellPartition.FromUserSubmission(new[] { listed });

        Assert.That(partition.Mode, Is.EqualTo(CellPartitionMode.Whitelist));
        Assert.That(partition.IsValid, Is.True);
        Assert.That(partition.Matches(ItemKey.Of("item.tool", "durability", "1")), Is.True);
        Assert.That(partition.Matches(ItemKey.Of("item.tool", "durability", "2")), Is.False);
        Assert.That(partition.Matches(new ItemKey("item.tool")), Is.False);
        Assert.That(partition.Matches(new ItemKey("item.other")), Is.False);
        Assert.That(partition.Matches(null), Is.False);
    }

    [Test]
    public void FromUserSubmission_CollapsesDuplicateKeys()
    {
        var first = ItemKey.Of("item.tool", "durability", "1");
        var second = new ItemKey("item.apple");

        var partition = CellPartition.FromUserSubmission(new[] { first, first, second, first });

        Assert.That(partition.Keys.Count, Is.EqualTo(2));
        Assert.That(partition.Keys, Is.EqualTo(new[] { first, second }));
    }

    [Test]
    public void FromUserSubmission_EmptyEditClearsBackToUnfiltered()
    {
        var fromEmptyList = CellPartition.FromUserSubmission(Array.Empty<ItemKey>());
        var fromEmptyItems = CellPartition.FromUserSubmission(Array.Empty<ItemKey?>());

        Assert.That(fromEmptyList.Mode, Is.EqualTo(CellPartitionMode.Unfiltered));
        Assert.That(fromEmptyList.IsValid, Is.True);
        Assert.That(fromEmptyList.Keys, Is.Empty);
        Assert.That(fromEmptyList.Matches(new ItemKey("item.apple")), Is.True);
        Assert.That(fromEmptyItems.Mode, Is.EqualTo(CellPartitionMode.Unfiltered));
        Assert.That(fromEmptyItems.IsValid, Is.True);
    }

    [Test]
    public void FromUserSubmission_Accepts63UniqueKeysAndRejects64()
    {
        var sixtyThree = Enumerable.Range(0, 63).Select(index => new ItemKey($"item.synthetic_{index}")).ToArray();
        var sixtyFour = Enumerable.Range(0, 64).Select(index => new ItemKey($"item.synthetic_{index}")).ToArray();

        var accepted = CellPartition.FromUserSubmission(sixtyThree);

        Assert.That(accepted.IsValid, Is.True);
        Assert.That(accepted.Keys.Count, Is.EqualTo(63));
        Assert.That(accepted.Matches(sixtyThree[62]), Is.True);
        Assert.Throws<ArgumentException>(new Action(() => CellPartition.FromUserSubmission(sixtyFour)));
    }

    [Test]
    public void FromUserSubmission_DeduplicatesBeforeApplyingTheUniqueLimit()
    {
        var key = new ItemKey("item.synthetic_repeated");
        var repeated = Enumerable.Range(0, 64).Select(_ => key).ToArray();

        var partition = CellPartition.FromUserSubmission(repeated);

        Assert.That(partition.IsValid, Is.True);
        Assert.That(partition.Keys.Count, Is.EqualTo(1));
    }

    [Test]
    public void FromUserSubmission_RejectsNullSubmissionAndNullKey()
    {
        Assert.Throws<ArgumentNullException>(new Action(() => CellPartition.FromUserSubmission(null!)));
        Assert.Throws<ArgumentException>(new Action(() => CellPartition.FromUserSubmission(new ItemKey?[]
        {
            new ItemKey("item.apple"), null
        })));
    }

    [Test]
    public void FromConfiguration_ExplicitWhitelistWithoutKeysIsInvalidAndFailsClosed()
    {
        var fromEmpty = CellPartition.FromConfiguration(CellPartitionMode.Whitelist, Array.Empty<ItemKey>());
        var fromNull = CellPartition.FromConfiguration(CellPartitionMode.Whitelist, null);

        Assert.That(fromEmpty.Mode, Is.EqualTo(CellPartitionMode.Whitelist));
        Assert.That(fromEmpty.IsValid, Is.False);
        Assert.That(fromEmpty.Failure, Is.EqualTo(CellPartitionFailure.EmptyWhitelist));
        Assert.That(fromEmpty.Matches(new ItemKey("item.apple")), Is.False);
        Assert.That(fromNull.IsValid, Is.False);
        Assert.That(fromNull.Failure, Is.EqualTo(CellPartitionFailure.MissingKeys));
        Assert.That(fromNull.Matches(new ItemKey("item.apple")), Is.False);
    }

    [Test]
    public void FromConfiguration_UsableConfigurationMatchesSuppliedKeys()
    {
        var listed = new ItemKey("item.apple");

        var unfiltered = CellPartition.FromConfiguration(CellPartitionMode.Unfiltered, Array.Empty<ItemKey>());
        var whitelist = CellPartition.FromConfiguration(CellPartitionMode.Whitelist, new[] { listed });

        Assert.That(unfiltered.IsValid, Is.True);
        Assert.That(unfiltered.Failure, Is.EqualTo(CellPartitionFailure.None));
        Assert.That(unfiltered.Mode, Is.EqualTo(CellPartitionMode.Unfiltered));
        Assert.That(unfiltered.Matches(listed), Is.True);
        Assert.That(whitelist.IsValid, Is.True);
        Assert.That(whitelist.Mode, Is.EqualTo(CellPartitionMode.Whitelist));
        Assert.That(whitelist.Matches(listed), Is.True);
        Assert.That(whitelist.Matches(new ItemKey("item.pear")), Is.False);
    }

    [TestCase(CellPartitionMode.Unfiltered)]
    [TestCase(CellPartitionMode.Whitelist)]
    public void FromConfiguration_MissingKeyCollectionIsInvalidAndNeverMatches(CellPartitionMode mode)
    {
        var listed = new ItemKey("item.apple");

        var partition = CellPartition.FromConfiguration(mode, null);

        Assert.That(partition.Mode, Is.EqualTo(mode));
        Assert.That(partition.IsValid, Is.False);
        Assert.That(partition.Failure, Is.EqualTo(CellPartitionFailure.MissingKeys));
        Assert.That(partition.Keys, Is.Empty);
        Assert.That(partition.Matches(listed), Is.False);
        Assert.That(partition.Matches(null), Is.False);
    }

    [Test]
    public void FromConfiguration_MalformedInputIsInvalidAndNeverFallsBackToUnfiltered()
    {
        var unknownMode = (CellPartitionMode)99;
        var listed = new ItemKey("item.apple");

        var unknown = CellPartition.FromConfiguration(unknownMode, new[] { listed });
        var unknownWithoutKeys = CellPartition.FromConfiguration(unknownMode, null);
        var unfilteredWithKeys = CellPartition.FromConfiguration(CellPartitionMode.Unfiltered, new[] { listed });
        var withNullKey = CellPartition.FromConfiguration(CellPartitionMode.Whitelist, new ItemKey?[] { listed, null });
        var tooMany = CellPartition.FromConfiguration(
            CellPartitionMode.Whitelist,
            Enumerable.Range(0, 64).Select(index => new ItemKey($"item.synthetic_{index}")).ToArray());

        Assert.That(unknown.IsValid, Is.False);
        Assert.That(unknown.Failure, Is.EqualTo(CellPartitionFailure.UnknownMode));
        Assert.That(unknown.Matches(listed), Is.False);

        Assert.That(unknownWithoutKeys.IsValid, Is.False);
        Assert.That(unknownWithoutKeys.Failure, Is.EqualTo(CellPartitionFailure.UnknownMode));
        Assert.That(unknownWithoutKeys.Matches(listed), Is.False);

        Assert.That(unfilteredWithKeys.IsValid, Is.False);
        Assert.That(unfilteredWithKeys.Failure, Is.EqualTo(CellPartitionFailure.UnfilteredWithKeys));
        Assert.That(unfilteredWithKeys.Matches(listed), Is.False);

        Assert.That(withNullKey.IsValid, Is.False);
        Assert.That(withNullKey.Failure, Is.EqualTo(CellPartitionFailure.NullKey));
        Assert.That(withNullKey.Matches(listed), Is.False);

        Assert.That(tooMany.IsValid, Is.False);
        Assert.That(tooMany.Failure, Is.EqualTo(CellPartitionFailure.TooManyKeys));
        Assert.That(tooMany.Matches(new ItemKey("item.synthetic_0")), Is.False);
    }

    [Test]
    public void Keys_AreDefensiveCopiesThatCannotChangeThePartition()
    {
        var mutable = new List<ItemKey?> { new ItemKey("item.apple") };

        var partition = CellPartition.FromUserSubmission(mutable);
        mutable.Add(new ItemKey("item.pear"));
        mutable.Clear();

        Assert.That(partition.Keys.Count, Is.EqualTo(1));
        Assert.That(partition.Matches(new ItemKey("item.apple")), Is.True);
        Assert.That(partition.Matches(new ItemKey("item.pear")), Is.False);
        Assert.That(partition.Keys, Is.Not.AssignableTo<List<ItemKey>>());
        Assert.Throws<NotSupportedException>(new Action(() =>
            ((IList<ItemKey>)partition.Keys).Add(new ItemKey("item.plum"))));
    }

    [Test]
    public void Partitions_AreIndependentImmutableValues()
    {
        var first = CellPartition.FromUserSubmission(new[] { new ItemKey("item.apple") });
        var second = CellPartition.FromUserSubmission(new[] { new ItemKey("item.pear") });
        var cleared = CellPartition.FromUserSubmission(Array.Empty<ItemKey>());

        Assert.That(first.Matches(new ItemKey("item.apple")), Is.True);
        Assert.That(first.Matches(new ItemKey("item.pear")), Is.False);
        Assert.That(second.Matches(new ItemKey("item.pear")), Is.True);
        Assert.That(second.Matches(new ItemKey("item.apple")), Is.False);
        Assert.That(cleared.Matches(new ItemKey("item.apple")), Is.True);
        Assert.That(cleared.Matches(new ItemKey("item.pear")), Is.True);
        Assert.That(ReferenceEquals(first, second), Is.False);
        Assert.That(ReferenceEquals(cleared, CellPartition.Unfiltered), Is.True);
    }

    [Test]
    public void PartitionAccepts63Keys()
    {
        var keys = Enumerable.Range(0, 63).Select(index => new ItemKey($"item.synthetic_{index}")).ToArray();
        var partition = CellPartition.FromUserSubmission(keys);

        Assert.That(partition.Keys.Count, Is.EqualTo(63));
    }
}
