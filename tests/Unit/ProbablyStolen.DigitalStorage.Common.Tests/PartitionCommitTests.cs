using System;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class PartitionCommitTests
{
    private static readonly ItemKey Apple = new("item.apple");

    /// <summary>A tag store in memory with injectable read and write failures.</summary>
    private sealed class FakeStore : ICellPartitionStore
    {
        internal PartitionSnapshot Stored { get; set; } = new(null, null, null, false);
        internal int FailWrites { get; set; }
        internal int Writes { get; private set; }
        internal int Captures { get; private set; }

        public PartitionSnapshot Capture()
        {
            Captures++;
            return Stored;
        }

        public void Replace(PartitionSnapshot snapshot)
        {
            Writes++;
            if (FailWrites > 0)
            {
                FailWrites--;
                throw new InvalidOperationException("synthetic write failure");
            }

            Stored = snapshot;
        }
    }

    private static PartitionConfig Draft(PartitionMode mode = PartitionMode.Whitelist, params PartitionRule[] rules) =>
        new(mode, rules.Length == 0 ? new[] { PartitionRule.ForSample("r0", 0, Apple) } : rules);

    [Test]
    public void Codec_RoundTripsRulesNamesUnicodeAndDisplayKeys()
    {
        var canonical = CanonicalIdentityEncoder.Encode(new CanonicalIdentityInput("item.poison", new[] { "POISON" },
            Array.Empty<CanonicalIdentityTag>(), Array.Empty<CanonicalIdentityFeature>()));
        var rules = new[]
        {
            new PartitionRule("id\t,|1", 3, "一小瓶“梦魇尘”,|\t", canonical, new[]
            {
                new PartitionCondition("item", PartitionOperator.Is, PartitionValue.Of("item.poison")),
                new PartitionCondition("heat", PartitionOperator.Range, PartitionValue.Of(-5), PartitionValue.Of(40)),
                new PartitionCondition("stolen", PartitionOperator.IsNot)
            }),
            new PartitionRule("r2", 0, null, Apple, new[] { new PartitionCondition("type", PartitionOperator.Contains, PartitionValue.Of("DRUG")) })
        };

        Assert.That(PartitionRuleCodec.TryDecode(PartitionRuleCodec.Encode(rules), out var decoded), Is.True);

        Assert.That(decoded.Count, Is.EqualTo(2));
        Assert.That((decoded[0].RuleId, decoded[0].Slot, decoded[0].Name), Is.EqualTo((rules[0].RuleId, 3, rules[0].Name)));
        Assert.That(decoded[0].DisplayKey, Is.EqualTo(canonical));
        Assert.That(decoded[0].Conditions, Is.EqualTo(rules[0].Conditions));
        Assert.That(decoded[1].DisplayKey!.Identifier, Is.EqualTo("item.apple"));
        Assert.That(decoded[1].Name, Is.Null);
    }

    [Test]
    public void Codec_HandlesALargeRuleSet()
    {
        var rules = Enumerable.Range(0, 1200).Select(i => new PartitionRule($"r{i}", i, $"rule {i}", null, new[]
        {
            new PartitionCondition("item", PartitionOperator.Is, PartitionValue.Of($"item.{i}")),
            new PartitionCondition("heat", PartitionOperator.Lt, PartitionValue.Of(i)),
            new PartitionCondition("legality", PartitionOperator.IsNot, PartitionValue.Of("high"))
        })).ToArray();

        Assert.That(PartitionRuleCodec.TryDecode(PartitionRuleCodec.Encode(rules), out var decoded), Is.True);
        Assert.That(decoded.Select(rule => rule.RuleId), Is.EqualTo(rules.Select(rule => rule.RuleId)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("v2\n")]
    [TestCase("v1\nonly\tfour\tfields\there")]
    [TestCase("v1\ncjA=\tx\t-\t-\t")]
    [TestCase("v1\ncjA=\t0\t-\t-\taXRlbQ==|0|q")]
    [TestCase("v1\ncjA=\t0\t-\t-\taXRlbQ==|0|t!!!|")]
    [TestCase("v1\ncjA=\t0\t-\tz\t")]
    public void Codec_RejectsAnyMalformedText(string? text)
    {
        Assert.That(PartitionRuleCodec.TryDecode(text, out var rules), Is.False);
        Assert.That(rules, Is.Empty);
    }

    [Test]
    public void Codec_EmptyRuleListRoundTrips()
    {
        Assert.That(PartitionRuleCodec.TryDecode(PartitionRuleCodec.Encode(Array.Empty<PartitionRule>()), out var rules), Is.True);
        Assert.That(rules, Is.Empty);
    }

    [Test]
    public void Interpret_ReplacesAnUnknownModeAndIgnoresALeftoverUnconfirmedFlag()
    {
        var clean = new PartitionSnapshot(null, null, null, false).Interpret();
        Assert.That((clean.Config.Mode, clean.Repaired), Is.EqualTo((PartitionMode.Whitelist, false)));

        var unknownMode = new PartitionSnapshot("odd", null, 3, false).Interpret();
        Assert.That((unknownMode.Config.Mode, unknownMode.Config.Revision, unknownMode.Repaired),
            Is.EqualTo((PartitionMode.Whitelist, 3, true)), "An unknown mode reads as the default whitelist.");

        var unconfirmed = new PartitionSnapshot("blacklist", null, 4, true).Interpret();
        Assert.That((unconfirmed.Config.Mode, unconfirmed.Config.Revision, unconfirmed.Repaired),
            Is.EqualTo((PartitionMode.Blacklist, 4, true)));
    }

    [Test]
    public void Interpret_DropsOnlyTheDamagedRulesAndKeepsTheRest()
    {
        var good = PartitionCodecLine(PartitionRule.ForSample("a", 0, Apple));
        var read = new PartitionSnapshot("whitelist", "v1\n" + good + "\nbad line", 5, false).Interpret();

        Assert.That(read.Config.Rules.Select(rule => rule.RuleId), Is.EqualTo(new[] { "a" }));
        Assert.That((read.DroppedRules, read.Repaired, read.Config.Revision), Is.EqualTo((1, true, 5)));
        Assert.That(CellPartition.From(read.Config, CanonicalAttributeSource.Instance).IsValid, Is.True,
            "The cell keeps filtering with the readable rules instead of refusing everything.");

        var unknownFormat = new PartitionSnapshot("whitelist", "v9\nwhatever\nmore", 5, false).Interpret();
        Assert.That((unknownFormat.Config.Rules.Count, unknownFormat.DroppedRules), Is.EqualTo((0, 2)));
    }

    private static string PartitionCodecLine(PartitionRule rule) => PartitionRuleCodec.Encode(new[] { rule }).Split('\n')[1];

    [Test]
    public void Interpret_KeepsTheFirstOfDuplicateSlotsAndIdentifiers()
    {
        var duplicateSlot = new[]
        {
            PartitionRule.ForSample("a", 0, Apple),
            PartitionRule.ForSample("b", 0, new ItemKey("item.banana"))
        };
        var duplicateId = new[]
        {
            PartitionRule.ForSample("same", 0, Apple),
            PartitionRule.ForSample("same", 1, new ItemKey("item.banana"))
        };

        var slotRead = new PartitionSnapshot("whitelist", PartitionRuleCodec.Encode(duplicateSlot), 1, false).Interpret();
        var idRead = new PartitionSnapshot("whitelist", PartitionRuleCodec.Encode(duplicateId), 1, false).Interpret();

        Assert.That((slotRead.Config.Rules.Single().RuleId, slotRead.DroppedRules), Is.EqualTo(("a", 1)));
        Assert.That((idRead.Config.Rules.Single().Slot, idRead.DroppedRules), Is.EqualTo((0, 1)));
    }

    [Test]
    public void Apply_WritesVerifiesAndAdvancesTheRevision()
    {
        var store = new FakeStore();

        var result = PartitionCommit.Apply(store, Draft(), 0);

        Assert.That(result.Status, Is.EqualTo(PartitionCommitStatus.Applied));
        Assert.That(result.Applied!.Revision, Is.EqualTo(1));
        var read = store.Stored.Interpret();
        Assert.That((read.Config.Mode, read.Config.Rules.Count, read.Config.Revision), Is.EqualTo((PartitionMode.Whitelist, 1, 1)));

        Assert.That(PartitionCommit.Apply(store, Draft(PartitionMode.Blacklist), 1).Status, Is.EqualTo(PartitionCommitStatus.Applied));
        Assert.That(store.Stored.Interpret().Config!.Mode, Is.EqualTo(PartitionMode.Blacklist));
    }

    [Test]
    public void Apply_RefusesAStaleRevisionWithoutWriting()
    {
        var store = new FakeStore { Stored = new PartitionSnapshot("whitelist", null, 5, false) };

        var result = PartitionCommit.Apply(store, Draft(), 4);

        Assert.That(result.Status, Is.EqualTo(PartitionCommitStatus.Conflict));
        Assert.That(store.Writes, Is.Zero);
    }

    [Test]
    public void Apply_RefusesAnInvalidDraftWithoutWriting()
    {
        var store = new FakeStore();
        var draft = new PartitionConfig(PartitionMode.Whitelist, new[] { new PartitionRule("r0", 0, null, null, Array.Empty<PartitionCondition>()) });

        Assert.That(PartitionCommit.Apply(store, draft, 0).Status, Is.EqualTo(PartitionCommitStatus.Invalid));
        Assert.That(store.Writes, Is.Zero);
    }

    public void Apply_ReturnsFailedWhenAtomicReplacementThrows()
    {
        var previous = new PartitionSnapshot("blacklist", PartitionRuleCodec.Encode(new[] { PartitionRule.ForSample("old", 0, Apple) }), 2, false);
        var store = new FakeStore { Stored = previous, FailWrites = 1 };

        var result = PartitionCommit.Apply(store, Draft(), 2);

        Assert.That(result.Status, Is.EqualTo(PartitionCommitStatus.Failed));
        Assert.That(store.Stored, Is.EqualTo(previous));
    }

    [Test]
    public void Apply_DoesNotReadBackAfterAtomicReplacement()
    {
        var store = new FakeStore();

        var result = PartitionCommit.Apply(store, Draft(), 0);

        Assert.That(result.Status, Is.EqualTo(PartitionCommitStatus.Applied));
        Assert.That(store.Writes, Is.EqualTo(1));
        Assert.That(store.Captures, Is.EqualTo(1));
    }

    [Test]
    public void Apply_RejectsDuplicateSlotsBeforeReplacement()
    {
        var rules = new[]
        {
            PartitionRule.ForSample("a", 0, Apple),
            PartitionRule.ForSample("b", 0, new ItemKey("item.banana"))
        };
        var store = new FakeStore();

        Assert.That(PartitionCommit.Apply(store, new PartitionConfig(PartitionMode.Whitelist, rules), 0).Status,
            Is.EqualTo(PartitionCommitStatus.Invalid));
        Assert.That(store.Writes, Is.Zero);
    }

    [Test]
    public void Apply_ClearsAnEarlierUnconfirmedFlag()
    {
        var store = new FakeStore { Stored = new PartitionSnapshot("whitelist", null, 3, true) };

        Assert.That(PartitionCommit.Apply(store, Draft(), 3).Status, Is.EqualTo(PartitionCommitStatus.Applied));
        Assert.That(store.Stored.Unconfirmed, Is.False);
    }

    [Test]
    public void HasRules_CountsOnlyUsableRules()
    {
        Assert.That(PartitionCommit.HasRules(new PartitionSnapshot("whitelist", null, 1, false)), Is.False);
        Assert.That(PartitionCommit.HasRules(new PartitionSnapshot("whitelist", PartitionRuleCodec.Encode(Array.Empty<PartitionRule>()), 1, false)), Is.False);
        Assert.That(PartitionCommit.HasRules(new PartitionSnapshot("whitelist", "garbage", 1, false)), Is.False,
            "Damaged rules are removed, so they do not block disassembly.");
        Assert.That(PartitionCommit.HasRules(new PartitionSnapshot(null, PartitionRuleCodec.Encode(new[] { PartitionRule.ForSample("r", 0, Apple) }), 1, false)), Is.True);
    }
}
