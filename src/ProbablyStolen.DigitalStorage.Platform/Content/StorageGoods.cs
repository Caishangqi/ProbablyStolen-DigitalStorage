using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;
using ProbablyStolen.DigitalStorage.Common.Domain.Commerce;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>Creates the native items behind storage offers and places them through the game's own paths.</summary>
internal sealed class StorageGoods
{
    private readonly CellModuleFactory _modules;

    internal StorageGoods(CellModuleFactory modules)
    {
        _modules = modules ?? throw new ArgumentNullException(nameof(modules));
    }

    /// <summary>Puts one offer on the front counter for the current customer to sell; false when it is not placed.</summary>
    internal bool OfferOnCounter(StorageOffer offer)
    {
        var counter = EmporiumEntry.Instance != null ? EmporiumEntry.Instance.frontInvinvElement : null;
        // Checked first so a full counter does not cost a freshly rolled module.
        if (counter != null && !counter.CanTakeAnything()) return false;
        var item = Create(offer);
        return item != null && PlayerStore.Instance.TryAddDirectSellingItemToTable(item);
    }

    /// <summary>
    /// Gives the player an owned item through the Wild network reward path, which the vanilla upgrades also use:
    /// the back room first, then the other store inventories. The game destroys an item no inventory can hold.
    /// </summary>
    internal static void Grant(GameItem? item)
    {
        if (item == null) return;
        ModHelper.SetItemOwnerShip(item, true);
        GraphUtils.EmporiumTryAdd(EmporiumEntry.Instance.backInvinvElement, item);
    }

    /// <summary>Creates the item for <paramref name="offer"/>; null when the item is not registered.</summary>
    internal GameItem? Create(StorageOffer offer)
    {
        if (offer == null) throw new ArgumentNullException(nameof(offer));
        return offer.Good switch
        {
            StorageGood.Housing => DirectoryMaster.Item(MaterialItem.ItemId),
            StorageGood.Component => DirectoryMaster.Item(offer.Tier!.ComponentId),
            StorageGood.EqualDistributionModule => DirectoryMaster.Item(CellUpgradeInventory.EqualDistributionId),
            StorageGood.StackDisplayModule => DirectoryMaster.Item(StackingModule.ModuleId),
            StorageGood.TypeLimitModule => CreateTypeLimit(offer.Quality),
            StorageGood.IoPort => DirectoryMaster.Item(IoPortItem.ItemId),
            _ => throw new ArgumentOutOfRangeException(nameof(offer), offer.Good, "Unknown storage good.")
        };
    }

    private GameItem? CreateTypeLimit(ModuleQuality quality)
    {
        var module = DirectoryMaster.Item(CellUpgradeInventory.TypeLimitId);
        if (module == null) return null;
        try
        {
            _modules.Roll(module, TypeLimitRolls.Roll(quality, NativeRandom.Instance));
            return module;
        }
        catch
        {
            module.Destroy();
            throw;
        }
    }
}
