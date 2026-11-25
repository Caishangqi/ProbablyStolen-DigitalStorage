using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;

/// <summary>Which quick stacking actions the kept selection allows.</summary>
public enum StackingQuickKind
{
    /// <summary>No quick action applies.</summary>
    None,

    /// <summary>Two or more slots of one item that has no rule yet.</summary>
    AddNew,

    /// <summary>Two or more slots of one item, merged or not, that already has a rule, switched on or off.</summary>
    AddToExisting,

    /// <summary>One merged slot; its item rule can be edited, and the rows merging it deleted.</summary>
    EditMixed
}

/// <summary>The outcome of a quick add.</summary>
public enum StackingQuickResult
{
    /// <summary>The rule was saved and every selected slot now shares one group.</summary>
    Merged,

    /// <summary>The rule was saved, but differences no rule can cover keep the slots apart.</summary>
    StillSeparate,

    /// <summary>The differences were added to a rule that is switched off, so nothing merges yet.</summary>
    RuleOff,

    /// <summary>The differences were added to the global rows, but the item's rule matches some of them.</summary>
    OverriddenByItem,

    /// <summary>No difference between the slots can be configured; nothing was saved.</summary>
    NothingConfigurable,

    /// <summary>
    /// Only unexposed or disabled features differ, which the advanced option, now off, would allow ignoring; nothing was
    /// saved.
    /// </summary>
    HiddenFeature,

    /// <summary>The save was refused; the previous settings stay in effect.</summary>
    SaveFailed,

    /// <summary>The selection no longer allows the action; nothing was saved.</summary>
    Unavailable
}

/// <summary>The classified selection: the action kind, the item and the selected slots.</summary>
public sealed class StackingQuickSelection
{
    internal static readonly StackingQuickSelection None =
        new(StackingQuickKind.None, null, Array.Empty<DisplayEntry>(), false);

    internal StackingQuickSelection(StackingQuickKind kind, string? itemId, IReadOnlyList<DisplayEntry> groups,
        bool hasItemRule)
    {
        Kind = kind;
        ItemId = itemId;
        Groups = groups;
        HasItemRule = hasItemRule;
    }

    /// <summary>Gets whether the item has a rule of its own, switched on or off.</summary>
    public bool HasItemRule { get; }

    /// <summary>Gets the action kind.</summary>
    public StackingQuickKind Kind { get; }

    /// <summary>Gets the item definition the action targets, or null when none applies.</summary>
    public string? ItemId { get; }

    /// <summary>Gets the selected slots.</summary>
    public IReadOnlyList<DisplayEntry> Groups { get; }
}

/// <summary>
/// The rule actions offered for the storage view's kept selection: adding the differences of two separate slots of one
/// item to its rule, and editing or deleting the rule behind one merged slot. Every action checks the selection and
/// settings again when it runs.
/// </summary>
public static class StackingQuickActions
{
    /// <summary>The message after a quick add merged the two slots.</summary>
    public const string MergedKey = "stacking_quick_merged";

    /// <summary>The message after a quick add saved a rule that still leaves the slots apart.</summary>
    public const string StillSeparateKey = "stacking_quick_still_separate";

    /// <summary>The message after a quick add extended a switched-off rule.</summary>
    public const string RuleOffKey = "stacking_quick_rule_off";

    /// <summary>The message when only hidden features differ while the advanced option is off.</summary>
    public const string HiddenFeatureKey = "stacking_quick_hidden";

    /// <summary>The message when no difference can be configured.</summary>
    public const string NothingConfigurableKey = "stacking_quick_nothing";

    /// <summary>The message after the rows merging a slot were deleted.</summary>
    public const string DeletedKey = "stacking_quick_deleted";

    /// <summary>The message when no row of the item's rule can be told apart as merging the slot.</summary>
    public const string NothingToDeleteKey = "stacking_quick_delete_none";

    /// <summary>The message after a global add whose fields the item's rule still matches.</summary>
    public const string OverriddenByItemKey = "stacking_quick_global_overridden";

