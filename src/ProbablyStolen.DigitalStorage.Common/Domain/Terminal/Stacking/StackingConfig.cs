using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

/// <summary>How one rule compares its field.</summary>
public enum StackingMode
{
    /// <summary>The field's value must be equal; the same as having no rule unless another rule ignores raw data.</summary>
    Match,

    /// <summary>Differences in the field do not keep items apart.</summary>
    Ignore
}

/// <summary>One rule row: a field and its mode.</summary>
public sealed class StackingRule : IEquatable<StackingRule>
{
    /// <summary>The longest field identifier a rule accepts.</summary>
    public const int MaxFieldIdLength = 200;

    /// <summary>Creates a rule.</summary>
    public StackingRule(string fieldId, StackingMode mode)
    {
        if (!IsValidFieldId(fieldId)) throw new ArgumentException("The field id is invalid.", nameof(fieldId));
        if (mode != StackingMode.Match && mode != StackingMode.Ignore) throw new ArgumentOutOfRangeException(nameof(mode));
        FieldId = fieldId;
        Mode = mode;
    }

    /// <summary>Gets the field identifier.</summary>
    public string FieldId { get; }

    /// <summary>Gets the mode.</summary>
    public StackingMode Mode { get; }

    /// <summary>Whether a text can be a field identifier: not empty, not too long, no control characters.</summary>
    public static bool IsValidFieldId(string? fieldId) =>
        !string.IsNullOrEmpty(fieldId) && fieldId!.Length <= MaxFieldIdLength && !fieldId.Any(char.IsControl);

    /// <inheritdoc />
    public bool Equals(StackingRule? other) =>
        other != null && FieldId == other.FieldId && Mode == other.Mode;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as StackingRule);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(FieldId) * 397 ^ (int)Mode;
}

/// <summary>
/// The stacking settings of one terminal: whether merged display is on, the marker tint and the rule rows. Immutable; every edit
/// returns a new configuration. Row order serves the page only and never changes grouping.
/// </summary>
public sealed class StackingConfig : IEquatable<StackingConfig>
{
    /// <summary>The most rows a configuration holds.</summary>
    public const int MaxRules = 256;

    /// <summary>Creates a configuration; field identifiers must be unique.</summary>
    public StackingConfig(bool enabled, IEnumerable<StackingRule> rules, StackingMarker? marker = null)
    {
        if (rules == null) throw new ArgumentNullException(nameof(rules));
        var list = rules.ToList();
        if (list.Any(rule => rule == null)) throw new ArgumentException("A rule is missing.", nameof(rules));
        if (list.Count > MaxRules) throw new ArgumentException("Too many rules.", nameof(rules));
        if (list.Select(rule => rule.FieldId).Distinct(StringComparer.Ordinal).Count() != list.Count)
            throw new ArgumentException("A field appears in more than one rule.", nameof(rules));
        Enabled = enabled;
        Marker = marker ?? StackingMarker.Default;
        Rules = list.AsReadOnly();
    }

    /// <summary>Gets the configuration of a terminal that has none saved: on, with no rules.</summary>
    public static StackingConfig Default { get; } = new(true, Array.Empty<StackingRule>());

    /// <summary>Gets whether merged display is on.</summary>
    public bool Enabled { get; }

    /// <summary>Gets the tint of the mixed group mark.</summary>
    public StackingMarker Marker { get; }

    /// <summary>Gets the rule rows in page order.</summary>
    public IReadOnlyList<StackingRule> Rules { get; }

    /// <summary>Gets whether any rule ignores a field; without one, grouping equals exact display.</summary>
    public bool HasIgnoreRule => Rules.Any(rule => rule.Mode == StackingMode.Ignore);

    /// <summary>Gets the mode of a field, or null when it has no row.</summary>
    public StackingMode? ModeOf(string fieldId) => Rules.FirstOrDefault(rule => rule.FieldId == fieldId)?.Mode;

    /// <summary>Returns the index of a field's row, or -1.</summary>
    public int IndexOf(string fieldId)
    {
        for (var i = 0; i < Rules.Count; i++)
            if (Rules[i].FieldId == fieldId) return i;
        return -1;
    }

    /// <summary>Returns a copy with merged display switched.</summary>
    public StackingConfig WithEnabled(bool enabled) => new(enabled, Rules, Marker);

    /// <summary>Returns a copy with another marker tint.</summary>
    public StackingConfig WithMarker(StackingMarker marker) =>
        new(Enabled, Rules, marker ?? throw new ArgumentNullException(nameof(marker)));

    /// <summary>Returns a copy with a row for the field appended, or unchanged when it already has one.</summary>
    public StackingConfig WithAddedRule(string fieldId, StackingMode mode) =>
        IndexOf(fieldId) >= 0 ? this : new StackingConfig(Enabled, Rules.Concat(new[] { new StackingRule(fieldId, mode) }), Marker);

    /// <summary>Returns a copy with one row's mode changed.</summary>
    public StackingConfig WithMode(int index, StackingMode mode) =>
        Replace(index, new StackingRule(RuleAt(index).FieldId, mode));

    /// <summary>Returns a copy with one row's field replaced, keeping its mode. The new field must have no row.</summary>
    public StackingConfig WithField(int index, string fieldId)
    {
        if (IndexOf(fieldId) >= 0) throw new ArgumentException("The field already has a row.", nameof(fieldId));
        return Replace(index, new StackingRule(fieldId, RuleAt(index).Mode));
    }

    /// <summary>Returns a copy without one row.</summary>
    public StackingConfig WithoutRule(int index)
    {
        RuleAt(index);
        return new StackingConfig(Enabled, Rules.Where((_, i) => i != index), Marker);
    }

    /// <summary>Returns a copy without rows, keeping the switch.</summary>
    public StackingConfig WithoutRules() => new(Enabled, Array.Empty<StackingRule>(), Marker);

    /// <inheritdoc />
    public bool Equals(StackingConfig? other) =>
        other != null && Enabled == other.Enabled && Marker.Equals(other.Marker) && Rules.SequenceEqual(other.Rules);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as StackingConfig);

    /// <inheritdoc />
    public override int GetHashCode() => Rules.Aggregate((Enabled ? 1 : 0) ^ Marker.Rgb << 1, (hash, rule) => hash * 31 + rule.GetHashCode());

    private StackingRule RuleAt(int index) =>
        index >= 0 && index < Rules.Count ? Rules[index] : throw new ArgumentOutOfRangeException(nameof(index));

    private StackingConfig Replace(int index, StackingRule rule)
    {
        RuleAt(index);
        return new StackingConfig(Enabled, Rules.Select((existing, i) => i == index ? rule : existing), Marker);
    }
}
