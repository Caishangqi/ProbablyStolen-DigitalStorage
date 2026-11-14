using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;
using ProbablyStolen.DigitalStorage.Platform.Content;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Lets the native module bay expansion kit work on storage cells without giving cells the standard machine tag,
/// which other native code reads (turbo boosters, module bay restore by storage index). Called from Harmony patches in
/// the mod assembly.
/// </summary>
public static class ModuleBayExpansionHooks
{
    /// <summary>
    /// Lets a new kit also target storage cells, keeping its native machine rule, and never target a terminal, whose
    /// module bay is already the kit's last stage. The terminal exclusion here only covers the rules present now;
    /// <see cref="RefusesTarget"/> enforces it on the final result.
    /// </summary>
    public static void ExtendKit(GameItem kit)
    {
        if (kit == null) return;
        kit.mayItemTargetItemFunc = TargetRules.Unless<GameItem, GameItem>(
            TargetRules.OrWith<GameItem, GameItem>(kit.mayItemTargetItemFunc, CanExpandCell), IsTerminal);
        kit.canItemTargetItemFunc = TargetRules.Unless<GameItem, GameItem>(
            TargetRules.OrWith<GameItem, GameItem>(kit.canItemTargetItemFunc, CanExpandCell), IsTerminal);
    }

    /// <summary>
    /// Handles the kit's expansion where the native method must not run: a cell expands its own bay, and a terminal is
    /// left unchanged (its kit targeting is refused already; this is the fallback). Returns false for other items.
    /// </summary>
    public static bool HandleExpansion(GameItem item) => IsTerminal(item) || ExpandCell(item);

    /// <summary>
    /// Gets whether the native load-time reshape (<c>MachineHelper.LoadUpgrade</c>, run for every decoded item) must
    /// skip this item. Terminals keep their full bay; their stage tag is neither read nor changed.
    /// </summary>
    public static bool SkipsUpgradeLoad(GameItem item) => IsTerminal(item);

    /// <summary>
    /// Gets whether <paramref name="source"/> must not target <paramref name="target"/>: the kit on a terminal. Checked
    /// on the final <c>GameItem.MayTarget</c>/<c>CanTarget</c> result, because the game ORs every rule on the kit's
    /// delegate chain (<c>InvokeAllReduce</c> with <c>BoolReduceOR</c>) and another mod may append a machine rule after
    /// <see cref="ExtendKit"/>. Without this, the kit would still be consumed and add value to the terminal while
    /// <see cref="HandleExpansion"/> leaves its bay unchanged.
    /// </summary>
    public static bool RefusesTarget(GameItem? source, GameItem? target) =>
        source?.identifier == KitId && IsTerminal(target);

    // ModuleDirectory registers the kit under this id (Add("module_bay_expansion_kit", ...)).
    private const string KitId = "module_bay_expansion_kit";

    private static bool IsTerminal(GameItem? item) => item?.identifier == TerminalItem.ItemId;

    /// <summary>
    /// Expands a cell's own module bay. Returns false for any other item so the native method runs; for a cell it
    /// always returns true, because the native method would reshape the inventory at storage index 1, which is a page.
    /// </summary>
    public static bool ExpandCell(GameItem item)
    {
        var tier = item == null ? null : StorageCellTier.FromCellId(item.identifier);
        if (tier == null) return false;
        var cell = item!;
        var bay = CellUpgradeInventory.Bay(cell);
        var previousStage = CellUpgradeInventory.Stage(cell);
        if (bay == null || !ModuleBayExpansion.Fits(previousStage + 1, tier)) return true;
        ModuleBayExpansion.Apply(previousStage, tier,
            (shape, width) => bay.SetShape(shape, width),
            stage => cell.ModifyTag(CellUpgradeInventory.StageTag, state => state.Enable().SetInt(stage)),
            () => cell.contentWindow?.Validate());
        return true;
    }

    private static bool CanExpandCell(GameItem source, GameItem target)
    {
        var tier = target == null ? null : StorageCellTier.FromCellId(target.identifier);
        // The native kit is consumed and adds value after ExpandModule whatever happens there, so every condition
        // ExpandCell needs is checked here, before the kit can be used at all.
        return tier != null && GeneralHelper.IsItemOwned(target) && GeneralHelper.IsItemOwned(source) &&
            !CellHealth.IsUnavailable(target!) && CellPages.IsComplete(target!) && CellUpgradeInventory.Bay(target!) != null &&
            ModuleBayExpansion.CanExpand(CellUpgradeInventory.Stage(target!), tier);
    }
}
