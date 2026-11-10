using System.Collections;
using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Composition;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

[HarmonyPatch(typeof(LocHelper), nameof(LocHelper.SetLocale))]
internal static class LocaleSelectionPatch
{
    private static void Postfix(ref IEnumerator __result) => __result = LocaleSelectionBridge.Wrap(__result);
}
