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
/// The stacking rule of one item definition: whether it groups, and its field rows. Immutable; every edit returns a new
/// rule. Row order serves the page only and never changes grouping.
/// </summary>
public sealed class StackingItemRule : IEquatable<StackingItemRule>
{
    /// <summary>The most rows one item rule holds.</summary>
    public const int MaxRules = 256;

    /// <summary>Creates an item rule; field identifiers must be unique.</summary>
    public StackingItemRule(string itemId, bool enabled, IEnumerable<StackingRule> rules)
    {
        if (!IsValidItemId(itemId)) throw new ArgumentException("The item id is invalid.", nameof(itemId));
        if (rules == null) throw new ArgumentNullException(nameof(rules));
        var list = rules.ToList();
        if (list.Any(rule => rule == null)) throw new ArgumentException("A rule is missing.", nameof(rules));
        if (list.Count > MaxRules) throw new ArgumentException("Too many rules.", nameof(rules));
        if (list.Select(rule => rule.FieldId).Distinct(StringComparer.Ordinal).Count() != list.Count)
            throw new ArgumentException("A field appears in more than one rule.", nameof(rules));
        ItemId = itemId;
        Enabled = enabled;
        Rules = list.AsReadOnly();
    }

    /// <summary>Gets the item definition identifier.</summary>
    public string ItemId { get; }

    /// <summary>Gets whether the rule groups; a switched-off rule keeps its rows.</summary>
    public bool Enabled { get; }

    /// <summary>Gets the rows in page order.</summary>
    public IReadOnlyList<StackingRule> Rules { get; }

    /// <summary>Gets whether any row ignores a field; without one, grouping equals exact display.</summary>
    public bool HasIgnoreRule => Rules.Any(rule => rule.Mode == StackingMode.Ignore);

    /// <summary>Gets whether the rule changes grouping at all.</summary>
    public bool IsActive => Enabled && HasIgnoreRule;

    /// <summary>Whether a text can be an item identifier; the same limits as a field identifier.</summary>
    public static bool IsValidItemId(string? itemId) => StackingRule.IsValidFieldId(itemId);

    /// <summary>Gets the mode of a field, or null when it has no row.</summary>
    public StackingMode? ModeOf(string fieldId) => Rules.FirstOrDefault(rule => rule.FieldId == fieldId)?.Mode;

    /// <summary>Returns the index of a field's row, or -1.</summary>
    public int IndexOf(string fieldId)
    {
        for (var i = 0; i < Rules.Count; i++)
            if (Rules[i].FieldId == fieldId) return i;
        return -1;
    }

    /// <summary>Returns a copy switched on or off.</summary>
    public StackingItemRule WithEnabled(bool enabled) => new(ItemId, enabled, Rules);

    /// <summary>Returns a copy with other rows.</summary>
    public StackingItemRule WithRules(IEnumerable<StackingRule> rules) => new(ItemId, Enabled, rules);

    /// <summary>
    /// Returns a copy with an ignore row appended for every field that has none yet; rows that exist keep their mode.
    /// </summary>
    public StackingItemRule WithIgnoredFields(IEnumerable<string> fieldIds)
    {
        if (fieldIds == null) throw new ArgumentNullException(nameof(fieldIds));
        var rules = Rules.ToList();
        foreach (var fieldId in fieldIds)
        {
            if (rules.All(rule => rule.FieldId != fieldId)) rules.Add(new StackingRule(fieldId, StackingMode.Ignore));
        }

        return new StackingItemRule(ItemId, Enabled, rules);
    }

    /// <inheritdoc />
    public bool Equals(StackingItemRule? other) =>
        other != null && ItemId == other.ItemId && Enabled == other.Enabled && Rules.SequenceEqual(other.Rules);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as StackingItemRule);

    /// <inheritdoc />
    public override int GetHashCode() => Rules.Aggregate(
        StringComparer.Ordinal.GetHashCode(ItemId) * 2 + (Enabled ? 1 : 0), (hash, rule) => hash * 31 + rule.GetHashCode());
}

