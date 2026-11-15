using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;

/// <summary>
/// Explains a merged slot in its own panel: how many entries it merges and which ignore rules act on them. Only field
/// names are listed, never values the player could not see on the items.
/// </summary>
public sealed class StackingInfoTooltip : IEntryTooltipContributor
{
    /// <summary>The contributor id.</summary>
    public const string ContributorId = "stacking_info";

    /// <summary>The panel id.</summary>
    public const string PanelId = "stacking";

    /// <summary>The title key.</summary>
    public const string TitleKey = "stacking_info_title";

    /// <summary>The entry count key.</summary>
    public const string KindsKey = "stacking_info_kinds";

    /// <summary>The rules caption key.</summary>
    public const string RulesKey = "stacking_info_rules";

    /// <summary>The key of one rule line; formatted with the field name.</summary>
    public const string RuleKey = "stacking_info_rule";

    /// <summary>
    /// Gets the panel after the storage view's hint panel: the game's tooltip text colors, set apart by a purple
    /// border.
    /// </summary>
    public static TooltipPanelDefinition Panel { get; } = new(PanelId, 10, TooltipPanelDefinition.Hint.TitleRgb,
        TooltipPanelDefinition.Hint.LabelRgb, TooltipPanelDefinition.Hint.BodyRgb, 0xC3A6FF);

    /// <inheritdoc />
    public string Id => ContributorId;

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsActive(TerminalContext context) => StackingModule.IsInstalled(context);

    /// <inheritdoc />
    public void Contribute(DisplayEntry entry, TerminalContext context, EntryTooltipSink sink)
    {
        if (!entry.IsMixed) return;
        var config = StackingProjector.StoreOf(context.FeatureData).Effective();
        sink.Title(PanelId, TitleKey);
        sink.Label(PanelId, KindsKey, entry.Members.Count);
        if (config == null) return;
        var fields = StackingProjection.AppliedIgnoredFields(entry.Representative.Key, config);
        if (fields.Count == 0) return;
        sink.Label(PanelId, RulesKey);
        foreach (var field in fields)
        {
            // Feature names are formatted with the feature id, the same way the rules page shows them.
            var name = field.FeatureId != null ? new LocalizedText(field.NameKey, field.FeatureId) : new LocalizedText(field.NameKey);
            sink.Body(PanelId, RuleKey, name);
        }
    }
}
