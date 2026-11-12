using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

/// <summary>Rule evaluation may match, miss or remain unknown.</summary>
public enum PartitionMatch
{
    /// <summary>The item matches.</summary>
    Match = 0,

    /// <summary>The item does not match.</summary>
    NoMatch = 1,

    /// <summary>What the player can see does not decide it.</summary>
    Unknown = 2
}

/// <summary>How a partition filters new items. An empty list admits everything in either mode.</summary>
public enum PartitionMode
{
    /// <summary>Only items some rule matches are accepted.</summary>
    Whitelist = 1,

    /// <summary>Items some rule matches are refused.</summary>
    Blacklist = 2
}

/// <summary>Why a condition, rule or configuration cannot be applied.</summary>
public enum PartitionIssue
{
    /// <summary>A rule has no condition.</summary>
    NoConditions = 0,

    /// <summary>A condition names a field that is not registered.</summary>
    UnknownField = 1,

    /// <summary>A condition uses an operator its field does not support.</summary>
    UnsupportedOperator = 2,

    /// <summary>A condition lacks a target or an upper bound it needs, or has one it must not have.</summary>
    MissingValue = 3,

    /// <summary>A condition's target has the wrong kind or is not one of the field's values.</summary>
    InvalidValue = 4,

    /// <summary>Two rules share an identifier.</summary>
    DuplicateRuleId = 5,

    /// <summary>Two rules share a slot, or a slot is negative.</summary>
    InvalidSlot = 6,

    /// <summary>The mode is unknown.</summary>
    UnknownMode = 7
}

/// <summary>One condition: a field compared with a target. Every condition of a rule must hold.</summary>
public sealed class PartitionCondition : IEquatable<PartitionCondition>
{
    /// <summary>Creates a condition; <see cref="Validate"/> checks it against the field registry.</summary>
    public PartitionCondition(string fieldId, PartitionOperator op, PartitionValue? target = null, PartitionValue? upper = null)
    {
        FieldId = fieldId ?? throw new ArgumentNullException(nameof(fieldId));
        Operator = op;
        Target = target;
        Upper = upper;
    }

    /// <summary>Gets the field identifier.</summary>
    public string FieldId { get; }

    /// <summary>Gets the operator.</summary>
    public PartitionOperator Operator { get; }

    /// <summary>Gets the target, or the lower bound of a range; null for booleans.</summary>
    public PartitionValue? Target { get; }

    /// <summary>Gets the upper bound of a range.</summary>
    public PartitionValue? Upper { get; }

    /// <summary>Gets why the condition is invalid, or null when it is valid.</summary>
    public PartitionIssue? Validate()
    {
        var field = PartitionFieldRegistry.Find(FieldId);
        if (field == null) return PartitionIssue.UnknownField;
        if (!field.Supports(Operator)) return PartitionIssue.UnsupportedOperator;
        var needsUpper = Operator == PartitionOperator.Range;
        if (field.HasTarget != (Target != null) || needsUpper != (Upper != null)) return PartitionIssue.MissingValue;
        if (!field.HasTarget) return null;

        var expected = field.Kind == PartitionFieldKind.Number ? PartitionValueKind.Number : PartitionValueKind.Text;
        if (Target!.Kind != expected || (Upper != null && Upper.Kind != expected)) return PartitionIssue.InvalidValue;
        if (field.EnumValues != null && !field.EnumValues.Contains(Target.Text)) return PartitionIssue.InvalidValue;
        return null;
    }

    /// <summary>Evaluates the condition against what the player knows; a valid condition is assumed.</summary>
    public PartitionMatch Evaluate(AttributeValue value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (!value.IsKnown) return PartitionMatch.Unknown;
        bool result = Operator switch
        {
            PartitionOperator.Is when Target == null => value.Flag,
            PartitionOperator.IsNot when Target == null => !value.Flag,
            PartitionOperator.Is => string.Equals(value.Text, Target!.Text, StringComparison.Ordinal),
            PartitionOperator.IsNot => !string.Equals(value.Text, Target!.Text, StringComparison.Ordinal),
            PartitionOperator.Contains => value.Texts.Contains(Target!.Text!),
            PartitionOperator.NotContains => !value.Texts.Contains(Target!.Text!),
            PartitionOperator.Eq => value.Number == Target!.Number,
            PartitionOperator.Ne => value.Number != Target!.Number,
            PartitionOperator.Gt => value.Number > Target!.Number,
            PartitionOperator.Ge => value.Number >= Target!.Number,
            PartitionOperator.Lt => value.Number < Target!.Number,
            PartitionOperator.Le => value.Number <= Target!.Number,
            PartitionOperator.Range => value.Number >= Target!.Number && value.Number <= Upper!.Number,
            _ => throw new InvalidOperationException($"Unknown operator {Operator}.")
        };
        return result ? PartitionMatch.Match : PartitionMatch.NoMatch;
    }

    /// <inheritdoc />
    public bool Equals(PartitionCondition? other) =>
        other != null && FieldId == other.FieldId && Operator == other.Operator &&
        Equals(Target, other.Target) && Equals(Upper, other.Upper);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as PartitionCondition);

    /// <inheritdoc />
    public override int GetHashCode() => (FieldId, Operator, Target, Upper).GetHashCode();
}

