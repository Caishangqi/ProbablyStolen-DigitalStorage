using ProbablyStolen.DigitalStorage.Platform.Compatibility;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>Every patch group of the mod, integrity groups first. Patches on the same method apply in this order.</summary>
internal static class GamePatchGroups
{
    internal static GamePatchGroup[] Create() => new[]
    {
        CellPageRestorePatch.Group(),
        StoredItemPresencePatch.Group(),
        StoredLiquidUpdatePatches.Group(),
        StoredItemChangePatches.Group(),
        ModuleBayExpansionPatches.Group(),
        TerminalOpenPatch.Group(),
        TerminalSingleDropPatches.Group(),
        TerminalGroupDropPatches.Group(),
        PartitionEditorClosePatches.Group(),
        DragWheelPatch.Group(),
        MachineBayGroupDropPatch.Group(),
        ForwardFillGridPatch.Group(),
        BorrowedPanelBandPatch.Group(),
        WindowBackPatches.Group(),
        WindowPositionForgetPatch.Group(),
        NetworkPowerTooltipPatch.Group(),
        TypeLimitModuleTooltipPatch.Group(),
        WorkbenchModuleHighlightPatch.Group(),
        LocaleSelectionPatch.Group(),
        ModConfigWindowPatch.Group()
    };
}
