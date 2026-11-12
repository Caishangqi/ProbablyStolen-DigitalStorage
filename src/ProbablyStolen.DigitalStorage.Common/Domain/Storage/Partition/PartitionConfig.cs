using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

/// <summary>One problem of a configuration, tied to a rule when it has one.</summary>
public readonly struct PartitionProblem
{
    /// <summary>Creates a problem.</summary>
    public PartitionProblem(PartitionIssue issue, string? ruleId)
    {
        Issue = issue;
        RuleId = ruleId;
    }

    /// <summary>Gets the issue.</summary>
    public PartitionIssue Issue { get; }

    /// <summary>Gets the affected rule identifier, or null when the issue affects the whole configuration.</summary>
    public string? RuleId { get; }

    /// <inheritdoc />
    public override string ToString() => RuleId == null ? Issue.ToString() : $"{Issue}@{RuleId}";
}

/// <summary>A cell's partition: a mode and rules. The mode lives on the housing and the rules on the assembled cell.</summary>
public sealed class PartitionConfig
{
    /// <summary>Creates a configuration.</summary>
    /// <param name="mode">The filter mode.</param>
    /// <param name="rules">The rules, in any order; slots order the icons.</param>
    /// <param name="revision">Incremented by every successful write, so concurrent edits are detected.</param>
    public PartitionConfig(PartitionMode mode, IEnumerable<PartitionRule> rules, int revision = 0)
    {
        var list = (rules ?? throw new ArgumentNullException(nameof(rules))).ToList();
        if (list.Any(rule => rule == null)) throw new ArgumentException("A rule cannot be null.", nameof(rules));
        Mode = mode;
        Rules = list.OrderBy(rule => rule.Slot).ToList().AsReadOnly();
        Revision = revision;
    }

    /// <summary>Gets the default configuration: a whitelist without rules, which admits everything.</summary>
    public static PartitionConfig Empty { get; } = new(PartitionMode.Whitelist, Array.Empty<PartitionRule>());

    /// <summary>Gets the filter mode.</summary>
    public PartitionMode Mode { get; }

    /// <summary>Gets the rules ordered by slot.</summary>
    public IReadOnlyList<PartitionRule> Rules { get; }

    /// <summary>Gets the revision.</summary>
    public int Revision { get; }

    /// <summary>Gets a copy with another mode, rules or revision.</summary>
    public PartitionConfig With(PartitionMode? mode = null, IEnumerable<PartitionRule>? rules = null, int? revision = null) =>
        new(mode ?? Mode, rules ?? Rules, revision ?? Revision);

    /// <summary>Lists every problem that prevents applying the configuration; empty means it may be applied.</summary>
    public IReadOnlyList<PartitionProblem> Validate()
    {
        var problems = new List<PartitionProblem>();
        if (!Enum.IsDefined(typeof(PartitionMode), Mode)) problems.Add(new PartitionProblem(PartitionIssue.UnknownMode, null));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var slots = new HashSet<int>();
        foreach (var rule in Rules)
        {
            if (!ids.Add(rule.RuleId)) problems.Add(new PartitionProblem(PartitionIssue.DuplicateRuleId, rule.RuleId));
            if (rule.Slot >= 0 && !slots.Add(rule.Slot)) problems.Add(new PartitionProblem(PartitionIssue.InvalidSlot, rule.RuleId));
            var issue = rule.Validate();
            if (issue != null) problems.Add(new PartitionProblem(issue.Value, rule.RuleId));
        }

        return problems;
    }
}
