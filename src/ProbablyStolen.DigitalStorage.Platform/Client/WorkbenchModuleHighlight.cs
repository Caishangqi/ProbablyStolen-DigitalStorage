using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Routes the native mouse highlight probe without changing actual removal checks.</summary>
public static class WorkbenchModuleHighlight
{
    /// <summary>Skips the removal probe for a passive highlight, whose color does not depend on removability.</summary>
    public static bool MayRemove(GameItem item, GameItem? pressed)
    {
        if (!ReferenceEquals(item, pressed) && item.identifier == CellUpgradeInventory.TypeLimitId &&
            CellUpgradeInventory.FindModuleBay(item) is { } location &&
            location.Cell.parentInventory is GameSlotInventory { identifier: CellUpgradeInventory.WorkbenchSlotId } slot &&
            ReferenceEquals(slot.childItem, location.Cell) && location.Bay.childItems.Contains(item)) return false;
        return item.MayRemove();
    }
}
