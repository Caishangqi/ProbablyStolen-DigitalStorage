using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Tooltips;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;

/// <summary>
/// Explains a merged slot in its own panel: how many entries it merges and which ignore rules act on them. Only field
/// names are listed, never values the player could not see on the items.
/// </summary>
public sealed class StackingInfoTooltip : IEntryTooltipContributor, ISelectionTooltipContributor
{
    /// <summary>The contributor id.</summary>
    public const string ContributorId = "stacking_info";

    /// <summary>The panel id.</summary>
    public const string PanelId = "stacking";

    /// <summary>The title key.</summary>
    public const string TitleKey = "stacking_info_title";

    /// <summary>The entry count key.</summary>
    public const string KindsKey = "stacking_info_kinds";

    /// <summary>The caption of the item rule rows acting on the group.</summary>
    public const string ItemRulesKey = "stacking_info_item_rules";

    /// <summary>The caption of the global rows acting on the group.</summary>
    public const string GlobalRulesKey = "stacking_info_global_rules";

    /// <summary>The line telling that the item's own rule is off, so only global rows act.</summary>
    public const string ItemRuleOffKey = "stacking_info_item_off";

    /// <summary>The key of a difference the item rule matches although a global row ignores it.</summary>
    public const string SelectionOverriddenKey = "stacking_info_difference_overridden";

    /// <summary>The key of one rule line; formatted with the field name.</summary>
    public const string RuleKey = "stacking_info_rule";

    /// <summary>The selected groups line.</summary>
    public const string SelectionKey = "stacking_info_selection";

    /// <summary>The unavailable diagnostic line.</summary>
    public const string SelectionUnavailableKey = "stacking_info_selection_unavailable";

    /// <summary>The line used when selected entries have no classifiable difference.</summary>
    public const string SelectionUnknownKey = "stacking_info_selection_unknown";

    /// <summary>The key of one selected difference line.</summary>
    public const string SelectionDifferenceKey = "stacking_info_difference";

    /// <summary>The line for a difference in unexposed or disabled features while the advanced option is off.</summary>
    public const string SelectionHiddenFeatureKey = "stacking_info_hidden_feature";

    /// <summary>The key of a raw tag that cannot be added as a rule.</summary>
    public const string SelectionTagUnavailableKey = "stacking_info_tag_unavailable";

    /// <summary>
    /// Gets the panel after the storage view's hint panel: the game's tooltip text colors, set apart by a purple
    /// border.
    /// </summary>
    public static TooltipPanelDefinition Panel { get; } = new(PanelId, 10, TooltipPanelDefinition.Hint.TitleRgb,
        TooltipPanelDefinition.Hint.LabelRgb, TooltipPanelDefinition.Hint.BodyRgb, 0xC3A6FF, expandToContent: true);

    /// <inheritdoc />
    public string Id => ContributorId;

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsActive(TerminalContext context) => StackingModule.IsInstalled(context);

    /// <inheritdoc />
    public void Contribute(DisplayEntry entry, TerminalContext context, EntryAttributes attributes, EntryTooltipSink sink)
    {
        if (!entry.IsMixed) return;
        var config = StackingProjector.StoreOf(context.FeatureData).Effective();
        sink.Title(PanelId, TitleKey);
        sink.Label(PanelId, KindsKey, entry.Members.Count);
        if (config == null) return;
        var members = entry.Members.Select(member => (member.Key, new StackingAttributes(attributes.For(member.Key)))).ToArray();
        var fields = StackingProjection.AppliedIgnoredFields(members, config);
        if (fields.Count == 0) return;
        var itemId = entry.Representative.Key.Identifier;
        var own = fields.Where(field => config.IsItemField(itemId, field.Id)).ToArray();
        var global = fields.Where(field => !config.IsItemField(itemId, field.Id)).ToArray();
        Group(sink, ItemRulesKey, own);
        if (global.Length > 0 && config.Find(itemId) is { Enabled: false }) sink.Label(PanelId, ItemRuleOffKey);
        Group(sink, GlobalRulesKey, global);
    }

    private static void Group(EntryTooltipSink sink, string captionKey, IReadOnlyList<StackingField> fields)
    {
        if (fields.Count == 0) return;
        sink.Label(PanelId, captionKey);
        foreach (var field in fields) sink.Body(PanelId, RuleKey, FieldName(field));
    }

    /// <inheritdoc />
    public void ContributeSelection(DisplayEntry entry, IReadOnlyList<DisplayEntry> selection,
        TerminalContext context, EntryAttributes attributes, EntryTooltipSink sink)
    {
        if (!IsActive(context) || selection.Count < 2) return;
        var identifier = entry.Representative.Key.Identifier;
        var groups = selection.Where(group => group.Representative.Key.Identifier == identifier)
            .GroupBy(group => group.Id, StringComparer.Ordinal).Select(group => group.First()).ToArray();
        if (groups.Length < 2 || groups.All(group => group.Id != entry.Id)) return;

        if (!entry.IsMixed) sink.Title(PanelId, TitleKey);
        var config = StackingProjector.StoreOf(context.FeatureData).Effective();
        if (config == null)
        {
            sink.Label(PanelId, SelectionUnavailableKey);
            return;
        }

        sink.Label(PanelId, SelectionKey, groups.Length);
        var members = groups.Select(group => group.Representative.Key)
            .Select(key => (key, new StackingAttributes(attributes.For(key)))).ToArray();
        var differences = StackingProjection.CollectDifferences(members, config);
        if (differences.Count == 0)
        {
            sink.Body(PanelId, SelectionUnknownKey);
            return;
        }

        foreach (var fieldId in differences)
        {
            var field = StackingFieldCatalog.Find(fieldId);
            var name = field == null
                ? UnknownField(fieldId)
                : FieldName(field);
            // Explains why a global add can leave the slots apart: the item's own rule matches what the global row ignores.
            var overridden = config.Find(identifier) is { Enabled: true } item && item.ModeOf(fieldId) == StackingMode.Match &&
                config.FindGlobal(fieldId) is { Enabled: true, Mode: StackingMode.Ignore };
            sink.Body(PanelId, overridden ? SelectionOverriddenKey : SelectionDifferenceKey, name);
        }
    }

    private static object FieldName(StackingField field)
    {
        if (field.TagKey != null) return TooltipLiteralText.ForRichText(field.TagKey);
        if (field.FeatureId != null) return new LocalizedText(field.NameKey, TooltipLiteralText.ForRichText(field.FeatureId));
        return new LocalizedText(field.NameKey);
    }

    private static LocalizedText UnknownField(string fieldId)
    {
        if (fieldId == StackingFieldCatalog.HiddenFeature) return new LocalizedText(SelectionHiddenFeatureKey);
        if (fieldId.StartsWith(StackingFieldCatalog.TagPrefix, StringComparison.Ordinal))
            return new LocalizedText(SelectionTagUnavailableKey,
                TooltipLiteralText.ForRichText(fieldId.Substring(StackingFieldCatalog.TagPrefix.Length)));
        return new LocalizedText(SelectionUnknownKey);
    }
}
