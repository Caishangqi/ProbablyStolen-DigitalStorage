using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;
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

    internal static void AppendRemovalRefusal(RichTextBuilder builder, GameItem moduleItem, ModText text)
    {
        if (moduleItem.parentInventory?.identifier != CellUpgradeInventory.BayId ||
            moduleItem.parentInventory.GetParentItem() is not { } cell ||
            CellUpgradeInventory.Module(moduleItem) is not { } module)
            return;

        var index = 0;
        foreach (var child in CellUpgradeInventory.Bay(cell)?.childItems ?? new System.Collections.Generic.List<GameItem>())
        {
            if (ReferenceEquals(child, moduleItem)) break;
            if (CellUpgradeInventory.Module(child) != null) index++;
        }

        var state = CellUpgradeInventory.State(cell, withUsage: module.Kind == CellModuleKind.TypeLimit);
        if (index >= state.Modules.Modules.Count) return;
        var check = CellUpgradeCallbacks.CanRemove(state, index);
        if (check.IsAllowed) return;
        builder.AddLine(RefusalText(check, text), ignoreEmpty: true, RenderHandler.ColorPalette.LightRed);
    }
}
