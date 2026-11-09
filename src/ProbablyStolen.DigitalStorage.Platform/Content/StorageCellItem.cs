using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.NativeContent;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal sealed class StorageCellItem
{
    internal const string ItemId = "digital_storage.item_storage_cell_1k";
    internal const string SpriteKey = "item_storage_cell_1k";
    internal const int Width = 1;
    internal const int Height = 2;
    internal const string NameKey = "item_storage_cell_1k_name";
    internal const string DescriptionKey = "item_storage_cell_1k_desc";
    private readonly ModText _text;
    private readonly ItemAppearance _appearance;
    private readonly StorageComponentFactory _component;
    private readonly StorageCellDisassemblyRecipe _recipe;
    private readonly ModLog _log;

    internal StorageCellItem(ModText text, ItemAppearance appearance, StorageComponentFactory component,
        StorageCellDisassemblyRecipe recipe, ModLog log)
    {
        _text = text;
        _appearance = appearance;
        _component = component;
        _recipe = recipe;
        _log = log;
    }

    internal GameItem Create() => CreateHost(true);

    internal GameItem CreateHost(bool assembled)
    {
        _appearance.EnsureReady(SpriteKey);
        _appearance.EnsureReady("item_cell_housing");
        var item = ItemDirectory.CreateEmptyItem(assembled ? ItemId : StorageCellDisassemblyRecipe.HousingId);
        try
        {
            _appearance.Apply(item, assembled ? SpriteKey : "item_cell_housing", Width, Height);
            var content = new CellContentWindow(item, _recipe, _appearance, _text, _log);
            var initializeSample = assembled && !SaveManager.IsDecoding;
            item.onSlotAddItemFunc += (_, _, _) =>
            {
                if (!initializeSample || SaveManager.IsDecoding)
                {
                    return;
                }

                // DirectoryMaster removes factory children. Seed only a fresh sample after placement.
                initializeSample = false;
                if (content.Slot.childItem != null)
                {
                    return;
                }

                GameItem? component = null;
                try
                {
                    component = _component.Create();
                    GeneralHelper.SetItemOwned(component, GeneralHelper.IsItemOwned(item));
                    if (!content.Slot.UncheckedAccept(component))
                    {
                        throw new InvalidOperationException("The new storage cell could not receive its component.");
                    }
                }
                catch (Exception error)
                {
                    // Only a newly allocated default component can be released here.
                    if (component != null && component.parentInventory == null)
                    {
                        component.Destroy();
                    }

                    _log.Error($"Digital Storage: new cell sample initialization failed ({error.GetType().Name}). The host is retained.");
                }
            };

            return item;
        }
        catch
        {
            item.Destroy();
            throw;
        }
    }
}
