using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>
/// Lets the native module bay expansion kit target storage cells. The kit factory is private, so it is named by its
/// literal member name as the modding guide recommends; every kit, new or decoded, is built by it.
/// </summary>
[HarmonyPatch(typeof(ModuleDirectory), "ModuleBayExpansionKit")]
internal static class ModuleBayExpansionKitPatch
{
    private static void Postfix(GameItem __result) => ModuleBayExpansionHooks.ExtendKit(__result);
}

/// <summary>
/// Redirects the kit's expansion of a storage cell to the cell's own module bay; the native method reshapes the
/// inventory at storage index 1, which on a cell is a storage page. A terminal is left unchanged. Other items run the
/// native method unchanged.
/// </summary>
[HarmonyPatch(typeof(MachineHelper), nameof(MachineHelper.ExpandModule))]
internal static class ExpandModulePatch
{
    private static bool Prefix(GameItem item) => !ModuleBayExpansionHooks.HandleExpansion(item);
}

/// <summary>
/// Refuses the kit on a terminal at the final result, so a rule another mod appends to the kit cannot make the kit
/// target (and be consumed by) a terminal whose bay is already the kit's last stage.
/// </summary>
[HarmonyPatch(typeof(GameItem), nameof(GameItem.MayTarget))]
internal static class KitMayTargetPatch
{
    private static void Postfix(GameItem __instance, GameItem targetItem, ref bool __result)
    {
        if (__result && ModuleBayExpansionHooks.RefusesTarget(__instance, targetItem)) __result = false;
    }
}

/// <summary>The same refusal for <c>CanTarget</c>, which the game checks before applying the kit.</summary>
[HarmonyPatch(typeof(GameItem), nameof(GameItem.CanTarget))]
internal static class KitCanTargetPatch
{
    private static void Postfix(GameItem __instance, GameItem targetItem, ref bool __result)
    {
        if (__result && ModuleBayExpansionHooks.RefusesTarget(__instance, targetItem)) __result = false;
    }
}

/// <summary>
/// Keeps a terminal's full module bay on load: the native method reshapes a standard machine's bay to its recorded kit
/// stage, which for a terminal would shrink it. Other items run the native method unchanged.
/// </summary>
[HarmonyPatch(typeof(MachineHelper), nameof(MachineHelper.LoadUpgrade))]
internal static class LoadUpgradePatch
{
    private static bool Prefix(GameItem item) => !ModuleBayExpansionHooks.SkipsUpgradeLoad(item);
}
