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
/// inventory at storage index 1, which on a cell is a storage page. Other items run the native method unchanged.
/// </summary>
[HarmonyPatch(typeof(MachineHelper), nameof(MachineHelper.ExpandModule))]
internal static class ExpandModulePatch
{
    private static bool Prefix(GameItem item) => !ModuleBayExpansionHooks.ExpandCell(item);
}
