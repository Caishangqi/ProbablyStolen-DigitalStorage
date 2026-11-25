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
        : this(itemId, enabled, rules, MaxRules)
    {
    }

    // The effective rule joins an item rule with the global rows, so it may hold more rows than either alone.
    private StackingItemRule(string itemId, bool enabled, IEnumerable<StackingRule> rules, int maxRules)
    {
        if (!IsValidItemId(itemId)) throw new ArgumentException("The item id is invalid.", nameof(itemId));
        if (rules == null) throw new ArgumentNullException(nameof(rules));
        var list = rules.ToList();
        if (list.Any(rule => rule == null)) throw new ArgumentException("A rule is missing.", nameof(rules));
        if (list.Count > maxRules) throw new ArgumentException("Too many rules.", nameof(rules));
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

    internal static StackingItemRule Joined(string itemId, IReadOnlyCollection<StackingRule> rules) =>
        new(itemId, true, rules, rules.Count);

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

/// <summary>One global rule row: a field, its mode, and whether it applies.</summary>
public sealed class StackingGlobalRule : IEquatable<StackingGlobalRule>
{
    /// <summary>Creates a global row.</summary>
    public StackingGlobalRule(string fieldId, StackingMode mode, bool enabled)
    {
        Rule = new StackingRule(fieldId, mode);
        Enabled = enabled;
    }

    /// <summary>Gets the field and mode.</summary>
    public StackingRule Rule { get; }

    /// <summary>Gets the field identifier.</summary>
    public string FieldId => Rule.FieldId;

    /// <summary>Gets the mode.</summary>
    public StackingMode Mode => Rule.Mode;

    /// <summary>Gets whether the row applies; a switched-off row is kept but ignored.</summary>
    public bool Enabled { get; }

    /// <summary>Returns a copy switched on or off.</summary>
    public StackingGlobalRule WithEnabled(bool enabled) => new(FieldId, Mode, enabled);

    /// <summary>Returns a copy with another mode.</summary>
    public StackingGlobalRule WithMode(StackingMode mode) => new(FieldId, mode, Enabled);

    /// <inheritdoc />
    public bool Equals(StackingGlobalRule? other) => other != null && Enabled == other.Enabled && Rule.Equals(other.Rule);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as StackingGlobalRule);

    /// <inheritdoc />
    public override int GetHashCode() => Rule.GetHashCode() * 2 + (Enabled ? 1 : 0);
}

/// <summary>
/// The stacking settings of one terminal: the marker tint, the advanced option, the global rows that apply to every
/// item, and at most one rule per item definition. An item rule overrides the global rows field by field. Immutable;
/// every edit returns a new configuration. Row and item order serve the page only.
/// </summary>
public sealed class StackingConfig : IEquatable<StackingConfig>
{
    /// <summary>The most item rules a configuration holds.</summary>
    public const int MaxItems = 1024;

    /// <summary>The most global rows a configuration holds.</summary>
    public const int MaxGlobals = StackingItemRule.MaxRules;

    private readonly Dictionary<string, StackingItemRule?> _effective = new(StringComparer.Ordinal);

    /// <summary>Creates a configuration; item identifiers and global fields must be unique.</summary>
    public StackingConfig(IEnumerable<StackingItemRule> items, StackingMarker? marker = null, bool advanced = true,
        IEnumerable<StackingGlobalRule>? globals = null)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        var list = items.ToList();
        if (list.Any(item => item == null)) throw new ArgumentException("An item rule is missing.", nameof(items));
        if (list.Count > MaxItems) throw new ArgumentException("Too many item rules.", nameof(items));
        if (list.Select(item => item.ItemId).Distinct(StringComparer.Ordinal).Count() != list.Count)
            throw new ArgumentException("An item appears in more than one rule.", nameof(items));
        var globalList = globals?.ToList() ?? new List<StackingGlobalRule>();
        if (globalList.Any(row => row == null)) throw new ArgumentException("A global row is missing.", nameof(globals));
        if (globalList.Count > MaxGlobals) throw new ArgumentException("Too many global rows.", nameof(globals));
        if (globalList.Select(row => row.FieldId).Distinct(StringComparer.Ordinal).Count() != globalList.Count)
            throw new ArgumentException("A field appears in more than one global row.", nameof(globals));
        Marker = marker ?? StackingMarker.Default;
        Advanced = advanced;
        Items = list.AsReadOnly();
        Globals = globalList.AsReadOnly();
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

    /// <summary>Gets the global rows in page order.</summary>
    public IReadOnlyList<StackingGlobalRule> Globals { get; }

    /// <summary>Gets whether any item rule or global row changes grouping.</summary>
    public bool HasActiveRule =>
        Items.Any(item => item.IsActive) || Globals.Any(row => row.Enabled && row.Mode == StackingMode.Ignore);

    /// <summary>Gets the rule of an item definition, or null.</summary>
    public StackingItemRule? Find(string? itemId) => itemId == null ? null : Items.FirstOrDefault(item => item.ItemId == itemId);

    /// <summary>Gets the global row of a field, or null.</summary>
    public StackingGlobalRule? FindGlobal(string? fieldId) =>
        fieldId == null ? null : Globals.FirstOrDefault(row => row.FieldId == fieldId);

