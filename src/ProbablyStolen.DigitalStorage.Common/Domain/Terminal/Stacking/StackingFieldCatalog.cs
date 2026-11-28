using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

/// <summary>A field offered in the attribute menu.</summary>
public sealed class StackingFieldOption
{
    internal StackingFieldOption(StackingField field, bool absentFromInventory, bool hidden = false)
    {
        Field = field;
        AbsentFromInventory = absentFromInventory;
        Hidden = hidden;
    }

    /// <summary>Gets whether the field is a feature no stored item shows openly: unexposed or disabled.</summary>
    public bool Hidden { get; }

    /// <summary>Gets the field.</summary>
    public StackingField Field { get; }

    /// <summary>Gets whether only a saved rule refers to the field and no stored item has it now.</summary>
    public bool AbsentFromInventory { get; }
}

/// <summary>
/// The stacking fields: fixed semantic fields, keys of root tags recorded in stored item identities, and visible item
/// features. The game has no catalogue of features, so tag and feature fields come from stored items and saved rules.
/// </summary>
public static class StackingFieldCatalog
{
    /// <summary>The prefix of feature field identifiers; the feature identifier follows it.</summary>
    public const string FeaturePrefix = "feature:";

    /// <summary>The prefix of raw tag field identifiers; the tag key follows it.</summary>
    public const string TagPrefix = "tag:";

    /// <summary>Creates the field identifier for one raw tag key.</summary>
    public static string TagFieldId(string tagKey) => TagPrefix + (tagKey ?? throw new ArgumentNullException(nameof(tagKey)));

    /// <summary>
    /// The difference reported for an unexposed or disabled feature while the advanced option is off. It names no feature,
    /// so nothing hidden leaks, and no rule can be made from it.
    /// </summary>
    public const string HiddenFeature = "hidden_feature";

    /// <summary>The estimated value shown in item tooltips.</summary>
    public const string AppraisedValue = "appraised_value";

    /// <summary>Stored value fields and purely economic modifiers.</summary>
    public const string PriceComposition = "price_composition";

    /// <summary>The items inside a container's content window.</summary>
    public const string ContainerContent = "container_content";

    /// <summary>
    /// The difference in a part no rule can ignore, or between identities that cannot be compared;
    /// <see cref="StackingProjection.IdentityParts"/> names the parts.
    /// </summary>
    public const string ItemIdentity = "item_identity";

    // Features ItemFeatureList creates only to change prices; gameplay states such as counterfeit or second hand stay.
    private static readonly string[] EconomicFeatures =
    {
        "discount", "discountBuy15", "discountSell50", "discountedBuy", "discountedBuy20", "discountedBuy25",
        "discountedBuy30", "discountedBuy50", "discountedBuy100", "discountedBuy200", "discountedBuy400",
        "discountedLowContraband", "discountedMidContraband", "discountedHighContraband", "discountedCriticalContraband",
        "premiumBuy", "premiumBuy15", "premiumBuyHypertech", "retailMarkUp", "retailMarkupBuy", "free",
        "firstTimeDiscount", "merchantDiscountBuy", "supplierDiscount", "bargainBuyingDiscount", "bargainBuyingMarkup",
        "bargainDiscount", "bargainMarkup", "blackmailDiscount", "blackmailMarkup", "threatenDiscount",
        "threatenMarkup", "confrontSucessDiscount", "bribeDonation", "tips", "touristTax", "scalperMarkup",
        "inventorEventPrice", "inventorMarkup", "vendingMachineBonus", "varietyBuyerBonus",
        "neuroActivePerfumeBuying", "neuroActivePerfumeSelling", "pheromonePerfumeBuying", "pheromonePerfumeSelling",
        "stolenBonusBuying", "stolenBonusBuyingLow", "stolenBonusBuyingMid", "stolenBonusBuyingHigh",
        "addictionBuyPenalty", "addictionSellPenalty", "awfulNegotiatorBuyPenalty", "awfulNegotiatorSellPenalty"
    };

