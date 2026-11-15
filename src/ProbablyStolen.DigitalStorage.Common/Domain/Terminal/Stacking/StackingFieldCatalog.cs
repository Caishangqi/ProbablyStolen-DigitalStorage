using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

/// <summary>A field offered in the attribute menu.</summary>
public sealed class StackingFieldOption
{
    internal StackingFieldOption(StackingField field, bool absentFromInventory)
    {
        Field = field;
        AbsentFromInventory = absentFromInventory;
    }

    /// <summary>Gets the field.</summary>
    public StackingField Field { get; }

    /// <summary>Gets whether only a saved rule refers to the field and no stored item has it now.</summary>
    public bool AbsentFromInventory { get; }
}

/// <summary>
/// The stacking fields: fixed fields whose raw data ownership is known for the target build, plus one field per item
/// feature players can see. The game has no catalogue of features (they are created on demand), so feature fields
/// come from the items the terminal stores and from saved rules.
/// </summary>
public static class StackingFieldCatalog
{
    /// <summary>The prefix of feature field identifiers; the feature identifier follows it.</summary>
    public const string FeaturePrefix = "feature:";

    /// <summary>The estimated value shown in item tooltips.</summary>
    public const string AppraisedValue = "appraised_value";

    /// <summary>Stored value fields, the price paid when bought, and purely economic modifiers.</summary>
    public const string PriceComposition = "price_composition";

    /// <summary>Metal purity of ingots.</summary>
    public const string MetalPurity = "metal_purity";

    /// <summary>The items inside a container's content window.</summary>
    public const string ContainerContent = "container_content";

    /// <summary>Contraband level.</summary>
    public const string Legality = "legality";

    /// <summary>Stolen status, heat included.</summary>
    public const string Stolen = "stolen";

    /// <summary>Stolen heat only.</summary>
    public const string Heat = "heat";

    private const string PurityTag = "INGOT_PURITY_TAG";
    private const string StolenValueTag = "STOLEN_VALUE_INT";

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

    private static readonly string[] LegalityFeatures =
    {
        "contrabandLowMarkUp", "contrabandMidMarkUp", "contrabandHighMarkUp", "contrabandCriticalMarkUp",
        "restrictedLowMarkUp", "restrictedMidMarkUp", "restrictedHighMarkUp"
    };

    private static readonly StackingField[] Fixed =
    {
        new(AppraisedValue, StackingFieldKind.Derived, Name(AppraisedValue), Description(AppraisedValue),
            StackingOwnedPaths.None, _ => true),
        new(PriceComposition, StackingFieldKind.Raw, Name(PriceComposition), Description(PriceComposition),
            new StackingOwnedPaths(valueFields: true, tagKeys: new[] { "BOUGHT_PRICE_TAG" }, featureIds: EconomicFeatures), _ => true),
        new(MetalPurity, StackingFieldKind.Raw, Name(MetalPurity), Description(MetalPurity),
            new StackingOwnedPaths(tagKeys: new[] { PurityTag, "INGOT_PURITY_FAKE_TAG" },
                featureIds: new[] { PurityTag, "INGOT_PURITY_INCONCLUSIVE" }),
            item => item.Tags.Any(tag => tag.Key == PurityTag),
            item => item.Features.Any(feature => feature.Identifier == PurityTag && feature.IsFeatureDiscovered)),
        new(ContainerContent, StackingFieldKind.Raw, Name(ContainerContent), Description(ContainerContent),
            new StackingOwnedPaths(contentWindow: true), item => item.ContentWindow != null),
        new(Legality, StackingFieldKind.Raw, Name(Legality), Description(Legality),
            new StackingOwnedPaths(tagKeys: new[] { "CONTRABAND_ITEM_TAG", "CONTRABAND_LEVEL" },
                featureIds: LegalityFeatures), _ => true),
        new(Stolen, StackingFieldKind.Raw, Name(Stolen), Description(Stolen),
            new StackingOwnedPaths(tagKeys: new[] { "STOLEN_TAG", StolenValueTag }), _ => true),
        new(Heat, StackingFieldKind.Raw, Name(Heat), Description(Heat),
            new StackingOwnedPaths(tagKeys: new[] { StolenValueTag }), _ => true)
    };

    private static readonly HashSet<string> FixedFeatureIds =
        new(Fixed.SelectMany(field => field.OwnedPaths.FeatureIds), StringComparer.Ordinal);

    /// <summary>Gets the fixed fields in menu order.</summary>
    public static IReadOnlyList<StackingField> FixedFields => Fixed;

    /// <summary>
    /// Gets a field by identifier. A feature identifier builds its feature field unless a fixed field owns that
    /// feature; an unknown identifier returns null.
    /// </summary>
    public static StackingField? Find(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var fixedField = Fixed.FirstOrDefault(field => field.Id == id);
        if (fixedField != null) return fixedField;
        if (!id!.StartsWith(FeaturePrefix, StringComparison.Ordinal)) return null;
        var featureId = id.Substring(FeaturePrefix.Length);
        return featureId.Length == 0 || FixedFeatureIds.Contains(featureId) ? null : StackingField.ForFeature(featureId);
    }

    /// <summary>
    /// Lists the menu: the fixed fields, every exposed and enabled feature of the stored items that no fixed field owns,
    /// and every field a saved rule names that is neither, marked as absent from the inventory.
    /// </summary>
    public static IReadOnlyList<StackingFieldOption> Discover(IEnumerable<ItemKey> storedKeys, IEnumerable<string> savedFieldIds)
    {
        if (storedKeys == null) throw new ArgumentNullException(nameof(storedKeys));
        if (savedFieldIds == null) throw new ArgumentNullException(nameof(savedFieldIds));
        var options = Fixed.Select(field => new StackingFieldOption(field, false)).ToList();
        var seen = new HashSet<string>(Fixed.Select(field => field.Id), StringComparer.Ordinal);
        var features = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var key in storedKeys)
        {
            var summary = key?.ReadSummary();
            if (summary == null) continue;
            foreach (var feature in summary.Features)
            {
                if (feature.Exposed && !feature.Disabled && !FixedFeatureIds.Contains(feature.Identifier))
                    features.Add(feature.Identifier);
            }
        }

        foreach (var featureId in features)
        {
            var field = StackingField.ForFeature(featureId);
            if (seen.Add(field.Id)) options.Add(new StackingFieldOption(field, false));
        }

        foreach (var id in savedFieldIds)
        {
            if (id == null || seen.Contains(id)) continue;
            var field = Find(id);
            if (field == null) continue;
            seen.Add(id);
            options.Add(new StackingFieldOption(field, true));
        }

        return options;
    }

    private static string Name(string id) => "stacking_field_" + id;

    private static string Description(string id) => "stacking_field_" + id + "_desc";
}