    /// <summary>The message after the global rows merging a slot were deleted.</summary>
    public const string GlobalDeletedKey = "stacking_quick_global_deleted";

    /// <summary>The message when no global row can be told apart as merging the slot.</summary>
    public const string NothingGlobalToDeleteKey = "stacking_quick_global_delete_none";

    // Ignoring a raw field can make the derived estimate part of the next comparison.
    private const int MaxRounds = 8;

    /// <summary>Finds the rule action available for the current selection.</summary>
    public static StackingQuickSelection Classify(TerminalContext context, StackingConfig? config)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (config == null || !StackingModule.IsInstalled(context)) return StackingQuickSelection.None;
        var selection = context.Selection;
        if (selection.Count >= 2)
        {
            var itemId = selection[0].Representative.Key.Identifier;
            // Members share the group's comparison key, so one representative is enough per group.
            if (selection.Any(group => group.Representative.Key.Identifier != itemId) ||
                selection.Select(group => group.Id).Distinct(StringComparer.Ordinal).Count() != selection.Count)
                return StackingQuickSelection.None;
            var hasRule = config.Find(itemId) != null;
            return new StackingQuickSelection(hasRule ? StackingQuickKind.AddToExisting : StackingQuickKind.AddNew, itemId,
                selection, hasRule);
        }

        // Global rows alone can merge a slot, so a merged slot offers editing even before its item has a rule.
        if (selection.Count == 1 && selection[0].IsMixed)
        {
            var itemId = selection[0].Representative.Key.Identifier;
            return new StackingQuickSelection(StackingQuickKind.EditMixed, itemId, selection, config.Find(itemId) != null);
        }

