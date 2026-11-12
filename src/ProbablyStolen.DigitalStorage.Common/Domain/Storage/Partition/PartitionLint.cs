using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

/// <summary>The kind of a partition warning. Warnings never stop a configuration from being applied.</summary>
public enum PartitionWarningKind
{
    /// <summary>The rule's conditions contradict each other, so it never matches.</summary>
    NeverMatches = 0,

    /// <summary>Another rule matches everything this rule matches, so this rule adds nothing.</summary>
    Covered = 1
}

/// <summary>A warning about one rule.</summary>
public readonly struct PartitionWarning
{
    /// <summary>Creates a warning.</summary>
    public PartitionWarning(PartitionWarningKind kind, string ruleId, string? byRuleId = null)
    {
        Kind = kind;
        RuleId = ruleId;
        ByRuleId = byRuleId;
    }

    /// <summary>Gets the kind.</summary>
    public PartitionWarningKind Kind { get; }

    /// <summary>Gets the rule warned about.</summary>
    public string RuleId { get; }

    /// <summary>Gets the broader rule that covers it, for <see cref="PartitionWarningKind.Covered"/>.</summary>
    public string? ByRuleId { get; }

    /// <inheritdoc />
    public override string ToString() => ByRuleId == null ? $"{Kind}@{RuleId}" : $"{Kind}@{RuleId}<{ByRuleId}";
}

/// <summary>
/// Finds contradictions and overlaps it can identify for certain. It does not promise to find every one, and a valid
/// configuration with warnings may still be applied.
/// </summary>
public static class PartitionLint
{
    /// <summary>Lists the warnings of valid rules; invalid rules are reported by validation instead.</summary>
    public static IReadOnlyList<PartitionWarning> Check(IEnumerable<PartitionRule> rules)
    {
        if (rules == null) throw new ArgumentNullException(nameof(rules));
        var valid = rules.Where(rule => rule != null && rule.Validate() == null).ToList();
        var warnings = new List<PartitionWarning>();
        foreach (var rule in valid)
        {
            if (NeverMatches(rule)) warnings.Add(new PartitionWarning(PartitionWarningKind.NeverMatches, rule.RuleId));
        }

        foreach (var narrow in valid)
        {
            if (NeverMatches(narrow)) continue;
            var broad = valid.FirstOrDefault(other => !ReferenceEquals(other, narrow) && Covers(other, narrow));
            if (broad != null) warnings.Add(new PartitionWarning(PartitionWarningKind.Covered, narrow.RuleId, broad.RuleId));
        }

        return warnings;
    }

    /// <summary>
    /// Gets whether <paramref name="broad"/> matches everything <paramref name="narrow"/> matches: its conditions are a
    /// strict subset, or an equal set on an earlier slot so only one of two identical rules is reported.
    /// </summary>
    private static bool Covers(PartitionRule broad, PartitionRule narrow)
    {
        var broadSet = new HashSet<PartitionCondition>(broad.Conditions);
        var narrowSet = new HashSet<PartitionCondition>(narrow.Conditions);
        if (!broadSet.IsSubsetOf(narrowSet)) return false;
        return broadSet.Count < narrowSet.Count || broad.Slot < narrow.Slot;
    }

    private static bool NeverMatches(PartitionRule rule)
    {
        foreach (var group in rule.Conditions.GroupBy(condition => condition.FieldId))
        {
            var field = PartitionFieldRegistry.Find(group.Key)!;
            var conditions = group.ToList();
            var contradicts = field.Kind switch
            {
                PartitionFieldKind.Number => EmptyInterval(conditions),
                PartitionFieldKind.Bool => conditions.Select(c => c.Operator).Distinct().Count() > 1,
                PartitionFieldKind.TypeSet => conditions.Any(c => c.Operator == PartitionOperator.Contains &&
                    conditions.Any(o => o.Operator == PartitionOperator.NotContains && Equals(o.Target, c.Target))),
                _ => ContradictoryEquality(conditions)
            };
            if (contradicts) return true;
        }

        return false;
    }

    private static bool ContradictoryEquality(IReadOnlyList<PartitionCondition> conditions)
    {
        var required = conditions.Where(c => c.Operator == PartitionOperator.Is).Select(c => c.Target).Distinct().ToList();
        if (required.Count > 1) return true;
        return required.Count == 1 && conditions.Any(c => c.Operator == PartitionOperator.IsNot && Equals(c.Target, required[0]));
    }

    private static bool EmptyInterval(IReadOnlyList<PartitionCondition> conditions)
    {
        long low = long.MinValue, high = long.MaxValue;
        var excluded = new HashSet<long>();
        foreach (var condition in conditions)
        {
            var value = condition.Target!.Number;
            switch (condition.Operator)
            {
                case PartitionOperator.Eq: low = Math.Max(low, value); high = Math.Min(high, value); break;
                case PartitionOperator.Ne: excluded.Add(value); break;
                case PartitionOperator.Gt:
                    if (value == long.MaxValue) return true;
                    low = Math.Max(low, value + 1);
                    break;
                case PartitionOperator.Ge: low = Math.Max(low, value); break;
                case PartitionOperator.Lt:
                    if (value == long.MinValue) return true;
                    high = Math.Min(high, value - 1);
                    break;
                case PartitionOperator.Le: high = Math.Min(high, value); break;
                case PartitionOperator.Range:
                    low = Math.Max(low, value);
                    high = Math.Min(high, condition.Upper!.Number);
                    break;
            }
        }

        if (low > high) return true;
        // Only a single remaining value can be fully excluded with certainty.
        return low == high && excluded.Contains(low);
    }
}