/// <summary>
/// The stacking settings of one terminal: the marker tint, the advanced option and at most one rule per item
/// definition. Immutable; every edit returns a new configuration. Item order serves the page only.
/// </summary>
public sealed class StackingConfig : IEquatable<StackingConfig>
{
    /// <summary>The most item rules a configuration holds.</summary>
    public const int MaxItems = 1024;

    /// <summary>Creates a configuration; item identifiers must be unique.</summary>
    public StackingConfig(IEnumerable<StackingItemRule> items, StackingMarker? marker = null, bool advanced = true)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        var list = items.ToList();
        if (list.Any(item => item == null)) throw new ArgumentException("An item rule is missing.", nameof(items));
        if (list.Count > MaxItems) throw new ArgumentException("Too many item rules.", nameof(items));
        if (list.Select(item => item.ItemId).Distinct(StringComparer.Ordinal).Count() != list.Count)
            throw new ArgumentException("An item appears in more than one rule.", nameof(items));
        Marker = marker ?? StackingMarker.Default;
        Advanced = advanced;
        Items = list.AsReadOnly();
    }

    /// <summary>Gets the configuration of a terminal that has none saved: no item rules, advanced option on.</summary>
    public static StackingConfig Default { get; } = new(Array.Empty<StackingItemRule>());

    /// <summary>Gets the tint of the mixed group mark.</summary>
    public StackingMarker Marker { get; }

    /// <summary>
    /// Gets whether the advanced option is on: unexposed and disabled features can then be listed by identifier and
    /// ignored. Saved with the terminal; on by default. While off, saved rows for such features stay but do not apply.
    /// </summary>
    public bool Advanced { get; }

    /// <summary>Gets the item rules in page order.</summary>
    public IReadOnlyList<StackingItemRule> Items { get; }

    /// <summary>Gets whether any item rule changes grouping.</summary>
    public bool HasActiveRule => Items.Any(item => item.IsActive);

    /// <summary>Gets the rule of an item definition, or null.</summary>
    public StackingItemRule? Find(string? itemId) => itemId == null ? null : Items.FirstOrDefault(item => item.ItemId == itemId);

    /// <summary>Returns a copy with another marker tint.</summary>
    public StackingConfig WithMarker(StackingMarker marker) =>
        new(Items, marker ?? throw new ArgumentNullException(nameof(marker)), Advanced);

    /// <summary>Returns a copy with the advanced option switched.</summary>
    public StackingConfig WithAdvanced(bool advanced) => new(Items, Marker, advanced);

    /// <summary>Returns a copy where the item's rule is replaced in place, or appended when it has none.</summary>
    public StackingConfig WithItem(StackingItemRule rule)
    {
        if (rule == null) throw new ArgumentNullException(nameof(rule));
        var replaced = false;
        var items = Items.Select(item =>
        {
            if (item.ItemId != rule.ItemId) return item;
            replaced = true;
            return rule;
        }).ToList();
        if (!replaced) items.Add(rule);
        return new StackingConfig(items, Marker, Advanced);
    }

    /// <summary>Returns a copy without the item's rule.</summary>
    public StackingConfig WithoutItem(string itemId) =>
        new(Items.Where(item => item.ItemId != itemId), Marker, Advanced);

    /// <summary>Returns a copy without any item rule, keeping the marker and the advanced option.</summary>
    public StackingConfig WithoutItems() => new(Array.Empty<StackingItemRule>(), Marker, Advanced);

    /// <summary>Returns a copy with every item rule switched on or off.</summary>
    public StackingConfig WithAllEnabled(bool enabled) =>
        new(Items.Select(item => item.WithEnabled(enabled)), Marker, Advanced);

    /// <inheritdoc />
    public bool Equals(StackingConfig? other) =>
        other != null && Advanced == other.Advanced && Marker.Equals(other.Marker) && Items.SequenceEqual(other.Items);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as StackingConfig);

    /// <inheritdoc />
    public override int GetHashCode() =>
        Items.Aggregate((Advanced ? 1 : 0) ^ Marker.Rgb << 1, (hash, item) => hash * 31 + item.GetHashCode());
}