    private static readonly StackingField[] Fixed =
    {
        new(AppraisedValue, StackingFieldKind.Derived, Name(AppraisedValue), Description(AppraisedValue),
            StackingOwnedPaths.None, _ => true),
        new(PriceComposition, StackingFieldKind.Raw, Name(PriceComposition), Description(PriceComposition),
            new StackingOwnedPaths(valueFields: true, featureIds: EconomicFeatures), _ => true),
        new(ContainerContent, StackingFieldKind.Raw, Name(ContainerContent), Description(ContainerContent),
            new StackingOwnedPaths(contentWindow: true), item => item.ContentWindow != null)
    };

    private static readonly HashSet<string> FixedFeatureIds =
        new(Fixed.SelectMany(field => field.OwnedPaths.FeatureIds), StringComparer.Ordinal);

    /// <summary>Gets the fixed fields in menu order.</summary>
    public static IReadOnlyList<StackingField> FixedFields => Fixed;

    /// <summary>
    /// Gets a field by identifier. Feature and tag identifiers build their field unless a fixed field owns that feature;
    /// an unknown or invalid identifier returns null.
    /// </summary>
    public static StackingField? Find(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var fixedField = Fixed.FirstOrDefault(field => field.Id == id);
        if (fixedField != null) return fixedField;
        if (id!.StartsWith(TagPrefix, StringComparison.Ordinal))
        {
            var tagKey = id.Substring(TagPrefix.Length);
            return tagKey.Length > 0 && StackingRule.IsValidFieldId(id) ? StackingField.ForTag(tagKey) : null;
        }

        if (!id!.StartsWith(FeaturePrefix, StringComparison.Ordinal)) return null;
        var featureId = id.Substring(FeaturePrefix.Length);
        return featureId.Length == 0 || FixedFeatureIds.Contains(featureId) ? null : StackingField.ForFeature(featureId);
    }

    /// <summary>
    /// Lists fixed fields, recorded tags, visible features and saved fields. Without advanced access, a feature is only
    /// listed when a stored sample exposes it.
    /// </summary>
    public static IReadOnlyList<StackingFieldOption> Discover(IEnumerable<ItemKey> storedKeys, IEnumerable<string> savedFieldIds,
        bool includeHidden = false)
    {
        if (storedKeys == null) throw new ArgumentNullException(nameof(storedKeys));
        if (savedFieldIds == null) throw new ArgumentNullException(nameof(savedFieldIds));
        var options = Fixed.Select(field => new StackingFieldOption(field, false)).ToList();
        var seen = new HashSet<string>(Fixed.Select(field => field.Id), StringComparer.Ordinal);
        var tags = new SortedSet<string>(StringComparer.Ordinal);
        var features = new SortedSet<string>(StringComparer.Ordinal);
        var shown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in storedKeys)
        {
            var summary = key?.ReadSummary();
            if (summary == null) continue;
            foreach (var tagKey in summary.Tags.Keys)
            {
                var fieldId = TagFieldId(tagKey);
                if (tagKey.Length > 0 && StackingRule.IsValidFieldId(fieldId)) tags.Add(tagKey);
            }

            foreach (var feature in summary.Features)
            {
                if (FixedFeatureIds.Contains(feature.Identifier)) continue;
                var open = feature.Exposed && !feature.Disabled;
                if (open) shown.Add(feature.Identifier);
                if (open || includeHidden) features.Add(feature.Identifier);
            }
        }

        foreach (var tagKey in tags)
        {
            var field = StackingField.ForTag(tagKey);
            if (seen.Add(field.Id)) options.Add(new StackingFieldOption(field, false));
        }

        foreach (var featureId in features)
        {
            var field = StackingField.ForFeature(featureId);
            if (seen.Add(field.Id)) options.Add(new StackingFieldOption(field, false, !shown.Contains(featureId)));
        }

        foreach (var id in savedFieldIds)
        {
            if (id == null || seen.Contains(id)) continue;
            var field = Find(id);
            if (field == null || !includeHidden && field.FeatureId != null) continue;
            seen.Add(id);
            options.Add(new StackingFieldOption(field, true));
        }

        return options;
    }

    private static string Name(string id) => "stacking_field_" + id;

    private static string Description(string id) => "stacking_field_" + id + "_desc";
}
