using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Registries;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>Accesses the cell-owned module bay and applies cell health guards.</summary>
internal sealed class CellModuleBayProvider : IModuleBayProvider
{
    public Identifier Id => Identifier.Parse("digital_storage:cell");
    public bool TryGetInventory(GameItem item, out GameGridInventory inventory)
    {
        inventory = item == null ? null! : CellUpgradeInventory.Bay(item)!;
        return item != null && inventory != null && (StorageCellTier.FromCellId(item.identifier) != null ||
            item.identifier == MaterialItem.ItemId);
    }
    public bool IsAvailable(GameItem item, out string? refusalReason)
    {
        var available = TryGetInventory(item, out _) && !CellHealth.IsUnavailable(item) && CellPages.IsComplete(item);
        refusalReason = available ? null : "storage cell health or module bay is unavailable";
        return available;
    }
}
