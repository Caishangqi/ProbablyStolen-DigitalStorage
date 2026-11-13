using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal sealed class ItemContentDefinition
{
    internal ItemContentDefinition(string itemId, string nameKey, string descriptionKey, long unitValue,
        string? flavorTextKey = null)
    {
        ItemId = itemId;
        NameKey = nameKey;
        DescriptionKey = descriptionKey;
        UnitValue = unitValue;
        FlavorTextKey = flavorTextKey;
    }

    internal string ItemId { get; }
    internal string NameKey { get; }
    internal string DescriptionKey { get; }
    internal long UnitValue { get; }
    internal string? FlavorTextKey { get; }
}

internal interface IItemContentState
{
    string? Identifier { get; }
    string? Name { get; set; }
    string? ShortDescription { get; set; }
    string? FlavorText { get; set; }
    long UnitValue { get; set; }
}

internal sealed class ItemContentLoadCallback
{
    private readonly ItemContentSynchronization _synchronization;
    private readonly Action<string> _logError;
    private bool _enabled = true;
    private bool _reportedFailure;

    internal ItemContentLoadCallback(ItemContentSynchronization synchronization, Action<string> logError)
    {
        _synchronization = synchronization ?? throw new ArgumentNullException(nameof(synchronization));
        _logError = logError ?? throw new ArgumentNullException(nameof(logError));
    }

    internal void Invoke(IItemContentState item)
    {
        if (!_enabled) return;
        try { _synchronization.Synchronize(item); }
        catch (Exception error)
        {
            if (!_reportedFailure)
            {
                _reportedFailure = true;
                try { _logError($"Digital Storage: item content sync failed ({error.GetType().Name})."); }
                catch { }
            }
        }
    }

    internal void Disable() => _enabled = false;
}

internal sealed class ItemContentSynchronization
{
    private readonly IReadOnlyDictionary<string, ItemContentDefinition> _definitions;
    private readonly Func<string, string> _resolveText;
    private readonly Action<string> _logError;
    private readonly Func<IItemContentState, long, long>? _adjustValue;
    private readonly HashSet<string> _reportedTextFailures = new(StringComparer.Ordinal);

    internal ItemContentSynchronization(Func<string, string> resolveText, Action<string> logError)
        : this(ItemContentDefinitions.All, resolveText, logError)
    {
    }

    internal ItemContentSynchronization(IReadOnlyDictionary<string, ItemContentDefinition> definitions,
        Func<string, string> resolveText, Action<string> logError,
        Func<IItemContentState, long, long>? adjustValue = null)
    {
        _adjustValue = adjustValue;
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _resolveText = resolveText ?? throw new ArgumentNullException(nameof(resolveText));
        _logError = logError ?? throw new ArgumentNullException(nameof(logError));
    }

    internal ItemContentResult Synchronize(IItemContentState item, bool textOnly = false)
    {
        var identifier = item?.Identifier;
        if (identifier == null || !_definitions.TryGetValue(identifier, out var definition))
        {
            return ItemContentResult.Skipped;
        }

        var changed = false;
        var failed = false;
        string? name = null;
        string? description = null;
        string? flavor = null;
        try
        {
            name = ResolveRequired(definition.NameKey);
            description = ResolveRequired(definition.DescriptionKey);
            flavor = string.IsNullOrEmpty(definition.FlavorTextKey)
                ? string.Empty : ResolveRequired(definition.FlavorTextKey!);
        }
        catch (Exception error)
        {
            failed = true;
            if (_reportedTextFailures.Add(identifier))
            {
                try { _logError($"Digital Storage: item text sync failed ({error.GetType().Name})."); }
                catch { }
            }
        }

        if (flavor != null)
        {
            string? originalName;
            string? originalDescription;
            string? originalFlavor;
            try
            {
                originalName = item!.Name;
                originalDescription = item.ShortDescription;
                originalFlavor = item.FlavorText;
            }
            catch (Exception error)
            {
                failed = true;
                try { _logError($"Digital Storage: item text read failed ({error.GetType().Name})."); }
                catch { }
                originalName = originalDescription = originalFlavor = null;
            }

            if (!failed)
            {
                try
                {
                    if (!string.Equals(item!.Name, name, StringComparison.Ordinal)) { item.Name = name; changed = true; }
                    if (!string.Equals(item.ShortDescription, description, StringComparison.Ordinal)) { item.ShortDescription = description; changed = true; }
                    if (!string.Equals(item.FlavorText, flavor, StringComparison.Ordinal)) { item.FlavorText = flavor; changed = true; }
                }
                catch (Exception error)
                {
                    failed = true;
                    changed = false;
                    TryRestore(() => item!.FlavorText = originalFlavor);
                    TryRestore(() => item!.ShortDescription = originalDescription);
                    TryRestore(() => item!.Name = originalName);
                    try { _logError($"Digital Storage: item text update failed ({error.GetType().Name})."); }
                    catch { }
                }
            }
        }

        if (!textOnly)
        {
            try
            {
                var value = _adjustValue?.Invoke(item!, definition.UnitValue) ?? definition.UnitValue;
                if (item!.UnitValue != value) { item.UnitValue = value; changed = true; }
            }
            catch (Exception error)
            {
                failed = true;
                try { _logError($"Digital Storage: item value sync failed ({error.GetType().Name})."); }
                catch { }
            }
        }
        return failed ? ItemContentResult.Failed : changed ? ItemContentResult.Changed : ItemContentResult.Unchanged;
    }

