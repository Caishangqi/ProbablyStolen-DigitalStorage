using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
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

    internal GameItem Create()
    {
        _appearance.EnsureReady(SpriteKey);
        if (!_component.IsStorageComponent(ItemId))
        {
            throw new InvalidOperationException("Storage component identity does not match its definition.");
        }

        var item = ItemDirectory.CreateEmptyItem(ItemId);
        try
        {
            _appearance.Apply(item, SpriteKey, Width, Height);
            NativeItemContentInitialization.Initialize(item, _contentSynchronization);
            // MATERIAL lets engineers and Dr. Jackson buy it. ModuleHelper.InitModuleItem below also makes it a MODULE
            // (ModuleHelper.cs:76-77), as before this change.
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
