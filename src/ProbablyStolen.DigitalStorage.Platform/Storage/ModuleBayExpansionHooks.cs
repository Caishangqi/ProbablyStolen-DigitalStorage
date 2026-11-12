using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Lets the native module bay expansion kit work on storage cells without giving cells the standard machine tag,
/// which other native code reads (turbo boosters, module bay restore by storage index). Called from Harmony patches in
/// the mod assembly.
/// </summary>
public static class ModuleBayExpansionHooks
{
    /// <summary>
    /// Adds the cell rule to a new kit. The kit's own rule refuses items without the machine tag, and targeting
    /// delegates of one item combine with OR, so only an added delegate on the kit itself can admit a cell.
    /// </summary>
    public static void ExtendKit(GameItem kit)
    {
        if (kit == null) return;
        kit.mayItemTargetItemFunc += CanExpandCell;
        kit.canItemTargetItemFunc += CanExpandCell;
    }

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
