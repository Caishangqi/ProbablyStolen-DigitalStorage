using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>
/// Loading replaces factory item types and tags with saved values. Restore MACHINE and MATERIAL classifications so
/// older controllers cannot nest in containers and older housings remain sellable. Assembled cells lose MATERIAL;
/// the native save records these changes.
/// </summary>
internal sealed class ItemTypeBackfill
{
    private readonly ModLog _log;

    internal ItemTypeBackfill(ModLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Runs once a save has loaded, over every item reachable from the store's inventories.</summary>
    internal void Run()
    {
        var updated = 0;
        foreach (var item in EmporiumEntry.Instance.GetAllItems())
        {
            if (item != null && Apply(item)) updated++;
        }

        if (updated > 0) _log.Log($"Digital Storage: item types added to {updated} item(s) from an older save.");
    }

    private static bool Apply(GameItem item)
    {
        if (StorageCellTier.FromComponentId(item.identifier) != null)
            return Ensure(item, ItemPrices.MaterialType, present: true);
        if (StorageCellTier.FromCellId(item.identifier) != null)
            return Ensure(item, ItemPrices.MaterialType, present: false);
        switch (item.identifier)
        {
            case ControllerItem.ItemId:
            case DriveItem.ItemId:
            case TerminalItem.ItemId:
                return Ensure(item, "MACHINE", present: true);
            case StorageCellDisassemblyRecipe.HousingId:
                return Ensure(item, ItemPrices.MaterialType, present: true);
            default:
                return false;
        }
    }

    /// <summary>Keeps the type list and its tag in step, as SetGameItemType/RemoveGameItemType do.</summary>
    private static bool Ensure(GameItem item, string type, bool present)
    {
        if (present == (item.IsGameItemType(type) && item.IsTag(type))) return false;
        if (present) item.SetGameItemType(type);
        else item.RemoveGameItemType(type);
        return true;
    }
}
