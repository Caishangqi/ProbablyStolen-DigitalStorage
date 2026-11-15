using System;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

/// <summary>How a stacking field takes part in the grouping key.</summary>
public enum StackingFieldKind
{
    /// <summary>Owns raw identity data that ignoring removes.</summary>
    Raw,

    /// <summary>A value computed from the item, such as the estimated value; it stays in the key unless ignored.</summary>
    Derived,

    /// <summary>One exposed item feature, owned as a whole.</summary>
    Feature
}

/// <summary>An attribute players can configure as match or ignore for every item of one terminal.</summary>
public sealed class StackingField
{
    private readonly Func<CanonicalIdentityInput, bool> _applies;
    private readonly Func<CanonicalIdentityInput, bool> _visible;

    internal StackingField(string id, StackingFieldKind kind, string nameKey, string descriptionKey,
        StackingOwnedPaths ownedPaths, Func<CanonicalIdentityInput, bool> applies,
        Func<CanonicalIdentityInput, bool>? visible = null, string? featureId = null)
    {
        Id = string.IsNullOrEmpty(id) ? throw new ArgumentException("A field id is required.", nameof(id)) : id;
        Kind = kind;
        NameKey = nameKey ?? throw new ArgumentNullException(nameof(nameKey));
        DescriptionKey = descriptionKey ?? throw new ArgumentNullException(nameof(descriptionKey));
        OwnedPaths = ownedPaths ?? throw new ArgumentNullException(nameof(ownedPaths));
        _applies = applies ?? throw new ArgumentNullException(nameof(applies));
        _visible = visible ?? (_ => true);
        FeatureId = featureId;
    }

    /// <summary>Gets the stable identifier saved in rules.</summary>
    public string Id { get; }

    /// <summary>Gets how the field takes part in the key.</summary>
    public StackingFieldKind Kind { get; }

    /// <summary>Gets the localization key of the name; feature fields format it with the feature id.</summary>
    public string NameKey { get; }

    /// <summary>Gets the localization key of the short description.</summary>
    public string DescriptionKey { get; }

    /// <summary>Gets the raw data the field owns.</summary>
    public StackingOwnedPaths OwnedPaths { get; }

    /// <summary>Gets the feature identifier of a feature field, or null.</summary>
    public string? FeatureId { get; }

    /// <summary>Whether the field describes this item at all.</summary>
    internal bool Applies(CanonicalIdentityInput item) => _applies(item);

    /// <summary>
    /// Whether the player can see the field's value on this item. A field the player cannot see is never ignored, so
    /// grouping reveals nothing the item tooltip would not.
    /// </summary>
    internal bool IsVisible(CanonicalIdentityInput item) => _visible(item);

    internal static StackingField ForFeature(string featureId) =>
        new(StackingFieldCatalog.FeaturePrefix + featureId, StackingFieldKind.Feature, "stacking_field_feature",
            "stacking_field_feature_desc", new StackingOwnedPaths(featureIds: new[] { featureId }),
            item => item.Features.Any(feature => feature.Identifier == featureId),
            item => item.Features.Any(feature => feature.Identifier == featureId && feature.IsFeatureExposed &&
                !feature.IsDisabled),
            featureId);
}
