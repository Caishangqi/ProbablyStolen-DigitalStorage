using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;

/// <summary>
/// The state behind the stacking rules page. Every edit builds a complete candidate and saves it at once; a refused save
/// keeps the previous settings and reports why. Unreadable settings disable every edit, so the page never overwrites
/// them.
/// </summary>
public sealed class StackingRulesEditor
{
    /// <summary>The message shown when a save is refused.</summary>
    public const string SaveFailedKey = "stacking_save_failed";

    /// <summary>The message key shown when a marker color is not #RGB or #RRGGBB.</summary>
    public const string MarkerInvalidKey = "stacking_marker_invalid";

    private readonly StackingConfigStore _store;
    private readonly Action _saved;

    /// <summary>Creates an editor over a terminal's settings.</summary>
    /// <param name="store">The terminal's settings.</param>
    /// <param name="saved">Runs after every successful save, for example to regroup the storage view.</param>
    public StackingRulesEditor(StackingConfigStore store, Action saved)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _saved = saved ?? throw new ArgumentNullException(nameof(saved));
    }

    /// <summary>Gets whether the saved settings can be read; edits are refused otherwise.</summary>
    public bool IsEditable => _store.Read().Status != StackingConfigStatus.Unreadable;

    /// <summary>Gets whether the saved settings come from a retired version whose rules no longer apply.</summary>
    public bool WasReset => _store.Read().Status == StackingConfigStatus.Reset;

    /// <summary>Gets the settings shown on the page; the default while unreadable.</summary>
    public StackingConfig Config => _store.Effective() ?? StackingConfig.Default;

    /// <summary>Gets the message of the last refused save, cleared by the next successful one.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Finds the item rules whose display name, item identifier or configured tag key matches the query, in page
    /// order. An empty query matches every rule.
    /// </summary>
    public static IReadOnlyList<StackingItemRule> FindItems(StackingConfig config, string query, Func<string, string> itemName,
        Func<string, string, bool> matches)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (itemName == null) throw new ArgumentNullException(nameof(itemName));
        if (matches == null) throw new ArgumentNullException(nameof(matches));
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0) return config.Items;
        return config.Items.Where(item => matches(itemName(item.ItemId), query) || matches(item.ItemId, query) ||
            item.Rules.Any(rule => StackingFieldCatalog.Find(rule.FieldId)?.TagKey is { } tag && matches(tag, query)))
            .ToArray();
    }

    /// <summary>Finds rows by their visible name or identifier, including a just located row.</summary>
    public static IReadOnlyList<int> FindRows(IReadOnlyList<StackingRule> rules, string query, Func<string, string> name,
        Func<string, string, bool> matches, string? revealedFieldId = null, Func<string, bool>? showsIdentifier = null)
    {
        if (rules == null) throw new ArgumentNullException(nameof(rules));
        query = (query ?? string.Empty).Trim();
        return Enumerable.Range(0, rules.Count).Where(row =>
        {
            var id = rules[row].FieldId;
            return query.Length == 0 || id == revealedFieldId || matches(name(id), query) ||
                (showsIdentifier == null || showsIdentifier(id)) && matches(id, query);
        }).ToArray();
    }

    /// <summary>Lists the distinct item identifiers of stored items, in stored order, for the add picker.</summary>
    public static IReadOnlyList<string> StoredItems(IEnumerable<ItemKey> storedKeys)
    {
        if (storedKeys == null) throw new ArgumentNullException(nameof(storedKeys));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return storedKeys.Where(key => key != null && StackingItemRule.IsValidItemId(key.Identifier) && seen.Add(key.Identifier))
            .Select(key => key.Identifier).ToArray();
    }

    /// <summary>Switches one item rule on or off, keeping its rows.</summary>
    public bool SetItemEnabled(string itemId, bool enabled)
    {
        var item = Config.Find(itemId);
        return item != null && Commit(Config.WithItem(item.WithEnabled(enabled)));
    }

    /// <summary>Removes one item rule; the item goes back to exact display.</summary>
    public bool RemoveItem(string itemId) => Config.Find(itemId) != null && Commit(Config.WithoutItem(itemId));

    /// <summary>Switches every item rule on or off.</summary>
    public bool SetAllEnabled(bool enabled) => Commit(Config.WithAllEnabled(enabled));

    /// <summary>Removes every item rule, keeping the marker color and the advanced option.</summary>
    public bool RemoveAll() => Commit(Config.WithoutItems());

    /// <summary>Switches the advanced option, which lets rules ignore unexposed and disabled features.</summary>
    public bool SetAdvanced(bool advanced) => advanced == Config.Advanced || Commit(Config.WithAdvanced(advanced));

    /// <summary>Saves an edited item rule, replacing the item's previous rule or adding it.</summary>
    public bool Apply(StackingItemRule rule)
    {
        if (rule == null) throw new ArgumentNullException(nameof(rule));
        StackingConfig candidate;
        try
        {
            candidate = Config.WithItem(rule);
        }
        catch (ArgumentException)
        {
            LastError = SaveFailedKey;
            return false;
        }

        return Commit(candidate);
    }

    /// <summary>Sets the marker color from #RGB or #RRGGBB; other text keeps the current color.</summary>
    public bool SetMarkerColor(string? text)
    {
        if (!StackingMarker.TryParse(text, out var marker))
        {
            LastError = MarkerInvalidKey;
            return false;
        }

        if (!marker.Equals(Config.Marker)) return Commit(Config.WithMarker(marker));
        LastError = null;
        return true;
    }

    private bool Commit(StackingConfig candidate)
    {
        if (!IsEditable || !_store.TryCommit(candidate))
        {
            LastError = SaveFailedKey;
            return false;
        }

        LastError = null;
        _saved();
        return true;
    }
}

