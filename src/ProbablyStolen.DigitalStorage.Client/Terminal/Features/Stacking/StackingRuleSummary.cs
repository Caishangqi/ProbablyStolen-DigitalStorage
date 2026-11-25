using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;

/// <summary>Contextual notes shown below a row of one item's stacking rule.</summary>
public static class StackingRuleSummary
{
    /// <summary>A match row alone changes nothing.</summary>
    public const string MatchAloneKey = "stacking_note_match_alone";

    /// <summary>Ignoring an estimated value alone changes nothing while the raw price still differs.</summary>
    public const string DerivedAloneKey = "stacking_note_derived_alone";

    /// <summary>Container contents feed the estimated value, which still keeps items apart.</summary>
    public const string ValueStillComparedKey = "stacking_note_value_still_compared";

    /// <summary>Merged containers may hold different items; the tooltip shows the first only.</summary>
    public const string ContainerFirstOnlyKey = "stacking_note_container_first_only";

    /// <summary>Returns the notes shown under one row.</summary>
    public static IReadOnlyList<string> RowNotes(IReadOnlyList<StackingRule> rules, int row)
    {
        if (rules == null) throw new ArgumentNullException(nameof(rules));
        if (row < 0 || row >= rules.Count) throw new ArgumentOutOfRangeException(nameof(row));
        var rule = rules[row];
        var notes = new List<string>();
        var ignoresRaw = rules.Any(other => other.Mode == StackingMode.Ignore && IsRaw(other.FieldId));
        var valueIgnored = rules.Any(other =>
            other.FieldId == StackingFieldCatalog.AppraisedValue && other.Mode == StackingMode.Ignore);
        if (rule.Mode == StackingMode.Match && !ignoresRaw) notes.Add(MatchAloneKey);
        if (rule.Mode != StackingMode.Ignore) return notes;
        if (rule.FieldId == StackingFieldCatalog.AppraisedValue && !ignoresRaw) notes.Add(DerivedAloneKey);
        if (rule.FieldId == StackingFieldCatalog.ContainerContent && !valueIgnored) notes.Add(ValueStillComparedKey);
        if (rule.FieldId == StackingFieldCatalog.ContainerContent) notes.Add(ContainerFirstOnlyKey);
        return notes;
    }

    private static bool IsRaw(string fieldId) =>
        StackingFieldCatalog.Find(fieldId)?.Kind is StackingFieldKind.Raw or StackingFieldKind.Feature;
}
