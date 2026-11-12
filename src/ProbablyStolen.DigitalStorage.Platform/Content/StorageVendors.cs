using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;
using ProbablyStolen.DigitalStorage.Platform.Storage;
using ProbablyStolen.DigitalStorage.Platform.Localization;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>
/// Puts the mod's items on sale through the game's own vendor paths:
/// Dr. Jackson's visiting stock (<c>ModHook.OnPlaceInventorInventoryItemLate</c>, filled with
/// <c>PlayerStore.AddDirectSellingItemToTable</c> like his vanilla goods), and a mod customer, the upper-level engineer,
/// queued from <c>ModHook.OnGenerateCustomerLate</c>, which runs once per day while the day's customers are generated.
/// Vanilla has no upper-level engineer; mods cannot add sprites or factions, so it reuses the engineer sprites and the
/// upper-level faction.
/// </summary>
internal sealed class StorageVendors : IDisposable
{
    internal const string EngineerId = "digital_storage.upper_engineer";
    // Dr. Jackson always stocks a machine the player does not own yet and a quarter of the time one already owned;
    // the workbench and the equal distribution module follow the same rule.
    private static readonly string[] Machines =
    {
        ControllerItem.ItemId, DriveItem.ItemId, TerminalItem.ItemId, CellWorkbenchItem.ItemId,
        CellUpgradeInventory.EqualDistributionId
    };
    private readonly ModText _text;
    private readonly Func<int> _engineerDailyChance;
    private readonly ModLog _log;
    private readonly CellModuleFactory _modules;
    private bool _enabled;

