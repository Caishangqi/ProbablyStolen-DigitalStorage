using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal sealed class ItemContentService : IDisposable
{
    private const string StuckIdentifier = "moduleStuck";
    private static readonly BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly ItemContentSynchronization _synchronization;
    private readonly ItemContentCoordinator<GameItem> _coordinator;
    private readonly Action<string> _log;
    private Func<GameItem, bool>? _presentModule;
    private ItemContentFeatureSnapshot? _nativeStuckTemplate;
    private IReadOnlyCollection<string> _nativeStuckConditionDisplays = Array.Empty<string>();
    private IReadOnlyCollection<string> _nativeStuckFeatureDisplays = Array.Empty<string>();
    private bool _loading;
    private bool _disposed;

    internal ItemContentService(ItemContentSynchronization synchronization, Action changed, Action<string> log)
    {
        _synchronization = synchronization;
        _log = log;
        _coordinator = new ItemContentCoordinator<GameItem>(Apply, () =>
        {
            if (!_loading && !SaveManager.IsDecoding) changed();
        },
            error => _log($"Digital Storage: item content failed ({error.GetType().Name})."));
    }

    internal void SetModulePresenter(Func<GameItem, bool> presenter) => _presentModule = presenter;

    internal void BeginLoad()
    {
        _coordinator.Reset();
        _loading = true;
    }

    internal void NewGame(string scenarioId)
    {
        if (_disposed) return;
        // The native hook fires after a modded scenario's onStart callback has created its starting items. Keep
        // weakly tracked factory candidates across this boundary, then add the items visible in the new store.
        _loading = false;
        if (EmporiumEntry.Instance != null)
        {
            foreach (var item in EmporiumEntry.Instance.GetAllItems())
                if (item?.identifier != null && ItemContentDefinitions.All.ContainsKey(item.identifier))
                    _coordinator.Track(item);
        }
    }

    internal void Loaded(GameItem item)
    {
        if (!_disposed) _coordinator.Track(item);
    }

    internal void Initialize(GameItem item)
    {
        if (_disposed) return;
        _coordinator.Apply(new[] { item }, SaveManager.IsDecoding);
    }

    internal void Changed(GameItem item)
    {
        if (_disposed || _loading || SaveManager.IsDecoding) return;
        _coordinator.Apply(new[] { item }, false, forceNotification: true);
    }

    internal void FinishLoad()
    {
        if (_disposed) return;
        _loading = false;
        try
        {
            var template = Snapshot(ItemFeatureList.ModuleStuckFeature());
            _nativeStuckTemplate = template;
            _nativeStuckConditionDisplays = ReadNativeLocalizedVariants("mech_condition_stuck",
                template.FakeCondition?.Display);
            _nativeStuckFeatureDisplays = ReadNativeLocalizedVariants("item_feature_module_stuck",
                template.PublicDisplay);
        }
        catch (Exception error)
        {
            _nativeStuckTemplate = null;
            _nativeStuckConditionDisplays = Array.Empty<string>();
            _nativeStuckFeatureDisplays = Array.Empty<string>();
            _log($"Digital Storage: legacy component state was not repaired ({error.GetType().Name}).");
        }
        try
        {
            IEnumerable<GameItem> items = EmporiumEntry.Instance == null
                ? Array.Empty<GameItem>() : EmporiumEntry.Instance.GetAllItems()
                    .Where(item => item != null && item.identifier != null && ItemContentDefinitions.All.ContainsKey(item.identifier));
            Report(_coordinator.ApplyTracked(false, items));
        }
        finally
        {
            _nativeStuckTemplate = null;
            _nativeStuckConditionDisplays = Array.Empty<string>();
            _nativeStuckFeatureDisplays = Array.Empty<string>();
        }
    }

    internal void RefreshLocale()
    {
        if (_disposed || _loading) return;
        Report(_coordinator.ApplyTracked(true));
    }

    private ItemContentResult Apply(GameItem item, bool textOnly)
    {
        var repaired = _nativeStuckTemplate is { } template &&
            RepairInstalledComponent(item, template, _nativeStuckConditionDisplays, _nativeStuckFeatureDisplays);
        var result = _synchronization.Synchronize(new NativeItemContentState(item), textOnly);
        if (result != ItemContentResult.Skipped &&
            (StorageCellTier.FromCellId(item.identifier) != null || item.identifier == MaterialItem.ItemId))
            CellContentWindow.RefreshTitle(item);
        if (result != ItemContentResult.Skipped && item.identifier == CellUpgradeInventory.TypeLimitId && _presentModule != null)
        {
            try { if (_presentModule(item)) repaired = true; }
            catch (Exception error)
            {
                _log($"Digital Storage: module presentation failed ({error.GetType().Name}).");
                result = ItemContentResult.Failed;
            }
        }
        return result == ItemContentResult.Failed ? result : repaired ? ItemContentResult.Changed : result;
    }

    private static bool RepairInstalledComponent(GameItem component, ItemContentFeatureSnapshot nativeTemplate,
        IReadOnlyCollection<string> nativeConditionDisplays, IReadOnlyCollection<string> nativeFeatureDisplays)
    {
        if (component.parentInventory?.identifier != CellContentWindow.SlotId) return false;
        var host = component.parentInventory.GetParentItem();
        if (host == null || !ItemContentRepairRules.IsInstalledComponent(component.identifier, component.unitCount,
                component.IsTag(ModuleHelper.MODULE_STUCK_TAG), true, host.identifier, host.unitCount,
                ReferenceEquals(CellContentWindow.Component(host), component))) return false;
        var features = new List<ItemContentFeatureSnapshot>();
        var sourceIndexes = new List<int>();
        for (var sourceFeatureIndex = 0; sourceFeatureIndex < component.itemFeatures.Count; sourceFeatureIndex++)
        {
            var feature = component.itemFeatures[sourceFeatureIndex];
            if (feature == null || !string.Equals(feature.identifier, StuckIdentifier, StringComparison.Ordinal)) continue;
            sourceIndexes.Add(sourceFeatureIndex);
            features.Add(new ItemContentFeatureSnapshot(feature.identifier, feature.category,
                feature.featureType.ToString(), feature.valueStage.ToString(), feature.parentItemUniqueId,
                ReadFeatureField<string>(feature, "publicDisplay"), ReadFeatureField<string>(feature, "actualDisplay"),
                feature.GetToolNeeded(), ReadFeatureField<int>(feature, "valueModifier"),
                ReadFeatureField<bool>(feature, "usePreExposeValue"), ReadFeatureField<int>(feature, "preExposeValueModifier"),
                feature.useCondition, feature.isFeatureMatch, feature.isExposable, feature.IsExposed(),
                feature.isExposing, feature.IsInitiallyShown(), feature.IsFeatureDiscovered(), feature.isPublicHidden,
                feature.isDisabled, feature.customIntValue1, Snapshot(feature.fakeCondition), Snapshot(feature.realCondition)));
        }
        var index = ItemContentRepairRules.FindNativeStuckFeature(features, component.uniqueId, nativeTemplate,
            nativeConditionDisplays, nativeFeatureDisplays);
        var sourceIndex = ItemContentRepairRules.ResolveNativeFeatureIndex(index, sourceIndexes);
        if (sourceIndex < 0) return false;
        component.RemoveItemFeature(component.itemFeatures[sourceIndex]);
        return true;
    }

    private static IReadOnlyCollection<string> ReadNativeLocalizedVariants(string key, string? fallback)
    {
        // Native saved features store localized strings; recognize translations from this build's loaded locales.
        var displays = new HashSet<string>(StringComparer.Ordinal);
        var locales = UnityEngine.Localization.Settings.LocalizationSettings.AvailableLocales?.Locales;
        if (locales != null)
        {
            foreach (var locale in locales)
            {
                try
                {
                    var display = UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase
                        .GetLocalizedString("Mechanic", key, locale,
                            UnityEngine.Localization.Settings.FallbackBehavior.UseProjectSettings);
                    if (!string.IsNullOrEmpty(display)) displays.Add(display);
                }
                catch { }
            }
        }
        if (!string.IsNullOrEmpty(fallback)) displays.Add(fallback);
        return displays;
    }

    private static T? ReadFeatureField<T>(ItemFeature feature, string name)
    {
        var field = typeof(ItemFeature).GetField(name, InstanceFields);
        if (field == null || field.DeclaringType != typeof(ItemFeature) || field.FieldType != typeof(T))
            throw new InvalidOperationException("A native item-feature field is unavailable.");
        var value = field.GetValue(feature);
        if (value == null && !typeof(T).IsValueType) return default;
        return value is T typed ? typed : throw new InvalidOperationException("A native item-feature field has an invalid value.");
    }

    private static ItemContentFeatureSnapshot Snapshot(ItemFeature feature) =>
        new(feature.identifier, feature.category, feature.featureType.ToString(), feature.valueStage.ToString(),
            feature.parentItemUniqueId, ReadFeatureField<string>(feature, "publicDisplay"),
            ReadFeatureField<string>(feature, "actualDisplay"), feature.GetToolNeeded(),
            ReadFeatureField<int>(feature, "valueModifier"), ReadFeatureField<bool>(feature, "usePreExposeValue"),
            ReadFeatureField<int>(feature, "preExposeValueModifier"), feature.useCondition, feature.isFeatureMatch,
            feature.isExposable, feature.IsExposed(), feature.isExposing, feature.IsInitiallyShown(),
            feature.IsFeatureDiscovered(), feature.isPublicHidden, feature.isDisabled, feature.customIntValue1,
            Snapshot(feature.fakeCondition), Snapshot(feature.realCondition));

    private static ItemContentConditionSnapshot? Snapshot(ItemCondition? condition) => condition == null ? null
        : new ItemContentConditionSnapshot(condition.display, condition.identifier, condition.category, condition.modValue,
            condition.modValueDisplay, condition.isTransformative, condition.hiddenAsPublic, condition.customValue,
            condition.onActivateActionId, condition.compareValue);

    private void Report(ItemContentBatchResult result)
    {
        if (result.Failed > 0) _log($"Digital Storage: item content batch had {result.Failed} failures and {result.Changed} changes.");
    }

    public void Dispose()
    {
        _disposed = true;
        _coordinator.Dispose();
    }
}
