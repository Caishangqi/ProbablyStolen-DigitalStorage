using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;

/// <summary>What the page says about the current rules.</summary>
public static class StackingRuleSummary
{
    /// <summary>Shown when merged display is off.</summary>
    public const string DisabledKey = "stacking_summary_disabled";

    /// <summary>Shown when no rule ignores anything, so every entry stays separate.</summary>
    public const string ExactKey = "stacking_summary_exact";

    /// <summary>Lists the ignored fields; the rest is still compared.</summary>
    public const string IgnoredKey = "stacking_summary_ignored";

    /// <summary>A match row alone changes nothing.</summary>
    public const string MatchAloneKey = "stacking_note_match_alone";

    /// <summary>Ignoring an estimated value alone changes nothing while the raw price still differs.</summary>
    public const string DerivedAloneKey = "stacking_note_derived_alone";

    /// <summary>Content and purity feed the estimated value, which still keeps items apart.</summary>
    public const string ValueStillComparedKey = "stacking_note_value_still_compared";

    /// <summary>Merged containers may hold different items; the tooltip shows the first only.</summary>
    public const string ContainerFirstOnlyKey = "stacking_note_container_first_only";

    /// <summary>Returns the summary key and the ignored field ids it names.</summary>
    public static (string Key, IReadOnlyList<string> IgnoredFields) Describe(StackingConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (!config.Enabled) return (DisabledKey, Array.Empty<string>());
        var ignored = config.Rules.Where(rule => rule.Mode == StackingMode.Ignore).Select(rule => rule.FieldId).ToArray();
        return ignored.Length == 0 ? (ExactKey, Array.Empty<string>()) : (IgnoredKey, ignored);
    }

    /// <summary>Returns the notes shown under one row.</summary>
    public static IReadOnlyList<string> RowNotes(StackingConfig config, int row)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (row < 0 || row >= config.Rules.Count) throw new ArgumentOutOfRangeException(nameof(row));
        var rule = config.Rules[row];
        var notes = new List<string>();
        var ignoresRaw = config.Rules.Any(other => other.Mode == StackingMode.Ignore && IsRaw(other.FieldId));
        var valueIgnored = config.ModeOf(StackingFieldCatalog.AppraisedValue) == StackingMode.Ignore;
        if (rule.Mode == StackingMode.Match && !ignoresRaw) notes.Add(MatchAloneKey);
        if (rule.Mode != StackingMode.Ignore) return notes;
        if (rule.FieldId == StackingFieldCatalog.AppraisedValue && !ignoresRaw) notes.Add(DerivedAloneKey);
        if ((rule.FieldId == StackingFieldCatalog.ContainerContent || rule.FieldId == StackingFieldCatalog.MetalPurity) &&
            !valueIgnored) notes.Add(ValueStillComparedKey);
        if (rule.FieldId == StackingFieldCatalog.ContainerContent) notes.Add(ContainerFirstOnlyKey);
        return notes;
    }

    private static bool IsRaw(string fieldId) => StackingFieldCatalog.Find(fieldId)?.Kind is StackingFieldKind.Raw or StackingFieldKind.Feature;
}
