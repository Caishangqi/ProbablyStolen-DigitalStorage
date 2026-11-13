using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal readonly struct ItemContentConditionSnapshot
{
    internal ItemContentConditionSnapshot(string? display, string? identifier, string? category, int modifier,
        int displayModifier, bool transformative, bool hidden, int customValue, string? onActivateActionId,
        int compareValue)
    {
        Display = display;
        Identifier = identifier;
        Category = category;
        Modifier = modifier;
        DisplayModifier = displayModifier;
        Transformative = transformative;
        Hidden = hidden;
        CustomValue = customValue;
        OnActivateActionId = onActivateActionId;
        CompareValue = compareValue;
    }

    internal string? Display { get; }
    internal string? Identifier { get; }
    internal string? Category { get; }
    internal int Modifier { get; }
    internal int DisplayModifier { get; }
    internal bool Transformative { get; }
    internal bool Hidden { get; }
    internal int CustomValue { get; }
    internal string? OnActivateActionId { get; }
    internal int CompareValue { get; }
}

internal readonly struct ItemContentFeatureSnapshot
{
    internal ItemContentFeatureSnapshot(string? identifier, string? category, string? featureType,
        string? valueStage, int parentItemUniqueId, string? publicDisplay, string? actualDisplay,
        string? removedByTool, int valueModifier, bool usePreExposeValue, int preExposeValueModifier,
        bool useCondition, bool featureMatch, bool exposable, bool exposed, bool exposing,
        bool initiallyShown, bool discovered, bool publicHidden, bool disabled, int customValue,
        ItemContentConditionSnapshot? fakeCondition, ItemContentConditionSnapshot? realCondition)
    {
        Identifier = identifier;
        Category = category;
        FeatureType = featureType;
        ValueStage = valueStage;
        ParentItemUniqueId = parentItemUniqueId;
        PublicDisplay = publicDisplay;
        ActualDisplay = actualDisplay;
        RemovedByTool = removedByTool;
        ValueModifier = valueModifier;
        UsePreExposeValue = usePreExposeValue;
        PreExposeValueModifier = preExposeValueModifier;
        UseCondition = useCondition;
        FeatureMatch = featureMatch;
        Exposable = exposable;
        Exposed = exposed;
        Exposing = exposing;
        InitiallyShown = initiallyShown;
        Discovered = discovered;
        PublicHidden = publicHidden;
        Disabled = disabled;
        CustomValue = customValue;
        FakeCondition = fakeCondition;
        RealCondition = realCondition;
    }

    internal string? Identifier { get; }
    internal string? Category { get; }
    internal string? FeatureType { get; }
    internal string? ValueStage { get; }
    internal int ParentItemUniqueId { get; }
    internal string? PublicDisplay { get; }
    internal string? ActualDisplay { get; }
    internal string? RemovedByTool { get; }
    internal int ValueModifier { get; }
    internal bool UsePreExposeValue { get; }
    internal int PreExposeValueModifier { get; }
    internal bool UseCondition { get; }
    internal bool FeatureMatch { get; }
    internal bool Exposable { get; }
    internal bool Exposed { get; }
    internal bool Exposing { get; }
    internal bool InitiallyShown { get; }
    internal bool Discovered { get; }
    internal bool PublicHidden { get; }
    internal bool Disabled { get; }
    internal int CustomValue { get; }
    internal ItemContentConditionSnapshot? FakeCondition { get; }
    internal ItemContentConditionSnapshot? RealCondition { get; }
}

internal static class ItemContentRepairRules
{
    private const string StuckIdentifier = "moduleStuck";

    internal static bool IsInstalledComponent(string? componentId, int componentCount, bool locked,
        bool correctSlot, string? hostId, int hostCount, bool sameComponent)
    {
        var tier = StorageCellTier.FromComponentId(componentId);
        return tier != null && componentCount == 1 && locked && correctSlot && hostCount == 1 && sameComponent &&
            string.Equals(hostId, tier.CellId, StringComparison.Ordinal);
    }

