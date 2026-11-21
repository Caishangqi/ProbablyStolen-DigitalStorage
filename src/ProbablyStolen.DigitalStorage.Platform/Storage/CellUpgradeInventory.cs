using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Reads the native module bay of a cell as a Common <see cref="IUpgradeInventory"/>, and answers the questions the
/// bay's callbacks ask. The bay is found by its inventory identifier, never by storage index: it comes after the
/// pages in save order, so its index changes whenever pages are added or removed.
/// </summary>
internal static class CellUpgradeInventory
{
    /// <summary>The module type tag that only cell module bays accept.</summary>
    internal const string ModuleType = "digital_storage.cell_module";

    /// <summary>The item identifier of type limit modules.</summary>
    internal const string TypeLimitId = "digital_storage.module_type_limit";

    /// <summary>The item identifier of the equal distribution module.</summary>
    internal const string EqualDistributionId = "digital_storage.module_equal_distribution";

    /// <summary>The identifier of a cell's module bay inventory.</summary>
    internal const string BayId = "digital_storage.module_bay";

    /// <summary>The identifier of the cell workbench's cell slot; modules change only while a cell sits there.</summary>
    internal const string WorkbenchSlotId = "digital_storage.workbench_slot";

    /// <summary>The native tag holding the module bay expansion stage; native machines use the same key.</summary>
    internal const string StageTag = "MODULE_UPGRADE_STAGE_INT";

    /// <summary>
    /// Raised with the cell after a module enters or leaves its bay during play, the only change of its installed
    /// upgrades: a module's roll never changes in a bay, and an expansion changes only the bay's shape.
    /// </summary>
    internal static event Action<GameItem>? ModulesChanged;

    /// <summary>Decoding adds existing modules again, so this notification must not change their state.</summary>
    internal static void RaiseModulesChanged(GameItem cell)
    {
        if (!SaveManager.IsDecoding) ModulesChanged?.Invoke(cell);
    }

    private static readonly ConditionalWeakTable<GameSlotInventory, Action<GameItem, ModuleChangeCheck>> RefusalListeners = new();

    /// <summary>
    /// Registers the workbench slot that shows why a module cannot be installed in the cell it holds. The native drop
    /// refusal has no text, so the reason goes to the workbench status line. Entries go away with their slot.
    /// </summary>
    internal static void ListenForRefusals(GameSlotInventory workbenchSlot, Action<GameItem, ModuleChangeCheck> listener)
    {
        RefusalListeners.Remove(workbenchSlot);
        RefusalListeners.Add(workbenchSlot, listener);
    }

    /// <summary>Reports a refused installation to the workbench that holds the cell, if any.</summary>
    internal static void ReportRefusal(GameItem cell, ModuleChangeCheck check)
    {
        if (cell.parentInventory is GameSlotInventory slot && RefusalListeners.TryGetValue(slot, out var listener))
            listener(cell, check);
    }

    /// <summary>The role of the cell's own modules panel; it is always hidden and the workbench draws the bay.</summary>
    internal const string ModulesPanelRole = "cell_modules";

    /// <summary>Gets the cell's own modules panel, which holds the bay as its content, or null.</summary>
    internal static MachineryPanel? BayPanel(GameItem cell) =>
        (cell.contentWindow?.childElement as MachineryPanelRow)?.GetPanel(ModulesPanelRole);

    /// <summary>
    /// Gets the element that holds the bay inside the cell's modules panel: <c>MachineHelper.Panel</c> wraps an
    /// inventory in a <see cref="MachineryIntake"/> (MachineHelper.WrapInIntake), so the panel's content is the intake.
    /// </summary>
    internal static MachineryIntake? BayHolder(GameItem cell) => BayPanel(cell)?.content as MachineryIntake;

    /// <summary>Gets the module bay of a cell or housing, or null.</summary>
    /// <remarks>
    /// Read from the modules panel directly: <c>MachineryPanelRow.GetStorage</c> collects every storage of the row on
    /// each call, and the bay comes after every page.
    /// </remarks>
    internal static GameGridInventory? Bay(GameItem cell) =>
        BayHolder(cell)?.content is GameGridInventory { identifier: BayId } bay ? bay : null;

    internal static (GameItem Cell, GameGridInventory Bay)? FindModuleBay(GameItem moduleItem) =>
        moduleItem.parentInventory is GameGridInventory { identifier: BayId } bay &&
        bay.GetParentItem() is { } cell && ReferenceEquals(Bay(cell), bay) ? (cell, bay) : null;

