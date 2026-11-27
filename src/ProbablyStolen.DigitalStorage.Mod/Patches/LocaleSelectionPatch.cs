using System.Collections;
using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Compatibility;
using ProbablyStolen.DigitalStorage.Platform.Composition;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

internal static class LocaleSelectionPatch
{
    internal static GamePatchGroup Group() => new(GameFeature.LocaleRefresh, GamePatchTier.Enhancement,
        new GamePatch("LocHelper.SetLocale", () => AccessTools.Method(typeof(LocHelper), nameof(LocHelper.SetLocale)),
            postfix: AccessTools.Method(typeof(LocaleSelectionPatch), nameof(Postfix))));

    private static void Postfix(ref IEnumerator __result) => __result = LocaleSelectionBridge.Wrap(__result);
}
