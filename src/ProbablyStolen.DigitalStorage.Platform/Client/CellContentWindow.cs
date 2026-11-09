using System;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts;
using ProbablyStolen.DigitalStorage.Common.Ports;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.NativeContent;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class CellContentWindow : ICellAssemblyPort
{
    internal const string SlotId = "digital_storage.component_slot";
    internal const string BayPrefix = "digital_storage.cell_bay.";
    private readonly GameItem _host;
    private readonly ItemAppearance _appearance;
    private readonly ModLog _log;
    private readonly CellAssemblyCallbacks _callbacks;
    private readonly string _housingName;
    private readonly string _housingDescription;
    private readonly string _cellName;
    private readonly string _cellDescription;
    private bool _reportedInvalid;

    internal GameSlotInventory Slot { get; }

    internal CellContentWindow(GameItem host, StorageCellDisassemblyRecipe recipe,
        ItemAppearance appearance, ModText text, ModLog log)
    {
        _host = host;
        _appearance = appearance;
        _log = log;
        _callbacks = new CellAssemblyCallbacks(recipe, this);
        _housingName = text.Get("item_cell_housing_name");
        _housingDescription = text.Get("item_cell_housing_desc");
        _cellName = text.Get("item_storage_cell_1k_name");
        _cellDescription = text.Get("item_storage_cell_1k_desc");
        Slot = new GameSlotInventory(1, 2) { identifier = SlotId };
        Slot.mayInventoryAddItemFunc = (item, _) =>
            _callbacks.CanInsert(ContentSnapshots.Item(item), ReferenceEquals(Slot.childItem, item),
                GeneralHelper.CanAddItemToInv(item, _host));
        Slot.maxSlotAmountItemFunc = (_, _, _) => 1;
        Slot.mayInventoryRemoveItemFunc = (item, _) => CanRemoveFromSlot(item);
        Slot.maxSlotRemoveItemFunc = (item, _, _) => CanRemoveFromSlot(item) ? 1 : 0;
        Slot.onSlotRemoveItemFunc = (item, _, _) =>
            _callbacks.Removing(ReferenceEquals(Slot.childItem, item));
        Slot.onSlotAddItemEndFunc = (_, _, _) =>
        {
            if (!_callbacks.Inserted())
            {
                CheckIntegrity();
            }
        };
        Slot.onSlotRemoveItemEndFunc = (_, _, _) => _callbacks.Removed();
        // Panel order is the native save order: the pages panel must stay last.
        var window = MachineHelper.CreateMachineryWindow(true,
            MachineHelper.Panel(text.Get("component_panel"), Slot, "component"),
            CellPages.CreatePanel(text.Get("storage_pages_panel")));
        host.SetContentWindow(window);
        host.onOneSecondItemFunc += (_, _, _) => CheckIntegrity();
        host.mayStackItemFunc = (_, _) => false;
        host.maxSlotAmountItemFunc = (_, _, _) => 1;
        Present(host.identifier == StorageCellDisassemblyRecipe.CellId);
    }

    CellAssemblySnapshot ICellAssemblyPort.Read() => ContentSnapshots.Cell(_host);

    void ICellAssemblyPort.FixComponent()
    {
        ModuleHelper.SetModuleStuck(Slot.childItem);
    }

    void ICellAssemblyPort.Present(bool assembled) => Present(assembled);

    internal static GameItem? Component(GameItem host)
    {
        var inventory = MachineHelper.GetBodyStorage(host, 0) as GameSlotInventory;
        return inventory?.identifier == SlotId ? inventory.childItem : null;
    }

    internal static StorageContentsState StorageState(GameItem host) =>
        CellPages.Tabs(host) == null ? StorageContentsState.Unavailable
        : CellPages.IsEmpty(host) ? StorageContentsState.Empty : StorageContentsState.NonEmpty;

    internal static bool IsMounted(GameItem host) =>
        host.parentInventory?.identifier?.StartsWith(BayPrefix, StringComparison.Ordinal) == true;

    internal static bool CanRemoveComponent(GameItem component, StorageCellDisassemblyRecipe recipe)
    {
        if (component.parentInventory?.identifier != SlotId)
        {
            // Without an assembled host there is no disassembly, so the component keeps its own rule.
            return !component.IsTag(ModuleHelper.MODULE_STUCK_TAG) && component.unitCount == 1;
        }

        var host = component.parentInventory.GetParentItem();
        if (host == null)
        {
            return false;
        }

        var snapshot = ContentSnapshots.Cell(host);
        return recipe.CanDisassemble(host.identifier, host.unitCount,
            component.identifier, component.unitCount, component.IsTag(ModuleHelper.MODULE_STUCK_TAG),
            snapshot.Mounted, storageEmpty: !snapshot.Restoring && snapshot.StorageState == StorageContentsState.Empty);
    }

    private bool CanRemoveFromSlot(GameItem component) =>
        ReferenceEquals(Slot.childItem, component) && _callbacks.CanRemove();

    private void Present(bool assembled)
    {
        _host.identifier = assembled ? StorageCellDisassemblyRecipe.CellId : StorageCellDisassemblyRecipe.HousingId;
        _host.name = assembled ? _cellName : _housingName;
        _host.shortDescription = assembled ? _cellDescription : _housingDescription;
        _appearance.SetPreparedSprite(_host, assembled ? "item_storage_cell_1k" : "item_cell_housing");
    }

    private void CheckIntegrity()
    {
        var valid = _callbacks.IsConsistent();
        if (!valid && !_reportedInvalid)
        {
            _log.Error("Digital Storage: inconsistent cell assembly retained. Mounting and disassembly are unavailable.");
        }

        _reportedInvalid = !valid;
    }
}