        return StackingQuickSelection.None;
    }

    /// <summary>Gets whether a merged slot can have global rows to delete: some switched-on global row ignores a field.</summary>
    public static bool HasGlobalIgnore(StackingConfig? config) =>
        config != null && config.Globals.Any(row => row.Enabled && row.Mode == StackingMode.Ignore);

    /// <summary>
    /// Lists the configurable differences between the selected slots that no global row names yet: what a global add
    /// would append. Fields that already have a global row keep it as it is, switched off or matching.
    /// </summary>
    public static IReadOnlyList<string> GlobalAdditions(StackingQuickSelection selection, StackingConfig config,
        EntryAttributes attributes)
    {
        if (selection == null) throw new ArgumentNullException(nameof(selection));
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (attributes == null) throw new ArgumentNullException(nameof(attributes));
        if (selection.Kind is not (StackingQuickKind.AddNew or StackingQuickKind.AddToExisting)) return Array.Empty<string>();
        var members = Members(selection, attributes);
        var added = new List<string>();
        var globals = config.WithoutItems();
        // Recompare after each change because the new rows can make additional fields relevant.
        for (var round = 0; round < MaxRounds; round++)
        {
            var trial = globals.WithGlobals(globals.Globals.Concat(added.Select(Ignored)));
            var fresh = StackingProjection.CollectDifferences(members, trial)
                .Where(id => StackingFieldCatalog.Find(id) != null && config.FindGlobal(id) == null && !added.Contains(id))
                .ToArray();
            if (fresh.Length == 0) break;
            added.AddRange(fresh);
        }

        return added;
    }

    /// <summary>
    /// Appends every configurable difference between the selected slots that no global row names as a switched-on
    /// "ignore" global row and saves it. Global rows that already exist keep their mode and switch.
    /// </summary>
    public static StackingQuickResult AddGlobalFromSelection(TerminalContext context, StackingConfigStore store,
        EntryAttributes attributes)
    {
        if (store == null) throw new ArgumentNullException(nameof(store));
        if (attributes == null) throw new ArgumentNullException(nameof(attributes));
        var config = store.Effective();
        var selection = Classify(context, config);
        if (config == null || selection.Kind is not (StackingQuickKind.AddNew or StackingQuickKind.AddToExisting))
            return StackingQuickResult.Unavailable;

        var added = GlobalAdditions(selection, config, attributes);
        var members = Members(selection, attributes);
        if (added.Count == 0)
        {
            return StackingProjection.CollectDifferences(members, config.WithoutItems())
                .Contains(StackingFieldCatalog.HiddenFeature) ? StackingQuickResult.HiddenFeature
                : StackingQuickResult.NothingConfigurable;
        }

        StackingConfig candidate;
        try
        {
            candidate = config.WithGlobals(config.Globals.Concat(added.Select(Ignored)));
        }
        catch (ArgumentException)
        {
            return StackingQuickResult.SaveFailed;
        }

        if (!store.TryCommit(candidate)) return StackingQuickResult.SaveFailed;
        if (Merges(members, candidate)) return StackingQuickResult.Merged;
        var itemRule = candidate.Find(selection.ItemId);
        return itemRule is { Enabled: true } && added.Any(id => itemRule.ModeOf(id) == StackingMode.Match)
            ? StackingQuickResult.OverriddenByItem
            : StackingQuickResult.StillSeparate;
    }

    /// <summary>
    /// Deletes only the global rows that merge the selected slot: switched-on "ignore" rows whose fields differ among
    /// its members and that its item rule does not override. The rows go for every item. Returns the message to show,
    /// or null when the selection no longer allows the action.
    /// </summary>
    public static string? DeleteGlobalForSelection(TerminalContext context, StackingConfigStore store,
        EntryAttributes attributes)
    {
        if (store == null) throw new ArgumentNullException(nameof(store));
        if (attributes == null) throw new ArgumentNullException(nameof(attributes));
        var config = store.Effective();
        var selection = Classify(context, config);
        if (config == null || selection.Kind != StackingQuickKind.EditMixed || !HasGlobalIgnore(config)) return null;
        var itemRule = config.Find(selection.ItemId);
        var own = itemRule is { Enabled: true } ? itemRule : null;
        var applied = AppliedFields(selection, config, attributes)
            .Where(id => own?.IndexOf(id) is null or < 0 && config.FindGlobal(id) is { Enabled: true }).ToArray();
        if (applied.Length == 0) return NothingGlobalToDeleteKey;
        var candidate = config.WithGlobals(config.Globals.Where(row => !applied.Contains(row.FieldId)));
        return store.TryCommit(candidate) ? GlobalDeletedKey : StackingRulesEditor.SaveFailedKey;
    }

    /// <summary>
    /// Adds every configurable difference between the selected slots to their item's rule as "ignore" and saves it.
    /// A new rule starts switched on; an existing rule keeps its switch and the modes of rows it already has.
    /// </summary>
    public static StackingQuickResult AddFromSelection(TerminalContext context, StackingConfigStore store,
        EntryAttributes attributes)
    {
        if (store == null) throw new ArgumentNullException(nameof(store));
        if (attributes == null) throw new ArgumentNullException(nameof(attributes));
        var config = store.Effective();
        var selection = Classify(context, config);
        if (config == null || selection.Kind is not (StackingQuickKind.AddNew or StackingQuickKind.AddToExisting))
            return StackingQuickResult.Unavailable;

        var members = Members(selection, attributes);
        var saved = config.Find(selection.ItemId);
        var rule = saved ?? new StackingItemRule(selection.ItemId!, true, Array.Empty<StackingRule>());
        var candidate = config;
        var hidden = false;
        // Recompare after each change because the candidate rule can make additional fields relevant.
        for (var round = 0; round < MaxRounds; round++)
        {
            var trial = config.WithItem(rule.WithEnabled(true));
            var differences = StackingProjection.CollectDifferences(members, trial);
            hidden |= differences.Contains(StackingFieldCatalog.HiddenFeature);

            var added = differences.Where(id => StackingFieldCatalog.Find(id) != null && rule.IndexOf(id) < 0).ToArray();
            if (added.Length == 0) break;
            try
            {
                rule = rule.WithIgnoredFields(added);
                candidate = config.WithItem(rule);
            }
            catch (ArgumentException)
            {
                return StackingQuickResult.SaveFailed;
            }
        }

        if (ReferenceEquals(candidate, config))
        {
            if (saved is { Enabled: false }) return StackingQuickResult.RuleOff;
            return hidden ? StackingQuickResult.HiddenFeature : StackingQuickResult.NothingConfigurable;
        }
        if (!store.TryCommit(candidate)) return StackingQuickResult.SaveFailed;
        if (!rule.Enabled) return StackingQuickResult.RuleOff;
        return Merges(members, candidate) ? StackingQuickResult.Merged : StackingQuickResult.StillSeparate;
    }

    /// <summary>
    /// Deletes only the item's ignore rows that merge the selected slot: the rows whose fields differ among its members.
    /// Other rows of the item's rule stay; a rule left without rows is removed. Global rows are left alone. Returns the
    /// message to show, or null when the selection no longer allows the action.
    /// </summary>
    public static string? DeleteForSelection(TerminalContext context, StackingConfigStore store, EntryAttributes attributes)
    {
        if (store == null) throw new ArgumentNullException(nameof(store));
        if (attributes == null) throw new ArgumentNullException(nameof(attributes));
        var config = store.Effective();
        var selection = Classify(context, config);
        if (config == null || selection.Kind != StackingQuickKind.EditMixed || !selection.HasItemRule) return null;
        var rule = config.Find(selection.ItemId)!;
        var applied = new HashSet<string>(AppliedFields(selection, config, attributes), StringComparer.Ordinal);
        // A switched-off rule merges nothing, and fields it does not name came from the global rows.
        if (!rule.Enabled || !rule.Rules.Any(row => applied.Contains(row.FieldId))) return NothingToDeleteKey;
        var remaining = rule.Rules.Where(row => !applied.Contains(row.FieldId)).ToArray();
        var candidate = remaining.Length == 0 ? config.WithoutItem(rule.ItemId) : config.WithItem(rule.WithRules(remaining));
        return store.TryCommit(candidate) ? DeletedKey : StackingRulesEditor.SaveFailedKey;
    }

    /// <summary>Gets the message shown for a quick add result, or null when nothing needs saying.</summary>
    public static string? MessageOf(StackingQuickResult result) => result switch
    {
        StackingQuickResult.Merged => MergedKey,
        StackingQuickResult.StillSeparate => StillSeparateKey,
        StackingQuickResult.RuleOff => RuleOffKey,
        StackingQuickResult.OverriddenByItem => OverriddenByItemKey,
        StackingQuickResult.NothingConfigurable => NothingConfigurableKey,
        StackingQuickResult.HiddenFeature => HiddenFeatureKey,
        StackingQuickResult.SaveFailed => StackingRulesEditor.SaveFailedKey,
        _ => null
    };

    private static StackingGlobalRule Ignored(string fieldId) => new(fieldId, StackingMode.Ignore, true);

    private static (Api.Stacks.ItemKey Key, StackingAttributes Attributes)[] Members(StackingQuickSelection selection,
        EntryAttributes attributes) =>
        selection.Groups.Select(group => group.Representative.Key)
            .Select(key => (Key: key, Attributes: new StackingAttributes(attributes.For(key)))).ToArray();

    private static IEnumerable<string> AppliedFields(StackingQuickSelection selection, StackingConfig config,
        EntryAttributes attributes)
    {
        var members = selection.Groups[0].Members
            .Select(member => (member.Key, new StackingAttributes(attributes.For(member.Key)))).ToArray();
        return StackingProjection.AppliedIgnoredFields(members, config).Select(field => field.Id);
    }

    private static bool Merges(IReadOnlyList<(Api.Stacks.ItemKey Key, StackingAttributes Attributes)> members,
        StackingConfig config)
    {
        var first = StackingProjection.Compute(members[0].Key, config, members[0].Attributes);
        return members.Skip(1).All(member => StackingProjection.Compute(member.Key, config, member.Attributes).Equals(first));
    }
}