/// <summary>One rule icon of a partition. Its conditions must all hold; its slot only orders the icons.</summary>
public sealed class PartitionRule
{
    /// <summary>Creates a rule.</summary>
    /// <param name="ruleId">A stable identifier that survives moves, renames and edits.</param>
    /// <param name="slot">The icon's grid slot; it never affects matching or routing.</param>
    /// <param name="name">An optional rule name entered by the player.</param>
    /// <param name="displayKey">The sample identity whose picture shows the icon, if any.</param>
    /// <param name="conditions">The conditions, in the order the player added them.</param>
    public PartitionRule(string ruleId, int slot, string? name, ItemKey? displayKey, IEnumerable<PartitionCondition> conditions)
    {
        if (string.IsNullOrEmpty(ruleId)) throw new ArgumentException("A rule needs an identifier.", nameof(ruleId));
        RuleId = ruleId;
        Slot = slot;
        Name = string.IsNullOrEmpty(name) ? null : name;
        DisplayKey = displayKey;
        var list = (conditions ?? throw new ArgumentNullException(nameof(conditions))).ToList();
        if (list.Any(condition => condition == null)) throw new ArgumentException("A condition cannot be null.", nameof(conditions));
        Conditions = list.AsReadOnly();
    }

    /// <summary>Gets the stable identifier.</summary>
    public string RuleId { get; }

    /// <summary>Gets the grid slot.</summary>
    public int Slot { get; }

    /// <summary>Gets the rule name entered by the player, or null.</summary>
    public string? Name { get; }

    /// <summary>Gets the identity whose picture shows the icon.</summary>
    public ItemKey? DisplayKey { get; }

    /// <summary>Gets the conditions.</summary>
    public IReadOnlyList<PartitionCondition> Conditions { get; }

    /// <summary>Creates the default rule for a dragged sample: "item is the sample's identifier".</summary>
    public static PartitionRule ForSample(string ruleId, int slot, ItemKey sample)
    {
        if (sample == null) throw new ArgumentNullException(nameof(sample));
        return new PartitionRule(ruleId, slot, null, sample, new[]
        {
            new PartitionCondition(PartitionFieldRegistry.Item, PartitionOperator.Is, PartitionValue.Of(sample.Identifier))
        });
    }

    /// <summary>Gets whether this is a default rule: one "item is" condition and no name.</summary>
    public bool IsDefaultFor(string identifier) =>
        Name == null && Conditions.Count == 1 &&
        Conditions[0].Equals(new PartitionCondition(PartitionFieldRegistry.Item, PartitionOperator.Is, PartitionValue.Of(identifier)));

    /// <summary>Gets a copy with other fields replaced.</summary>
    public PartitionRule With(int? slot = null, string? name = null, bool clearName = false,
        IEnumerable<PartitionCondition>? conditions = null, string? ruleId = null) =>
        new(ruleId ?? RuleId, slot ?? Slot, clearName ? null : name ?? Name, DisplayKey, conditions ?? Conditions);

    /// <summary>
    /// Gets why the rule is invalid, or null. A feature condition must name a feature that the rule's sample shows:
    /// features have no catalog, and an item without a feature reads as known false, so an unknown identifier would
    /// otherwise match every item as "does not have it" instead of making the partition invalid.
    /// </summary>
    public PartitionIssue? Validate()
    {
        if (Slot < 0) return PartitionIssue.InvalidSlot;
        if (Conditions.Count == 0) return PartitionIssue.NoConditions;
        HashSet<string>? sampleFeatures = null;
        foreach (var condition in Conditions)
        {
            var issue = condition.Validate();
            if (issue != null) return issue;
            if (!condition.FieldId.StartsWith(PartitionFieldRegistry.FeaturePrefix, StringComparison.Ordinal)) continue;
            sampleFeatures ??= SampleFeatureFields();
            if (!sampleFeatures.Contains(condition.FieldId)) return PartitionIssue.UnknownField;
        }

        return null;
    }

    private HashSet<string> SampleFeatureFields()
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        var summary = DisplayKey?.ReadSummary();
        if (summary == null) return fields;
        foreach (var feature in summary.Features)
            if (feature.Exposed && !string.IsNullOrEmpty(feature.Identifier)) fields.Add(PartitionFieldRegistry.FeatureField(feature.Identifier));
        return fields;
    }

    /// <summary>Every condition must match; a miss ends evaluation and otherwise unknown results are preserved.</summary>
    public PartitionMatch Evaluate(ItemAttributeSet attributes)
    {
        if (attributes == null) throw new ArgumentNullException(nameof(attributes));
        var unknown = false;
        foreach (var condition in Conditions)
        {
            switch (condition.Evaluate(attributes.Get(condition.FieldId)))
            {
                case PartitionMatch.NoMatch:
                    return PartitionMatch.NoMatch;
                case PartitionMatch.Unknown:
                    unknown = true;
                    break;
            }
        }

        return unknown ? PartitionMatch.Unknown : PartitionMatch.Match;
    }
}
