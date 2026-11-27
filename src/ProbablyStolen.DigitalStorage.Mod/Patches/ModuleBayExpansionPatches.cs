using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>
/// Module bay expansion on storage machines. All patches go together: without the load patch the native two-stage
/// layout would be applied to managed machines on load and could push installed modules out of their bays.
/// </summary>
internal static class ModuleBayExpansionPatches
{
    internal static GamePatchGroup Group() => new(GameFeature.ModuleBayExpansion, GamePatchTier.Integrity,
        new GamePatch("ModuleDirectory.ModuleBayExpansionKit",
            () => AccessTools.Method(typeof(ModuleDirectory), "ModuleBayExpansionKit"),
            postfix: AccessTools.Method(typeof(ModuleBayExpansionPatches), nameof(KitCreatedPostfix))),
        new GamePatch("MachineHelper.CanExpand", () => AccessTools.Method(typeof(MachineHelper), nameof(MachineHelper.CanExpand)),
            prefix: AccessTools.Method(typeof(ModuleBayExpansionPatches), nameof(CanExpandPrefix))),
        new GamePatch("MachineHelper.ExpandModule", () => AccessTools.Method(typeof(MachineHelper), nameof(MachineHelper.ExpandModule)),
            prefix: AccessTools.Method(typeof(ModuleBayExpansionPatches), nameof(ExpandModulePrefix))),
        new GamePatch("GameItem.MayTarget", () => AccessTools.Method(typeof(GameItem), nameof(GameItem.MayTarget)),
            postfix: AccessTools.Method(typeof(ModuleBayExpansionPatches), nameof(TargetPostfix))),
        new GamePatch("GameItem.CanTarget", () => AccessTools.Method(typeof(GameItem), nameof(GameItem.CanTarget)),
            postfix: AccessTools.Method(typeof(ModuleBayExpansionPatches), nameof(TargetPostfix))),
        new GamePatch("MachineHelper.LoadUpgrade", () => AccessTools.Method(typeof(MachineHelper), nameof(MachineHelper.LoadUpgrade)),
            prefix: AccessTools.Method(typeof(ModuleBayExpansionPatches), nameof(LoadUpgradePrefix))),
        new GamePatch("DirectoryMaster.Item",
            () => AccessTools.Method(typeof(DirectoryMaster), nameof(DirectoryMaster.Item), new[] { typeof(string), typeof(bool) }),
            postfix: AccessTools.Method(typeof(ModuleBayExpansionPatches), nameof(ItemCreatedPostfix))));

    /// <summary>Wraps every newly constructed native kit before it is used or decoded.</summary>
    private static void KitCreatedPostfix(GameItem __result) => ModuleBayExpansionHooks.ExtendKit(__result);

    /// <summary>Uses the bound stage limit for native expansion checks.</summary>
    private static bool CanExpandPrefix(GameItem item, ref bool __result)
    {
        if (!ModuleBayExpansionHooks.IsManaged(item)) return true;
        __result = !ModuleBayExpansionHooks.RejectsThread && ModuleBayExpansionHooks.CanExpand(item);
        return false;
    }

    /// <summary>Changes only the bound layout and stage, retaining native callers' tool ownership.</summary>
    private static bool ExpandModulePrefix(GameItem item)
    {
        if (!ModuleBayExpansionHooks.IsManaged(item)) return true;
        if (!ModuleBayExpansionHooks.RejectsThread) ModuleBayExpansionHooks.Expand(item);
        return false;
    }

    /// <summary>
    /// Checks the final native targeting result after combined rules have run, both for the highlight
    /// (<c>MayTarget</c>) and for the permission before the tool action (<c>CanTarget</c>).
    /// </summary>
    private static void TargetPostfix(GameItem __instance, GameItem targetItem, ref bool __result)
    {
        if (__result && ModuleBayExpansionHooks.RefusesTarget(__instance, targetItem)) __result = false;
    }

    /// <summary>Restores registered stages without applying the native two-stage pattern switch.</summary>
    private static bool LoadUpgradePrefix(GameItem item)
    {
        if (!ModuleBayExpansionHooks.IsManaged(item)) return true;
        if (!ModuleBayExpansionHooks.RejectsThread) ModuleBayExpansionHooks.Initialize(item);
        return false;
    }

    /// <summary>Initializes bound products created by native or external directories.</summary>
    private static void ItemCreatedPostfix(GameItem __result) => ModuleBayExpansionHooks.Initialize(__result);
}
