using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal sealed class StorageComponentFactory
{
    internal const string ItemId = "digital_storage.cell_component_1k";
    internal const string SpriteKey = "cell_component_1k";
    internal const int Width = 1;
    internal const int Height = 2;
    internal const string NameKey = "cell_component_1k_name";
    internal const string DescriptionKey = "cell_component_1k_desc";
    private readonly ItemAppearance _appearance;
    private readonly StorageComponent _component;
    private readonly StorageCellDisassemblyRecipe _recipe;
    private readonly ItemContentSynchronization _contentSynchronization;

    internal StorageComponentFactory(ItemAppearance appearance,
        StorageComponent component, StorageCellDisassemblyRecipe recipe, ItemContentSynchronization contentSynchronization)
    {
        _appearance = appearance;
        _component = component;
        _recipe = recipe;
        _contentSynchronization = contentSynchronization;
    }

    internal GameItem Create(StorageCellTier tier)
    {
        var spriteKey = "cell_component_" + tier.Name;
        _appearance.EnsureReady(spriteKey);
        if (!_component.IsStorageComponent(tier.ComponentId))
        {
            throw new InvalidOperationException("Storage component identity does not match its definition.");
        }

        var item = ItemDirectory.CreateEmptyItem(tier.ComponentId);
        try
        {
            _appearance.Apply(item, spriteKey, Width, Height);
            NativeItemContentInitialization.Initialize(item, _contentSynchronization);
            // MATERIAL lets engineers and Dr. Jackson buy it. ModuleHelper.InitModuleItem below also makes it a MODULE
            // as well.
            item.SetGameItemType(ItemPrices.MaterialType);
            ModuleHelper.InitModuleItem(item, "digital_storage.component_module", isShipModule: false);
            item.mayStackItemFunc = (_, _) => false;
            item.maxSlotAmountItemFunc = (_, _, _) => 1;
            item.mayInventoryRemoveItemFunc = (component, _) =>
                CellContentWindow.CanRemoveComponent(component, _recipe);
            item.maxSlotRemoveItemFunc = (component, _, _) =>
                CellContentWindow.CanRemoveComponent(component, _recipe) ? 1 : 0;
            return item;
        }
        catch
        {
            item.Destroy();
            throw;
        }
    }
}