    /// <summary>Gets the module an item stands for, or null for any other item.</summary>
    internal static InstalledModule? Module(GameItem item) => item.identifier switch
    {
        TypeLimitId => InstalledModule.TypeLimit(ModuleTags.Read(item)),
        EqualDistributionId => InstalledModule.EqualDistribution(),
        _ => null
    };

    /// <summary>Gets the modules in a cell's bay, in bay order.</summary>
    internal static IUpgradeInventory Read(GameItem cell)
    {
        var bay = Bay(cell);
        if (bay == null) return UpgradeInventory.Empty;
        var modules = new List<InstalledModule>();
        foreach (var item in bay.childItems)
        {
            if (Module(item) is { } module) modules.Add(module);
        }

        return new UpgradeInventory(modules);
    }

    /// <summary>Gets whether the bay holds any item.</summary>
    internal static bool HasModules(GameItem cell) => Bay(cell)?.childItems.Count > 0;

    /// <summary>Gets the recorded expansion stage.</summary>
    internal static int Stage(GameItem item) => item.GetTagReadonly(StageTag).GetInt();

    /// <summary>Restores the cell's registered module layout without changing its inventory nodes.</summary>
    internal static void ApplyShape(GameItem cell) => ModuleBayExpansionHooks.Initialize(cell);

    /// <summary>Gets whether a cell sits in a cell workbench.</summary>
    internal static bool InWorkbench(GameItem cell) => cell.parentInventory?.identifier == WorkbenchSlotId;

    internal static CellModuleState InstallationState(GameItem cell)
    {
        var tier = StorageCellTier.FromCellId(cell.identifier);
        var restoring = SaveManager.IsDecoding;
        return new CellModuleState(tier, Read(cell), InWorkbench(cell),
            !restoring && tier != null && HasCompleteLayout(cell), restoring, 0, 0);
    }

    internal static bool TryReadModuleIndex(GameGridInventory bay, GameItem item, out IUpgradeInventory modules, out int index)
    {
        index = -1;
        var installed = new List<InstalledModule>();
        foreach (var child in bay.childItems)
        {
            if (Module(child) is not { } module) continue;
            if (ReferenceEquals(child, item) && ReferenceEquals(item.parentInventory, bay)) index = installed.Count;
            installed.Add(module);
        }

        modules = new UpgradeInventory(installed);
        return index >= 0;
    }

    internal static bool NeedsUsage(InstalledModule module) => module.Kind == CellModuleKind.TypeLimit && module.IsValid;

    // The actual bay callback makes its own decision even when the workbench has a ready preview.
    internal static bool CanRemove(GameItem cell, GameGridInventory bay, GameItem item)
    {
        if (item.identifier != TypeLimitId && item.identifier != EqualDistributionId) return true;
        if (!TryReadModuleIndex(bay, item, out var modules, out var index)) return false;
        var tier = StorageCellTier.FromCellId(cell.identifier);
        var inWorkbench = InWorkbench(cell);
        var restoring = SaveManager.IsDecoding;
        var available = false;
        var usage = default(CellModuleUsage);
        if (!restoring && inWorkbench && tier != null)
        {
            if (NeedsUsage(modules.Modules[index]))
            {
                usage = ReadUsage(cell, ReadLive);
                available = usage.Status == CellModuleUsage.ReadStatus.Ready;
            }
            else
            {
                available = HasCompleteLayout(cell);
            }
        }

        var state = new CellModuleState(tier, modules, inWorkbench, available, restoring, usage.StoredTypes, usage.AreaUnits);
        return CellUpgradeCallbacks.CanRemove(state, index).IsAllowed;
    }

    internal static CellModuleUsage ReadUsage(GameItem cell, Func<GameItem, CellView> read, Action<Exception>? reportError = null)
    {
        var layoutAvailable = false;
        try
        {
            if (!HasCompleteLayout(cell)) return CellModuleUsage.Unavailable(layoutUnavailable: true);
            layoutAvailable = true;
            var view = read(cell);
            return view.Pages.Any(page => page.IsProtected)
                ? CellModuleUsage.Unavailable(layoutUnavailable: false)
                : CellModuleUsage.Ready(view.TypeCount, view.Usage.AreaUnits);
        }
        catch (Exception error)
        {
            // A partial or failed read cannot authorize a capacity reduction.
            reportError?.Invoke(error);
            return CellModuleUsage.Unavailable(layoutUnavailable: !layoutAvailable);
        }
    }

    private static bool HasCompleteLayout(GameItem cell) => !CellHealth.IsUnavailable(cell) && CellPages.IsComplete(cell);

    private static CellView ReadLive(GameItem cell) => CellPages.Read(cell, StorageService.Identify, CellWriteState.Writable);
}
