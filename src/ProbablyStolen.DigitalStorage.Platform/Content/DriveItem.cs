using System;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Application.Events;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Ports;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Events;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.NativeContent;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal sealed class DriveItem
{
    internal const string ItemId = "digital_storage.storage_drive";
    internal const string SpriteKey = "drive";
    internal const int Width = 3;
    internal const int Height = 3;
    internal const string NameKey = "drive_name";
    internal const string DescriptionKey = "drive_desc";
    private readonly ItemAppearance _appearance;
    private readonly MachineContentWindow _windows;
    private readonly DriveCellInventory _inventory;
    private readonly NativeNetworkEvents _events;
    private readonly Func<DriveWindow> _createWindow;
    private readonly ModText _text;

    internal DriveItem(ItemAppearance appearance, MachineContentWindow windows, DriveCellInventory inventory,
        NativeNetworkEvents events, ModText text, Func<DriveWindow> createWindow)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _events = events;
        _createWindow = createWindow ?? throw new ArgumentNullException(nameof(createWindow));
        _appearance = appearance;
        _windows = windows;
        _inventory = inventory;
    }

    internal int GetCellCount() => _inventory.Size();

    internal GameItem Create()
    {
        _appearance.EnsureReady(SpriteKey);
        var item = ItemDirectory.CreateEmptyItem(ItemId);
        try
        {
            _appearance.Apply(item, SpriteKey, Width, Height);
            var bays = new GridPixelElement(7, 2);
            ForwardFillGrids.Register(bays);
            var slots = new GameSlotInventory[GetCellCount()];
            var ports = new ICellBayPort[GetCellCount()];
            for (var index = 0; index < slots.Length; index++)
            {
                var bay = new GameSlotInventory(1, 2)
                {
                    identifier = CellContentWindow.BayPrefix + index
                };
                slots[index] = bay;
                ports[index] = new NativeCellBay(bay);
                bays.AttachPos(bay, index % 7, index / 7);
            }

            var binding = new DriveBayBinding(_inventory, ports);
            for (var index = 0; index < slots.Length; index++)
            {
                var bayIndex = index;
                var bay = slots[index];
                // The borrowed panel can still receive a native drop while its owner is being destroyed.
                bay.mayInventoryAddItemFunc = (cell, _) => DriveWindow.HasLiveOwner(item) &&
                    ReferenceEquals(GraphUtils.FindParentType<GameItem>(bay), item) &&
                    binding.CanInsert(bayIndex, ContentSnapshots.Cell(cell), ReferenceEquals(bay.childItem, cell),
                        GeneralHelper.CanAddItemToInv(cell, item));
                bay.maxSlotAmountItemFunc = (_, _, _) => _inventory.GetSlotLimit(bayIndex);
                _events.AttachSlot(bay, NetworkChangeReason.CellInserted, NetworkChangeReason.CellRemoved);
            }

            // The native window keeps the cell bays and module bay; the drive window borrows them while it is open.
            _windows.Attach(item, bays, additionalPanels: new[] { DrivePriorityPanel.Create(item, _text).Panel });
            _createWindow().Attach(item);
            return item;
        }
        catch
        {
            item.Destroy();
            throw;
        }
    }

    internal static ItemKey? CaptureDraggedIdentity()
    {
        var drag = ItemMouseDragHandler.current;
        if (drag?.IsDraggingItem != true || drag.GetCurrentItem() is not GameItem current) return null;
        var key = StorageService.Identify(current);
        return key?.IsCanonical == true ? key : null;
    }

    private sealed class NativeCellBay : ICellBayPort
    {
        private readonly GameSlotInventory _slot;

        internal NativeCellBay(GameSlotInventory slot)
        {
            _slot = slot;
        }

        public CellAssemblySnapshot? Read() =>
            _slot.childItem == null ? null : ContentSnapshots.Cell(_slot.childItem);
    }
}