    private void TryRestore(Action restore)
    {
        try { restore(); }
        catch (Exception error)
        {
            try { _logError($"Digital Storage: item text rollback failed ({error.GetType().Name})."); }
            catch { }
        }
    }

    private string ResolveRequired(string key)
    {
        var value = _resolveText(key);
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Localization entry is unavailable.");
        }

        return value;
    }
}

internal static class ItemContentDefinitions
{
    internal static readonly IReadOnlyDictionary<string, ItemContentDefinition> All = Create();

    private static IReadOnlyDictionary<string, ItemContentDefinition> Create()
    {
        var definitions = new Dictionary<string, ItemContentDefinition>(StringComparer.Ordinal)
        {
            [TerminalItem.ItemId] = new(TerminalItem.ItemId, TerminalItem.NameKey, TerminalItem.DescriptionKey, ItemPrices.Terminal),
            [DriveItem.ItemId] = new(DriveItem.ItemId, DriveItem.NameKey, DriveItem.DescriptionKey, ItemPrices.Drive),
            [ControllerItem.ItemId] = new(ControllerItem.ItemId, ControllerItem.NameKey, ControllerItem.DescriptionKey, ItemPrices.Controller),
            [MaterialItem.ItemId] = new(MaterialItem.ItemId, MaterialItem.NameKey, MaterialItem.DescriptionKey, ItemPrices.CellHousing),
            [CellWorkbenchItem.ItemId] = new(CellWorkbenchItem.ItemId, CellWorkbenchItem.NameKey, CellWorkbenchItem.DescriptionKey,
                ItemPrices.Workbench),
            [CellUpgradeInventory.TypeLimitId] = new(CellUpgradeInventory.TypeLimitId, "module_type_limit_name",
                "module_type_limit_desc", ItemPrices.TypeLimitModule),
            [CellUpgradeInventory.EqualDistributionId] = new(CellUpgradeInventory.EqualDistributionId,
                "module_equal_distribution_name", "module_equal_distribution_desc", ItemPrices.EqualDistributionModule)
        };
        foreach (var tier in StorageCellTier.All)
        {
            var componentKey = "cell_component_" + tier.Name;
            var cellKey = "item_storage_cell_" + tier.Name;
            definitions.Add(tier.ComponentId, new(tier.ComponentId, componentKey + "_name", componentKey + "_desc", tier.ComponentValue));
            definitions.Add(tier.CellId, new(tier.CellId, cellKey + "_name", cellKey + "_desc", ItemPrices.CellHousing));
        }
        return definitions;
    }
}

internal sealed class NativeItemContentState : IItemContentState
{
    private readonly GameItem _item;
    internal NativeItemContentState(GameItem item) => _item = item;

    internal GameItem Item => _item;
    public string? Identifier => _item.identifier;
    public string? Name { get => _item.name; set => _item.name = value; }
    public string? ShortDescription { get => _item.shortDescription; set => _item.shortDescription = value; }
    public string? FlavorText { get => _item.flavorText; set => _item.flavorText = value; }
    public long UnitValue { get => _item.unitValue; set => _item.unitValue = value; }
}

internal static class NativeItemContentInitialization
{
    internal static void Initialize(GameItem item, ItemContentSynchronization synchronization) =>
        synchronization.Synchronize(new NativeItemContentState(item));
}

/// <summary>
/// Adjusts synchronized values: a module bay expansion adds 100 per stage, like the native kit, so the value survives
/// the resynchronization on load and assembly; a type limit module is priced from its rolled values.
/// </summary>
internal static class ItemValueAdjustment
{
    internal static long Adjust(IItemContentState state, long baseValue)
    {
        if (state is not NativeItemContentState native) return baseValue;
        var item = native.Item;
        if (item.identifier == CellUpgradeInventory.TypeLimitId)
        {
            var roll = ModuleTags.Read(item);
            return roll != null && TypeLimitRolls.IsValid(roll) ? TypeLimitRolls.Price(roll)
                : throw new InvalidOperationException("Invalid module roll.");
        }

        if (item.identifier == CellUpgradeInventory.EqualDistributionId) return baseValue;
        var stage = CellUpgradeInventory.Stage(item);
        var tier = StorageCellTier.FromCellId(item.identifier);
        var maxStage = tier == null ? ModuleBayExpansion.MaxNativeStage : ModuleBayExpansion.MaxStage(tier);
        return ItemContentPriceRules.WithExpansion(baseValue, stage, maxStage, ItemPrices.ExpansionStage);
    }
}
