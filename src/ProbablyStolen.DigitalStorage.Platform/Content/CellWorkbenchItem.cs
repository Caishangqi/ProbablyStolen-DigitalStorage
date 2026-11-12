using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;
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
    private readonly Action<string> _log;
    private bool _summaryFailureLogged;
    private readonly ConditionalWeakTable<GameSlotInventory, Action> _summaries = new();

    internal CellWorkbenchItem(ModText text, ItemAppearance appearance, ItemContentSynchronization contentSynchronization,
        Action<GameItem> openPartition, Action<GameItem> cellLeft, Action<string> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        _contentSynchronization = contentSynchronization ?? throw new ArgumentNullException(nameof(contentSynchronization));
        _openPartition = openPartition ?? throw new ArgumentNullException(nameof(openPartition));
        _cellLeft = cellLeft ?? throw new ArgumentNullException(nameof(cellLeft));
        CellUpgradeInventory.ModulesChanged += RefreshSummary;
        CellPartitionTags.Committed += RefreshSummary;
    }

    internal void Detach()
    {
        CellUpgradeInventory.ModulesChanged -= RefreshSummary;
        CellPartitionTags.Committed -= RefreshSummary;
    }

    private void RefreshSummary(GameItem cell)
    {
        if (cell.parentInventory is GameSlotInventory slot && _summaries.TryGetValue(slot, out var refresh)) refresh();
    }

    // Native slot and module callbacks must survive an unreadable cell; keep its name or notice visible.
    private string Summary(GameItem? cell, string? notice, string storedTypes)
    {
        if (cell == null) return notice ?? _text.Get("workbench_status_empty");
        var first = notice ?? cell.GetDisplayName(preferCustomName: true);
        try
        {
            var config = new CellPartitionTags(cell).Capture().Interpret().Config;
            var limit = CellTypeLimitSource.EffectiveOrBase(cell);
            var mode = _text.Get(config.Mode == PartitionMode.Blacklist ? "partition_mode_blacklist" : "partition_mode_whitelist");
            return string.Join("\n", new List<string>
            {
                first,
                Line("workbench_info_mode", mode),
                Line("workbench_info_rules", _text.Get("workbench_info_count", config.Rules.Count, limit)),
                Line("workbench_info_types", _text.Get("workbench_info_count", storedTypes, limit))
            });
        }
        catch (Exception error)
        {
            if (!_summaryFailureLogged)
            {
                _summaryFailureLogged = true;
                _log($"Digital Storage: workbench cannot summarize its cell ({error.GetType().Name}).");
            }
            return first;
        }
    }

    private string Line(string labelKey, string value) =>
        _text.Get(labelKey) + ": <color=#" + ColorUtility.ToHtmlStringRGB(MachineryStyle.VALUE_TEXT) + ">" + value + "</color>";

    private string StoredTypes(GameItem cell)
    {
        try
        {
            if (SaveManager.IsDecoding || !CellPages.IsComplete(cell)) return "?";
            return CellPages.Read(cell, StorageService.Identify, CellWriteState.ExtractOnly).TypeCount.ToString();
        }
        catch (Exception error)
        {
            // Runs inside native slot callbacks and module or partition events, which must not see an exception.
            if (!_summaryFailureLogged)
            {
                _summaryFailureLogged = true;
                _log($"Digital Storage: workbench cannot count the cell's stored types ({error.GetType().Name}).");
            }
            return "?";
        }
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
            NativeItemContentInitialization.Initialize(item, _contentSynchronization);
            var slot = new GameSlotInventory(1, 2) { identifier = CellUpgradeInventory.WorkbenchSlotId };
            var info = new MachineryText(string.Empty, alignment: TMPro.HorizontalAlignmentOptions.Left, uppercase: false);
            // Counting stored types reads every page; a notice during a drag reuses the last count.
            var storedTypes = "?";
            void Refresh()
            {
                storedTypes = slot.childItem is { } cell ? StoredTypes(cell) : "?";
                info.SetText(Summary(slot.childItem, null, storedTypes));
            }
            void ShowNotice(string notice) => info.SetText(Summary(slot.childItem, notice, storedTypes));
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
                Refresh();
                if (cell == null) configure.Disable();
                else configure.Enable();
                if (cell != null && CellUpgradeInventory.Bay(cell) is { } bay && CellUpgradeInventory.BayHolder(cell) is { } home)
                    bayView.Show(bay, home);
                else
                    bayView.Clear();
            }

            _summaries.Add(slot, Refresh);
            slot.onSlotAddItemEndFunc += (_, _, _) => Sync();
            slot.onSlotRemoveItemEndFunc += (cell, _, _) =>
            {
                _cellLeft(cell);
                Sync();
            };
            var window = MachineHelper.CreateMachineryWindow(true,
                MachineHelper.Panel(_text.Get("workbench_cell_panel"), slot, "workbench_cell"),
                MachineHelper.Panel(_text.Get("modules_panel"), bayView, "workbench_modules"),
                MachineHelper.Panel(_text.Get("workbench_actions_panel"),
                    MachineryStack.Vertical(new MachineryIntake(info, vents: false, styleInventories: false), configure),
                    "workbench_actions"));
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