    /// <summary>
    /// Gets the rule that groups an item: the item's own rows while its rule is on, then every switched-on global row
    /// whose field the item rule does not name. Null when no row applies. The result is cached per item.
    /// </summary>
    public StackingItemRule? EffectiveRule(string? itemId)
    {
        if (itemId == null) return null;
        lock (_effective)
        {
            if (_effective.TryGetValue(itemId, out var cached)) return cached;
            var item = Find(itemId);
            var own = item is { Enabled: true } ? item.Rules : Array.Empty<StackingRule>();
            var named = new HashSet<string>(own.Select(rule => rule.FieldId), StringComparer.Ordinal);
            var rules = own.Concat(Globals.Where(row => row.Enabled && !named.Contains(row.FieldId)).Select(row => row.Rule))
                .ToArray();
            var effective = rules.Length == 0 ? null : StackingItemRule.Joined(itemId, rules);
            _effective[itemId] = effective;
            return effective;
        }
    }

    /// <summary>
    /// Gets the switched-on global row that an item rule row overrides: one for the same field with the other mode.
    /// Null when no global row applies to the field or it has the same mode.
    /// </summary>
    public StackingGlobalRule? OverriddenGlobal(StackingRule row)
    {
        if (row == null) throw new ArgumentNullException(nameof(row));
        return FindGlobal(row.FieldId) is { Enabled: true } global && global.Mode != row.Mode ? global : null;
    }

    /// <summary>Gets whether a switched-on global row names the field with the same mode as the item rule row.</summary>
    public bool RepeatsGlobal(StackingRule row)
    {
        if (row == null) throw new ArgumentNullException(nameof(row));
        return FindGlobal(row.FieldId) is { Enabled: true } global && global.Mode == row.Mode;
    }

    /// <summary>Counts the switched-on item rules whose row for the field overrides the global row.</summary>
    public int CountOverrides(string fieldId) => Items.Count(item =>
        item.Enabled && item.Rules.Any(row => row.FieldId == fieldId && OverriddenGlobal(row) != null));

    /// <summary>
    /// Gets whether the field's effective row for the item comes from its own switched-on rule rather than the global
    /// rows.
    /// </summary>
    public bool IsItemField(string itemId, string fieldId) => Find(itemId) is { Enabled: true } item && item.IndexOf(fieldId) >= 0;

    /// <summary>Returns a copy with another marker tint.</summary>
    public StackingConfig WithMarker(StackingMarker marker) =>
        new(Items, marker ?? throw new ArgumentNullException(nameof(marker)), Advanced, Globals);

    /// <summary>Returns a copy with the advanced option switched.</summary>
    public StackingConfig WithAdvanced(bool advanced) => new(Items, Marker, advanced, Globals);

    /// <summary>Returns a copy with other global rows.</summary>
    public StackingConfig WithGlobals(IEnumerable<StackingGlobalRule> globals) => new(Items, Marker, Advanced, globals);

    /// <summary>Returns a copy where the field's global row is replaced in place, or appended when it has none.</summary>
    public StackingConfig WithGlobal(StackingGlobalRule row)
    {
        if (row == null) throw new ArgumentNullException(nameof(row));
        var replaced = false;
        var rows = Globals.Select(existing =>
        {
            if (existing.FieldId != row.FieldId) return existing;
            replaced = true;
            return row;
        }).ToList();
        if (!replaced) rows.Add(row);
        return WithGlobals(rows);
    }

    /// <summary>Returns a copy without the field's global row.</summary>
    public StackingConfig WithoutGlobal(string fieldId) => WithGlobals(Globals.Where(row => row.FieldId != fieldId));

    /// <summary>Returns a copy with every global row switched on or off.</summary>
    public StackingConfig WithAllGlobalsEnabled(bool enabled) => WithGlobals(Globals.Select(row => row.WithEnabled(enabled)));

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
        return new StackingConfig(items, Marker, Advanced, Globals);
    }

    /// <summary>Returns a copy without the item's rule.</summary>
    public StackingConfig WithoutItem(string itemId) =>
        new(Items.Where(item => item.ItemId != itemId), Marker, Advanced, Globals);

    /// <summary>Returns a copy without any item rule, keeping the marker, the advanced option and the global rows.</summary>
    public StackingConfig WithoutItems() => new(Array.Empty<StackingItemRule>(), Marker, Advanced, Globals);

    /// <summary>Returns a copy with every item rule switched on or off.</summary>
    public StackingConfig WithAllEnabled(bool enabled) =>
        new(Items.Select(item => item.WithEnabled(enabled)), Marker, Advanced, Globals);

    /// <inheritdoc />
    public bool Equals(StackingConfig? other) =>
        other != null && Advanced == other.Advanced && Marker.Equals(other.Marker) && Items.SequenceEqual(other.Items) &&
        Globals.SequenceEqual(other.Globals);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as StackingConfig);

    /// <inheritdoc />
    public override int GetHashCode() =>
        Globals.Aggregate(Items.Aggregate((Advanced ? 1 : 0) ^ Marker.Rgb << 1, (hash, item) => hash * 31 + item.GetHashCode()),
            (hash, row) => hash * 17 + row.GetHashCode());
}
