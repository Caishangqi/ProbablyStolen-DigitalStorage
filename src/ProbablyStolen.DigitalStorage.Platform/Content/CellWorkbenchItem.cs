using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;
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
    private readonly Action<GameItem> _openPartition;
    private readonly Action<GameItem> _cellLeft;
    private readonly Action<string> _log;
    private readonly WorkbenchUsage _usage;
    private bool _summaryFailureLogged;

    internal CellWorkbenchItem(ModText text, ItemAppearance appearance,
        Action<GameItem> openPartition, Action<GameItem> cellLeft, Action<string> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        _openPartition = openPartition ?? throw new ArgumentNullException(nameof(openPartition));
        _cellLeft = cellLeft ?? throw new ArgumentNullException(nameof(cellLeft));
        _usage = new WorkbenchUsage(error => ReportSummaryFailure("count the cell's stored types", error));
        CellPages.UsageChanged += _usage.InvalidateContents;
        CellUpgradeInventory.ModulesChanged += _usage.RefreshSummary;
        CellPartitionTags.Committed += _usage.RefreshSummary;
    }

    internal void Detach()
    {
        CellPages.UsageChanged -= _usage.InvalidateContents;
        CellUpgradeInventory.ModulesChanged -= _usage.RefreshSummary;
        CellPartitionTags.Committed -= _usage.RefreshSummary;
        _usage.Detach();
    }

    internal CellModuleUsage? FindUsage(GameItem cell) => _usage.Find(cell);

    private string Summary(GameItem? cell, string? notice) =>
        WorkbenchCellSummary.Render(cell, notice, _text, FindUsage, error => ReportSummaryFailure("summarize its cell", error));

    private void ReportSummaryFailure(string operation, Exception error)
    {
        if (_summaryFailureLogged) return;
        _summaryFailureLogged = true;
        _log($"Digital Storage: workbench cannot {operation} ({error.GetType().Name}).");
    }

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
            var slot = new GameSlotInventory(1, 2) { identifier = CellUpgradeInventory.WorkbenchSlotId };
            var info = new MachineryText(string.Empty, alignment: TMPro.HorizontalAlignmentOptions.Left, uppercase: false);
            string? notice = null;
            void Render() => info.SetText(Summary(slot.childItem, notice));
            void RefreshSummary()
            {
                notice = null;
                Render();
            }
            void ShowNotice(string message)
            {
                notice = message;
                Render();
            }
            slot.mayInventoryAddItemFunc = (cell, _) =>
            {
                if (SaveManager.IsDecoding) return true;
                var refusal = Refusal(cell);
                // The native slot gives no reason for a refused drop, so the info box says why until the next refresh.
                if (refusal != null) ShowNotice(_text.Get(refusal));
                return refusal == null;
            };
            slot.maxSlotAmountItemFunc = (_, _, _) => 1;
            // A text button fills the panel width by default; keep it at its label width, centered.
            var configure = new MachineryButton(_text.Get("workbench_configure"), () =>
            {
                if (slot.childItem is { } cell) _openPartition(cell);
            }).SetFillWidth(false);
            var bayView = new ModuleBayView();
            CellUpgradeInventory.ListenForRefusals(slot, (target, check) =>
            {
                if (ReferenceEquals(target, slot.childItem)) ShowNotice(ModuleChangeTooltipPresenter.RefusalText(check, _text));
            });

            void Sync()
            {
                var cell = slot.childItem;
                RefreshSummary();
                // Without the close patches an open editor would outlive its cell's removal from the slot.
                if (cell == null || !GamePatchStatus.IsActive(GameFeature.PartitionEditorClose)) configure.Disable();
                else configure.Enable();
                if (cell != null && CellUpgradeInventory.Bay(cell) is { } bay && CellUpgradeInventory.BayHolder(cell) is { } home)
                    bayView.Show(bay, home);
                else
                    bayView.Clear();
            }

            var window = MachineHelper.CreateMachineryWindow(true,
                MachineHelper.Panel(_text.Get("workbench_cell_panel"), slot, "workbench_cell"),
                MachineHelper.Panel(_text.Get("modules_panel"), bayView, "workbench_modules"),
                MachineHelper.Panel(_text.Get("workbench_actions_panel"),
                    MachineryStack.Vertical(new MachineryIntake(info, vents: false, styleInventories: false), configure),
                    "workbench_actions"));
            item.SetContentWindow(window);
            var usage = _usage.Register(slot, item, window,
                () => window.handler != null && window.handler.gameObject.activeInHierarchy, RefreshSummary);
            window.handler.gameObject.AddComponent<WorkbenchUsageRefresh>().Bind(usage, Render);
            void InvalidateAndSync()
            {
                usage.Invalidate();
                Sync();
            }

            slot.onSlotAddItemEndFunc += (_, _, _) => InvalidateAndSync();
            slot.onSlotRemoveItemEndFunc += (cell, _, _) =>
            {
                usage.Invalidate();
                _cellLeft(cell);
                Sync();
            };
            window.openCallback += InvalidateAndSync;
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
