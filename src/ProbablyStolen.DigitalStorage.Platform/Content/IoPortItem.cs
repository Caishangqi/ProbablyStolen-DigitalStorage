using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Network;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal sealed partial class IoPortItem
{
    internal const string SpriteKey = "io_port";
    internal const int Width = 3;
    internal const int Height = 3;
    internal const string NameKey = "io_port_name";
    internal const string DescriptionKey = "io_port_desc";
    private const int BankColumns = 2;
    private const int SlotsPerBank = IoPortSettings.SlotsPerBank;
    private readonly ItemAppearance _appearance;
    private readonly MachineContentWindow _windows;
    private readonly Func<GameItem, IoPortWindow> _createWindow;
    private readonly IoPortCoordinator _coordinator;
    private readonly ModText _text;

    internal IoPortItem(ItemAppearance appearance, MachineContentWindow windows, Func<GameItem, IoPortWindow> createWindow,
        IoPortCoordinator coordinator, ModText text)
    {
        (_appearance, _windows, _createWindow, _coordinator, _text) = (appearance, windows, createWindow, coordinator, text);
    }

    internal GameItem Create()
    {
        _appearance.EnsureReady(SpriteKey);
        var machine = ItemDirectory.CreateEmptyItem(ItemId);
        try
        {
            _appearance.Apply(machine, SpriteKey, Width, Height);
            machine.shortDescription = _text.Get(DescriptionKey);
            var (left, leftSlots) = CreateBank(right: false);
            var (right, rightSlots) = CreateBank(right: true);
            // The native window keeps the banks and module bay; the IO Port window borrows them while it is open.
            _windows.Attach(machine, additionalPanels: new[]
            {
                MachineHelper.Panel(_text.Get("io_bank_input"), left, IoPortWindow.InputPanelRole),
                MachineHelper.Panel(_text.Get("io_bank_output"), right, IoPortWindow.OutputPanelRole)
            });

            var modules = MachineHelper.GetModuleInv(machine) ?? throw new InvalidOperationException("IO Port module bay is missing.");
            modules.onSlotAddItemEndFunc += OnModuleChanged;
            modules.onSlotRemoveItemEndFunc += OnModuleChanged;
            _coordinator.Attach(machine, leftSlots, rightSlots);
            _createWindow(machine).Attach(machine);
            // Both the ordinary tick and compressed night simulation supply the actual item instance.
            machine.onOneSecondItemFunc += (current, _, _) => _coordinator.OnGameSecond(current);
            return machine;
        }
        catch
        {
            _coordinator.Detach(machine);
            machine.Destroy();
            throw;
        }
    }

    private (GridPixelElement Grid, GameSlotInventory[] Slots) CreateBank(bool right)
    {
        var grid = new GridPixelElement(BankColumns, SlotsPerBank / BankColumns);
        var slots = new GameSlotInventory[SlotsPerBank];
        for (var index = 0; index < slots.Length; index++)
        {
            var bay = new GameSlotInventory(1, 2) { identifier = BayPrefix + (right ? index + SlotsPerBank : index) };
            bay.mayInventoryAddItemFunc = (cell, inventory) => inventory is GameSlotInventory slot && CanInsert(cell, slot, right);
            bay.maxSlotAmountItemFunc = (_, _, _) => 1;
            bay.onSlotAddItemEndFunc += OnBankChanged;
            bay.onSlotRemoveItemEndFunc += OnBankChanged;
            slots[index] = bay;
            if (!grid.AttachPos(bay, index % BankColumns, index / BankColumns))
                throw new InvalidOperationException("IO Port cell bay could not be attached.");
        }
        return (grid, slots);
    }

    private bool CanInsert(GameItem cell, GameSlotInventory slot, bool right)
    {
        if (slot.GetParentItem() is not { identifier: ItemId } owner ||
            (slot.childItem != null && !ReferenceEquals(slot.childItem, cell)) ||
            !GeneralHelper.CanAddItemToInv(cell, owner) || !BasicCellHandler.IsCell(cell) ||
            cell.shape.width != 1 || cell.shape.height != 2) return false;

        // The output bank only takes cells the machine ejects. Native decoding restores children by unchecked inventory
        // position, independently of this admission rule.
        return !right || _coordinator.IsEjecting(owner, cell, slot);
    }

    private void OnBankChanged(GameItem _, GameInventory inventory, SlotMarker marker)
    {
        if (!SaveManager.IsDecoding && inventory is GameSlotInventory slot && slot.GetParentItem() is { identifier: ItemId } owner)
            _coordinator.Wake(owner);
    }

    private void OnModuleChanged(GameItem _, GameInventory inventory, SlotMarker marker)
    {
        if (!SaveManager.IsDecoding && inventory.GetParentItem() is { identifier: ItemId } owner)
            _coordinator.Wake(owner);
    }
}
