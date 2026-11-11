using System;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Ports;
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
    private readonly ItemContentSynchronization _contentSynchronization;
    private readonly CellAssemblyCallbacks _callbacks;
    private bool _reportedInvalid;

    internal GameSlotInventory Slot { get; }

    internal CellContentWindow(GameItem host, StorageCellDisassemblyRecipe recipe,
        ItemAppearance appearance, ModText text, ModLog log, Func<bool> showPages,
        ItemContentSynchronization contentSynchronization)
    {
        _host = host;
        _appearance = appearance;
        _log = log;
        _contentSynchronization = contentSynchronization;
        _callbacks = new CellAssemblyCallbacks(recipe, this);
        Slot = new GameSlotInventory(1, 2) { identifier = SlotId };
        Slot.mayInventoryAddItemFunc = (item, _) =>
            !CellPages.SwapOf(_host).Pending && ShellReady() && _callbacks.CanInsert(ContentSnapshots.Item(item), ReferenceEquals(Slot.childItem, item),
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
        var pages = CellPages.CreatePanel(text.Get("storage_pages_panel"));
        var component = MachineHelper.Panel(text.Get("component_panel"), Slot, "component");
        var window = MachineHelper.CreateMachineryWindow(true, component, pages);
        host.SetContentWindow(window);
        // Players reach stored items through the terminal; the pages show only with the debug setting. Hiding keeps
        // the panel, so the saved inventory order is unchanged.
        void SyncPages()
        {
            var hide = !showPages();
            if (pages.isHidden == hide) return;
            // The row stretches every panel to the tallest one and MachineryPanel/MachineryIntake keep that size as
            // their requested size, so the component panel would stay as tall as the hidden pages.
            MachineryContainerUtils.TryResize(component.content, 0, 0);
            component.ResizePixels(0, 0);
            pages.SetHidden(hide);
        }

        window.openCallback += SyncPages;
        // A cell sold by a customer is seeded with an unowned component, and buying it marks only the cell as owned
        // (GeneralHelper.SetItemOwned does not touch children). The component must be owned to be taken out.
        window.openCallback += () =>
        {
            var part = Slot.childItem;
            if (part != null && GeneralHelper.IsItemOwned(host) && !GeneralHelper.IsItemOwned(part))
                GeneralHelper.SetItemOwned(part);
        };
        window.openCallback += RetryReset;
        window.openCallback += SyncTitle;
        SyncPages();
        host.onOneSecondItemFunc += (_, _, _) => CheckIntegrity();
        host.mayStackItemFunc = (_, _) => false;
        host.maxSlotAmountItemFunc = (_, _, _) => 1;
        var initialTier = StorageCellTier.FromCellId(host.identifier);
        if (initialTier != null || host.identifier == StorageCellDisassemblyRecipe.HousingId)
            PresentInitial(initialTier);
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
        !CellPages.IsComplete(host) || CellHealth.IsUnavailable(host) ? StorageContentsState.Unavailable
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
        var tier = assembled ? StorageCellTier.FromComponentId(Slot.childItem?.identifier) : null;
        if (assembled && tier == null) throw new InvalidOperationException("Unknown storage component tier.");
        _host.identifier = assembled ? tier!.CellId : StorageCellDisassemblyRecipe.HousingId;
        PresentInitial(tier);
        if (!assembled)
        {
            CellPages.SwapOf(_host).Request();
            RetryReset();
        }
    }

    private void PresentInitial(StorageCellTier? tier)
    {
        var assembled = tier != null;
        _contentSynchronization.Synchronize(new NativeItemContentState(_host));
        // A housing is a material customers buy. An assembled cell is not: its value includes the stored items, so
        // selling it to a customer would sell its contents. Item types and their tags are both saved.
        if (assembled) _host.RemoveGameItemType(ItemPrices.MaterialType);
        else _host.SetGameItemType(ItemPrices.MaterialType);
        _appearance.SetPreparedSprite(_host, assembled ? "item_storage_cell_" + tier!.Name : "item_cell_housing");
        SyncTitle();
    }

    private bool ShellReady() => _host.identifier != StorageCellDisassemblyRecipe.HousingId ||
        CellPages.IsAtAnchor(_host);

    private void RetryReset()
    {
        if (SaveManager.IsDecoding || _host.identifier != StorageCellDisassemblyRecipe.HousingId || Slot.childItem != null)
            return;
        if (!CellPages.SwapOf(_host).Pending && ShellReady()) return;
        try
        {
            CellPages.ResetToAnchor(_host);
        }
        catch (Exception error)
        {
            _log.Error($"Digital Storage: empty cell reset failed ({error.GetType().Name}); reassembly is blocked.");
        }
    }

    private void SyncTitle()
    {
        var window = _host.contentWindow;
        window.SetTitle(_host.GetDisplayName(preferCustomName: true));
        MachineryTitleFit.Fit(window);
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
