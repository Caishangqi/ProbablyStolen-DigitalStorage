using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

/// <summary>Rebuilds appended storage cell pages while a save decodes.</summary>
internal static class CellPageRestorePatch
{
    internal static GamePatchGroup Group() => new(GameFeature.CellPageRestore, GamePatchTier.Integrity,
        new GamePatch("StorageExpansionHelper.RestoreTabs",
            () => AccessTools.Method(typeof(StorageExpansionHelper), nameof(StorageExpansionHelper.RestoreTabs)),
            postfix: AccessTools.Method(typeof(CellPageRestorePatch), nameof(Postfix))));

    private static void Postfix(GameItem item) => CellPageRestore.Restore(item);
}
