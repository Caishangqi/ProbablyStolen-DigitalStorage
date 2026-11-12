using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>
/// The cell workbench: a 3x3 item with one cell slot and a button that opens the partition editor. It needs no
/// network or power. A cell in its slot is outside every network, and its modules are shown and changed only here.
/// </summary>
internal sealed class CellWorkbenchItem
{
    internal const string ItemId = "digital_storage.cell_workbench";
    internal const string SpriteKey = "workbench";
    internal const int Width = 3;
    internal const int Height = 3;
    internal const string NameKey = "workbench_name";
    internal const string DescriptionKey = "workbench_desc";
    private readonly ModText _text;
    private readonly ItemAppearance _appearance;
    private readonly ItemContentSynchronization _contentSynchronization;
    private readonly Action<GameItem> _openPartition;
    private readonly Action<GameItem> _cellLeft;

    /// <summary>Creates the factory.</summary>
    /// <param name="text">Localized texts.</param>
    /// <param name="appearance">Sprite loading and application.</param>
    /// <param name="contentSynchronization">Name, description and value synchronization.</param>
    /// <param name="openPartition">Opens the partition editor for the cell in the slot.</param>
    /// <param name="cellLeft">Ends any editing session of a cell that leaves the slot.</param>
    internal CellWorkbenchItem(ModText text, ItemAppearance appearance, ItemContentSynchronization contentSynchronization,
        Action<GameItem> openPartition, Action<GameItem> cellLeft)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        _contentSynchronization = contentSynchronization ?? throw new ArgumentNullException(nameof(contentSynchronization));
        _openPartition = openPartition ?? throw new ArgumentNullException(nameof(openPartition));
        _cellLeft = cellLeft ?? throw new ArgumentNullException(nameof(cellLeft));
    }

    /// <summary>Gets why a cell may not enter the slot, or null when it may.</summary>
    internal static string? Refusal(GameItem candidate)
    {
        if (StorageCellTier.FromCellId(candidate.identifier) == null) return "workbench_reject_not_cell";
        var schema = CellSchema.Classify(CellTags.ReadSchema(candidate));
        // A migratable cell, such as one assembled from an old housing, only needs a powered network once.
        if (schema == SchemaGate.Migrate) return "workbench_reject_migrate";
        if (schema != SchemaGate.Current) return "workbench_reject_schema";
        if (CellHealth.IsUnavailable(candidate) || !CellPages.IsComplete(candidate)) return "workbench_reject_unavailable";
        return GeneralHelper.IsItemOwned(candidate) ? null : "workbench_reject_not_owned";
    }

    internal GameItem Create()
    {
        _appearance.EnsureReady(SpriteKey);
        var item = ItemDirectory.CreateEmptyItem(ItemId);
        try
        {
            _appearance.Apply(item, SpriteKey, Width, Height);
            NativeItemContentInitialization.Initialize(item, _contentSynchronization);
            var slot = new GameSlotInventory(1, 2) { identifier = CellUpgradeInventory.WorkbenchSlotId };
            var status = new MachineryText(string.Empty, alignment: TMPro.HorizontalAlignmentOptions.Left, uppercase: false);
            slot.mayInventoryAddItemFunc = (cell, _) =>
            {
                if (SaveManager.IsDecoding) return true;
                var refusal = Refusal(cell);
                // The native slot gives no reason for a refused drop, so the status line says why.
                if (refusal != null) status.SetText(_text.Get(refusal));
                return refusal == null;
            };
            slot.maxSlotAmountItemFunc = (_, _, _) => 1;
            var configure = new MachineryButton(_text.Get("workbench_configure"), () =>
            {
                if (slot.childItem is { } cell) _openPartition(cell);
            });
            var bayView = new ModuleBayView();
            CellUpgradeInventory.ListenForRefusals(slot, (target, check) =>
            {
                if (ReferenceEquals(target, slot.childItem)) status.SetText(ModuleChangeTooltipPresenter.RefusalText(check, _text));
            });

            void Sync()
            {
                var cell = slot.childItem;
                status.SetText(cell == null ? _text.Get("workbench_status_empty") : cell.GetDisplayName(preferCustomName: true));
                if (cell == null) configure.Disable();
                else configure.Enable();
                if (cell != null && CellUpgradeInventory.Bay(cell) is { } bay && CellUpgradeInventory.BayHolder(cell) is { } home)
                    bayView.Show(bay, home);
                else
                    bayView.Clear();
            }

            slot.onSlotAddItemEndFunc += (_, _, _) => Sync();
            slot.onSlotRemoveItemEndFunc += (cell, _, _) =>
            {
                _cellLeft(cell);
                Sync();
            };
            var window = MachineHelper.CreateMachineryWindow(true,
                MachineHelper.Panel(_text.Get("workbench_cell_panel"), slot, "workbench_cell"),
                MachineHelper.Panel(_text.Get("modules_panel"), bayView, "workbench_modules"),
                MachineHelper.Panel(_text.Get("workbench_actions_panel"), MachineryStack.Vertical(status, configure), "workbench_actions"));
            item.SetContentWindow(window);
            window.openCallback += Sync;
            window.openCallback += () => window.SetTitle(item.GetDisplayName(preferCustomName: true));
            // Classified like vanilla machines: no nesting in containers.
            item.SetGameItemType("MACHINE");
            item.mayStackItemFunc = (_, _) => false;
            item.maxSlotAmountItemFunc = (_, _, _) => 1;
            Sync();
            return item;
        }
        catch
        {
            item.Destroy();
            throw;
        }
    }
}
