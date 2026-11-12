using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class PartitionRuleTests
{
    private static PartitionCondition Cond(string field, PartitionOperator op, object? target = null, long? upper = null) =>
        new(field, op, target switch
        {
            null => null,
            string text => PartitionValue.Of(text),
            int number => PartitionValue.Of(number),
            long number => PartitionValue.Of(number),
            _ => throw new ArgumentException("Unsupported target.")
        }, upper == null ? null : PartitionValue.Of(upper.Value));

    private static PartitionRule Rule(string id, int slot, params PartitionCondition[] conditions) =>
        new(id, slot, null, null, conditions);

    private static ItemAttributeSet Attributes(params (string Field, AttributeValue Value)[] values) =>
        new(values.ToDictionary(v => v.Field, v => v.Value), hasHiddenFeatures: false);

    [Test]
    public void Validate_ReportsEachKindOfInvalidCondition()
    {
        Assert.That(Cond("heat", PartitionOperator.Lt, 40).Validate(), Is.Null);
        Assert.That(Cond("stolen", PartitionOperator.Is).Validate(), Is.Null);
        Assert.That(Cond("price", PartitionOperator.Is, "1").Validate(), Is.EqualTo(PartitionIssue.UnknownField));
        Assert.That(Cond("heat", PartitionOperator.Contains, 40).Validate(), Is.EqualTo(PartitionIssue.UnsupportedOperator));
        Assert.That(Cond("heat", PartitionOperator.Lt).Validate(), Is.EqualTo(PartitionIssue.MissingValue));
        Assert.That(Cond("heat", PartitionOperator.Range, 10).Validate(), Is.EqualTo(PartitionIssue.MissingValue));
        Assert.That(Cond("stolen", PartitionOperator.Is, "yes").Validate(), Is.EqualTo(PartitionIssue.MissingValue));
        Assert.That(Cond("heat", PartitionOperator.Lt, "40").Validate(), Is.EqualTo(PartitionIssue.InvalidValue));
        Assert.That(Cond("legality", PartitionOperator.Is, "illegal").Validate(), Is.EqualTo(PartitionIssue.InvalidValue));
        Assert.That(Cond("legality", PartitionOperator.Is, "high").Validate(), Is.Null);
    }

    [Test]
    public void Validate_RefusesEmptyRulesAndDuplicateIdsOrSlots()
    {
        Assert.That(Rule("a", 0).Validate(), Is.EqualTo(PartitionIssue.NoConditions));
        Assert.That(Rule("a", -1, Cond("stolen", PartitionOperator.Is)).Validate(), Is.EqualTo(PartitionIssue.InvalidSlot));

        var config = new PartitionConfig(PartitionMode.Whitelist, new[]
        {
            Rule("a", 0, Cond("stolen", PartitionOperator.Is)),
            Rule("a", 1, Cond("stolen", PartitionOperator.Is)),
            Rule("b", 1, Cond("stolen", PartitionOperator.IsNot)),
            Rule("c", 2)
        });

        Assert.That(config.Validate().Select(p => p.ToString()),
            Is.EquivalentTo(new[] { "DuplicateRuleId@a", "InvalidSlot@b", "NoConditions@c" }));
        Assert.That(new PartitionConfig((PartitionMode)9, Array.Empty<PartitionRule>()).Validate().Single().Issue,
            Is.EqualTo(PartitionIssue.UnknownMode));
        Assert.That(PartitionConfig.Empty.Validate(), Is.Empty);
    }

    [TestCase(PartitionOperator.Eq, 40, 40, true)]
    [TestCase(PartitionOperator.Ne, 40, 40, false)]
    [TestCase(PartitionOperator.Gt, 40, 40, false)]
    [TestCase(PartitionOperator.Ge, 40, 40, true)]
    [TestCase(PartitionOperator.Lt, 40, 39, true)]
    [TestCase(PartitionOperator.Le, 40, 41, false)]
    public void Evaluate_ComparesNumbers(PartitionOperator op, int target, long value, bool matches)
    {
        Assert.That(Cond("heat", op, target).Evaluate(AttributeValue.Of(value)),
            Is.EqualTo(matches ? PartitionMatch.Match : PartitionMatch.NoMatch));
    }

    [Test]
    public void Evaluate_RangeIsInclusiveAndUnknownStaysUnknown()
    {
        var range = Cond("heat", PartitionOperator.Range, 10, 20);
        Assert.That(range.Evaluate(AttributeValue.Of(10L)), Is.EqualTo(PartitionMatch.Match));
        Assert.That(range.Evaluate(AttributeValue.Of(20L)), Is.EqualTo(PartitionMatch.Match));
        Assert.That(range.Evaluate(AttributeValue.Of(21L)), Is.EqualTo(PartitionMatch.NoMatch));
        Assert.That(range.Evaluate(AttributeValue.Unknown), Is.EqualTo(PartitionMatch.Unknown));
        Assert.That(Cond("heat", PartitionOperator.Ne, 0).Evaluate(AttributeValue.Unknown), Is.EqualTo(PartitionMatch.Unknown),
            "A negated condition never turns an unknown value into a match.");
    }

    [Test]
    public void Evaluate_ComparesTextsSetsAndBooleans()
    {
        Assert.That(Cond("item", PartitionOperator.Is, "poison").Evaluate(AttributeValue.Of("poison")), Is.EqualTo(PartitionMatch.Match));
        Assert.That(Cond("item", PartitionOperator.IsNot, "poison").Evaluate(AttributeValue.Of("poison")), Is.EqualTo(PartitionMatch.NoMatch));
        var types = AttributeValue.OfSet(new[] { "POISON", "DRUG" });
        Assert.That(Cond("type", PartitionOperator.Contains, "DRUG").Evaluate(types), Is.EqualTo(PartitionMatch.Match));
        Assert.That(Cond("type", PartitionOperator.NotContains, "DRUG").Evaluate(types), Is.EqualTo(PartitionMatch.NoMatch));
        Assert.That(Cond("stolen", PartitionOperator.Is).Evaluate(AttributeValue.Of(false)), Is.EqualTo(PartitionMatch.NoMatch));
        Assert.That(Cond("stolen", PartitionOperator.IsNot).Evaluate(AttributeValue.Of(false)), Is.EqualTo(PartitionMatch.Match));
    }

    [Test]
    public void Rule_CombinesConditionsWithThreeValuedAnd()
    {
        var rule = Rule("a", 0, Cond("item", PartitionOperator.Is, "poison"), Cond("heat", PartitionOperator.Lt, 40));

        Assert.That(rule.Evaluate(Attributes(("item", AttributeValue.Of("poison")), ("heat", AttributeValue.Of(0L)))),
            Is.EqualTo(PartitionMatch.Match));
        Assert.That(rule.Evaluate(Attributes(("item", AttributeValue.Of("poison")))), Is.EqualTo(PartitionMatch.Unknown));
        Assert.That(rule.Evaluate(Attributes(("item", AttributeValue.Of("apple")))), Is.EqualTo(PartitionMatch.NoMatch),
            "A known miss decides the rule even when another value is unknown.");
    }

    [Test]
    public void ForSample_BuildsTheDefaultItemRule()
    {
        var rule = PartitionRule.ForSample("r1", 3, new ItemKey("poison"));

        Assert.That(rule.IsDefaultFor("poison"), Is.True);
        Assert.That(rule.IsDefaultFor("apple"), Is.False);
        Assert.That(rule.With(name: "mine").IsDefaultFor("poison"), Is.False);
        Assert.That(rule.DisplayKey!.Identifier, Is.EqualTo("poison"));
        Assert.That(rule.Validate(), Is.Null);
    }

    [Test]
    public void Lint_FindsContradictionsItCanProve()
    {
        var warnings = PartitionLint.Check(new[]
        {
            Rule("interval", 0, Cond("heat", PartitionOperator.Gt, 40), Cond("heat", PartitionOperator.Lt, 30)),
            Rule("point", 1, Cond("heat", PartitionOperator.Eq, 5), Cond("heat", PartitionOperator.Ne, 5)),
            Rule("bool", 2, Cond("stolen", PartitionOperator.Is), Cond("stolen", PartitionOperator.IsNot)),
            Rule("enum", 3, Cond("legality", PartitionOperator.Is, "low"), Cond("legality", PartitionOperator.Is, "high")),
            Rule("set", 4, Cond("type", PartitionOperator.Contains, "DRUG"), Cond("type", PartitionOperator.NotContains, "DRUG")),
            Rule("fine", 5, Cond("heat", PartitionOperator.Range, 10, 20), Cond("heat", PartitionOperator.Ne, 15))
        });

        Assert.That(warnings.Where(w => w.Kind == PartitionWarningKind.NeverMatches).Select(w => w.RuleId),
            Is.EquivalentTo(new[] { "interval", "point", "bool", "enum", "set" }));
    }

    [Test]
    public void Lint_ReportsARuleThatABroaderRuleCovers()
    {
        var broad = Rule("broad", 0, Cond("item", PartitionOperator.Is, "poison"));
        var narrow = Rule("narrow", 1, Cond("item", PartitionOperator.Is, "poison"), Cond("heat", PartitionOperator.Lt, 40));
        var twin = Rule("twin", 2, Cond("item", PartitionOperator.Is, "poison"));

        var warnings = PartitionLint.Check(new[] { narrow, broad, twin }).Select(w => w.ToString()).ToList();

        Assert.That(warnings, Is.EquivalentTo(new[] { "Covered@narrow<broad", "Covered@twin<broad" }));
    }
}