/// <summary>
/// An unsaved copy of one item's rule on the editor page. Edits change only the draft; <see cref="ToRule"/> gives the
/// rule to apply, and leaving the page drops the draft.
/// </summary>
public sealed class StackingItemDraft
{
    private readonly List<StackingRule> _rules;

    /// <summary>Starts a draft from the item's saved rule, or an empty switched-on rule when it has none.</summary>
    public StackingItemDraft(StackingConfig config, string itemId)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (!StackingItemRule.IsValidItemId(itemId)) throw new ArgumentException("The item id is invalid.", nameof(itemId));
        var saved = config.Find(itemId);
        ItemId = itemId;
        Enabled = saved?.Enabled ?? true;
        IsNew = saved == null;
        _rules = saved?.Rules.ToList() ?? new List<StackingRule>();
    }

    /// <summary>Gets the item definition identifier.</summary>
    public string ItemId { get; }

    /// <summary>Gets whether the rule is switched on; the draft keeps the saved switch.</summary>
    public bool Enabled { get; }

    /// <summary>Gets whether the item had no saved rule when the draft started.</summary>
    public bool IsNew { get; }

    /// <summary>Gets the drafted rows.</summary>
    public IReadOnlyList<StackingRule> Rules => _rules;

    /// <summary>
    /// Lists the attribute menu from the item's stored identities and the drafted rows; unexposed and disabled features
    /// are listed only with <paramref name="includeHidden"/>, the advanced option.
    /// </summary>
    public IReadOnlyList<StackingFieldOption> MenuOptions(IEnumerable<ItemKey> storedKeys, bool includeHidden)
    {
        if (storedKeys == null) throw new ArgumentNullException(nameof(storedKeys));
        return StackingFieldCatalog.Discover(storedKeys.Where(key => key?.Identifier == ItemId),
            _rules.Select(rule => rule.FieldId), includeHidden);
    }

    /// <summary>Adds an "ignore" row for the field, or finds the row it already has. Returns the row index, or -1.</summary>
    public int AddOrLocate(string fieldId)
    {
        var existing = IndexOf(fieldId);
        if (existing >= 0) return existing;
        if (!StackingRule.IsValidFieldId(fieldId) || _rules.Count >= StackingItemRule.MaxRules) return -1;
        _rules.Add(new StackingRule(fieldId, StackingMode.Ignore));
        return _rules.Count - 1;
    }

    /// <summary>Changes a row's field, keeping its mode; when another row has the field, returns that row instead.</summary>
    public int ReplaceField(int row, string fieldId)
    {
        RuleAt(row);
        var existing = IndexOf(fieldId);
        if (existing >= 0) return existing;
        if (!StackingRule.IsValidFieldId(fieldId)) return -1;
        _rules[row] = new StackingRule(fieldId, _rules[row].Mode);
        return row;
    }

    /// <summary>Changes a row's mode.</summary>
    public void SetMode(int row, StackingMode mode) => _rules[row] = new StackingRule(RuleAt(row).FieldId, mode);

    /// <summary>Removes a row.</summary>
    public void Remove(int row)
    {
        RuleAt(row);
        _rules.RemoveAt(row);
    }

    /// <summary>Removes every row.</summary>
    public void Clear() => _rules.Clear();

    /// <summary>Gets the rule to apply.</summary>
    public StackingItemRule ToRule() => new(ItemId, Enabled, _rules);

    private int IndexOf(string fieldId) => _rules.FindIndex(rule => rule.FieldId == fieldId);

    private StackingRule RuleAt(int row) =>
        row >= 0 && row < _rules.Count ? _rules[row] : throw new ArgumentOutOfRangeException(nameof(row));
}
