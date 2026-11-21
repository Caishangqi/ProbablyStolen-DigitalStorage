using System;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Explains why a module cannot be installed in or removed from a cell, with the amount still to clear.</summary>
internal static class ModuleChangeTooltipPresenter
{
    /// <summary>Gets the player text of a refused module change.</summary>
    internal static string RefusalText(ModuleChangeCheck check, ModText text) => check.Failure switch
    {
        ModuleChangeFailure.TypesExceedLimit => text.Get("module_refusal_typesexceedlimit", check.Shortfall),
        ModuleChangeFailure.BytesExceedCapacity => text.Get("module_refusal_bytesexceedcapacity", check.Shortfall),
        _ => text.Get("module_refusal_" + check.Failure.ToString().ToLowerInvariant())
    };

    internal static void AppendRemovalRefusal(RichTextBuilder builder, GameItem moduleItem, ModText text,
        Func<GameItem, CellModuleUsage?> findUsage)
    {
        if (CellUpgradeInventory.FindModuleBay(moduleItem) is not { } location ||
            !CellUpgradeInventory.TryReadModuleIndex(location.Bay, moduleItem, out var modules, out var index)) return;
        var cell = location.Cell;
        if (findUsage(cell) is not { } usage) return;

        var needsUsage = CellUpgradeInventory.NeedsUsage(modules.Modules[index]);
        if (needsUsage && usage.Status == CellModuleUsage.ReadStatus.Pending) return;
        var available = needsUsage ? usage.Status == CellModuleUsage.ReadStatus.Ready : !usage.LayoutUnavailable;
        var state = new CellModuleState(StorageCellTier.FromCellId(cell.identifier), modules, inWorkbench: true,
            available, restoring: false, usage.StoredTypes, usage.AreaUnits);
        var check = CellUpgradeCallbacks.CanRemove(state, index);
        if (check.IsAllowed) return;
        builder.AddLine(RefusalText(check, text), ignoreEmpty: true, RenderHandler.ColorPalette.LightRed);
    }
}
