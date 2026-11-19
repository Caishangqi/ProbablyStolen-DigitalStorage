using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Commerce;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>
/// The digital storage tab of Wild's network. The first upgrade grants a working starter network and brings the data
/// scientist; the second grants the workbench and modules and widens both vendors' stock.
/// </summary>
internal sealed class DigitalStorageNetwork : IDisposable
{
    // Saves record purchases by these ids; renaming one forgets the purchase.
    internal const string StarterId = "digital_storage.DIGITAL_STORAGE_STARTER";
    internal const string AdvancedId = "digital_storage.DIGITAL_STORAGE_ADVANCED";
    internal const int StarterCost = 598;
    internal const int AdvancedCost = 998;
    private const string Tab = "digital_storage";
    private const string Card = "DigitalStorage";
    private const string StarterBatteryId = "energy_credit";
    private readonly string _modId;
    private readonly ModText _text;
    private readonly StorageGoods _goods;
    private readonly ModLog _log;
    private bool _enabled;

    internal DigitalStorageNetwork(string modId, ModText text, StorageGoods goods, ModLog log)
    {
        _modId = modId ?? throw new ArgumentNullException(nameof(modId));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _goods = goods ?? throw new ArgumentNullException(nameof(goods));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    internal static bool IsStarterUnlocked() => ModHelper.IsNetworkUpgradeUnlocked(StarterId);

    internal static bool IsAdvancedUnlocked() => ModHelper.IsNetworkUpgradeUnlocked(AdvancedId);

    /// <summary>
    /// Registers the upgrades for every run. <c>NetworkUpgrade.RegisterModUpgrade</c> reads <c>PlayerStore.Instance</c>,
    /// whose getter constructs the store, and that constructor throws while mods load in the main menu. Registration
    /// therefore waits for a run: <c>OnGameLoadedInit</c> fires before the save is decoded and missing upgrades are
    /// added, and <c>OnNewGameStarted</c> fires after the new run's upgrade list exists, which registration extends.
    /// </summary>
    internal void Enable()
    {
        if (_enabled) return;
        _enabled = true;
        ModHelper.SetNetworkTabLabel(Tab, () => _text.Get("network_tab_digital_storage"));
        ModHook.OnGameLoadedInit += Register;
        ModHook.OnGameLoadedLate += SyncCosts;
        ModHook.OnNewGameStarted += OnNewGameStarted;
        if (PlayerStore.HasInstance) Register();
    }

    public void Dispose()
    {
        if (!_enabled) return;
        _enabled = false;
        ModHook.OnGameLoadedInit -= Register;
        ModHook.OnGameLoadedLate -= SyncCosts;
        ModHook.OnNewGameStarted -= OnNewGameStarted;
        ModHelper.RemoveNetworkUpgrades(_modId);
        ModHelper.SetNetworkTabLabel(Tab, null);
    }

    private void OnNewGameStarted(string scenario) => Register();

    /// <summary>
    /// A save keeps each upgrade's price, and registration does not replace an upgrade the save already has. Neither
    /// upgrade repeats, so an unpurchased one has no raised price to keep and takes the current price instead.
    /// </summary>
    private void SyncCosts()
    {
        try
        {
            SyncCost(StarterId, StarterCost);
            SyncCost(AdvancedId, AdvancedCost);
        }
        catch (Exception error)
        {
            _log.Error($"Digital Storage: network upgrade prices were not updated ({error.GetType().Name}: {error.Message}).");
        }
    }

    private void SyncCost(string id, int cost)
    {
        var upgrade = ModHelper.GetNetworkUpgrade(id);
        if (upgrade == null || upgrade.IsUnlocked() || upgrade.cost == cost) return;
        _log.Log($"Digital Storage: network upgrade {id} price updated from {upgrade.cost} to {cost}.");
        upgrade.cost = cost;
    }

    /// <summary>Registers both upgrades; repeated registration replaces the earlier definitions.</summary>
    private void Register()
    {
        if (!PlayerStore.HasInstance) return;
        try
        {
            ModHelper.AddNetworkUpgrade(_modId, StarterId, StarterCost, Card,
                () => _text.Get("network_digital_storage_starter_title"),
                () => _text.Get("network_digital_storage_starter_desc"),
                GrantStarter, tab: Tab, iconKey: NetworkSprites.IconKey(_modId, 1),
                subtitleLabel: () => _text.Get("network_card_digital_storage"));
            ModHelper.AddNetworkUpgrade(_modId, AdvancedId, AdvancedCost, Card,
                () => _text.Get("network_digital_storage_advanced_title"),
                () => _text.Get("network_digital_storage_advanced_desc"),
                GrantAdvanced, tab: Tab, iconKey: NetworkSprites.IconKey(_modId, 2),
                prerequisites: new[] { StarterId },
                subtitleLabel: () => _text.Get("network_card_digital_storage"));
        }
        catch (Exception error)
        {
            _log.Error($"Digital Storage: the network upgrades could not be registered ({error.GetType().Name}: {error.Message}).");
        }
    }

    /// <summary>A controller, drive, terminal, a battery for the controller and an empty 1k cell.</summary>
    private void GrantStarter() => Grant("starter", () =>
    {
        StorageGoods.Grant(DirectoryMaster.Item(ControllerItem.ItemId));
        StorageGoods.Grant(DirectoryMaster.Item(DriveItem.ItemId));
        StorageGoods.Grant(DirectoryMaster.Item(TerminalItem.ItemId));
        StorageGoods.Grant(DirectoryMaster.Item(StarterBatteryId));
        StorageGoods.Grant(DirectoryMaster.Item(StorageCellTier.All[0].CellId));
    });

    /// <summary>A cell workbench, an equal distribution module, a stack display module and a common type limit module.</summary>
    private void GrantAdvanced() => Grant("advanced", () =>
    {
        StorageGoods.Grant(DirectoryMaster.Item(CellWorkbenchItem.ItemId));
        StorageGoods.Grant(_goods.Create(StorageOffer.EqualDistribution));
        StorageGoods.Grant(_goods.Create(StorageOffer.StackDisplay));
        StorageGoods.Grant(_goods.Create(StorageOffer.TypeLimit(ModuleQuality.Common)));
    });

    private void Grant(string name, Action grant)
    {
        try
        {
            grant();
        }
        catch (Exception error)
        {
            _log.Error($"Digital Storage: the {name} network reward was not fully granted ({error.GetType().Name}: {error.Message}).");
        }
    }
}