    internal StorageVendors(ModText text, Func<int> engineerDailyChance, ModLog log, CellModuleFactory modules)
    {
        _modules = modules ?? throw new ArgumentNullException(nameof(modules));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _engineerDailyChance = engineerDailyChance ?? throw new ArgumentNullException(nameof(engineerDailyChance));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    internal void Enable()
    {
        if (_enabled) return;
        _enabled = true;
        ModHook.OnPlaceInventorInventoryItemLate += AddInventorStock;
        ModHook.OnGenerateCustomerLate += QueueEngineer;
    }

    public void Dispose()
    {
        if (!_enabled) return;
        _enabled = false;
        ModHook.OnPlaceInventorInventoryItemLate -= AddInventorStock;
        ModHook.OnGenerateCustomerLate -= QueueEngineer;
    }

    /// <summary>
    /// Dr. Jackson brings each machine while the player owns none, and otherwise 25% of the time, as he does with his
    /// storage bay; a housing 40% and a storage component 10% of the time.
    /// </summary>
    private void AddInventorStock(List<GameItem> ownedItems)
    {
        try
        {
            var owned = new HashSet<string>(ownedItems.Where(item => item != null).Select(item => item.identifier));
            foreach (var machine in Machines)
            {
                if (!owned.Contains(machine) || RNG.Roll(25)) PlayerStore.Instance.AddDirectSellingItemToTable(machine);
            }

            if (RNG.Roll(40)) PlayerStore.Instance.AddDirectSellingItemToTable(MaterialItem.ItemId);
            if (RNG.Roll(10)) PlayerStore.Instance.AddDirectSellingItemToTable(DrawTier().ComponentId);
            // Dr. Jackson only sells common type limit modules; rarer ones come from the upper-level engineer.
            if (RNG.Roll(25)) OfferTypeLimitModule(ModuleQuality.Common);
        }
        catch (Exception error)
        {
            _log.Error($"Digital Storage: Dr. Jackson stock was not added: {error.GetType().Name}");
        }
    }

    private void QueueEngineer(StoreClientManager manager)
    {
        try
        {
            var chance = Math.Max(0, Math.Min(100, _engineerDailyChance()));
            if (manager != null && RNG.Roll(chance)) manager.AddClient(CreateEngineer());
        }
        catch (Exception error)
        {
            _log.Error($"Digital Storage: upper-level engineer was not queued: {error.GetType().Name}");
        }
    }

    /// <summary>
    /// Sells one to three storage goods (30% component, 25% storage cell, 10% housing, 10% workbench, 7% equal
    /// distribution module, 18% type limit module of a weighted quality) and buys the mod's housings, components,
    /// modules and machines. Assembled cells are never bought: their value includes the items stored inside.
    /// </summary>
    private StoreClient CreateEngineer()
    {
        var client = new StoreClient
        {
            identifier = EngineerId,
            displayName = _text.Get("upper_engineer_name"),
            spriteName = SpriteDict.GetRandomEngSpriteName(),
            clientIntent = StoreClient.ClientIntent.SELLNBUY
        };
        StoreClientFactionSetup.InitUpperLevelCitizen(client);
        client.SetClientBudget(800 + StoreStation.GetDayCounter() * 5, 400);
        foreach (var id in new[] { MaterialItem.ItemId, CellUpgradeInventory.TypeLimitId }
                     .Concat(StorageCellTier.All.Select(tier => tier.ComponentId)).Concat(Machines))
        {
            client.clientBuyingIdList.Add(id);
        }

        var name = client.displayName;
        client.mainDialogue.SetText(name, _text.Get("upper_engineer_greet_1")).NextDialogue()
            .SetText(name, _text.Get("upper_engineer_greet_2"))
            .SetEndAction(PlaceEngineerStock);
        client.placedWrongItemWhenSellingToDialogue = new Dialogue().SetText(name, _text.Get("upper_engineer_wrong_item"));
        client.placeRightItemWhenSellingToDialogue = new Dialogue().SetText(name, _text.Get("upper_engineer_right_item"));
        client.repeatDialogue = new Dialogue().SetText(name, _text.Get("upper_engineer_repeat"));
        client.acceptDealDialogue = new Dialogue().SetText(name, _text.Get("upper_engineer_accept"));
        client.AddBasicDialog();
        client.CompleteClientCreation();
        return client;
    }

    private void PlaceEngineerStock()
    {
        var count = RNG.GetRandomInt(1, 3);
        for (var index = 0; index < count; index++)
        {
            var roll = RNG.GetRandomInt(1, 100);
            if (roll <= 30) PlayerStore.Instance.AddDirectSellingItemToTable(DrawTier().ComponentId);
            else if (roll <= 55) PlayerStore.Instance.AddDirectSellingItemToTable(DrawTier().CellId);
            else if (roll <= 65) PlayerStore.Instance.AddDirectSellingItemToTable(MaterialItem.ItemId);
            else if (roll <= 75) PlayerStore.Instance.AddDirectSellingItemToTable(CellWorkbenchItem.ItemId);
            else if (roll <= 82) PlayerStore.Instance.AddDirectSellingItemToTable(CellUpgradeInventory.EqualDistributionId);
            else OfferTypeLimitModule(TypeLimitRolls.RollQuality(NativeRandom.Instance));
        }
    }

    /// <summary>Puts a freshly rolled type limit module on the counter, or creates nothing when the counter is full.</summary>
    private void OfferTypeLimitModule(ModuleQuality quality)
    {
        var counter = EmporiumEntry.Instance?.frontInvinvElement;
        if (counter != null && !counter.CanTakeAnything()) return;
        var module = DirectoryMaster.Item(CellUpgradeInventory.TypeLimitId);
        if (module == null) return;
        try
        {
            _modules.Roll(module, TypeLimitRolls.Roll(quality, NativeRandom.Instance));
            if (!PlayerStore.Instance.TryAddDirectSellingItemToTable(module)) module.Destroy();
        }
        catch
        {
            if (module.parentInventory == null) module.Destroy();
            throw;
        }
    }

    private static StorageCellTier DrawTier() => StorageCellTier.FromMerchantRoll(RNG.GetRandomInt(1, 100));
}
