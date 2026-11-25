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

    /// <summary>One merged slot whose item rule can be edited or deleted.</summary>
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
    internal static readonly StackingQuickSelection None = new(StackingQuickKind.None, null, Array.Empty<DisplayEntry>());

    internal StackingQuickSelection(StackingQuickKind kind, string? itemId, IReadOnlyList<DisplayEntry> groups)
    {
        Kind = kind;
        ItemId = itemId;
        Groups = groups;
    }

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
            var kind = config.Find(itemId) == null ? StackingQuickKind.AddNew : StackingQuickKind.AddToExisting;
            return new StackingQuickSelection(kind, itemId, selection);
        }

        if (selection.Count == 1 && selection[0].IsMixed)
        {
            var itemId = selection[0].Representative.Key.Identifier;
            if (config.Find(itemId) != null)
                return new StackingQuickSelection(StackingQuickKind.EditMixed, itemId, selection);
        }

        return StackingQuickSelection.None;
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

        var members = selection.Groups.Select(group => group.Representative.Key)
            .Select(key => (Key: key, Attributes: new StackingAttributes(attributes.For(key)))).ToArray();
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
        var first = StackingProjection.Compute(members[0].Key, candidate, members[0].Attributes);
        var merged = members.Skip(1).All(member =>
            StackingProjection.Compute(member.Key, candidate, member.Attributes).Equals(first));
        return merged ? StackingQuickResult.Merged : StackingQuickResult.StillSeparate;
    }

    /// <summary>
    /// Deletes only the ignore rows that merge the selected slot: the rows whose fields differ among its members. Other
    /// rows of the item's rule stay; a rule left without rows is removed. Returns the message to show, or null when the
    /// selection no longer allows the action.
    /// </summary>
    public static string? DeleteForSelection(TerminalContext context, StackingConfigStore store, EntryAttributes attributes)
    {
        if (store == null) throw new ArgumentNullException(nameof(store));
        if (attributes == null) throw new ArgumentNullException(nameof(attributes));
        var config = store.Effective();
        var selection = Classify(context, config);
        if (config == null || selection.Kind != StackingQuickKind.EditMixed) return null;
        var rule = config.Find(selection.ItemId)!;
        var members = selection.Groups[0].Members
            .Select(member => (member.Key, new StackingAttributes(attributes.For(member.Key)))).ToArray();
        var applied = new HashSet<string>(StackingProjection.AppliedIgnoredFields(members, config).Select(field => field.Id),
            StringComparer.Ordinal);
        if (applied.Count == 0) return NothingToDeleteKey;
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
        StackingQuickResult.NothingConfigurable => NothingConfigurableKey,
        StackingQuickResult.HiddenFeature => HiddenFeatureKey,
        StackingQuickResult.SaveFailed => StackingRulesEditor.SaveFailedKey,
        _ => null
    };
}
