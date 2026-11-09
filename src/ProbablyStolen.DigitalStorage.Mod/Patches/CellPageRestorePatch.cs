using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>
/// Rebuilds appended storage cell pages while a save decodes. It lives in the IMod assembly because the game's
/// loader runs <c>Harmony.PatchAll</c> only on that assembly (Steam build 26496118,
/// <c>ModLoader.LoadModDll</c>); the loader also removes it when the mod is disabled.
/// </summary>
[HarmonyPatch(typeof(StorageExpansionHelper), nameof(StorageExpansionHelper.RestoreTabs))]
internal static class CellPageRestorePatch
{
    private static void Postfix(GameItem item) => CellPageRestore.Restore(item);
}