    /// <summary>Returns the sole feature matching the current native factory signature, otherwise leaves it untouched.</summary>
    internal static int FindNativeStuckFeature(IReadOnlyList<ItemContentFeatureSnapshot> features, int itemUniqueId,
        ItemContentFeatureSnapshot nativeTemplate, IReadOnlyCollection<string> nativeConditionDisplays,
        IReadOnlyCollection<string> nativeFeatureDisplays)
    {
        if (features == null) throw new ArgumentNullException(nameof(features));
        if (nativeConditionDisplays == null) throw new ArgumentNullException(nameof(nativeConditionDisplays));
        if (nativeFeatureDisplays == null) throw new ArgumentNullException(nameof(nativeFeatureDisplays));
        var identifierCount = 0;
        var match = -1;
        for (var index = 0; index < features.Count; index++)
        {
            var feature = features[index];
            if (!string.Equals(feature.Identifier, StuckIdentifier, StringComparison.Ordinal)) continue;
            identifierCount++;
            if (feature.ParentItemUniqueId == itemUniqueId &&
                MatchesTemplate(feature, nativeTemplate, nativeConditionDisplays, nativeFeatureDisplays)) match = index;
        }

        return identifierCount == 1 ? match : -1;
    }

    internal static int ResolveNativeFeatureIndex(int matchIndex, IReadOnlyList<int> sourceIndexes)
    {
        if (sourceIndexes == null) throw new ArgumentNullException(nameof(sourceIndexes));
        return matchIndex >= 0 && matchIndex < sourceIndexes.Count && sourceIndexes[matchIndex] >= 0
            ? sourceIndexes[matchIndex] : -1;
    }

    private static bool MatchesTemplate(ItemContentFeatureSnapshot feature, ItemContentFeatureSnapshot template,
        IReadOnlyCollection<string> nativeConditionDisplays, IReadOnlyCollection<string> nativeFeatureDisplays) =>
        string.Equals(feature.Identifier, template.Identifier, StringComparison.Ordinal) &&
        string.Equals(feature.Category, template.Category, StringComparison.Ordinal) &&
        string.Equals(feature.FeatureType, template.FeatureType, StringComparison.Ordinal) &&
        string.Equals(feature.ValueStage, template.ValueStage, StringComparison.Ordinal) &&
        nativeFeatureDisplays.Contains(feature.PublicDisplay ?? string.Empty) &&
        nativeFeatureDisplays.Contains(feature.ActualDisplay ?? string.Empty) &&
        string.Equals(feature.RemovedByTool, template.RemovedByTool, StringComparison.Ordinal) &&
        feature.ValueModifier == template.ValueModifier &&
        feature.UsePreExposeValue == template.UsePreExposeValue &&
        feature.PreExposeValueModifier == template.PreExposeValueModifier &&
        feature.UseCondition == template.UseCondition && feature.FeatureMatch == template.FeatureMatch &&
        feature.Exposable == template.Exposable && feature.Exposed == template.Exposed &&
        feature.Exposing == template.Exposing && feature.InitiallyShown == template.InitiallyShown &&
        feature.Discovered == template.Discovered && feature.PublicHidden == template.PublicHidden &&
        feature.Disabled == template.Disabled && feature.CustomValue == template.CustomValue &&
        SameCondition(feature.FakeCondition, template.FakeCondition, nativeConditionDisplays) &&
        SameCondition(feature.RealCondition, template.RealCondition, nativeConditionDisplays);

    private static bool SameCondition(ItemContentConditionSnapshot? left, ItemContentConditionSnapshot? right,
        IReadOnlyCollection<string> nativeConditionDisplays) =>
        left is { } a && right is { } b && nativeConditionDisplays.Contains(a.Display ?? string.Empty) &&
        nativeConditionDisplays.Contains(b.Display ?? string.Empty) &&
        string.Equals(a.Identifier, b.Identifier, StringComparison.Ordinal) &&
        string.Equals(a.Category, b.Category, StringComparison.Ordinal) && a.Modifier == b.Modifier &&
        a.DisplayModifier == b.DisplayModifier && a.Transformative == b.Transformative && a.Hidden == b.Hidden &&
        a.CustomValue == b.CustomValue && string.Equals(a.OnActivateActionId, b.OnActivateActionId, StringComparison.Ordinal) &&
        a.CompareValue == b.CompareValue;
}
