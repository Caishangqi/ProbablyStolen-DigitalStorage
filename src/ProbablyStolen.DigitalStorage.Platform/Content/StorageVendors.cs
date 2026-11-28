using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Content;
using ProbablyStolen.DigitalStorage.Api.Registries;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;
using ProbablyStolen.DigitalStorage.Common.Domain.Commerce;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;
using ProbablyStolen.DigitalStorage.Platform.Storage;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>
/// The data scientist sells storage equipment and buys standalone storage components. After the starter upgrade,
/// she visits on the retired farmer's day. Dr. Jackson stocks storage equipment on his regular visits.
/// </summary>
internal sealed class StorageVendors : IDisposable
{
    internal static readonly Identifier ScientistId = Identifier.Parse("digital_storage:data_scientist");
    internal const string ScientistPortrait = "digital_storage:digital_scientist";
    // Dr. Jackson always stocks a machine the player does not own yet and a quarter of the time one already owned;
    // the workbench, the equal distribution module and the stack display module follow the same rule.
    private static readonly string[] Machines =
    {
        ControllerItem.ItemId, DriveItem.ItemId, TerminalItem.ItemId, CellWorkbenchItem.ItemId,
        CellUpgradeInventory.EqualDistributionId, StackingModule.ModuleId
    };
    private readonly ModText _text;
    private readonly CategoryLog _log;
    private readonly StorageGoods _goods;
    private readonly NativeCustomerCatalog _customers;
    private bool _enabled;

    internal StorageVendors(ModText text, CategoryLog log, StorageGoods goods, NativeCustomerCatalog customers)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _goods = goods ?? throw new ArgumentNullException(nameof(goods));
        _customers = customers ?? throw new ArgumentNullException(nameof(customers));
    }

    internal void Enable()
    {
        if (_enabled) return;
        _enabled = true;
        ModHook.OnPlaceInventorInventoryItemLate += AddInventorStock;
        ModHook.OnGenerateCustomerLate += QueueScientist;
    }

    public void Dispose()
    {
        if (!_enabled) return;
        _enabled = false;
        ModHook.OnPlaceInventorInventoryItemLate -= AddInventorStock;
        ModHook.OnGenerateCustomerLate -= QueueScientist;
    }

    /// <summary>
    /// Dr. Jackson brings each machine while the player owns none, and otherwise 25% of the time, as he does with his
    /// storage bay. A housing appears 40% of the time and a component 10% of the time. Advanced upgrades raise the
    /// component ceiling, while the third adds guaranteed 256k and IO Port stock.
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
            // Dr. Jackson only sells common type limit modules; rarer ones come from the data scientist.
            if (RNG.Roll(25)) _goods.OfferOnCounter(StorageOffer.TypeLimit(ModuleQuality.Common));
            if (DigitalStorageNetwork.IsAdvancedUnlocked())
                Offer("Dr. Jackson", DigitalStorageOffers.JacksonGuaranteedStock(StoreStation.GetDayCounter()));
            if (DigitalStorageNetwork.IsExpertUnlocked())
                Offer("Dr. Jackson", DigitalStorageOffers.ExpertStock());
        }
        catch (Exception error)
        {
            _log.Error("vendor_stock_failed", $"Dr. Jackson stock was not added: {error.GetType().Name}");
        }
    }

    private void QueueScientist(StoreClientManager manager)
    {
        try
        {
            // StoreClientManager.GenerateClient runs once per day, so the visit is queued at most once a day.
            if (manager == null || !DigitalStorageOffers.ScientistVisits(StoreStation.GetDayCounter()) ||
                !DigitalStorageNetwork.IsStarterUnlocked()) return;
            if (_customers.TryCreate(ScientistId, out var scientist)) manager.AddClient(scientist);
            else _log.Error("vendor_unavailable", "the data scientist is unavailable; the visit is skipped.");
        }
        catch (Exception error)
        {
            _log.Error("vendor_queue_failed", $"the data scientist was not queued: {error.GetType().Name}");
        }
    }

    /// <summary>Registers the data scientist in the customer registry; the registry sets her portrait.</summary>
    internal void RegisterCustomers(RegistryRegistration registries)
    {
        var customers = DeferredRegister<CustomerDefinition>.Create(registries, DigitalStorageRegistries.Customer, ScientistId.Namespace);
        customers.Register(ScientistId.Path, () => new CustomerDefinition(CreateScientist, ScientistPortrait));
    }

    private StoreClient CreateScientist()
    {
        var client = new StoreClient
        {
            identifier = CustomerIdentity.ToNativeId(ScientistId),
            nonShootable = true,
            displayName = _text.Get("data_scientist_name"),
            clientIntent = StoreClient.ClientIntent.SELLNBUY
        };
        StoreClientFactionSetup.InitUpperLevelCitizen(client);
        client.SetBudget(DigitalStorageBuybackTerms.VisitBudget, 0);
        client.OverrideBudget(DigitalStorageBuybackTerms.VisitBudget);
        var remainingBudget = DigitalStorageBuybackTerms.VisitBudget;
        client.willAcceptItem = item => DigitalStorageBuybackTerms.CanAccept(
            item.identifier, item.GetNegociatedValue(), remainingBudget, client.GetBudget());
        client.onItemBought += (item, payout) =>
        {
            if (!GeneralHelper.IsItemOwned(item)) return;
            remainingBudget = DigitalStorageBuybackTerms.RemainingAfterSale(remainingBudget, payout);
        };
        client.onItemAddedToCounters += item =>
        {
            if (GeneralHelper.IsItemOwned(item) && DigitalStorageBuybackTerms.Accepts(item.identifier))
                item.AddItemFeature(ItemFeatureList.DiscountSell50());
        };
        var name = client.displayName;
        client.mainDialogue.SetText(name, _text.Get("data_scientist_greet_1")).NextDialogue()
            .SetText(name, _text.Get("data_scientist_greet_2"))
            .SetEndAction(PlaceScientistStock);
        client.AddBasicDialog();
        client.CompleteClientCreation();
        return client;
    }

    private void PlaceScientistStock()
    {
        try
        {
            var offers = DigitalStorageOffers.ScientistStock(StoreStation.GetDayCounter(),
                DigitalStorageNetwork.IsAdvancedUnlocked()).ToList();
            if (DigitalStorageNetwork.IsExpertUnlocked())
                offers.AddRange(DigitalStorageOffers.ExpertStock());
            Offer("the data scientist", offers);
        }
        catch (Exception error)
        {
            _log.Error("vendor_placement_failed", $"the data scientist stock was not placed: {error.GetType().Name}");
        }
    }

    private void Offer(string vendor, IEnumerable<StorageOffer> offers)
    {
        var skipped = offers.Where(offer => !_goods.OfferOnCounter(offer)).Select(offer => offer.ToString()).ToList();
        if (skipped.Count > 0)
            _log.Info("vendor_counter_full", $"{vendor} could not place {string.Join(", ", skipped)}; the counter is full.");
    }

    private static StorageCellTier DrawTier()
    {
        var maximumMultiplier = DigitalStorageNetwork.IsExpertUnlocked() ? 256 :
            DigitalStorageNetwork.IsAdvancedUnlocked() ? 64 : 4;
        var eligible = StorageCellTier.All.Where(tier => tier.Multiplier <= maximumMultiplier).ToArray();
        var totalWeight = eligible.Sum(tier => tier.MerchantWeight);
        return StorageCellTier.FromMerchantRoll(RNG.GetRandomInt(1, totalWeight), maximumMultiplier);
    }
}
